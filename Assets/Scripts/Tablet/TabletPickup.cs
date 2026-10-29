using UnityEngine;

/// <summary>
/// A tablet lying in the world. Needs a Box/Sphere collider (can be a trigger)
/// on this object or a child, on a layer included in the interactor's mask.
/// Later, dungeon puzzles will gate or spawn this instead of leaving it freely takeable.
/// </summary>
public class TabletPickup : MonoBehaviour, IInteractable
{
    [SerializeField] private TabletTracker tracker;
    [SerializeField] private TabletDefinition tablet;
    [SerializeField] private string prompt = "Take tablet";

    public string Prompt => prompt;
    public bool CanInteract => tablet != null && tracker != null && !tracker.Has(tablet);

    public void Interact(PlayerInteractor interactor)
    {
        if (tracker.Collect(tablet))
            gameObject.SetActive(false); // temporary "pickup" feedback; SFX and VFX come later
    }
}