using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A world object (the gusle, a ritual stone...) that starts the rhythm mini game on E.
/// Same shape as QuizInteractable: optional tablet reward plus UnityEvents for doors and traps.
/// Once a third mini game exists, the shared parts are worth moving into a base class.
/// </summary>
public class RhythmInteractable : MonoBehaviour, IInteractable
{
    [SerializeField] private MiniGameHost host;
    [SerializeField] private RhythmChartData chart;
    [SerializeField] private string prompt = "Play the gusle";
    [SerializeField] private bool repeatable = false;

    [Header("Reward (optional)")]
    [SerializeField] private TabletTracker tracker;
    [SerializeField] private TabletDefinition rewardTablet;

    [Header("Events")]
    [SerializeField] private UnityEvent onPassed;
    [SerializeField] private UnityEvent onFailed;

    private bool completed;

    public string Prompt => prompt;
    public bool CanInteract => host != null && chart != null && (repeatable || !completed);

    public void Interact(PlayerInteractor interactor)
    {
        host.StartRhythm(chart, OnFinished);
    }

    private void OnFinished(bool passed)
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
