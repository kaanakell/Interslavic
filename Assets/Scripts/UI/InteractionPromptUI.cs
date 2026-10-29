using TMPro;
using UnityEngine;

/// <summary>
/// Shows "[E] Prompt text" while the player has something in focus.
/// Put it on a Canvas child with a TextMeshPro label; assign the label and the interactor.
/// </summary>
public class InteractionPromptUI : MonoBehaviour
{
    [SerializeField] private PlayerInteractor interactor;
    [SerializeField] private GameObject root;   // the object to show/hide (panel or the label itself)
    [SerializeField] private TMP_Text label;
    [SerializeField] private string keyHint = "E";

    private void OnEnable()
    {
        interactor.FocusChanged += OnFocusChanged;
        OnFocusChanged(interactor.Current); // sync immediately in case focus already exists
    }

    private void OnDisable()
    {
        interactor.FocusChanged -= OnFocusChanged;
    }

    private void OnFocusChanged(IInteractable focus)
    {
        bool show = focus != null;
        root.SetActive(show);
        if (show) label.text = $"[{keyHint}] {focus.Prompt}";
    }
}