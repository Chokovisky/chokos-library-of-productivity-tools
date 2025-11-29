namespace RadialMenu.Models;

public class RadialMenuItem
{
    /// <summary>
    /// Identifier returned to AHK / caller when this item is selected.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable label shown in the radial menu.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Optional icon/emoji for the item.
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Optional item-specific color (e.g. hex string).
    /// </summary>
    public string? Color { get; set; }
}