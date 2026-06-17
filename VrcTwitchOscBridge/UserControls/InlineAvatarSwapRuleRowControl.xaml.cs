using System.Windows;
using System.Windows.Controls;
using VrcTwitchOscBridge.ViewModels;

namespace VrcTwitchOscBridge.UserControls;

public partial class InlineAvatarSwapRuleRowControl : UserControl
{
    public static readonly DependencyProperty RowProperty = DependencyProperty.Register(
        nameof(Row), typeof(InlineAvatarSwapRuleRowViewModel), typeof(InlineAvatarSwapRuleRowControl),
        new PropertyMetadata(null));

    public InlineAvatarSwapRuleRowViewModel? Row
    {
        get => (InlineAvatarSwapRuleRowViewModel?)GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    public InlineAvatarSwapRuleRowControl() => InitializeComponent();
}
