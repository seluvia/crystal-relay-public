using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;
using CrystalRelayLiveList.Services;
using Xunit;

namespace CrystalRelayLiveList.Tests;

public sealed class TrayServiceTests
{
    [Fact]
    public void MinimizedWindow_RemainsVisibleInsteadOfHidingToTray()
    {
        RunOnStaThread(() =>
        {
            var window = new Window
            {
                Width = 240,
                Height = 120,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000
            };

            using var tray = new TrayService(window, () => { }, () => { });
            try
            {
                window.Show();
                window.WindowState = WindowState.Minimized;
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                Assert.Equal(Visibility.Visible, window.Visibility);
                Assert.True(window.IsVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }
}
