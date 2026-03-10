using System.Windows.Controls;
using System.Windows.Input;
using CodexVsix.ViewModels;

namespace CodexVsix;

public partial class CodexToolWindowControl : UserControl
{
    private readonly CodexToolWindowViewModel _viewModel;

    public CodexToolWindowControl()
    {
        InitializeComponent();
        _viewModel = new CodexToolWindowViewModel();
        DataContext = _viewModel;
        PreviewKeyDown += OnPreviewKeyDown;
        Unloaded += OnUnloaded;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V && System.Windows.Clipboard.ContainsImage())
        {
            _viewModel.PasteImageFromClipboard();
            e.Handled = true;
        }
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        PreviewKeyDown -= OnPreviewKeyDown;
        _viewModel.Dispose();
    }
}
