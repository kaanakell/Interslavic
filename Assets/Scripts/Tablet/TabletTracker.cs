using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scene-level record of which tablets the player owns.
/// Pickups call Collect(); UI and the boss door subscribe to the events,
/// so none of them need to know about each other.
/// </summary>
public class TabletTracker : MonoBehaviour
{
    [Tooltip("All tablets needed to open the final area.")]
    [SerializeField] private List<TabletDefinition> requiredTablets = new List<TabletDefinition>();

    private readonly HashSet<TabletDefinition> collected = new HashSet<TabletDefinition>();

    public event Action<TabletDefinition> TabletCollected;
    public event Action AllTabletsCollected;

    public int Count => collected.Count;
    public int Total => requiredTablets.Count;
    public bool AllCollected => collected.Count >= requiredTablets.Count;

    public bool Has(TabletDefinition tablet) => tablet != null && collected.Contains(tablet);

    /// <returns>true if it was newly collected, false if null or already owned.</returns>
    public bool Collect(TabletDefinition tablet)
    {
        if (tablet == null || !collected.Add(tablet)) return false;

        Debug.Log($"Collected tablet '{tablet.DisplayName}' ({Count}/{Total})");
        TabletCollected?.Invoke(tablet);

        if (AllCollected) AllTabletsCollected?.Invoke();
        return true;
    }
}