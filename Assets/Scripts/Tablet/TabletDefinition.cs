using UnityEngine;

/// <summary>
/// Static data describing one tablet (name, text, icon). Create assets via
/// Assets > Create > Interslavic > Tablet Definition. No runtime state lives here:
/// whether it is collected is tracked by TabletTracker.
/// </summary>
[CreateAssetMenu(menuName = "Interslavic/Tablet Definition", fileName = "NewTablet")]
public class TabletDefinition : ScriptableObject
{
    [SerializeField] private string displayName = "Tablet";
    [SerializeField, TextArea] private string description;
    [SerializeField] private Sprite icon;

    public string DisplayName => displayName;
    public string Description => description;
    public Sprite Icon => icon;
}