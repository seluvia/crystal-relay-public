using System.Windows;
using System.Windows.Controls;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.ViewModels;

namespace VrcTwitchOscBridge;

public partial class UniversalTriggerCreateWizardWindow : Window
{
    public UniversalTriggerCreateWizardWindow()
    {
        InitializeComponent();
        if (DataContext is UniversalTriggerCreateWizardViewModel vm)
        {
            vm.CloseRequested += () => { DialogResult = false; Close(); };
            vm.SaveRequested += _ => { DialogResult = true; Close(); };
        }
    }

    private void OnEventCardClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is UniversalTriggerType t && DataContext is UniversalTriggerCreateWizardViewModel vm)
        {
            vm.SelectedEventType = t;
        }
    }
}