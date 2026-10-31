using System;
using System.Collections.Generic;

public enum Judgement { None, Perfect, Good, Miss }

/// <summary>Runtime state of one note in a play-through.</summary>
public class RhythmNote
{
    public readonly float Time;       // when the note starts, in seconds on the song clock
    public readonly float Duration;   // 0 for a tap, otherwise how long to hold

    public bool IsHold => Duration > 0f;
    public float EndTime => Time + Duration;

    public bool Holding;              // hold note: pressed on time, key currently down
    public bool Broken;               // hold note: released too early
    public bool Resolved;
    public Judgement HeadResult;      // how well the press itself was timed
    public Judgement Result;          // final outcome

    public RhythmNote(float time, float duration) { Time = time; Duration = duration; }
}

/// <summary>
/// Pure rhythm logic for a single string, no UI and no clock of its own: the UI reports "pressed at song time T",
/// "released at song time T" and "song time is now T". Keeping time as a parameter makes this deterministic.
///
/// Tap:  one press within the timing window.
/// Hold: the press is judged like a tap (Perfect/Good) and the note then stays "Holding". It completes when the
///       key is released within HoldForgiveness of the end (or is still down at the end). Releasing earlier breaks it.
/// </summary>
public class RhythmSession
{
    private const int HoldBonus = 50;

    private readonly RhythmChartData chart;
    private readonly List<RhythmNote> notes = new List<RhythmNote>();
    private int cursor;                 // first possibly-unresolved note
    private int resolvedCount;
    private RhythmNote holding;         // the hold note currently being played (single string = at most one)

    public IReadOnlyList<RhythmNote> Notes => notes;   // sorted by start time
    public int Perfect { get; private set; }
    public int Good { get; private set; }
    public int Misses { get; private set; }
    public int Combo { get; private set; }
    public int MaxCombo { get; private set; }
    public int Score { get; private set; }

    public bool IsFinished => resolvedCount >= notes.Count;
    public float Accuracy => notes.Count == 0 ? 1f : (Perfect + 0.5f * Good) / notes.Count;
    public bool Passed => Accuracy >= chart.PassAccuracy;

    /// <summary>When the last note is fully over (a hold counts until its end).</summary>
    public float LastEndTime
    {
        get
        {
            float last = 0f;
            foreach (RhythmNote n in notes) last = Math.Max(last, n.EndTime);
            return last;
        }
    }

    /// <summary>A hold note was pressed on time and is now being held. Fires with the press judgement.</summary>
    public event Action<RhythmNote, Judgement> NoteStarted;

    /// <summary>A note reached its final result (taps on press, holds on completion or break, anything on miss).</summary>
    public event Action<RhythmNote, Judgement> NoteResolved;

    public RhythmSession(RhythmChartData chart)
    {
        this.chart = chart;

        foreach (ChartNote n in chart.Notes)
        {
            float duration = n.lengthBeats > 0f ? chart.BeatsToDuration(n.lengthBeats) : 0f;
            notes.Add(new RhythmNote(chart.BeatToSeconds(n.beat), duration));
        }

        notes.Sort((a, b) => a.Time.CompareTo(b.Time));
    }

    /// <summary>The key went down. Presses far from any note are ignored, so mashing costs nothing.</summary>
    public Judgement Hit(float songTime)
    {
        if (holding != null) return Judgement.None;

        RhythmNote note = NextUnresolved();
        if (note == null) return Judgement.None;

        float delta = Math.Abs(songTime - note.Time);
        if (delta > chart.GoodWindow) return Judgement.None; // too early (late ones are caught by Update)

        Judgement head = delta <= chart.PerfectWindow ? Judgement.Perfect : Judgement.Good;

        if (note.IsHold)
        {
            note.Holding = true;
            note.HeadResult = head;
            holding = note;
            NoteStarted?.Invoke(note, head);
        }
        else
        {
            Resolve(note, head);
        }

        return head;
    }

    /// <summary>The key went up. Only matters while a hold note is being played.</summary>
    public void Release(float songTime)
    {
        if (holding == null) return;

        RhythmNote note = holding;
        if (songTime >= note.EndTime - chart.HoldForgiveness)
        {
            Complete(note);
        }
        else
        {
            note.Broken = true;
            note.Holding = false;
            holding = null;
            Resolve(note, Judgement.Miss);
        }
    }

    /// <summary>Call every frame: finishes holds that reached their end and turns skipped notes into misses.</summary>
    public void Update(float songTime)
    {
        // Still holding when the note ends: completed.
        if (holding != null && songTime >= holding.EndTime) Complete(holding);

        // A note that was never pressed and has slipped past its window is a miss.
        RhythmNote note;
        while ((note = NextUnresolved()) != null && !note.Holding && songTime - note.Time > chart.GoodWindow)
            Resolve(note, Judgement.Miss);
    }

    private void Complete(RhythmNote note)
    {
        note.Holding = false;
        holding = null;
        Resolve(note, note.HeadResult);
    }

    private RhythmNote NextUnresolved()
    {
        while (cursor < notes.Count && notes[cursor].Resolved) cursor++;
        return cursor < notes.Count ? notes[cursor] : null;
    }

    private void Resolve(RhythmNote note, Judgement result)
    {
        note.Resolved = true;
        note.Result = result;
        resolvedCount++;

        switch (result)
        {
            case Judgement.Perfect: Perfect++; Score += 100; Combo++; break;
            case Judgement.Good:    Good++;    Score += 50;  Combo++; break;
            case Judgement.Miss:    Misses++;  Combo = 0;            break;
        }

        if (note.IsHold && result != Judgement.Miss) Score += HoldBonus;
        MaxCombo = Math.Max(MaxCombo, Combo);

        NoteResolved?.Invoke(note, result);
    }
}