using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A set of questions plus the rules for running them.
/// Create via Assets > Create > Interslavic > Quiz > Quiz.
/// </summary>
[CreateAssetMenu(menuName = "Interslavic/Quiz/Quiz", fileName = "NewQuiz")]
public class QuizData : ScriptableObject
{
    [SerializeField] private string title = "Quiz";
    [SerializeField] private List<QuestionData> questions = new List<QuestionData>();
    [SerializeField] private bool shuffleQuestions = true;
    [SerializeField] private bool shuffleOptions = true;

    [Tooltip("0 = unlimited retries. Above 0 = the quiz fails once this many mistakes are made (hearts).")]
    [SerializeField, Min(0)] private int maxMistakes = 0;

    public string Title => title;
    public IReadOnlyList<QuestionData> Questions => questions;
    public bool ShuffleQuestions => shuffleQuestions;
    public bool ShuffleOptions => shuffleOptions;
    public int MaxMistakes => maxMistakes;
}
