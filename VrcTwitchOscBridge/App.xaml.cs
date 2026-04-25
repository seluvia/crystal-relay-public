using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using VrcTwitchOscBridge.Services;
using Application = System.Windows.Application;

namespace VrcTwitchOscBridge;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\CrystalRelay.SingleInstance";
    private const string ActivateExistingWindowMessageName = "CrystalRelay.ActivateExistingWindow";
    private static readonly int activateExistingWindowMessageId = RegisterWindowMessage(ActivateExistingWindowMessageName);
    private static int crashNoticeShown;

    private Mutex? singleInstanceMutex;
    private bool ownsSingleInstanceMutex;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnTaskSchedulerUnhandledException;
    }

    public static int ActivateExistingWindowMessageId => activateExistingWindowMessageId;

    protected override void OnStartup(StartupEventArgs e)
    {
        singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        ownsSingleInstanceMutex = createdNew;

        if (!createdNew)
        {
            NotifyExistingInstance();
            Shutdown();
            return;
        }

        var settingsStore = new SettingsStore();
        LocalizationService.Initialize(settingsStore.LoadLanguagePreference());

        base.OnStartup(e);

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (ownsSingleInstanceMutex)
            {
                singleInstanceMutex?.ReleaseMutex();
            }
        }
        catch
        {
        }
        finally
        {
            singleInstanceMutex?.Dispose();
            singleInstanceMutex = null;
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var crashLogResult = CrashLogService.TryWrite("DispatcherUnhandledException", e.Exception, isTerminating: true);
        TryShowCrashMessage(crashLogResult, e.Exception);
        e.Handled = true;
        Shutdown(-1);
    }

    private void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var crashLogResult = CrashLogService.TryWrite(
            "CurrentDomainUnhandledException",
            e.ExceptionObject,
            e.IsTerminating);
        TryShowCrashMessage(crashLogResult, e.ExceptionObject);
    }

    private void OnTaskSchedulerUnhandledException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        CrashLogService.TryWrite("TaskScheduler.UnobservedTaskException", e.Exception, isTerminating: false);
        e.SetObserved();
    }

    private static void TryShowCrashMessage(CrashLogWriteResult crashLogResult, object? exceptionObject)
    {
        if (Interlocked.Exchange(ref crashNoticeShown, 1) == 1)
        {
            return;
        }

        try
        {
            var builder = new StringBuilder();
            builder.AppendLine(LocalizationService.Translate("Crystal Relay ran into an unexpected error and needs to close."));

            var exceptionMessage = exceptionObject switch
            {
                Exception exception => exception.Message,
                null => null,
                _ => exceptionObject.ToString()
            };

            if (!string.IsNullOrWhiteSpace(exceptionMessage))
            {
                builder.AppendLine();
                builder.AppendLine(exceptionMessage);
            }

            if (!string.IsNullOrWhiteSpace(crashLogResult.CrashLogPath))
            {
                builder.AppendLine();
                builder.AppendLine(LocalizationService.Translate("A crash log was saved to:"));
                builder.AppendLine(crashLogResult.CrashLogPath);
            }
            else if (!string.IsNullOrWhiteSpace(crashLogResult.FailureReason))
            {
                builder.AppendLine();
                builder.AppendLine(LocalizationService.Translate("Crystal Relay tried to save a crash log, but logging failed."));
                builder.AppendLine(crashLogResult.FailureReason);
            }

            builder.AppendLine();
            builder.AppendLine(LocalizationService.Translate("If you report the issue, please include that crash log file."));

            void ShowMessage()
            {
                MessageBox.Show(
                    builder.ToString(),
                    LocalizationService.Translate("Crystal Relay Crash"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            if (Current?.Dispatcher is Dispatcher dispatcher
                && !dispatcher.HasShutdownStarted
                && !dispatcher.HasShutdownFinished)
            {
                if (dispatcher.CheckAccess())
                {
                    ShowMessage();
                }
                else
                {
                    dispatcher.Invoke(ShowMessage, DispatcherPriority.Send);
                }

                return;
            }

            ShowMessage();
        }
        catch
        {
            // If the process is too unstable to show a dialog, the text log is still the fallback.
        }
    }

    private static void NotifyExistingInstance()
    {
        if (activateExistingWindowMessageId == 0)
        {
            return;
        }

        PostMessage(new IntPtr(0xffff), activateExistingWindowMessageId, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
