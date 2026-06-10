using System.Windows;
using VrcTwitchOscBridge.ViewModels;

namespace VrcTwitchOscBridge;

public partial class UniversalTriggerImportPreviewWindow : Window
{
    public UniversalTriggerImportPreviewWindow()
    {
        InitializeComponent();
        if (DataContext is UniversalTriggerImportPreviewViewModel vm)
        {
            vm.CancelRequested += () => { DialogResult = false; Close(); };
            vm.ImportRequested += _ => { DialogResult = true; Close(); };
        }
    }
}