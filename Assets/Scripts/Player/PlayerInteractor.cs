using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Finds the best IInteractable near the player each frame and triggers it on the Interact button.
/// "Best" = closest one inside a cone in front of the character.
/// Put this on the Player root. The UI listens to FocusChanged to show/hide the prompt.
/// </summary>
public class PlayerInteractor : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private InputActionReference interactAction;

    [Header("Detection")]
    [SerializeField] private Transform facing;                       // the Visuals child (it is what turns)
    [SerializeField] private float range = 2f;
    [SerializeField, Range(10f, 360f)] private float viewAngle = 120f; // total cone width in degrees
    [SerializeField] private LayerMask interactableMask = ~0;         // set to an "Interactable" layer later

    // Reused buffer so we don't allocate garbage every frame.
    private readonly Collider[] hits = new Collider[16];

    /// <summary>The interactable currently in focus, or null.</summary>
    public IInteractable Current { get; private set; }

    /// <summary>Fires only when the focus actually changes (null = nothing in focus).</summary>
    public event Action<IInteractable> FocusChanged;

    private void OnEnable()
    {
        if (interactAction != null) interactAction.action.Enable();
    }

    private void OnDisable()
    {
        // Disabling this component (mini game open, dialogue...) clears the prompt.
        SetCurrent(null);
    }

    private void Update()
    {
        SetCurrent(FindBest());

        if (Current != null && interactAction != null && interactAction.action.WasPressedThisFrame())
            Current.Interact(this);
    }

    private IInteractable FindBest()
    {
        Vector3 forward = facing != null ? facing.forward : transform.forward;
        Vector3 origin = transform.position;

        int count = Physics.OverlapSphereNonAlloc(origin, range, hits, interactableMask, QueryTriggerInteraction.Collide);

        IInteractable best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            Collider col = hits[i];

            // The collider may sit on a child; walk up to find the script that implements the interface.
            IInteractable candidate = col.GetComponentInParent<IInteractable>();
            if (candidate == null || !candidate.CanInteract) continue;

            // Flat vector from player to the nearest point of that collider.
            // (ClosestPoint needs Box/Sphere/Capsule/convex colliders, so use those on interactables.)
            Vector3 toTarget = col.ClosestPoint(origin) - origin;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;

            // Inside the cone? Objects we're basically touching always count.
            if (distance > 0.1f && Vector3.Angle(forward, toTarget) > viewAngle * 0.5f) continue;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }

    private void SetCurrent(IInteractable next)
    {
        if (ReferenceEquals(next, Current)) return;
        Current = next;
        FocusChanged?.Invoke(Current);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, range);
    }
#endif
}