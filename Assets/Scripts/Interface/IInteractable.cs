/// <summary>
/// Anything the player can press "E" on: tablets, doors, NPCs, instruments...
/// The player's detector only knows this interface, never the concrete types.
/// </summary>
public interface IInteractable
{
    /// <summary>Text shown in the prompt, e.g. "Pick up tablet" or "Play the gusle".</summary>
    string Prompt { get; }

    /// <summary>Return false to hide this object from the detector (already used, locked, etc.).</summary>
    bool CanInteract { get; }

    /// <summary>Called once when the player presses Interact while this is the focused object.</summary>
    void Interact(PlayerInteractor interactor);
}