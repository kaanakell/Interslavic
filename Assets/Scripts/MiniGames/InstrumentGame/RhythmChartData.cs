using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One note on the string. Measured in beats (not seconds) so changing the BPM keeps the pattern intact.
/// </summary>
[Serializable]
public struct ChartNote
{
    [Tooltip("Position in beats from the start of the song (1 = one quarter note).")]
    public float beat;

    [Tooltip("0 = tap. Above 0 = hold for this many beats (like drawing the bow across the string).")]
    [Min(0f)] public float lengthBeats;
}

/// <summary>
/// A playable song for the gusle: audio, tempo, note pattern and judging rules.
/// Create via Assets > Create > Interslavic > Rhythm > Chart, then use the asset's context menu
/// (three dots in the Inspector header) > "Generate Test Pattern" to fill it with notes.
/// </summary>
[CreateAssetMenu(menuName = "Interslavic/Rhythm/Chart", fileName = "NewChart")]
public class RhythmChartData : ScriptableObject
{
    [SerializeField] private string title = "Melody";
    [Tooltip("Optional for now. Without a clip the game runs silently on the same clock.")]
    [SerializeField] private AudioClip song;
    [SerializeField, Min(30f)] private float bpm = 90f;
    [Tooltip("Seconds between the start of the audio and beat 0 (if your recording has silence at the start).")]
    [SerializeField] private float offsetSeconds = 0f;
    [Tooltip("Countdown before the song starts.")]
    [SerializeField, Min(0f)] private float leadInSeconds = 2f;
    [SerializeField] private List<ChartNote> notes = new List<ChartNote>();

    [Header("Judging")]
    [SerializeField, Range(0.02f, 0.2f)] private float perfectWindow = 0.06f;  // +/- seconds
    [SerializeField, Range(0.05f, 0.4f)] private float goodWindow = 0.12f;     // +/- seconds
    [Tooltip("Holds: releasing up to this many seconds before the note ends still counts as completed.")]
    [SerializeField, Range(0f, 0.6f)] private float holdForgiveness = 0.25f;
    [Tooltip("Share of notes that must be hit (Perfect = 1, Good = 0.5) to pass.")]
    [SerializeField, Range(0f, 1f)] private float passAccuracy = 0.6f;

    [Header("Test pattern generator")]
    [SerializeField, Min(5f)] private float testDurationSeconds = 50f;
    [SerializeField] private int testSeed = 12345;

    public string Title => title;
    public AudioClip Song => song;
    public float Bpm => bpm;
    public float LeadInSeconds => leadInSeconds;
    public IReadOnlyList<ChartNote> Notes => notes;
    public float PerfectWindow => perfectWindow;
    public float GoodWindow => Mathf.Max(goodWindow, perfectWindow);
    public float HoldForgiveness => holdForgiveness;
    public float PassAccuracy => passAccuracy;

    /// <summary>Beat position to seconds on the song clock.</summary>
    public float BeatToSeconds(float beat) => offsetSeconds + beat * 60f / bpm;

    /// <summary>A length in beats to a length in seconds (no offset, because it's a duration).</summary>
    public float BeatsToDuration(float beats) => beats * 60f / bpm;

    // Hand-authoring dozens of notes in the Inspector is painful, so this makes a playable pattern:
    // single taps, pairs of quick taps, and holds of 1 to 2 beats, always with breathing room after a hold.
    [ContextMenu("Generate Test Pattern")]
    private void GenerateTestPattern()
    {
        notes.Clear();
        var rng = new System.Random(testSeed);
        float totalBeats = testDurationSeconds * bpm / 60f;
        float beat = 2f;

        while (beat < totalBeats - 2f)
        {
            double roll = rng.NextDouble();

            if (roll < 0.25)
            {
                // Hold, then one empty beat to release and get ready.
                float length = rng.Next(0, 2) == 0 ? 1f : 2f;
                notes.Add(new ChartNote { beat = beat, lengthBeats = length });
                beat += length + 1f;
            }
            else if (roll < 0.5)
            {
                // Two quick taps.
                notes.Add(new ChartNote { beat = beat, lengthBeats = 0f });
                notes.Add(new ChartNote { beat = beat + 0.5f, lengthBeats = 0f });
                beat += 1.5f;
            }
            else
            {
                notes.Add(new ChartNote { beat = beat, lengthBeats = 0f });
                beat += 1f;
            }
        }

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
}