using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Duolingo-style overlay: pick an answer, press Check, see feedback, press Continue.
/// Lives on an always-active Canvas object; 'root' is the part that gets shown/hidden.
/// All game rules live in QuizSession, this class only displays state and forwards clicks.
/// </summary>
public class QuizUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject root;

    [Header("Header")]
    [SerializeField] private TMP_Text titleLabel;
    [SerializeField] private Slider progressBar;
    [SerializeField] private TMP_Text heartsLabel;

    [Header("Question")]
    [SerializeField] private TMP_Text instructionLabel;
    [SerializeField] private Button listenButton;
    [SerializeField] private TMP_Text spokenTextLabel;   // only shown when the question has no audio yet
    [SerializeField] private AudioSource audioSource;

    [Header("Options (up to 4)")]
    [SerializeField] private Button[] optionButtons;

    [Header("Check / Feedback")]
    [SerializeField] private Button checkButton;
    [SerializeField] private GameObject feedbackPanel;
    [SerializeField] private Image feedbackBackground;
    [SerializeField] private TMP_Text feedbackLabel;
    [SerializeField] private Button continueButton;

    [Header("Result")]
    [SerializeField] private GameObject resultPanel;     // full-size panel drawn on top of everything
    [SerializeField] private TMP_Text resultLabel;
    [SerializeField] private Button finishButton;

    [Header("Colors")]
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color selectedColor = new Color(0.75f, 0.88f, 1f);
    [SerializeField] private Color correctColor = new Color(0.62f, 0.9f, 0.62f);
    [SerializeField] private Color wrongColor = new Color(0.95f, 0.55f, 0.55f);

    private QuizSession session;
    private Action<bool> onFinished;
    private TMP_Text[] optionLabels;
    private int selectedIndex = -1;

    private void Awake()
    {
        // Cache each button's text once.
        optionLabels = new TMP_Text[optionButtons.Length];
        for (int i = 0; i < optionButtons.Length; i++)
        {
            int index = i; // copy, so each lambda captures its own value
            optionLabels[i] = optionButtons[i].GetComponentInChildren<TMP_Text>(true);
            optionButtons[i].onClick.AddListener(() => OnOptionClicked(index));
        }

        checkButton.onClick.AddListener(OnCheck);
        continueButton.onClick.AddListener(OnContinue);
        finishButton.onClick.AddListener(OnFinish);
        listenButton.onClick.AddListener(PlayAudio);

        root.SetActive(false);
    }

    // ---- Public entry point ----------------------------------------------
    public void Open(QuizData data, Action<bool> finishedCallback)
    {
        onFinished = finishedCallback;
        session = new QuizSession(data);

        if (titleLabel != null) titleLabel.text = data.Title;
        root.SetActive(true);

        if (session.State == QuizState.Answering) ShowQuestion();
        else ShowResult();
    }

    // ---- Screens ----------------------------------------------------------
    private void ShowQuestion()
    {
        QuestionData q = session.Current;
        selectedIndex = -1;

        resultPanel.SetActive(false);
        feedbackPanel.SetActive(false);
        checkButton.gameObject.SetActive(true);
        checkButton.interactable = false;

        instructionLabel.text = q.Instruction;

        bool hasAudio = q.AudioClip != null;
        listenButton.interactable = hasAudio;
        spokenTextLabel.gameObject.SetActive(!hasAudio); // fallback for testing without recordings
        spokenTextLabel.text = q.SpokenText;

        var options = session.Options;
        for (int i = 0; i < optionButtons.Length; i++)
        {
            bool used = i < options.Count;
            optionButtons[i].gameObject.SetActive(used);
            if (!used) continue;

            optionLabels[i].text = options[i];
            optionButtons[i].interactable = true;
            optionButtons[i].image.color = normalColor;
        }

        UpdateHeader();
        Focus(optionButtons[0].gameObject); // keyboard/gamepad navigation starts here
        if (hasAudio) PlayAudio();
    }

    private void ShowResult()
    {
        feedbackPanel.SetActive(false);
        resultPanel.SetActive(true);
        UpdateHeader();

        bool passed = session.State == QuizState.Passed;
        resultLabel.text = passed
            ? $"Well done!\nMistakes: {session.Mistakes}"
            : "Out of hearts. Try again!";

        Focus(finishButton.gameObject);
    }

    private void UpdateHeader()
    {
        if (progressBar != null) progressBar.value = session.Progress;
        if (heartsLabel != null)
        {
            heartsLabel.gameObject.SetActive(session.UsesHearts);
            heartsLabel.text = $"Hearts: {session.HeartsLeft}";
        }
    }

    // ---- Button handlers --------------------------------------------------
    private void OnOptionClicked(int index)
    {
        if (session.State != QuizState.Answering) return;

        selectedIndex = index;
        for (int i = 0; i < optionButtons.Length; i++)
            if (optionButtons[i].gameObject.activeSelf)
                optionButtons[i].image.color = i == selectedIndex ? selectedColor : normalColor;

        checkButton.interactable = true;
    }

    private void OnCheck()
    {
        if (selectedIndex < 0 || session.State != QuizState.Answering) return;

        bool correct = session.Submit(selectedIndex);

        // Lock the options and color them: chosen one green/red, correct one always green.
        for (int i = 0; i < optionButtons.Length; i++)
            optionButtons[i].interactable = false;

        optionButtons[session.CorrectOptionIndex].image.color = correctColor;
        if (!correct) optionButtons[selectedIndex].image.color = wrongColor;

        feedbackPanel.SetActive(true);
        feedbackBackground.color = correct ? correctColor : wrongColor;

        string text = correct ? "Correct!" : $"Correct answer:\n{session.Options[session.CorrectOptionIndex]}";
        if (!string.IsNullOrEmpty(session.Current.Explanation))
            text += $"\n{session.Current.Explanation}";
        feedbackLabel.text = text;

        checkButton.gameObject.SetActive(false);
        UpdateHeader();
        Focus(continueButton.gameObject);
    }

    private void OnContinue()
    {
        session.Next();

        if (session.State == QuizState.Answering) ShowQuestion();
        else ShowResult();
    }

    private void OnFinish()
    {
        bool passed = session.State == QuizState.Passed;
        root.SetActive(false);

        Action<bool> callback = onFinished;
        onFinished = null;
        callback?.Invoke(passed);
    }

    // ---- Helpers ----------------------------------------------------------
    private void PlayAudio()
    {
        if (session?.Current == null || session.Current.AudioClip == null) return;
        audioSource.Stop();
        audioSource.clip = session.Current.AudioClip;
        audioSource.Play(); // audio keeps playing while Time.timeScale = 0
    }

    private static void Focus(GameObject target)
    {
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(target);
    }
}
