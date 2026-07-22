using System.Collections.Generic;

namespace VrcTwitchOscBridge.Models;

public sealed record InventoryItemSummary(
    string Id,
    string Name,
    string ImageUrl,
    string Description,
    string ItemType,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Flags
);
