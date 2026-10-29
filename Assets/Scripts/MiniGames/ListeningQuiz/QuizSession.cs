using System.Collections.Generic;
using UnityEngine;

public enum QuizState { Answering, Feedback, Passed, Failed }

/// <summary>
/// Pure quiz logic: no UI, no MonoBehaviour. The UI just asks it what to show and tells it what the player did.
/// Duolingo rule: a wrong question goes to the back of the queue and must be answered correctly later.
/// </summary>
public class QuizSession
{
    private readonly QuizData data;
    private readonly Queue<QuestionData> queue = new Queue<QuestionData>();
    private readonly List<string> shuffledOptions = new List<string>();
    private int correctOption;
    private int correctAnswers;

    public QuizState State { get; private set; }
    public QuestionData Current { get; private set; }

    /// <summary>Options in the (possibly shuffled) order the UI should display them.</summary>
    public IReadOnlyList<string> Options => shuffledOptions;
    public int CorrectOptionIndex => correctOption;

    public int Mistakes { get; private set; }
    public int TotalQuestions { get; private set; }
    public bool LastAnswerCorrect { get; private set; }

    public bool UsesHearts => data.MaxMistakes > 0;
    public int HeartsLeft => UsesHearts ? Mathf.Max(0, data.MaxMistakes - Mistakes) : -1;

    /// <summary>0..1, counts questions answered correctly at least once.</summary>
    public float Progress => TotalQuestions == 0 ? 1f : (float)correctAnswers / TotalQuestions;

    public QuizSession(QuizData data)
    {
        this.data = data;

        var list = new List<QuestionData>();
        foreach (var q in data.Questions)
            if (q != null) list.Add(q);

        if (data.ShuffleQuestions) Shuffle(list);
        foreach (var q in list) queue.Enqueue(q);
        TotalQuestions = list.Count;

        if (TotalQuestions == 0)
        {
            Debug.LogWarning($"Quiz '{data.name}' has no questions, counting it as passed.");
            State = QuizState.Passed;
            return;
        }

        LoadNext();
    }

    /// <summary>Player chose an option and pressed Check. Returns whether it was correct.</summary>
    public bool Submit(int optionIndex)
    {
        if (State != QuizState.Answering) return false;

        LastAnswerCorrect = optionIndex == correctOption;
        if (LastAnswerCorrect)
        {
            correctAnswers++;
        }
        else
        {
            Mistakes++;
            queue.Enqueue(Current); // must be answered correctly later
        }

        State = QuizState.Feedback;
        return LastAnswerCorrect;
    }

    /// <summary>Player pressed Continue after seeing the feedback.</summary>
    public void Next()
    {
        if (State != QuizState.Feedback) return;

        if (UsesHearts && Mistakes >= data.MaxMistakes) { State = QuizState.Failed; return; }
        if (queue.Count == 0) { State = QuizState.Passed; return; }

        LoadNext();
    }

    private void LoadNext()
    {
        Current = queue.Dequeue();

        // Build a display order. We shuffle indices (not strings) so we can track where the correct one lands.
        var order = new List<int>();
        for (int i = 0; i < Current.Options.Count; i++) order.Add(i);
        if (data.ShuffleOptions) Shuffle(order);

        shuffledOptions.Clear();
        for (int i = 0; i < order.Count; i++)
        {
            shuffledOptions.Add(Current.Options[order[i]]);
            if (order[i] == Current.CorrectIndex) correctOption = i;
        }

        State = QuizState.Answering;
    }

    private static void Shuffle<T>(IList<T> list) // Fisher-Yates
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
