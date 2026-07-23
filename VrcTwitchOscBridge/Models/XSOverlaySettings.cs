using VrcTwitchOscBridge.Infrastructure;

namespace VrcTwitchOscBridge.Models;

public sealed class XSOverlaySettings : ObservableObject
{
    private bool _enableFollowNotifications = true;
    private bool _enableRaidNotifications = true;
    private bool _enableGiftSubNotifications = true;
    private float _followTimeout = 4f;
    private float _raidTimeout = 6f;
    private float _giftSubTimeout = 5f;
    private float _followDebounceWindow = 3f;
    private float _raidDebounceWindow = 3f;
    private float _giftSubDebounceWindow = 3f;

    public bool EnableFollowNotifications
    {
        get => _enableFollowNotifications;
        set => SetProperty(ref _enableFollowNotifications, value);
    }

    public bool EnableRaidNotifications
    {
        get => _enableRaidNotifications;
        set => SetProperty(ref _enableRaidNotifications, value);
    }

    public bool EnableGiftSubNotifications
    {
        get => _enableGiftSubNotifications;
        set => SetProperty(ref _enableGiftSubNotifications, value);
    }

    public float FollowTimeout
    {
        get => _followTimeout;
        set => SetProperty(ref _followTimeout, value);
    }

    public float RaidTimeout
    {
        get => _raidTimeout;
        set => SetProperty(ref _raidTimeout, value);
    }

    public float GiftSubTimeout
    {
        get => _giftSubTimeout;
        set => SetProperty(ref _giftSubTimeout, value);
    }

    public float FollowDebounceWindow
    {
        get => _followDebounceWindow;
        set => SetProperty(ref _followDebounceWindow, value);
    }

    public float RaidDebounceWindow
    {
        get => _raidDebounceWindow;
        set => SetProperty(ref _raidDebounceWindow, value);
    }

    public float GiftSubDebounceWindow
    {
        get => _giftSubDebounceWindow;
        set => SetProperty(ref _giftSubDebounceWindow, value);
    }
}
