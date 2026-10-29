using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One listen-and-pick question. Create via Assets > Create > Interslavic > Quiz > Question.
/// If no audio clip is assigned yet, the UI shows 'spokenText' instead so you can test without recordings.
/// </summary>
[CreateAssetMenu(menuName = "Interslavic/Quiz/Question", fileName = "NewQuestion")]
public class QuestionData : ScriptableObject
{
    [SerializeField] private string instruction = "What does this mean?";
    [SerializeField] private AudioClip audioClip;
    [SerializeField, TextArea] private string spokenText;     // the Interslavic sentence (fallback + reference)
    [SerializeField] private List<string> options = new List<string>(); // 2 to 4 answers
    [SerializeField] private int correctIndex;                  // index into 'options'
    [SerializeField, TextArea] private string explanation;      // optional, shown after answering

    public string Instruction => instruction;
    public AudioClip AudioClip => audioClip;
    public string SpokenText => spokenText;
    public IReadOnlyList<string> Options => options;
    public int CorrectIndex => correctIndex;
    public string Explanation => explanation;

    private void OnValidate()
    {
        correctIndex = Mathf.Clamp(correctIndex, 0, Mathf.Max(0, options.Count - 1));
        if (options.Count > 4)
            Debug.LogWarning($"{name}: the UI only has 4 option buttons, extra options will be ignored.", this);
    }
}
