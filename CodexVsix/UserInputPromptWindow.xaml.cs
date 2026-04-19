using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CodexVsix;

public partial class UserInputPromptWindow : Window
{
    public UserInputPromptWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var firstInput = FindFirstInput(this);
        if (firstInput is null)
        {
            return;
        }

        firstInput.Focus();
        Keyboard.Focus(firstInput);
    }

    private static Control? FindFirstInput(DependencyObject parent)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is Control control
                && control.IsVisible
                && control.IsEnabled
                && (control is TextBox || control is ComboBox))
            {
                return control;
            }

            var nested = FindFirstInput(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
