using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class AvatarSwapCardViewModel : ObservableObject, IDisposable
{
    private readonly AvatarImageService _imageService;
    private CancellationTokenSource? _imageCts;
    private ImageSource? image;
    private bool isUpdatingThumbnail;

    public AvatarSwapCardViewModel(AvatarSwapProfile profile, AvatarImageService imageService)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _imageService = imageService ?? throw new ArgumentNullException(nameof(imageService));
        Profile.PropertyChanged += OnProfilePropertyChanged;
        TriggerImageLoad(Profile.TargetThumbnailUrl);
    }

    public AvatarSwapProfile Profile { get; }

    public ImageSource? Image
    {
        get => image;
        private set
        {
            if (SetProperty(ref image, value))
            {
                RaisePropertyChanged(nameof(HasImage));
            }
        }
    }

    public bool HasImage => image is not null;

    public string DisplayTitle => string.IsNullOrWhiteSpace(Profile.TargetAvatarName) ? "Pick Avatar" : Profile.TargetAvatarName;

    public string AvatarSubtitle => Profile.AvatarSubtitle;

    public string StatusText => Profile.IsEnabled ? "On" : "Off";

    public string RuleCountText
    {
        get
        {
            var cp = Profile.ChannelPointRules?.Count ?? 0;
            var bits = Profile.BitsRules?.Count ?? 0;
            var subs = Profile.SubsRules?.Count ?? 0;
            var pay = Profile.PaymentRules?.Count ?? 0;
            return (cp + bits + subs + pay).ToString();
        }
    }

    public bool HasTarget => !string.IsNullOrWhiteSpace(Profile.TargetAvatarId);

    public bool IsEnabled => Profile.IsEnabled;

    public bool HasRules => Profile.HasRules;

    public bool UsesChannelPointRules => Profile.UsesChannelPointRules;

    public bool UsesBitsRules => Profile.UsesBitsRules;

    public bool UsesSubsRules => Profile.UsesSubsRules;

    public bool UsesPaymentRules => Profile.UsesPaymentRules;

    public bool HasAnyRules => UsesChannelPointRules || UsesBitsRules || UsesSubsRules || UsesPaymentRules;

    public System.Windows.Media.Brush StatusStripeBrush => Profile.IsEnabled
        ? System.Windows.Media.Brushes.MediumSeaGreen
        : System.Windows.Media.Brushes.Gray;

    public void SetThumbnailUrl(string? thumbnailUrl)
    {
        if (isUpdatingThumbnail)
        {
            return;
        }
        isUpdatingThumbnail = true;
        try
        {
            Profile.TargetThumbnailUrl = thumbnailUrl;
        }
        finally
        {
            isUpdatingThumbnail = false;
        }
        TriggerImageLoad(thumbnailUrl);
    }

    public void Dispose()
    {
        _imageCts?.Cancel();
        _imageCts?.Dispose();
        _imageCts = null;
        Profile.PropertyChanged -= OnProfilePropertyChanged;
    }

    private void TriggerImageLoad(string? thumbnailUrl)
    {
        _imageCts?.Cancel();
        _imageCts?.Dispose();
        _imageCts = new CancellationTokenSource();
        var avatarId = Profile.TargetAvatarId;
        var ct = _imageCts.Token;

        var syncImage = _imageService.GetAvatarImage(avatarId, null, thumbnailUrl);
        if (syncImage is not null && !ct.IsCancellationRequested)
        {
            Application.Current?.Dispatcher.InvokeAsync(() => Image = syncImage);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var asyncImage = await _imageService.GetAvatarImageAsync(avatarId, null, thumbnailUrl, ct);
                if (asyncImage is not null && !ct.IsCancellationRequested)
                {
                    Application.Current?.Dispatcher.InvokeAsync(() => Image = asyncImage);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                if (!ct.IsCancellationRequested)
                {
                    var placeholder = _imageService.GetPlaceholderImage();
                    Application.Current?.Dispatcher.InvokeAsync(() => Image = placeholder);
                }
            }
        }, ct);
    }

    private void OnProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AvatarSwapProfile.TargetAvatarId):
            case nameof(AvatarSwapProfile.TargetThumbnailUrl):
                RaisePropertyChanged(nameof(HasTarget));
                TriggerImageLoad(Profile.TargetThumbnailUrl);
                break;
            case nameof(AvatarSwapProfile.TargetAvatarName):
                RaisePropertyChanged(nameof(DisplayTitle));
                break;
            case nameof(AvatarSwapProfile.IsEnabled):
                RaisePropertyChanged(nameof(IsEnabled));
                RaisePropertyChanged(nameof(StatusText));
                RaisePropertyChanged(nameof(StatusStripeBrush));
                break;
            // AvatarSwapProfile.Bump() raises HasRules/Uses*/AvatarSubtitle on every rule
            // collection change (it does not raise the collection property names), so we
            // forward those to refresh the card's count badge and subtitle live.
            case nameof(AvatarSwapProfile.HasRules):
            case nameof(AvatarSwapProfile.UsesChannelPointRules):
            case nameof(AvatarSwapProfile.UsesBitsRules):
            case nameof(AvatarSwapProfile.UsesSubsRules):
            case nameof(AvatarSwapProfile.UsesPaymentRules):
            case nameof(AvatarSwapProfile.AvatarSubtitle):
                RaisePropertyChanged(nameof(AvatarSubtitle));
                RaisePropertyChanged(nameof(RuleCountText));
                RaisePropertyChanged(nameof(HasRules));
                RaisePropertyChanged(nameof(HasAnyRules));
                RaisePropertyChanged(nameof(UsesChannelPointRules));
                RaisePropertyChanged(nameof(UsesBitsRules));
                RaisePropertyChanged(nameof(UsesSubsRules));
                RaisePropertyChanged(nameof(UsesPaymentRules));
                break;
        }
    }
}
