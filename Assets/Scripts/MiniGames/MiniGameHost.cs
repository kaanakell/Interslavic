using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Opens mini games as overlays and freezes the 3D world while they run.
/// Every mini game (quiz now, instrument later) goes through here, so pause/unpause logic exists once.
/// </summary>
public class MiniGameHost : MonoBehaviour
{
    [SerializeField] private QuizUI quizUI;
    [SerializeField] private RhythmGameUI rhythmUI;

    [Tooltip("Gameplay components to switch off while a mini game is open: PlayerController, PlayerInteractor, " +
             "the Cinemachine Input Axis Controller (otherwise the mouse still orbits the camera).")]
    [SerializeField] private Behaviour[] disableWhileOpen;

    public bool IsOpen { get; private set; }

    private float previousTimeScale = 1f;
    private CursorLockMode previousLockState;
    private bool previousCursorVisible;

    /// <returns>false if another mini game is already open or the data is missing.</returns>
    public bool StartQuiz(QuizData quiz, Action<bool> onFinished)
    {
        if (IsOpen || quiz == null || quizUI == null) return false;

        Enter();
        quizUI.Open(quiz, passed => StartCoroutine(Exit(passed, onFinished)));
        return true;
    }

    /// <returns>false if another mini game is already open or the data is missing.</returns>
    public bool StartRhythm(RhythmChartData chart, Action<bool> onFinished)
    {
        if (IsOpen || chart == null || rhythmUI == null) return false;

        Enter();
        rhythmUI.Open(chart, passed => StartCoroutine(Exit(passed, onFinished)));
        return true;
    }

    private void Enter()
    {
        IsOpen = true;

        previousTimeScale = Time.timeScale;
        Time.timeScale = 0f; // freezes physics, animations, anything on scaled time

        previousLockState = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        SetGameplayEnabled(false);
    }

    private IEnumerator Exit(bool passed, Action<bool> onFinished)
    {
        Time.timeScale = previousTimeScale;
        Cursor.lockState = previousLockState;
        Cursor.visible = previousCursorVisible;

        // Wait one frame before giving control back. The button press that closed the UI
        // (e.g. gamepad South = Jump) would otherwise also be read by the player this frame.
        yield return null;

        SetGameplayEnabled(true);
        IsOpen = false;

        onFinished?.Invoke(passed);
    }

    private void SetGameplayEnabled(bool value)
    {
        foreach (var behaviour in disableWhileOpen)
            if (behaviour != null) behaviour.enabled = value;
    }
}
