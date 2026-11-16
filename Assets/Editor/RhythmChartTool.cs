using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// EDITOR ONLY: keep this file in a folder named "Editor" (e.g. Assets/Scripts/Editor).

/// <summary>
/// Window for building a rhythm chart from a piece of music:
///   Interslavic > Rhythm Chart Tool                 (open the window)
///   right-click an AudioClip > Interslavic > Create Rhythm Chart From Clip   (one-click)
/// It detects the tempo, shows the beat grid over the waveform so you can check it,
/// and can generate taps and holds from the music. Everything can be adjusted by hand before it is written
/// into the RhythmChartData asset.
/// </summary>
public class RhythmChartTool : EditorWindow
{
    [SerializeField] private RhythmChartData chart;
    [SerializeField] private AudioClip clip;
    [SerializeField] private float bpm = 90f;
    [SerializeField] private float offsetSeconds;
    [SerializeField] private NoteGenSettings settings = new NoteGenSettings();
    [SerializeField] private float viewStart;
    [SerializeField] private float viewLength = 12f;

    // Not serialized: large arrays are lost when scripts recompile, then you just press Analyze again.
    private AudioAnalysis analysis;
    private readonly List<ChartNote> previewNotes = new List<ChartNote>();

    private Vector2 scroll;
    private string message;
    private MessageType messageType = MessageType.Info;

    // ---- Menu entries -----------------------------------------------------
    [MenuItem("Interslavic/Rhythm Chart Tool")]
    private static void OpenWindow() => GetWindow<RhythmChartTool>("Rhythm Chart Tool");

    [MenuItem("Assets/Interslavic/Create Rhythm Chart From Clip", true)]
    private static bool CanCreateFromClip() => Selection.activeObject is AudioClip;

    // One click: new chart asset next to the clip, tempo detected, notes generated with default settings.
    [MenuItem("Assets/Interslavic/Create Rhythm Chart From Clip")]
    private static void CreateFromClip()
    {
        var audio = (AudioClip)Selection.activeObject;
        string clipPath = AssetDatabase.GetAssetPath(audio);
        string folder = Path.GetDirectoryName(clipPath).Replace('\\', '/');
        string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{audio.name}_Chart.asset");

        var newChart = CreateInstance<RhythmChartData>();
        AssetDatabase.CreateAsset(newChart, path);

        var window = GetWindow<RhythmChartTool>("Rhythm Chart Tool");
        window.chart = newChart;
        window.clip = audio;
        window.RunAnalysis();

        if (window.analysis != null)
        {
            window.GeneratePreview();
            window.ApplyToChart(true);
        }

        Selection.activeObject = newChart;
        EditorGUIUtility.PingObject(newChart);
    }

    // ---- GUI --------------------------------------------------------------
    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        DrawSource();

        if (analysis == null)
        {
            EditorGUILayout.HelpBox(
                "Pick an audio clip and press Analyze. The analysis is not kept when scripts recompile, so press Analyze again if this message comes back.",
                MessageType.Info);
        }
        else
        {
            DrawTempo();
            DrawWaveform();
            DrawNoteGeneration();
        }

        if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, messageType);

        EditorGUILayout.EndScrollView();
    }

    private void DrawSource()
    {
        EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        chart = (RhythmChartData)EditorGUILayout.ObjectField("Chart", chart, typeof(RhythmChartData), false);
        if (EditorGUI.EndChangeCheck() && chart != null && clip == null) clip = chart.Song;

        clip = (AudioClip)EditorGUILayout.ObjectField("Audio clip", clip, typeof(AudioClip), false);

        using (new EditorGUI.DisabledScope(clip == null))
        {
            if (GUILayout.Button("Analyze audio")) RunAnalysis();
        }
    }

    private void DrawTempo()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Tempo", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Analysed length", $"{analysis.Duration:0.0} s");
        EditorGUILayout.LabelField("Confidence", $"{analysis.Confidence:0.0}x  (higher = clearer pulse; low values mean: check by eye)");

        EditorGUILayout.LabelField("Detected tempos (click to use):");
        foreach (TempoCandidate c in analysis.Candidates)
        {
            if (GUILayout.Button($"{c.Bpm:0.0} BPM    (beat at {c.OffsetSeconds:0.00} s)"))
            {
                bpm = c.Bpm;
                offsetSeconds = c.OffsetSeconds;
                previewNotes.Clear();
            }
        }

        EditorGUI.BeginChangeCheck();
        bpm = Mathf.Max(30f, EditorGUILayout.FloatField("BPM", bpm));
        offsetSeconds = EditorGUILayout.FloatField(new GUIContent("Offset (s)", "Time of beat 0 in the audio."), offsetSeconds);
        if (EditorGUI.EndChangeCheck()) previewNotes.Clear(); // generated notes depended on the old tempo

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("÷ 2")) SetTiming(bpm / 2f, offsetSeconds);
            if (GUILayout.Button("× 2")) SetTiming(bpm * 2f, offsetSeconds);
            if (GUILayout.Button("Round BPM")) SetTiming(Mathf.Round(bpm), offsetSeconds);
            if (GUILayout.Button("Offset -10 ms")) SetTiming(bpm, offsetSeconds - 0.01f);
            if (GUILayout.Button("Offset +10 ms")) SetTiming(bpm, offsetSeconds + 0.01f);
        }
    }

    private void DrawWaveform()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Waveform check", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Grey = loudness, orange = detected hits, white lines = beats (brighter = every 4th). Cyan = generated notes.", EditorStyles.miniLabel);

        viewLength = EditorGUILayout.Slider("Visible seconds", viewLength, 4f, 40f);
        float maxStart = Mathf.Max(0f, analysis.Duration - viewLength);
        viewStart = EditorGUILayout.Slider("Start (s)", Mathf.Min(viewStart, maxStart), 0f, maxStart);

        Rect rect = GUILayoutUtility.GetRect(0f, 10000f, 150f, 150f, GUILayout.ExpandWidth(true));
        if (Event.current.type != EventType.Repaint) return; // draw only when repainting; the rect above is needed on every event

        EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f));

        float[] energy = analysis.Energy;
        float[] onset = analysis.Onset;
        int fps = RhythmAudioAnalyzer.FramesPerSecond;
        int startFrame = Mathf.FloorToInt(viewStart * fps);
        int visibleFrames = Mathf.RoundToInt(viewLength * fps);
        int endFrame = Mathf.Min(energy.Length, startFrame + visibleFrames);
        if (endFrame <= startFrame) return;

        float maxEnergy = 0.0001f;
        for (int f = startFrame; f < endFrame; f++) maxEnergy = Mathf.Max(maxEnergy, energy[f]);

        // One thin bar per pixel column: the loudest frame in that slice of time.
        int columns = Mathf.Max(1, Mathf.FloorToInt(rect.width));
        float framesPerColumn = visibleFrames / (float)columns;
        for (int x = 0; x < columns; x++)
        {
            int f0 = startFrame + Mathf.FloorToInt(x * framesPerColumn);
            int f1 = Mathf.Min(endFrame, startFrame + Mathf.FloorToInt((x + 1) * framesPerColumn) + 1);
            if (f0 >= endFrame) break;

            float e = 0f, o = 0f;
            for (int f = f0; f < f1; f++)
            {
                e = Mathf.Max(e, energy[f]);
                o = Mathf.Max(o, onset[f]);
            }

            float energyHeight = e / maxEnergy * rect.height;
            EditorGUI.DrawRect(new Rect(rect.x + x, rect.yMax - energyHeight, 1f, energyHeight), new Color(0.55f, 0.55f, 0.55f));

            float onsetHeight = Mathf.Min(o, 1f) * rect.height;
            if (onsetHeight > 1f)
                EditorGUI.DrawRect(new Rect(rect.x + x, rect.yMax - onsetHeight, 1f, onsetHeight), new Color(1f, 0.55f, 0.1f, 0.6f));
        }

        // Beat grid. If the grid lines sit on the orange spikes, the tempo and offset are right.
        float beatSeconds = 60f / bpm;
        float viewEnd = viewStart + viewLength;
        int firstBeat = Mathf.Max(0, Mathf.CeilToInt((viewStart - offsetSeconds) / beatSeconds));
        for (int b = firstBeat; ; b++)
        {
            float t = offsetSeconds + b * beatSeconds;
            if (t > viewEnd) break;
            float x = rect.x + (t - viewStart) / viewLength * rect.width;
            EditorGUI.DrawRect(new Rect(x, rect.y, 1f, rect.height), new Color(1f, 1f, 1f, b % 4 == 0 ? 0.9f : 0.3f));
        }

        // Notes: the generated preview, or what is already in the chart if nothing was generated yet.
        IReadOnlyList<ChartNote> notes = previewNotes;
        Color noteColor = new Color(0.3f, 0.9f, 1f);
        if (previewNotes.Count == 0 && chart != null)
        {
            notes = chart.Notes;
            noteColor = new Color(0.5f, 1f, 0.5f);
        }

        foreach (ChartNote n in notes)
        {
            float t = offsetSeconds + n.beat * beatSeconds;
            float tEnd = t + n.lengthBeats * beatSeconds;
            if (tEnd < viewStart || t > viewEnd) continue;

            float x0 = rect.x + (Mathf.Max(t, viewStart) - viewStart) / viewLength * rect.width;
            float x1 = rect.x + (Mathf.Min(tEnd, viewEnd) - viewStart) / viewLength * rect.width;
            EditorGUI.DrawRect(new Rect(x0 - 3f, rect.y + 2f, 7f, 7f), noteColor);
            if (n.lengthBeats > 0f)
                EditorGUI.DrawRect(new Rect(x0, rect.y + 4f, Mathf.Max(1f, x1 - x0), 3f), noteColor);
        }
    }

    private void DrawNoteGeneration()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Notes", EditorStyles.boldLabel);

        settings.Subdivision = EditorGUILayout.IntSlider(
            new GUIContent("Grid (steps per beat)", "1 = on the beat, 2 = also half beats, 4 = sixteenths."),
            settings.Subdivision, 1, 4);
        settings.Sensitivity = EditorGUILayout.Slider("Sensitivity", settings.Sensitivity, 0f, 1f);
        settings.MinGapBeats = Mathf.Max(0.25f, EditorGUILayout.FloatField("Min gap (beats)", settings.MinGapBeats));
        settings.CreateHolds = EditorGUILayout.Toggle("Create holds", settings.CreateHolds);

        if (settings.CreateHolds)
        {
            EditorGUI.indentLevel++;
            settings.MinHoldBeats = Mathf.Max(0.5f, EditorGUILayout.FloatField("Min hold (beats)", settings.MinHoldBeats));
            settings.MaxHoldBeats = Mathf.Max(settings.MinHoldBeats, EditorGUILayout.FloatField("Max hold (beats)", settings.MaxHoldBeats));
            settings.SustainRatio = EditorGUILayout.Slider(
                new GUIContent("Sustain needed", "How much of the note's loudness must remain for it to count as a hold. Lower = more holds."),
                settings.SustainRatio, 0.1f, 1f);
            EditorGUI.indentLevel--;
        }

        settings.MaxSeconds = Mathf.Max(5f, EditorGUILayout.FloatField("Max song length (s)", settings.MaxSeconds));
        if (analysis.Duration > settings.MaxSeconds + 1f)
        {
            EditorGUILayout.HelpBox(
                $"The clip is {analysis.Duration:0} s. Notes are only generated for the first {settings.MaxSeconds:0} s, " +
                "but the game plays the whole clip, so trim the audio to match.",
                MessageType.Warning);
        }

        if (GUILayout.Button("Generate preview")) GeneratePreview();

        if (previewNotes.Count > 0)
        {
            int holds = 0;
            foreach (ChartNote n in previewNotes) if (n.lengthBeats > 0f) holds++;
            EditorGUILayout.LabelField("Preview", $"{previewNotes.Count} notes ({holds} holds)");
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("Apply tempo only to chart"))
        {
            if (EnsureChart()) ApplyToChart(false);
        }
        if (GUILayout.Button("Apply tempo + notes to chart"))
        {
            if (EnsureChart())
            {
                if (previewNotes.Count == 0) GeneratePreview();
                ApplyToChart(true);
            }
        }
    }

    // ---- Actions ----------------------------------------------------------
    private void RunAnalysis()
    {
        message = null;
        previewNotes.Clear();

        AudioAnalysis result = null;
        string error = null;
        bool ok = false;
        try
        {
            ok = RhythmAudioAnalyzer.TryAnalyze(clip, out result, out error);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (!ok)
        {
            analysis = null;
            message = error;
            messageType = MessageType.Error;
            return;
        }

        analysis = result;
        bpm = analysis.Candidates[0].Bpm;
        offsetSeconds = analysis.Candidates[0].OffsetSeconds;
        viewStart = 0f;

        message = $"Best guess: {bpm:0.0} BPM. Check it in the waveform below: the white lines should sit on the orange spikes.";
        messageType = MessageType.Info;
    }

    private void SetTiming(float newBpm, float newOffset)
    {
        bpm = Mathf.Max(30f, newBpm);
        offsetSeconds = newOffset;
        previewNotes.Clear();
    }

    private void GeneratePreview()
    {
        previewNotes.Clear();
        previewNotes.AddRange(RhythmAudioAnalyzer.GenerateNotes(analysis, bpm, offsetSeconds, settings));

        if (previewNotes.Count == 0)
        {
            message = "No notes found. Raise Sensitivity, lower Min gap, or check the tempo.";
            messageType = MessageType.Warning;
        }
        else
        {
            int holds = 0;
            foreach (ChartNote n in previewNotes) if (n.lengthBeats > 0f) holds++;
            message = $"Generated {previewNotes.Count} notes ({holds} holds). Nothing is written to the chart until you press Apply.";
            messageType = MessageType.Info;
        }
    }

    private bool EnsureChart()
    {
        if (chart != null) return true;

        string path = EditorUtility.SaveFilePanelInProject(
            "Create rhythm chart", (clip != null ? clip.name : "NewChart") + "_Chart", "asset", "Choose where to save the new chart.");
        if (string.IsNullOrEmpty(path)) return false;

        chart = CreateInstance<RhythmChartData>();
        AssetDatabase.CreateAsset(chart, path);
        return true;
    }

    // Writes through SerializedObject (the same path the Inspector uses), so Undo works.
    private void ApplyToChart(bool includeNotes)
    {
        var so = new SerializedObject(chart);
        so.FindProperty("song").objectReferenceValue = clip;
        so.FindProperty("bpm").floatValue = bpm;
        so.FindProperty("offsetSeconds").floatValue = offsetSeconds;

        if (includeNotes)
        {
            SerializedProperty notes = so.FindProperty("notes");
            notes.arraySize = previewNotes.Count;
            for (int i = 0; i < previewNotes.Count; i++)
            {
                SerializedProperty element = notes.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("beat").floatValue = previewNotes[i].beat;
                element.FindPropertyRelative("lengthBeats").floatValue = previewNotes[i].lengthBeats;
            }
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(chart);
        AssetDatabase.SaveAssets();

        message = includeNotes
            ? $"Chart '{chart.name}' updated: {bpm:0.0} BPM, offset {offsetSeconds:0.00} s, {previewNotes.Count} notes."
            : $"Chart '{chart.name}' updated: {bpm:0.0} BPM, offset {offsetSeconds:0.00} s (notes unchanged).";
        messageType = MessageType.Info;
    }
}