using System;
using System.Windows;
using System.Windows.Input;
using VrcTwitchOscBridge.ViewModels;

namespace VrcTwitchOscBridge;

public partial class UniversalTriggersManagerWindow : Window
{
    public UniversalTriggersManagerWindow(UniversalTriggersManagerViewModel viewModel)
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
    }

    private UniversalTriggersManagerViewModel Vm => (UniversalTriggersManagerViewModel)DataContext;

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void OnToggleChatSection(object sender, MouseButtonEventArgs e) => Vm.IsChatSectionCollapsed = !Vm.IsChatSectionCollapsed;

    private void OnToggleRewardSection(object sender, MouseButtonEventArgs e) => Vm.IsRewardSectionCollapsed = !Vm.IsRewardSectionCollapsed;

    private void OnToggleBitsSection(object sender, MouseButtonEventArgs e) => Vm.IsBitsSectionCollapsed = !Vm.IsBitsSectionCollapsed;

    private void OnToggleSubsSection(object sender, MouseButtonEventArgs e) => Vm.IsSubsSectionCollapsed = !Vm.IsSubsSectionCollapsed;

    private void OnToggleFollowsSection(object sender, MouseButtonEventArgs e) => Vm.IsFollowsSectionCollapsed = !Vm.IsFollowsSectionCollapsed;

    private void OnEditorBackdropClicked(object sender, MouseButtonEventArgs e)
    {
        if (Vm.CloseEditorCommand.CanExecute(null))
        {
            Vm.CloseEditorCommand.Execute(null);
        }
    }
}
