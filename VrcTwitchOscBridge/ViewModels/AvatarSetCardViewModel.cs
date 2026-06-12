using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class AvatarSetCardViewModel : ObservableObject, IDisposable
{
    private readonly AvatarImageService _imageService;
    private CancellationTokenSource? _imageCts;
    private ImageSource? _image;

    public AvatarTriggerProfile Profile { get; }

    public AvatarSetCardViewModel(AvatarTriggerProfile profile, AvatarImageService imageService)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _imageService = imageService ?? throw new ArgumentNullException(nameof(imageService));
        Profile.PropertyChanged += OnProfilePropertyChanged;

        // Load initial image
        TriggerImageLoad();
    }

    public string DisplayTitle => !string.IsNullOrWhiteSpace(Profile.Name)
        ? Profile.Name
        : !string.IsNullOrWhiteSpace(Profile.AvatarName)
            ? Profile.AvatarName
            : "New Set";

    public string AvatarSubtitle =>
        !string.IsNullOrWhiteSpace(Profile.AvatarName)
            ? Profile.AvatarName
            : !string.IsNullOrWhiteSpace(Profile.AvatarId)
                ? Profile.AvatarId
                : "(no avatar picked)";

    public bool IsEnabled
    {
        get => Profile.IsEnabled;
        set
        {
            if (Profile.IsEnabled == value) return;
            Profile.IsEnabled = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(IsDisabled));
            RaisePropertyChanged(nameof(StatusPillText));
            RaisePropertyChanged(nameof(StatusStripeBrush));
            RaisePropertyChanged(nameof(LiveText));
            RaisePropertyChanged(nameof(LiveTextBrush));
            RaisePropertyChanged(nameof(CanTest));
            RaisePropertyChanged(nameof(IsTestDisabled));
        }
    }
    public bool IsDisabled => !Profile.IsEnabled;
    public bool IsMaster => Profile.IsMasterProfile;
    public bool IsLive => Profile.IsCurrentAvatarActive;

    public bool HasAvatar => !string.IsNullOrWhiteSpace(Profile.AvatarId);

    public bool IsWardrobeMode => Profile.UseWardrobeMode;

    public int RedeemCount => Profile.ChannelPointRules?.Count ?? 0;

    public int OutfitCount => Profile.WardrobeOutfits?.Count ?? 0;

    public bool HasAnyRules => IsWardrobeMode ? OutfitCount > 0 : RedeemCount > 0;

    public string CountPillText
    {
        get
        {
            if (IsWardrobeMode)
            {
                var count = OutfitCount;
                if (count == 0) return TF("Avatar Sets Card Count Zero");
                return TF("Avatar Sets Card Count Outfits Format", count);
            }
            else
            {
                var count = RedeemCount;
                if (count == 0) return TF("Avatar Sets Card Count Zero");
                return TF("Avatar Sets Card Count Redeems Format", count);
            }
        }
    }

    public string ModePillText => IsWardrobeMode
        ? TF("Avatar Sets Mode Wardrobe")
        : TF("Avatar Sets Mode Standard");

    public string StatusPillText
    {
        get
        {
            if (IsDisabled) return TF("Avatar Sets Card Disabled");
            if (!HasAvatar) return TF("Avatar Sets Card Setup Needed");
            return TF("Avatar Sets Card Ready");
        }
    }

    public string LiveText
    {
        get
        {
            if (IsLive) return TF("Avatar Sets Card Live");
            if (IsDisabled) return TF("Avatar Sets Card Off");
            if (!HasAvatar) return TF("Avatar Sets Card Pick Avatar Hint");
            return TF("Avatar Sets Card Waiting");
        }
    }

    public Brush StatusStripeBrush
    {
        get
        {
            var app = Application.Current;
            if (IsLive)
            {
                var key = "StatusStripeReadyBrush";
                return app?.TryFindResource(key) as Brush ?? Brushes.LimeGreen;
            }
            if (IsDisabled)
            {
                var key = "StatusStripeOffBrush";
                return app?.TryFindResource(key) as Brush ?? Brushes.Gray;
            }
            if (!HasAvatar)
            {
                var key = "StatusStripeWarnBrush";
                return app?.TryFindResource(key) as Brush ?? Brushes.Goldenrod;
            }
            var readyKey = "StatusStripeReadyBrush";
            return app?.TryFindResource(readyKey) as Brush ?? Brushes.LimeGreen;
        }
    }

    public Brush ModePillBrush
    {
        get
        {
            var app = Application.Current;
            if (IsWardrobeMode)
            {
                var key = "ModePillWardrobeBrush";
                return app?.TryFindResource(key) as Brush ?? new SolidColorBrush(Color.FromRgb(236, 72, 153)); // pink
            }
            var key2 = "ModePillStandardBrush";
            return app?.TryFindResource(key2) as Brush ?? new SolidColorBrush(Color.FromRgb(99, 102, 241)); // indigo
        }
    }

    public Brush LiveTextBrush
    {
        get
        {
            var app = Application.Current;
            if (IsLive)
            {
                var key = "LiveTextReadyBrush";
                return app?.TryFindResource(key) as Brush ?? Brushes.LimeGreen;
            }
            if (IsDisabled)
            {
                var key = "LiveTextOffBrush";
                return app?.TryFindResource(key) as Brush ?? Brushes.Gray;
            }
            if (!HasAvatar)
            {
                var key = "LiveTextWarnBrush";
                return app?.TryFindResource(key) as Brush ?? Brushes.Goldenrod;
            }
            var key2 = "LiveTextReadyBrush";
            return app?.TryFindResource(key2) as Brush ?? Brushes.LimeGreen;
        }
    }

    public bool CanTest => IsWardrobeMode && OutfitCount > 0;

    public bool IsTestDisabled => !CanTest;

    public ImageSource? Image
    {
        get => _image;
        private set => SetProperty(ref _image, value);
    }

    public ICommand? OpenEditorCommand { get; set; }

    public ICommand? TestCommand { get; set; }

    public void SetThumbnailUrl(string? thumbnailUrl)
    {
        _pendingThumbnailUrl = thumbnailUrl;
        TriggerImageLoad(thumbnailUrl);
    }

    private string? _pendingThumbnailUrl;

    private void TriggerImageLoad(string? thumbnailUrl = null)
    {
        // Cancel any pending load for a previous avatar
        _imageCts?.Cancel();
        _imageCts?.Dispose();
        _imageCts = new CancellationTokenSource();

        var avatarId = Profile.AvatarId;
        var ct = _imageCts.Token;

        // Use sync path for custom icons only; async VRChat thumbnail loading happens below
        string? customIconPath = null; // AvatarTriggerProfile has no custom icon path field
        var syncImage = _imageService.GetAvatarImage(avatarId, customIconPath, thumbnailUrl);
        if (syncImage != null && !ct.IsCancellationRequested)
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                Image = syncImage;
            });
            return;
        }

        // Kick off async thumbnail load
        System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                var asyncImage = await _imageService.GetAvatarImageAsync(avatarId, customIconPath, thumbnailUrl, ct);
                if (asyncImage != null && !ct.IsCancellationRequested)
                {
                    Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        Image = asyncImage;
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when avatar changes
            }
            catch
            {
                // Fall back to placeholder if load fails
                if (!ct.IsCancellationRequested)
                {
                    var placeholder = _imageService.GetPlaceholderImage();
                    Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        Image = placeholder;
                    });
                }
            }
        }, ct);
    }

    private void OnProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AvatarTriggerProfile.AvatarId):
                // Re-fetch image on avatar change, using the latest pending thumbnail URL
                TriggerImageLoad(_pendingThumbnailUrl);
                RaiseAllPropertiesChanged();
                break;
            case nameof(AvatarTriggerProfile.Name):
                RaisePropertyChanged(nameof(DisplayTitle));
                break;
            case nameof(AvatarTriggerProfile.AvatarName):
                RaisePropertyChanged(nameof(AvatarSubtitle));
                RaisePropertyChanged(nameof(DisplayTitle));
                break;
            case nameof(AvatarTriggerProfile.IsEnabled):
                RaisePropertyChanged(nameof(IsEnabled));
                RaisePropertyChanged(nameof(IsDisabled));
                RaisePropertyChanged(nameof(StatusPillText));
                RaisePropertyChanged(nameof(LiveText));
                RaisePropertyChanged(nameof(StatusStripeBrush));
                RaisePropertyChanged(nameof(LiveTextBrush));
                break;
            case nameof(AvatarTriggerProfile.IsCurrentAvatarActive):
                RaisePropertyChanged(nameof(IsLive));
                RaisePropertyChanged(nameof(LiveText));
                RaisePropertyChanged(nameof(StatusStripeBrush));
                RaisePropertyChanged(nameof(LiveTextBrush));
                break;
            case nameof(AvatarTriggerProfile.UseWardrobeMode):
                RaisePropertyChanged(nameof(IsWardrobeMode));
                RaisePropertyChanged(nameof(ModePillText));
                RaisePropertyChanged(nameof(ModePillBrush));
                RaisePropertyChanged(nameof(CountPillText));
                RaisePropertyChanged(nameof(HasAnyRules));
                RaisePropertyChanged(nameof(CanTest));
                RaisePropertyChanged(nameof(IsTestDisabled));
                break;
            case nameof(AvatarTriggerProfile.IsMasterProfile):
                RaisePropertyChanged(nameof(IsMaster));
                RaisePropertyChanged(nameof(DisplayTitle));
                break;
            case nameof(AvatarTriggerProfile.ChannelPointRules):
                if (!IsWardrobeMode)
                {
                    RaisePropertyChanged(nameof(RedeemCount));
                    RaisePropertyChanged(nameof(HasAnyRules));
                    RaisePropertyChanged(nameof(CountPillText));
                }
                break;
            case nameof(AvatarTriggerProfile.WardrobeOutfits):
                if (IsWardrobeMode)
                {
                    RaisePropertyChanged(nameof(OutfitCount));
                    RaisePropertyChanged(nameof(HasAnyRules));
                    RaisePropertyChanged(nameof(CountPillText));
                    RaisePropertyChanged(nameof(CanTest));
                    RaisePropertyChanged(nameof(IsTestDisabled));
                }
                break;
        }
    }

    private void RaiseAllPropertiesChanged()
    {
        RaisePropertyChanged(nameof(DisplayTitle));
        RaisePropertyChanged(nameof(AvatarSubtitle));
        RaisePropertyChanged(nameof(IsEnabled));
        RaisePropertyChanged(nameof(IsDisabled));
        RaisePropertyChanged(nameof(IsMaster));
        RaisePropertyChanged(nameof(IsLive));
        RaisePropertyChanged(nameof(HasAvatar));
        RaisePropertyChanged(nameof(IsWardrobeMode));
        RaisePropertyChanged(nameof(RedeemCount));
        RaisePropertyChanged(nameof(OutfitCount));
        RaisePropertyChanged(nameof(HasAnyRules));
        RaisePropertyChanged(nameof(CountPillText));
        RaisePropertyChanged(nameof(ModePillText));
        RaisePropertyChanged(nameof(StatusPillText));
        RaisePropertyChanged(nameof(LiveText));
        RaisePropertyChanged(nameof(StatusStripeBrush));
        RaisePropertyChanged(nameof(ModePillBrush));
        RaisePropertyChanged(nameof(LiveTextBrush));
        RaisePropertyChanged(nameof(CanTest));
        RaisePropertyChanged(nameof(IsTestDisabled));
    }

    private static string TF(string key, params object[] args) =>
        LocalizationService.Format(key, args);

    public void Dispose()
    {
        _imageCts?.Cancel();
        _imageCts?.Dispose();
        Profile.PropertyChanged -= OnProfilePropertyChanged;
    }
}