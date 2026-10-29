using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A world object that starts a quiz when the player presses E.
/// On pass it can award a tablet and/or fire UnityEvents (open a door, disable a trap...).
/// For now, drop one in the graybox scene to test; dungeons will just wire the events.
/// </summary>
public class QuizInteractable : MonoBehaviour, IInteractable
{
    [SerializeField] private MiniGameHost host;
    [SerializeField] private QuizData quiz;
    [SerializeField] private string prompt = "Start quiz";
    [SerializeField] private bool repeatable = false;

    [Header("Reward (optional)")]
    [SerializeField] private TabletTracker tracker;
    [SerializeField] private TabletDefinition rewardTablet;

    [Header("Events")]
    [SerializeField] private UnityEvent onPassed;
    [SerializeField] private UnityEvent onFailed;

    private bool completed;

    public string Prompt => prompt;
    public bool CanInteract => host != null && quiz != null && (repeatable || !completed);

    public void Interact(PlayerInteractor interactor)
    {
        host.StartQuiz(quiz, OnQuizFinished);
    }

    private void OnQuizFinished(bool passed)
    {
        if (passed)
        {
            completed = true;
            if (tracker != null && rewardTablet != null) tracker.Collect(rewardTablet);
            onPassed.Invoke();
        }
        else
        {
            onFailed.Invoke();
        }
    }
}
