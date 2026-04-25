using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ComboBox = System.Windows.Controls.ComboBox;

namespace VrcTwitchOscBridge;

public static class ComboBoxInteraction
{
    private static readonly MouseWheelEventHandler PopupMouseWheelHandler = OnPopupMouseWheel;

    private static readonly DependencyProperty PopupOwnerProperty =
        DependencyProperty.RegisterAttached(
            "PopupOwner",
            typeof(ComboBox),
            typeof(ComboBoxInteraction),
            new PropertyMetadata(null));

    public static readonly DependencyProperty EnableGuardedInteractionProperty =
        DependencyProperty.RegisterAttached(
            "EnableGuardedInteraction",
            typeof(bool),
            typeof(ComboBoxInteraction),
            new PropertyMetadata(false, OnEnableGuardedInteractionChanged));

    public static bool GetEnableGuardedInteraction(DependencyObject element) =>
        (bool)element.GetValue(EnableGuardedInteractionProperty);

    public static void SetEnableGuardedInteraction(DependencyObject element, bool value) =>
        element.SetValue(EnableGuardedInteractionProperty, value);

    private static void OnEnableGuardedInteractionChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not ComboBox comboBox)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            comboBox.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            comboBox.PreviewMouseWheel += OnPreviewMouseWheel;
            comboBox.Loaded += OnComboBoxLoaded;
            comboBox.DropDownOpened += OnComboBoxDropDownOpened;
            return;
        }

        comboBox.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        comboBox.PreviewMouseWheel -= OnPreviewMouseWheel;
        comboBox.Loaded -= OnComboBoxLoaded;
        comboBox.DropDownOpened -= OnComboBoxDropDownOpened;
        UnhookPopupMouseWheel(comboBox);
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ComboBox comboBox)
        {
            return;
        }

        var origin = e.OriginalSource as DependencyObject;
        if (origin is null)
        {
            return;
        }

        if (FindAncestor<ComboBoxItem>(origin) is not null
            || FindAncestor<ToggleButton>(origin) is not null
            || FindAncestor<ScrollBar>(origin) is not null)
        {
            return;
        }

        comboBox.Focus();

        if (comboBox.IsEditable && FindAncestor<TextBox>(origin) is not null)
        {
            if (!comboBox.IsDropDownOpen)
            {
                comboBox.IsDropDownOpen = true;
            }

            return;
        }

        comboBox.IsDropDownOpen = !comboBox.IsDropDownOpen;

        e.Handled = true;
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ComboBox comboBox)
        {
            return;
        }

        e.Handled = true;

        if (!comboBox.IsDropDownOpen)
        {
            BubbleMouseWheelToParent(comboBox, e);
        }
    }

    private static void BubbleMouseWheelToParent(ComboBox comboBox, MouseWheelEventArgs e)
    {
        DependencyObject? current = comboBox;
        while (current is not null)
        {
            current = VisualTreeHelper.GetParent(current);
            if (current is UIElement element)
            {
                var mouseWheelEvent = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = comboBox
                };

                element.RaiseEvent(mouseWheelEvent);
                return;
            }
        }
    }

    private static void OnComboBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            HookPopupMouseWheel(comboBox);
        }
    }

    private static void OnComboBoxDropDownOpened(object? sender, EventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            HookPopupMouseWheel(comboBox);
        }
    }

    private static void HookPopupMouseWheel(ComboBox comboBox)
    {
        if (comboBox.Template.FindName("PART_Popup", comboBox) is not Popup popup
            || popup.Child is not UIElement child)
        {
            return;
        }

        child.SetValue(PopupOwnerProperty, comboBox);
        child.RemoveHandler(UIElement.PreviewMouseWheelEvent, PopupMouseWheelHandler);
        child.AddHandler(UIElement.PreviewMouseWheelEvent, PopupMouseWheelHandler, true);

        if (FindDescendant<ScrollViewer>(child) is { } scrollViewer)
        {
            scrollViewer.RemoveHandler(UIElement.PreviewMouseWheelEvent, PopupMouseWheelHandler);
            scrollViewer.RemoveHandler(UIElement.MouseWheelEvent, PopupMouseWheelHandler);
            scrollViewer.AddHandler(UIElement.PreviewMouseWheelEvent, PopupMouseWheelHandler, true);
            scrollViewer.AddHandler(UIElement.MouseWheelEvent, PopupMouseWheelHandler, true);
        }
    }

    private static void UnhookPopupMouseWheel(ComboBox comboBox)
    {
        if (comboBox.Template.FindName("PART_Popup", comboBox) is not Popup popup
            || popup.Child is not UIElement child)
        {
            return;
        }

        child.RemoveHandler(UIElement.PreviewMouseWheelEvent, PopupMouseWheelHandler);
        if (FindDescendant<ScrollViewer>(child) is { } scrollViewer)
        {
            scrollViewer.RemoveHandler(UIElement.PreviewMouseWheelEvent, PopupMouseWheelHandler);
            scrollViewer.RemoveHandler(UIElement.MouseWheelEvent, PopupMouseWheelHandler);
        }

        child.ClearValue(PopupOwnerProperty);
    }

    private static void OnPopupMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scrollViewer = FindAncestor<ScrollViewer>(e.OriginalSource as DependencyObject)
            ?? FindDescendant<ScrollViewer>(sender as DependencyObject);
        if (scrollViewer is not null)
        {
            ScrollViewerByMouseWheel(scrollViewer, e.Delta);
        }

        e.Handled = true;
    }

    private static void ScrollViewerByMouseWheel(ScrollViewer scrollViewer, int delta)
    {
        if (delta == 0 || scrollViewer.ScrollableHeight <= 0)
        {
            return;
        }

        var notchCount = Math.Max(1, Math.Abs(delta) / Mouse.MouseWheelDeltaForOneLine);
        var wheelLines = SystemParameters.WheelScrollLines;
        if (wheelLines == int.MaxValue)
        {
            if (delta > 0)
            {
                scrollViewer.PageUp();
            }
            else
            {
                scrollViewer.PageDown();
            }

            return;
        }

        var lines = Math.Max(1, wheelLines) * notchCount;
        var unit = scrollViewer.CanContentScroll ? 1.0 : 16.0;
        var offsetDelta = lines * unit * (delta > 0 ? -1 : 1);
        var targetOffset = Math.Clamp(scrollViewer.VerticalOffset + offsetDelta, 0, scrollViewer.ScrollableHeight);
        scrollViewer.ScrollToVerticalOffset(targetOffset);
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject? current) where T : DependencyObject
    {
        if (current is null)
        {
            return null;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(current);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(current, i);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindDescendant<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }
}
