using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge;

public partial class VrChatLoginWindow : Window
{
    private readonly VrChatApiClient apiClient;
    private AppTheme currentTheme;
    private VrChatApiClient.VrChatLoginResponse? loginResponse;
    private bool isTwoFactorMode;
    private bool isProcessing;

    public VrChatLoginWindow(AppTheme theme, VrChatApiClient apiClient, string? initialUsername = null)
    {
        this.apiClient = apiClient;
        InitializeComponent();
        currentTheme = theme;
        ThemeManager.ApplyToResources(Resources, theme);
        ThemeManager.ThemeChanged += OnThemeManagerThemeChanged;
        Closed += OnWindowClosed;
        UsernameTextBox.Text = initialUsername ?? string.Empty;
        Loaded += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(UsernameTextBox.Text))
            {
                UsernameTextBox.Focus();
                UsernameTextBox.SelectAll();
                return;
            }

            PasswordInput.Focus();
        };
    }

    public string VrChatUsername => UsernameTextBox.Text.Trim();

    public string VrChatPassword => PasswordInput.Password;

    public VrChatAccountSettings? AccountResult { get; private set; }

    public bool IsTwoFactorMode => isTwoFactorMode;

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeManagerThemeChanged;
        Closed -= OnWindowClosed;
    }

    private void OnThemeManagerThemeChanged(object? sender, EventArgs e)
    {
        currentTheme = ThemeManager.CurrentTheme;
        Dispatcher.BeginInvoke(() => ThemeManager.ApplyToResources(Resources));
    }

    private async void OnContinueClicked(object sender, RoutedEventArgs e)
    {
        if (isProcessing)
        {
            return;
        }

        if (!isTwoFactorMode)
        {
            await HandleInitialLoginAsync();
        }
        else
        {
            await HandleTwoFactorVerificationAsync();
        }
    }

    private async Task HandleInitialLoginAsync()
    {
        if (string.IsNullOrWhiteSpace(VrChatUsername) || string.IsNullOrWhiteSpace(VrChatPassword))
        {
            ThemedDialogWindow.ShowOk(
                this,
                currentTheme,
                LocalizationService.Translate("VRChat Login"),
                LocalizationService.Translate("Enter both the VRChat username and password before continuing."),
                LocalizationService.Translate("OK"));
            return;
        }

        SetProcessingState(true);
        ShowStatus(LocalizationService.Translate("Connecting to VRChat..."));

        try
        {
            loginResponse = await apiClient.LoginWithCredentialsAsync(VrChatUsername, VrChatPassword, CancellationToken.None);

            if (loginResponse.RequiredTwoFactorMethods.Count > 0)
            {
                EnterTwoFactorMode(loginResponse.RequiredTwoFactorMethods);
                SetProcessingState(false);
                return;
            }

            if (loginResponse.Account is not null)
            {
                AccountResult = loginResponse.Account;
                DialogResult = true;
                return;
            }

            ShowStatus(LocalizationService.Translate("VRChat login returned no account details. Try again."));
        }
        catch (Exception ex)
        {
            ShowStatus(GetFriendlyError(ex));
        }

        SetProcessingState(false);
    }

    private async Task HandleTwoFactorVerificationAsync()
    {
        var code = CodeTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            ThemedDialogWindow.ShowOk(
                this,
                currentTheme,
                LocalizationService.Translate("VRChat 2FA"),
                LocalizationService.Translate("Enter the VRChat verification code before continuing."),
                LocalizationService.Translate("OK"));
            return;
        }

        if (loginResponse is null)
        {
            ShowStatus(LocalizationService.Translate("Login session expired. Try again."));
            return;
        }

        SetProcessingState(true);
        ShowStatus(LocalizationService.Translate("Verifying 2FA code..."));

        try
        {
            var method = MethodComboBox.SelectedItem is VrChatTwoFactorMethodOption option
                ? option.Value
                : VrChatTwoFactorMethod.Totp;

            var account = await apiClient.CompleteTwoFactorAsync(
                loginResponse.AuthCookie,
                method,
                code,
                CancellationToken.None);

            AccountResult = account;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ShowStatus(GetFriendlyError(ex));
        }

        SetProcessingState(false);
    }

    private void EnterTwoFactorMode(IReadOnlyCollection<VrChatTwoFactorMethod> availableMethods)
    {
        isTwoFactorMode = true;

        var options = availableMethods.Count == 0
            ? [new VrChatTwoFactorMethodOption(VrChatTwoFactorMethod.Totp, LocalizationService.Translate("Authenticator App"), LocalizationService.Translate("Use the 6-digit code from your authenticator app."))]
            : availableMethods.Select(method => new VrChatTwoFactorMethodOption(
                    method,
                    method switch
                    {
                        VrChatTwoFactorMethod.EmailOtp => LocalizationService.Translate("Email Code"),
                        VrChatTwoFactorMethod.RecoveryCode => LocalizationService.Translate("Recovery Code"),
                        _ => LocalizationService.Translate("Authenticator App")
                    },
                    method switch
                    {
                        VrChatTwoFactorMethod.EmailOtp => LocalizationService.Translate("Use the verification code VRChat emailed to you."),
                        VrChatTwoFactorMethod.RecoveryCode => LocalizationService.Translate("Use one of your VRChat recovery codes."),
                        _ => LocalizationService.Translate("Use the 6-digit code from your authenticator app.")
                    }))
                .ToArray();

        MethodComboBox.ItemsSource = options;
        MethodComboBox.DisplayMemberPath = nameof(VrChatTwoFactorMethodOption.Label);
        MethodComboBox.SelectedIndex = 0;

        TwoFactorSection.Visibility = Visibility.Visible;
        ContinueButton.Content = LocalizationService.Translate("Verify");
        ShowStatus(LocalizationService.Translate("Enter the verification code from your authenticator app or one of your recovery codes."));

        CodeTextBox.Focus();
        UpdateHint();
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        if (isTwoFactorMode && loginResponse is not null)
        {
            _ = SafeLogoutAsync(loginResponse.AuthCookie);
        }
        DialogResult = false;
    }

    private async Task SafeLogoutAsync(string authCookie)
    {
        try
        {
            await apiClient.LogoutAsync(authCookie, CancellationToken.None);
        }
        catch
        {
        }
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void OnCloseClicked(object sender, RoutedEventArgs e) => OnCancelClicked(sender, e);

    private void OnMethodSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateHint();

    private void UpdateHint()
    {
        HintTextBlock.Text = MethodComboBox.SelectedItem is VrChatTwoFactorMethodOption option
            ? option.Hint
            : LocalizationService.Translate("Use the VRChat verification code you were given.");
        TwoFactorTitleText.Text = MethodComboBox.SelectedItem is VrChatTwoFactorMethodOption selected
            ? selected.Value switch
            {
                VrChatTwoFactorMethod.EmailOtp => LocalizationService.Translate("A verification code was sent to your VRChat email."),
                VrChatTwoFactorMethod.RecoveryCode => LocalizationService.Translate("Use one of your VRChat recovery codes."),
                _ => LocalizationService.Translate("Two-factor authentication is required for this account. Enter the verification code below.")
            }
            : LocalizationService.Translate("Two-factor authentication is required for this account. Enter the verification code below.");
    }

    private void SetProcessingState(bool processing)
    {
        isProcessing = processing;
        ContinueButton.IsEnabled = !processing;
        CancelButton.IsEnabled = !processing;
        UsernameTextBox.IsEnabled = !processing && !isTwoFactorMode;
        PasswordInput.IsEnabled = !processing && !isTwoFactorMode;
        if (isTwoFactorMode)
        {
            CodeTextBox.IsEnabled = !processing;
            MethodComboBox.IsEnabled = !processing;
        }
    }

    private void ShowStatus(string message)
    {
        StatusTextBlock.Text = message;
        StatusTextBlock.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string GetFriendlyError(Exception ex)
    {
        return ex switch
        {
            VrChatApiException apiEx when apiEx.StatusCode == System.Net.HttpStatusCode.Unauthorized
                => LocalizationService.Translate("VRChat login was not accepted. Double-check the username, password, and 2FA code."),
            VrChatApiException apiEx when apiEx.ApiMessage.Contains("Missing Credentials", StringComparison.OrdinalIgnoreCase)
                => LocalizationService.Translate("VRChat login was missing credentials. Try connecting again."),
            VrChatApiException
                => LocalizationService.Translate("VRChat login failed. Try again."),
            InvalidOperationException invalidEx
                => invalidEx.Message,
            _
                => string.Format("VRChat avatar access failed: {0}", ex.Message)
        };
    }

    private sealed record VrChatTwoFactorMethodOption(VrChatTwoFactorMethod Value, string Label, string Hint);
}