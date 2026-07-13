namespace VrcTwitchOscBridge.Models;

/// <summary>
/// One entry in the picker's group or tag filter dropdown.
/// Id is null for "All", "ungrouped" for the Ungrouped pseudo-group,
/// or a real AvatarGroup/AvatarTag Id.
/// </summary>
public sealed record FilterOption(string? Id, string Display);
