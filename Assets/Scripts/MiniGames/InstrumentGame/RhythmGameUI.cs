using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Single-string rhythm overlay (the gusle). Notes fall down a vertical string toward a hit line.
///
/// Look:  a TAP is a short block. A HOLD is the same block with a thinner tail rising above it.
/// Feedback: a hit note vanishes with a flash on the hit line. A missed note (or what is left of a
///           broken hold) turns grey and keeps falling off the bottom, so the player can see what they missed.
///
/// Timing: everything is driven by AudioSettings.dspTime (the audio hardware clock). It ignores
/// Time.timeScale, so it works while the 3D world is paused, and it stays in sync with the music.
/// Note positions are a pure function of song time (no per-frame accumulation), so they can't drift.
/// The timing point is the CENTER of the HitLine image: a note is "on time" when its block is centered on it.
/// </summary>
public class RhythmGameUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject root;

    [Header("HUD")]
    [SerializeField] private TMP_Text titleLabel;
    [SerializeField] private TMP_Text scoreLabel;
    [SerializeField] private TMP_Text comboLabel;
    [SerializeField] private TMP_Text countdownLabel;
    [SerializeField] private TMP_Text judgementLabel;
    [SerializeField] private Slider progressBar;

    [Header("Playfield")]
    [Tooltip("The vertical string. Notes are children of this. Add a RectMask2D to it so notes are clipped at the edges.")]
    [SerializeField] private RectTransform stringLane;
    [Tooltip("Optional: an Image on the string that lights up while the key is down.")]
    [SerializeField] private Image stringHighlight;
    [Tooltip("UI Image prefab for a note head. Keep its anchors as a single point (the default); its width/height set the " +
             "note size. Keep it short (about 30-40 high) so quick notes don't merge into each other.")]
    [SerializeField] private RectTransform notePrefab;
    [Tooltip("Assign the HitLine image. Notes arrive exactly at its center, so the visual line and the timing can never disagree.")]
    [SerializeField] private RectTransform hitLine;
    [Tooltip("Fallback: distance above the bottom of the string, only used when no Hit Line is assigned.")]
    [SerializeField] private float hitLineY = 140f;
    [Tooltip("Seconds a note takes to fall from the top to the hit line.")]
    [SerializeField] private float fallTimeSeconds = 2f;

    [Header("Input")]
    [Tooltip("One button: e.g. Space and gamepad South. Hold it for long notes.")]
    [SerializeField] private InputActionReference playAction;
    [Tooltip("Seconds of input latency to compensate. Increase if you feel you must press early.")]
    [SerializeField] private float inputOffsetSeconds = 0f;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;

    [Header("Buttons")]
    [SerializeField] private Button quitButton;

    [Header("Result")]
    [SerializeField] private GameObject resultPanel;
    [SerializeField] private TMP_Text resultLabel;
    [SerializeField] private Button finishButton;

    [Header("Colors")]
    [SerializeField] private Color perfectColor = new Color(1f, 0.9f, 0.3f);
    [SerializeField] private Color goodColor = new Color(0.5f, 0.9f, 0.5f);
    [SerializeField] private Color missColor = new Color(0.95f, 0.4f, 0.4f);
    [SerializeField] private Color holdingColor = new Color(1f, 0.85f, 0.4f);

    [Header("Note look")]
    [Tooltip("Colour of the tail of a hold note (the head uses the prefab's colour).")]
    [SerializeField] private Color tailColor = new Color(0.45f, 0.65f, 1f, 0.9f);
    [Tooltip("Tail width as a share of the head width.")]
    [SerializeField, Range(0.2f, 0.8f)] private float tailWidthRatio = 0.4f;
    [Tooltip("Colour of missed notes while they fall away.")]
    [SerializeField] private Color missedNoteColor = new Color(0.5f, 0.5f, 0.5f, 0.4f);

    // One falling note: the head (from the prefab) plus a child tail that is only shown for holds.
    private class NoteView
    {
        public RectTransform Head;
        public Image HeadImage;
        public RectTransform Tail;
        public Image TailImage;
    }

    // A note that was missed or broken: kept on screen, greyed out, until it falls off the bottom.
    private class FadingNote
    {
        public NoteView View;
        public float HeadTime;   // song time the head corresponds to (it keeps falling like any other note)
        public float Duration;   // length of what is left of the tail, in seconds
    }

    private const float HitFlashDuration = 0.2f;

    private RhythmChartData chart;
    private RhythmSession session;
    private Action<bool> onFinished;

    private double songStartDsp;            // dspTime at which song time = 0
    private bool running;
    private float pixelsPerSecond;
    private float hitY;                     // resolved hit position, measured up from the bottom of the string
    private float endTime;
    private int spawnIndex;
    private float judgementTimer;

    private float noteWidth = 100f;
    private float noteHeight = 36f;
    private Color defaultNoteColor = Color.white;

    private Image hitLineImage;
    private Color hitLineBaseColor = Color.white;
    private float hitFlashTimer;
    private Color hitFlashColor;

    private readonly Dictionary<RhythmNote, NoteView> activeNotes = new Dictionary<RhythmNote, NoteView>();
    private readonly List<FadingNote> fadingNotes = new List<FadingNote>();
    private readonly Stack<NoteView> pool = new Stack<NoteView>();

    private float SongTime => (float)(AudioSettings.dspTime - songStartDsp);

    private void Awake()
    {
        quitButton.onClick.AddListener(OnQuit);
        finishButton.onClick.AddListener(OnFinish);

        // The quit button must not be reachable with gamepad/keyboard navigation while playing,
        // otherwise the play button (e.g. South = UI Submit) could trigger it by accident. Mouse only.
        quitButton.navigation = new Navigation { mode = Navigation.Mode.None };

        if (notePrefab != null)
        {
            noteWidth = notePrefab.sizeDelta.x;
            noteHeight = notePrefab.sizeDelta.y;
            Image img = notePrefab.GetComponent<Image>();
            if (img != null) defaultNoteColor = img.color;
        }

        if (hitLine != null)
        {
            hitLineImage = hitLine.GetComponent<Image>();
            if (hitLineImage != null) hitLineBaseColor = hitLineImage.color;
        }

        root.SetActive(false);
    }

    // ---- Public entry point ----------------------------------------------
    public void Open(RhythmChartData data, Action<bool> finishedCallback)
    {
        if (stringLane == null || playAction == null || notePrefab == null)
        {
            Debug.LogError("RhythmGameUI: assign String Lane, Note Prefab and Play Action in the Inspector.", this);
            finishedCallback?.Invoke(false);
            return;
        }

        chart = data;
        onFinished = finishedCallback;
        session = new RhythmSession(chart);
        session.NoteStarted += OnNoteStarted;
        session.NoteResolved += OnNoteResolved;

        root.SetActive(true);
        resultPanel.SetActive(false);
        judgementLabel.gameObject.SetActive(false);
        if (titleLabel != null) titleLabel.text = chart.Title;

        // Measure the string after layout so the fall speed adapts to any resolution.
        Canvas.ForceUpdateCanvases();

        // Where is the hit line? Convert its position into the string's local space and measure up from the
        // string's bottom edge. This works wherever the HitLine sits in the hierarchy and however it is anchored.
        hitY = hitLine != null
            ? stringLane.InverseTransformPoint(hitLine.position).y - stringLane.rect.yMin
            : hitLineY;

        float travel = Mathf.Max(100f, stringLane.rect.height - hitY);
        pixelsPerSecond = travel / fallTimeSeconds;

        spawnIndex = 0;
        ClearNotes();
        ResetHitLine();
        playAction.action.Enable();

        // Don't leave any UI element selected: the play button must not also press a UI button.
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

        endTime = session.LastEndTime + 1.5f;
        songStartDsp = AudioSettings.dspTime + chart.LeadInSeconds;
        if (chart.Song != null)
        {
            audioSource.clip = chart.Song;
            audioSource.PlayScheduled(songStartDsp); // sample-accurate start on the same clock
        }

        running = true;
        UpdateHud(SongTime);
    }

    // ---- Per-frame loop ---------------------------------------------------
    private void Update()
    {
        if (!running) return;

        float songTime = SongTime;
        float inputTime = songTime - inputOffsetSeconds;
        InputAction play = playAction.action;

        // 1) Input: a press starts a note, a release ends a hold.
        if (play.WasPressedThisFrame()) session.Hit(inputTime);
        if (play.WasReleasedThisFrame()) session.Release(inputTime);

        // 2) Logic: finish holds that ran to the end, turn skipped notes into misses.
        session.Update(songTime);

        // 3) Visuals.
        SpawnNotes(songTime);
        MoveNotes(songTime);
        UpdateHud(songTime);
        DecayFeedback(play.IsPressed());

        // 4) End.
        if (session.IsFinished && songTime >= endTime) EndSong();
    }

    // ---- Notes ------------------------------------------------------------
    private void SpawnNotes(float songTime)
    {
        var notes = session.Notes;
        while (spawnIndex < notes.Count && notes[spawnIndex].Time - songTime <= fallTimeSeconds)
        {
            RhythmNote note = notes[spawnIndex++];
            if (note.Resolved) continue;
            activeNotes.Add(note, Acquire());
        }
    }

    private void MoveNotes(float songTime)
    {
        foreach (var pair in activeNotes)
        {
            RhythmNote note = pair.Key;

            if (note.Holding)
            {
                // While played, the head stays on the line and the tail drains away as the bow is drawn.
                Place(pair.Value, songTime, note.EndTime - songTime, songTime);
            }
            else
            {
                Place(pair.Value, note.Time, note.Duration, songTime);
            }
        }

        // Missed notes keep falling the same way, then get recycled once fully below the string.
        for (int i = fadingNotes.Count - 1; i >= 0; i--)
        {
            FadingNote f = fadingNotes[i];
            Place(f.View, f.HeadTime, f.Duration, songTime);

            float headY = hitY + (f.HeadTime - songTime) * pixelsPerSecond;
            float topY = headY + Mathf.Max(f.Duration * pixelsPerSecond, noteHeight * 0.5f);
            if (topY < 0f)
            {
                Release(f.View);
                fadingNotes.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Positions a note. 'headTime' is the song time the head belongs to; it is on the hit line
    /// when headTime == songTime and above it before that. The tail rises from the top of the head.
    /// </summary>
    private void Place(NoteView view, float headTime, float durationSeconds, float songTime)
    {
        float headY = hitY + (headTime - songTime) * pixelsPerSecond;
        view.Head.anchoredPosition = new Vector2(0f, headY);

        // The note ends at headY + duration * speed. The tail starts at the head's top edge,
        // so its length is shortened by half a head to make the very top land on the end time.
        float tailLength = durationSeconds * pixelsPerSecond - noteHeight * 0.5f;
        bool hasTail = tailLength > 2f;

        if (view.Tail.gameObject.activeSelf != hasTail) view.Tail.gameObject.SetActive(hasTail);
        if (hasTail) view.Tail.sizeDelta = new Vector2(noteWidth * tailWidthRatio, tailLength);
    }

    private NoteView Acquire()
    {
        NoteView view = pool.Count > 0 ? pool.Pop() : CreateView();
        view.Head.gameObject.SetActive(true);
        view.Tail.gameObject.SetActive(false);
        view.HeadImage.color = defaultNoteColor;
        view.TailImage.color = tailColor;
        return view;
    }

    private NoteView CreateView()
    {
        var view = new NoteView();

        // Head: the prefab, centered on its position and sized from the prefab.
        view.Head = Instantiate(notePrefab, stringLane, false);
        view.Head.anchorMin = view.Head.anchorMax = new Vector2(0.5f, 0f); // bottom-center of the string
        view.Head.pivot = new Vector2(0.5f, 0.5f);
        view.Head.sizeDelta = new Vector2(noteWidth, noteHeight);
        view.HeadImage = view.Head.GetComponent<Image>();
        if (view.HeadImage != null) view.HeadImage.raycastTarget = false;

        // Tail: a child anchored to the top-center of the head, growing upward.
        var tailObject = new GameObject("Tail", typeof(RectTransform), typeof(Image));
        view.Tail = tailObject.GetComponent<RectTransform>();
        view.Tail.SetParent(view.Head, false);
        view.Tail.anchorMin = view.Tail.anchorMax = new Vector2(0.5f, 1f);
        view.Tail.pivot = new Vector2(0.5f, 0f);
        view.Tail.anchoredPosition = Vector2.zero;
        view.TailImage = tailObject.GetComponent<Image>();
        view.TailImage.raycastTarget = false;

        return view;
    }

    private void Release(NoteView view)
    {
        view.Head.gameObject.SetActive(false);
        pool.Push(view);
    }

    private void ClearNotes()
    {
        foreach (var pair in activeNotes) Release(pair.Value);
        activeNotes.Clear();

        foreach (var fading in fadingNotes) Release(fading.View);
        fadingNotes.Clear();
    }

    private void OnNoteStarted(RhythmNote note, Judgement head)
    {
        // A hold was pressed on time: show how well, flash the line, and light the note up.
        ShowJudgement(head);
        FlashHitLine(head);

        if (activeNotes.TryGetValue(note, out NoteView view))
        {
            view.HeadImage.color = holdingColor;
            view.TailImage.color = holdingColor;
        }
    }

    private void OnNoteResolved(RhythmNote note, Judgement result)
    {
        if (activeNotes.TryGetValue(note, out NoteView view))
        {
            activeNotes.Remove(note);

            if (result == Judgement.Miss)
            {
                // Grey it out and let it keep falling. For a broken hold, only the part not yet played remains.
                float now = SongTime;
                view.HeadImage.color = missedNoteColor;
                view.TailImage.color = missedNoteColor;

                fadingNotes.Add(new FadingNote
                {
                    View = view,
                    HeadTime = note.Broken ? now : note.Time,
                    Duration = note.Broken ? Mathf.Max(0f, note.EndTime - now) : note.Duration
                });
            }
            else
            {
                Release(view); // hit: it vanishes
            }
        }

        if (result != Judgement.Miss) FlashHitLine(result);

        // A completed hold already showed its judgement when it started.
        if (note.IsHold && result != Judgement.Miss) return;

        ShowJudgement(result, note.Broken ? "TOO SHORT" : null);
    }

    // ---- HUD & feedback ---------------------------------------------------
    private void UpdateHud(float songTime)
    {
        scoreLabel.text = $"Score: {session.Score}";
        comboLabel.text = session.Combo > 1 ? $"Combo x{session.Combo}" : string.Empty;
        if (progressBar != null) progressBar.value = Mathf.Clamp01(songTime / endTime);

        bool counting = songTime < 0f;
        countdownLabel.gameObject.SetActive(counting);
        if (counting) countdownLabel.text = Mathf.CeilToInt(-songTime).ToString();
    }

    private void ShowJudgement(Judgement result, string textOverride = null)
    {
        judgementLabel.gameObject.SetActive(true);
        judgementTimer = 0.4f;

        switch (result)
        {
            case Judgement.Perfect: judgementLabel.text = "PERFECT"; judgementLabel.color = perfectColor; break;
            case Judgement.Good: judgementLabel.text = "GOOD"; judgementLabel.color = goodColor; break;
            default: judgementLabel.text = "MISS"; judgementLabel.color = missColor; break;
        }

        if (textOverride != null) judgementLabel.text = textOverride;
    }

    private void FlashHitLine(Judgement result)
    {
        hitFlashColor = result == Judgement.Perfect ? perfectColor : goodColor;
        hitFlashTimer = HitFlashDuration;
    }

    private void ResetHitLine()
    {
        hitFlashTimer = 0f;
        if (hitLineImage != null) hitLineImage.color = hitLineBaseColor;
        if (hitLine != null) hitLine.localScale = Vector3.one;
    }

    private void DecayFeedback(bool keyDown)
    {
        // unscaledDeltaTime: Time.timeScale is 0 while a mini game is open.
        float dt = Time.unscaledDeltaTime;

        if (judgementLabel.gameObject.activeSelf)
        {
            judgementTimer -= dt;
            if (judgementTimer <= 0f) judgementLabel.gameObject.SetActive(false);
        }

        if (stringHighlight != null)
        {
            // Lit while the key is down, fades out after release.
            Color c = stringHighlight.color;
            c.a = keyDown ? 0.5f : Mathf.MoveTowards(c.a, 0f, 3f * dt);
            stringHighlight.color = c;
        }

        if (hitFlashTimer > 0f && hitLine != null)
        {
            // Flash: colour and a small scale pop that settle back to normal. The last step lands exactly on 0.
            hitFlashTimer -= dt;
            float t = Mathf.Clamp01(hitFlashTimer / HitFlashDuration);
            if (hitLineImage != null) hitLineImage.color = Color.Lerp(hitLineBaseColor, hitFlashColor, t);
            hitLine.localScale = Vector3.one * (1f + 0.2f * t);
        }
    }

    // ---- Ending -----------------------------------------------------------
    private void EndSong()
    {
        running = false;
        audioSource.Stop();
        ClearNotes();
        ResetHitLine();
        playAction.action.Disable();

        bool passed = session.Passed;
        resultPanel.SetActive(true);
        resultLabel.text =
            $"{(passed ? "Melody complete!" : "Out of tune. Try again!")}\n" +
            $"Accuracy: {session.Accuracy:P0}\n" +
            $"Perfect: {session.Perfect}   Good: {session.Good}   Missed: {session.Misses}\n" +
            $"Best combo: {session.MaxCombo}";

        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(finishButton.gameObject);
    }

    private void OnFinish() => Close(session.Passed);

    private void OnQuit()
    {
        running = false;
        audioSource.Stop();
        Close(false);
    }

    private void Close(bool passed)
    {
        session.NoteStarted -= OnNoteStarted;
        session.NoteResolved -= OnNoteResolved;
        playAction.action.Disable();
        ClearNotes();
        ResetHitLine();
        root.SetActive(false);

        Action<bool> callback = onFinished;
        onFinished = null;
        callback?.Invoke(passed);
    }
}