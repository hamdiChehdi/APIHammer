using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Xaml.Behaviors;

namespace APIHammerUI.Behaviors;

/// <summary>
/// Behavior to bind PasswordBox.Password to a Command for MVVM scenarios
/// </summary>
public class PasswordBoxPasswordChangedBehavior : Behavior<PasswordBox>
{
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(PasswordBoxPasswordChangedBehavior));

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject != null)
        {
            AssociatedObject.PasswordChanged += OnPasswordChanged;
        }
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject != null)
        {
            AssociatedObject.PasswordChanged -= OnPasswordChanged;
        }
        base.OnDetaching();
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (Command?.CanExecute(AssociatedObject?.Password) == true)
        {
            Command.Execute(AssociatedObject?.Password);
        }
    }
}

/// <summary>
/// Behavior to handle GotFocus events through commands
/// </summary>
public class GotFocusBehavior : Behavior<FrameworkElement>
{
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(GotFocusBehavior));

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject != null)
        {
            AssociatedObject.GotFocus += OnGotFocus;
        }
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject != null)
        {
            AssociatedObject.GotFocus -= OnGotFocus;
        }
        base.OnDetaching();
    }

    private void OnGotFocus(object sender, RoutedEventArgs e)
    {
        if (Command?.CanExecute(null) == true)
        {
            Command.Execute(null);
        }
    }
}