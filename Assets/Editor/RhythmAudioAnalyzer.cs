using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// EDITOR ONLY: keep this file in a folder named "Editor" (e.g. Assets/Scripts/Editor).

/// <summary>One possible tempo for the track.</summary>
public struct TempoCandidate
{
    public float Bpm;
    public float OffsetSeconds;   // time of a beat in the audio (the first beat line after the start)
    public float Score;           // higher = the beat grid lines up with more of the music's hits
}

/// <summary>Result of analysing a clip. Everything is stored at a fixed 100 frames per second.</summary>
public class AudioAnalysis
{
    public float[] Energy;        // loudness (RMS) per frame
    public float[] Onset;         // "something just started" strength per frame, roughly 0..1
    public float Duration;        // seconds that were analysed
    public float Confidence;      // best score divided by the average score: higher = clearer pulse
    public List<TempoCandidate> Candidates = new List<TempoCandidate>(); // best first
}

/// <summary>Knobs for turning an analysis into notes.</summary>
[Serializable]
public class NoteGenSettings
{
    public int Subdivision = 2;            // grid steps per beat: 1 = beats, 2 = half beats, 4 = sixteenths
    public float Sensitivity = 0.5f;       // 0..1, higher finds more (and weaker) hits
    public float MinGapBeats = 0.5f;       // never place two notes closer than this
    public bool CreateHolds = true;
    public float MinHoldBeats = 1f;
    public float MaxHoldBeats = 4f;
    public float SustainRatio = 0.4f;      // how much of the note's loudness must remain to count as a hold
    public float MaxSeconds = 60f;         // only generate notes for this much of the song
}

/// <summary>
/// Finds the tempo and beat position of a clip, and proposes notes from where hits start.
///
/// How it works:
///  1. Loudness per 10 ms frame (RMS).
///  2. Onset strength: how much louder a frame is than the few frames before it. Spikes = something started.
///  3. Tempo: for every BPM from 60 to 200 (step 0.1) lay a beat grid over the onset curve at every possible
///     start position, and score how strongly the music hits on the grid lines. Highest score wins.
///
/// It assumes a steady tempo and works best with a clear pulse. It is a heuristic, so always check the result
/// against the waveform and fix it by hand when needed.
/// </summary>
public static class RhythmAudioAnalyzer
{
    public const int FramesPerSecond = 100;

    private const float MinBpm = 60f;
    private const float MaxBpm = 200f;
    private const float BpmStep = 0.1f;
    private const float TempoSearchSeconds = 90f;   // the tempo is estimated from the first 90 s
    private const float MaxAnalyzeSeconds = 180f;

    private struct NoteCandidate
    {
        public float Beat;
        public float Strength;
        public int Frame;
    }

    // ---- Analysis ---------------------------------------------------------
    public static bool TryAnalyze(AudioClip clip, out AudioAnalysis analysis, out string error)
    {
        analysis = null;
        error = null;

        if (clip == null) { error = "No audio clip assigned."; return false; }

        int channels = clip.channels;
        int sampleCount = Mathf.Min(clip.samples, Mathf.RoundToInt(MaxAnalyzeSeconds * clip.frequency));
        if (sampleCount < clip.frequency) { error = "The clip is shorter than one second."; return false; }

        EditorUtility.DisplayProgressBar("Analyzing audio", "Reading samples...", 0.1f);

        // GetData only works when the clip is fully decoded in memory.
        float[] data = new float[sampleCount * channels];
        if (clip.loadType == AudioClipLoadType.Streaming || !clip.GetData(data, 0))
        {
            error = "Could not read the audio data. Select the clip in the Project window, set Load Type to " +
                    "'Decompress On Load' in the Inspector, press Apply, then analyze again.";
            return false;
        }

        // 1) Loudness per frame.
        int hop = clip.frequency / FramesPerSecond;
        int frames = sampleCount / hop;
        float[] energy = new float[frames];

        for (int f = 0; f < frames; f++)
        {
            double sum = 0.0;
            int start = f * hop;
            for (int s = 0; s < hop; s++)
            {
                float mono = 0f;
                int index = (start + s) * channels;
                for (int c = 0; c < channels; c++) mono += data[index + c];
                mono /= channels;
                sum += mono * mono;
            }
            energy[f] = (float)Math.Sqrt(sum / hop);
        }

        // 2) Onset strength: log-loudness jump compared with the average of the previous 3 frames.
        EditorUtility.DisplayProgressBar("Analyzing audio", "Finding hits...", 0.25f);
        float[] logEnergy = new float[frames];
        for (int f = 0; f < frames; f++) logEnergy[f] = Mathf.Log(1f + 1000f * energy[f]);

        float[] onset = new float[frames];
        for (int f = 3; f < frames; f++)
        {
            float previous = (logEnergy[f - 1] + logEnergy[f - 2] + logEnergy[f - 3]) / 3f;
            onset[f] = Mathf.Max(0f, logEnergy[f] - previous);
        }

        if (!NormalizeByPercentile(onset))
        {
            error = "The clip looks silent, nothing to analyze.";
            return false;
        }

        // 3) Tempo and beat position.
        int searchFrames = Mathf.Min(frames, Mathf.RoundToInt(TempoSearchSeconds * FramesPerSecond));
        List<TempoCandidate> candidates = FindTempo(Smooth(onset), searchFrames, out float confidence);
        if (candidates.Count == 0)
        {
            error = "No tempo found. The clip may have no clear pulse; set the BPM by hand.";
            return false;
        }

        analysis = new AudioAnalysis
        {
            Energy = energy,
            Onset = onset,
            Duration = sampleCount / (float)clip.frequency,
            Candidates = candidates,
            Confidence = confidence
        };
        return true;
    }

    private static List<TempoCandidate> FindTempo(float[] smooth, int searchFrames, out float confidence)
    {
        int count = Mathf.RoundToInt((MaxBpm - MinBpm) / BpmStep) + 1;
        var scores = new float[count];
        var phases = new float[count];
        double scoreSum = 0.0;

        for (int i = 0; i < count; i++)
        {
            if (i % 100 == 0)
                EditorUtility.DisplayProgressBar("Analyzing audio", "Searching for the tempo...", 0.3f + 0.7f * i / count);

            float bpm = MinBpm + i * BpmStep;
            float period = 60f * FramesPerSecond / bpm;       // frames between beats (not a whole number)
            int phaseCount = Mathf.CeilToInt(period);

            // Try every start position of the grid; keep the one where the music hits hardest on the lines.
            float bestMean = 0f;
            int bestPhase = 0;
            for (int p = 0; p < phaseCount; p++)
            {
                float sum = 0f;
                int n = 0;
                for (int k = 0; ; k++)
                {
                    float position = p + k * period;
                    if (position >= searchFrames - 1) break;
                    sum += smooth[(int)(position + 0.5f)];
                    n++;
                }
                if (n == 0) continue;

                float mean = sum / n;
                if (mean > bestMean) { bestMean = mean; bestPhase = p; }
            }

            scores[i] = bestMean * Prior(bpm);
            phases[i] = bestPhase;
            scoreSum += scores[i];
        }

        // Keep the local maxima, best first. Double and half tempo often show up as separate peaks:
        // the user picks between them in the window.
        int radius = Mathf.RoundToInt(2f / BpmStep);
        var peaks = new List<int>();
        for (int i = 0; i < count; i++)
        {
            bool isPeak = scores[i] > 0f;
            int from = Mathf.Max(0, i - radius);
            int to = Mathf.Min(count - 1, i + radius);
            for (int j = from; j <= to && isPeak; j++)
            {
                if (j == i) continue;
                if (scores[j] > scores[i] || (scores[j] == scores[i] && j < i)) isPeak = false;
            }
            if (isPeak) peaks.Add(i);
        }
        peaks.Sort((a, b) => scores[b].CompareTo(scores[a]));

        var result = new List<TempoCandidate>();
        for (int i = 0; i < peaks.Count && result.Count < 4; i++)
        {
            int index = peaks[i];
            result.Add(new TempoCandidate
            {
                Bpm = MinBpm + index * BpmStep,
                OffsetSeconds = phases[index] / FramesPerSecond,
                Score = scores[index]
            });
        }

        double average = scoreSum / count;
        confidence = average > 0.0 && result.Count > 0 ? (float)(result[0].Score / average) : 0f;
        return result;
    }

    // Gentle preference for common tempos so we don't jump to double/half time on a near tie.
    private static float Prior(float bpm)
    {
        float octaves = Mathf.Log(bpm / 110f, 2f);
        return Mathf.Exp(-0.5f * octaves * octaves);
    }

    // Blur by a couple of frames so a hit that is 10-20 ms off the grid still counts.
    private static float[] Smooth(float[] input)
    {
        var output = new float[input.Length];
        for (int i = 0; i < input.Length; i++)
        {
            float sum = 0f;
            for (int d = -2; d <= 2; d++)
            {
                int j = Mathf.Clamp(i + d, 0, input.Length - 1);
                sum += input[j] * (3 - Mathf.Abs(d));
            }
            output[i] = sum / 9f;
        }
        return output;
    }

    // Scale so the loud hits sit around 1. Using the 98th percentile stops one huge spike from squashing the rest.
    private static bool NormalizeByPercentile(float[] values)
    {
        float[] sorted = (float[])values.Clone();
        Array.Sort(sorted);
        float scale = sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.98f))];
        if (scale < 1e-6f) scale = sorted[sorted.Length - 1];
        if (scale < 1e-6f) return false;

        for (int i = 0; i < values.Length; i++) values[i] = Mathf.Min(values[i] / scale, 1.5f);
        return true;
    }

    // ---- Note generation --------------------------------------------------
    /// <summary>Proposes notes from the hits in the audio, snapped to the beat grid given by bpm and offset.</summary>
    public static List<ChartNote> GenerateNotes(AudioAnalysis a, float bpm, float offsetSeconds, NoteGenSettings s)
    {
        var result = new List<ChartNote>();
        if (a == null || bpm <= 0f) return result;

        int limitFrames = Mathf.Min(a.Onset.Length, Mathf.RoundToInt(s.MaxSeconds * FramesPerSecond));
        if (limitFrames < 10) return result;

        // Threshold = average + k * spread; more sensitivity lowers k.
        double mean = 0.0;
        for (int f = 0; f < limitFrames; f++) mean += a.Onset[f];
        mean /= limitFrames;
        double variance = 0.0;
        for (int f = 0; f < limitFrames; f++) variance += (a.Onset[f] - mean) * (a.Onset[f] - mean);
        double deviation = Math.Sqrt(variance / limitFrames);
        float threshold = (float)(mean + Mathf.Lerp(2.5f, 0.3f, s.Sensitivity) * deviation);

        // Local peaks above the threshold, snapped to the grid.
        var candidates = new List<NoteCandidate>();
        for (int f = 3; f < limitFrames - 3; f++)
        {
            float v = a.Onset[f];
            if (v <= threshold) continue;

            bool isPeak = true;
            for (int d = -3; d <= 3 && isPeak; d++)
                if (d != 0 && a.Onset[f + d] > v) isPeak = false;
            if (!isPeak) continue;

            float time = f / (float)FramesPerSecond;
            float beat = (time - offsetSeconds) * bpm / 60f;
            if (beat < 0f) continue;

            float snapped = Mathf.Round(beat * s.Subdivision) / s.Subdivision;
            candidates.Add(new NoteCandidate { Beat = snapped, Strength = v, Frame = f });
        }
        candidates.Sort((x, y) => x.Beat.CompareTo(y.Beat));

        // Enforce the minimum gap: when two are too close, keep the stronger one.
        float minGap = Mathf.Max(s.MinGapBeats, 1f / s.Subdivision);
        var kept = new List<NoteCandidate>();
        foreach (NoteCandidate c in candidates)
        {
            if (kept.Count > 0 && c.Beat - kept[kept.Count - 1].Beat < minGap - 0.001f)
            {
                if (c.Strength > kept[kept.Count - 1].Strength) kept[kept.Count - 1] = c;
            }
            else
            {
                kept.Add(c);
            }
        }

        // Holds: a note followed by a long gap where the sound keeps ringing becomes a hold.
        float beatSeconds = 60f / bpm;
        float songEndBeat = (Mathf.Min(a.Duration, s.MaxSeconds) - offsetSeconds) / beatSeconds;

        for (int i = 0; i < kept.Count; i++)
        {
            float nextBeat = i + 1 < kept.Count ? kept[i + 1].Beat : songEndBeat;
            float gap = nextBeat - kept[i].Beat;
            float length = 0f;

            if (s.CreateHolds && gap >= s.MinHoldBeats + 1f) // always leave one beat to release and re-press
            {
                float longest = Mathf.Min(s.MaxHoldBeats, gap - 1f);
                float snappedLength = Mathf.Floor(longest * s.Subdivision) / s.Subdivision;
                if (snappedLength >= s.MinHoldBeats &&
                    IsSustained(a, kept[i].Frame, snappedLength * beatSeconds, s.SustainRatio))
                    length = snappedLength;
            }

            result.Add(new ChartNote { beat = kept[i].Beat, lengthBeats = length });
        }

        return result;
    }

    // Does the loudness after the hit stay high enough, compared with the hit itself, to be a held note?
    private static bool IsSustained(AudioAnalysis a, int frame, float durationSeconds, float ratio)
    {
        int start = frame + 15; // skip the first 150 ms (the attack)
        int end = Mathf.Min(a.Energy.Length - 1, frame + Mathf.RoundToInt(durationSeconds * FramesPerSecond));
        if (end - start < 10) return false;

        float peak = 0f;
        for (int f = frame; f <= Mathf.Min(frame + 8, a.Energy.Length - 1); f++) peak = Mathf.Max(peak, a.Energy[f]);
        if (peak < 1e-5f) return false;

        float sum = 0f;
        for (int f = start; f <= end; f++) sum += a.Energy[f];
        float average = sum / (end - start + 1);

        return average / peak >= ratio;
    }
}