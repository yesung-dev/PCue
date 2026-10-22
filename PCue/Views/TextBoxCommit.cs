using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TextBox = System.Windows.Controls.TextBox;

namespace PCue.Views;

/// <summary>
/// Enter commits (and runs <c>Command</c>), Esc reverts; both hand focus back to the
/// window so playback hotkeys work again. Losing focus also commits.
/// Use with <c>UpdateSourceTrigger=Explicit</c> or <c>PropertyChanged</c>.
/// </summary>
public static class TextBoxCommit
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(TextBoxCommit),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.RegisterAttached(
            "Command",
            typeof(ICommand),
            typeof(TextBoxCommit),
            new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    public static ICommand? GetCommand(DependencyObject obj) => (ICommand?)obj.GetValue(CommandProperty);

    public static void SetCommand(DependencyObject obj, ICommand? value) => obj.SetValue(CommandProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box)
            return;

        box.PreviewKeyDown -= OnPreviewKeyDown;
        box.LostKeyboardFocus -= OnLostKeyboardFocus;
        box.GotKeyboardFocus -= OnGotKeyboardFocus;
        box.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;

        if (e.NewValue is true)
        {
            box.PreviewKeyDown += OnPreviewKeyDown;
            box.LostKeyboardFocus += OnLostKeyboardFocus;
            box.GotKeyboardFocus += OnGotKeyboardFocus;
            box.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        }
    }

    // First click focuses and selects everything instead of placing the caret.
    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox box || box.IsKeyboardFocusWithin)
            return;

        box.Focus();
        e.Handled = true;
    }

    private static void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box)
            box.SelectAll();
    }

    private static void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not TextBox box)
            return;

        switch (e.Key)
        {
            case Key.Enter:
                Commit(box);
                if (GetCommand(box) is { } command && command.CanExecute(null))
                    command.Execute(null);
                ReleaseFocus(box);
                e.Handled = true;
                break;
            case Key.Escape:
                box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
                ReleaseFocus(box);
                e.Handled = true;
                break;
        }
    }

    private static void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box)
            Commit(box);
    }

    private static void Commit(TextBox box)
    {
        var binding = box.GetBindingExpression(TextBox.TextProperty);
        if (binding is null)
            return;

        binding.UpdateSource();
        // Re-read so the text shows the normalized (or reverted) value.
        binding.UpdateTarget();
    }

    private static void ReleaseFocus(TextBox box)
    {
        if (Window.GetWindow(box) is { } window)
            window.Focus();
        else
            Keyboard.ClearFocus();
    }
}
