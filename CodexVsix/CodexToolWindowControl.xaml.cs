using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
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
        _viewModel.Messages.CollectionChanged += OnMessagesCollectionChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
        PreviewKeyDown += OnPreviewKeyDown;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdatePromptTextBoxMaxHeight();
        ScrollChatToEnd();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdatePromptTextBoxMaxHeight();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control && PromptTextBox.IsKeyboardFocusWithin)
        {
            if (_viewModel.SendCommand.CanExecute(null))
            {
                _viewModel.SendCommand.Execute(null);
            }

            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V && System.Windows.Clipboard.ContainsImage())
        {
            _viewModel.PasteImageFromClipboard();
            e.Handled = true;
        }
    }

    private void UpdatePromptTextBoxMaxHeight()
    {
        var availableHeight = ActualHeight > 0 ? ActualHeight : SystemParameters.WorkArea.Height;
        PromptTextBox.MaxHeight = Math.Max(PromptTextBox.MinHeight, availableHeight * 0.5d);
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScrollChatToEnd();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(CodexToolWindowViewModel.IsBusy), StringComparison.Ordinal))
        {
            ScrollChatToEnd();
        }
    }

    private void ScrollChatToEnd()
    {
        _ = Dispatcher.InvokeAsync(() => ChatScrollViewer.ScrollToEnd());
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Messages.CollectionChanged -= OnMessagesCollectionChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Loaded -= OnLoaded;
        SizeChanged -= OnSizeChanged;
        Unloaded -= OnUnloaded;
        PreviewKeyDown -= OnPreviewKeyDown;
        _viewModel.Dispose();
    }
}
