using System.Windows;
using System.Windows.Controls;

namespace VrcTwitchOscBridge;

public static class PasswordBoxBinding
{
    public static readonly DependencyProperty BindPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BindPassword",
            typeof(bool),
            typeof(PasswordBoxBinding),
            new PropertyMetadata(false, OnBindPasswordChanged));

    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BoundPassword",
            typeof(string),
            typeof(PasswordBoxBinding),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnBoundPasswordChanged));

    private static readonly DependencyProperty IsUpdatingProperty =
        DependencyProperty.RegisterAttached(
            "IsUpdating",
            typeof(bool),
            typeof(PasswordBoxBinding),
            new PropertyMetadata(false));

    public static bool GetBindPassword(DependencyObject dependencyObject) =>
        (bool)dependencyObject.GetValue(BindPasswordProperty);

    public static void SetBindPassword(DependencyObject dependencyObject, bool value) =>
        dependencyObject.SetValue(BindPasswordProperty, value);

    public static string GetBoundPassword(DependencyObject dependencyObject) =>
        (string)(dependencyObject.GetValue(BoundPasswordProperty) ?? string.Empty);

    public static void SetBoundPassword(DependencyObject dependencyObject, string value) =>
        dependencyObject.SetValue(BoundPasswordProperty, value ?? string.Empty);

    private static bool GetIsUpdating(DependencyObject dependencyObject) =>
        (bool)dependencyObject.GetValue(IsUpdatingProperty);

    private static void SetIsUpdating(DependencyObject dependencyObject, bool value) =>
        dependencyObject.SetValue(IsUpdatingProperty, value);

    private static void OnBindPasswordChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not PasswordBox passwordBox)
        {
            return;
        }

        passwordBox.PasswordChanged -= PasswordChanged;
        if (e.NewValue is true)
        {
            passwordBox.Password = GetBoundPassword(passwordBox);
            passwordBox.PasswordChanged += PasswordChanged;
        }
    }

    private static void OnBoundPasswordChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not PasswordBox passwordBox || GetIsUpdating(passwordBox))
        {
            return;
        }

        passwordBox.PasswordChanged -= PasswordChanged;
        passwordBox.Password = e.NewValue as string ?? string.Empty;
        passwordBox.PasswordChanged += PasswordChanged;
    }

    private static void PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox passwordBox)
        {
            return;
        }

        SetIsUpdating(passwordBox, true);
        SetBoundPassword(passwordBox, passwordBox.Password);
        SetIsUpdating(passwordBox, false);
    }
}
