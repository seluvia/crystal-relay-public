using System;
using System.Text.Json.Serialization;
using VrcTwitchOscBridge.Infrastructure;

namespace VrcTwitchOscBridge.Models;

public sealed class InventoryItemSpawnRule : ObservableObject, IJsonOnDeserialized
{
    private string id = Guid.NewGuid().ToString();
    private string inventoryItemId = string.Empty;
    private string itemName = string.Empty;
    private string itemImageUrl = string.Empty;
    private string itemType = string.Empty;
    private string? rewardId;
    private string rewardTitle = string.Empty;
    private string rewardDescription = string.Empty;
    private int rewardCost = 100;
    private TwitchRewardSyncMode syncMode = TwitchRewardSyncMode.CreateOrManage;
    private bool isEnabled = true;
    private int cooldownSeconds;
    private string rewardVersionFingerprint = string.Empty;

    public string Id
    {
        get => id;
        set => SetProperty(ref id, value ?? Guid.NewGuid().ToString());
    }

    public string InventoryItemId
    {
        get => inventoryItemId;
        set => SetProperty(ref inventoryItemId, value ?? string.Empty);
    }

    public string ItemName
    {
        get => itemName;
        set => SetProperty(ref itemName, value ?? string.Empty);
    }

    public string ItemImageUrl
    {
        get => itemImageUrl;
        set => SetProperty(ref itemImageUrl, value ?? string.Empty);
    }

    public string ItemType
    {
        get => itemType;
        set => SetProperty(ref itemType, value ?? string.Empty);
    }

    public string? RewardId
    {
        get => rewardId;
        set => SetProperty(ref rewardId, value);
    }

    public string RewardTitle
    {
        get => rewardTitle;
        set => SetProperty(ref rewardTitle, value ?? string.Empty);
    }

    public string RewardDescription
    {
        get => rewardDescription;
        set => SetProperty(ref rewardDescription, value ?? string.Empty);
    }

    public int RewardCost
    {
        get => rewardCost;
        set => SetProperty(ref rewardCost, Math.Max(1, value));
    }

    public TwitchRewardSyncMode SyncMode
    {
        get => syncMode;
        set => SetProperty(ref syncMode, value);
    }

    public bool IsEnabled
    {
        get => isEnabled;
        set => SetProperty(ref isEnabled, value);
    }

    public int CooldownSeconds
    {
        get => cooldownSeconds;
        set => SetProperty(ref cooldownSeconds, Math.Max(0, value));
    }

    public string RewardVersionFingerprint
    {
        get => rewardVersionFingerprint;
        set => SetProperty(ref rewardVersionFingerprint, value ?? string.Empty);
    }

    public string DisplayTitle => !string.IsNullOrWhiteSpace(rewardTitle) ? rewardTitle : itemName;

    public string? SyncStatusBadge => syncMode switch
    {
        TwitchRewardSyncMode.CreateOrManage => string.IsNullOrWhiteSpace(rewardId) ? "Not Created" : "Created",
        TwitchRewardSyncMode.LinkExisting => string.IsNullOrWhiteSpace(rewardId) ? "Not Linked" : "Linked",
        _ => null
    };

    public void OnDeserialized() { }
}
