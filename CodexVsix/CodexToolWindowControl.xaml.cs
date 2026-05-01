using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using CodexVsix.Models;
using CodexVsix.Services;
using CodexVsix.ViewModels;
using Microsoft.VisualStudio.Shell;

namespace CodexVsix;

public partial class CodexToolWindowControl : UserControl
{
    private readonly CodexToolWindowViewModel _viewModel;
    private readonly List<FrameworkElement> _chatSelectableElements = new();
    private readonly HashSet<ChatMessage> _subscribedChatMessages = new();
    private FrameworkElement? _selectionAnchorElement;
    private object? _selectionAnchorPosition;
    private bool _isSelectingAcrossBubbles;
    private bool _chatScrollToEndScheduled;
    private UserInputPromptWindow? _userInputPromptWindow;
    private bool _suppressUserInputWindowClosedCancel;

    public CodexToolWindowControl()
    {
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            ActivityLog.TryLogError("CodexVsix", new LocalizationService().ToolWindowXamlLoadLogMessage + Environment.NewLine + ex);
            throw;
        }

        try
        {
            _viewModel = CodexViewModelHost.GetOrCreate();
        }
        catch (Exception ex)
        {
            ActivityLog.TryLogError("CodexVsix", new LocalizationService().ToolWindowViewModelCreateLogMessage + Environment.NewLine + ex);
            throw;
        }

        DataContext = _viewModel;
        _viewModel.Messages.CollectionChanged += OnMessagesCollectionChanged;
        foreach (var message in _viewModel.Messages)
        {
            SubscribeMessage(message);
        }

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.EnsureToolWindowStartupState();
        UpdatePromptTextBoxMaxHeight();
        ScrollChatToEnd();
        SyncUserInputPromptWindow();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CloseUserInputPromptWindow(suppressCancel: true);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdatePromptTextBoxMaxHeight();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && PromptTextBox.IsKeyboardFocusWithin)
        {
            ExecuteSendShortcut(e);
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V && System.Windows.Clipboard.ContainsImage())
        {
            _viewModel.PasteImageFromClipboard();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.A && GetFocusedChatSelectableElement() is not null)
        {
            SelectAllChatText();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
        {
            var selectedChatText = GetSelectedChatText();
            if (!string.IsNullOrWhiteSpace(selectedChatText))
            {
                Clipboard.SetText(selectedChatText);
                e.Handled = true;
            }
        }
    }

    private void UpdatePromptTextBoxMaxHeight()
    {
        var availableHeight = ActualHeight > 0 ? ActualHeight : SystemParameters.WorkArea.Height;
        PromptTextBox.MaxHeight = Math.Max(PromptTextBox.MinHeight, availableHeight * 0.5d);
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<ChatMessage>())
            {
                UnsubscribeMessage(item);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<ChatMessage>())
            {
                SubscribeMessage(item);
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var item in _subscribedChatMessages.ToList())
            {
                UnsubscribeMessage(item);
            }

            foreach (var item in _viewModel.Messages)
            {
                SubscribeMessage(item);
            }
        }

        ScrollChatToEnd();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(CodexToolWindowViewModel.IsBusy), StringComparison.Ordinal))
        {
            ScrollChatToEnd();
        }

        if (string.IsNullOrEmpty(e.PropertyName)
            || string.Equals(e.PropertyName, nameof(CodexToolWindowViewModel.CurrentUserInputPrompt), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(CodexToolWindowViewModel.HasCurrentUserInputPrompt), StringComparison.Ordinal))
        {
            SyncUserInputPromptWindow();
        }
    }

    private void ScrollChatToEnd()
    {
        if (_chatScrollToEndScheduled)
        {
            return;
        }

        _chatScrollToEndScheduled = true;
        _ = Dispatcher.InvokeAsync(() =>
        {
            _chatScrollToEndScheduled = false;
            if (_viewModel.Messages.Count == 0)
            {
                return;
            }

            ChatContentHost.ScrollIntoView(_viewModel.Messages[_viewModel.Messages.Count - 1]);
        }, DispatcherPriority.Background);
    }

    private void SubscribeMessage(ChatMessage message)
    {
        if (_subscribedChatMessages.Add(message))
        {
            message.PropertyChanged += OnMessagePropertyChanged;
        }
    }

    private void UnsubscribeMessage(ChatMessage message)
    {
        if (_subscribedChatMessages.Remove(message))
        {
            message.PropertyChanged -= OnMessagePropertyChanged;
        }
    }

    private void OnMessagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ChatMessage message || !ReferenceEquals(message, _viewModel.Messages.LastOrDefault()))
        {
            return;
        }

        if (string.Equals(e.PropertyName, nameof(ChatMessage.Text), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(ChatMessage.DisplayText), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(ChatMessage.Detail), StringComparison.Ordinal))
        {
            ScrollChatToEnd();
        }
    }

    private void OnChatTextBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && !_chatSelectableElements.Contains(element))
        {
            _chatSelectableElements.Add(element);

            switch (element)
            {
                case TextBox textBox:
                    textBox.ContextMenu ??= CreateChatContextMenu();
                    break;
                case RichTextBox richTextBox:
                    richTextBox.ContextMenu ??= CreateChatContextMenu();
                    break;
            }
        }
    }

    private void OnChatTextBoxUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        _chatSelectableElements.Remove(element);
        if (ReferenceEquals(_selectionAnchorElement, element))
        {
            _selectionAnchorElement = null;
            _selectionAnchorPosition = null;
            _isSelectingAcrossBubbles = false;
        }
    }

    private void OnChatTextBoxPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        _selectionAnchorElement = element;
        _selectionAnchorPosition = GetSelectionPoint(element, e.GetPosition(element));
        _isSelectingAcrossBubbles = true;
    }

    private void OnMenuButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.ContextMenu is null)
        {
            return;
        }

        element.ContextMenu.DataContext = element.DataContext;
        element.ContextMenu.PlacementTarget = element;
        element.ContextMenu.IsOpen = true;
        e.Handled = true;
    }

    private void OnOpenHistoryPanelClick(object sender, RoutedEventArgs e)
    {
        ExecuteViewModelCommand(_viewModel.OpenHistoryPanelCommand);
        e.Handled = true;
    }

    private void OnOpenSettingsPanelClick(object sender, RoutedEventArgs e)
    {
        ExecuteViewModelCommand(_viewModel.OpenSettingsPanelCommand);
        e.Handled = true;
    }

    private void OnSetMarkdownViewClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ChatMessage message, Tag: string modeTag })
        {
            message.SetMarkdownView(string.Equals(modeTag, "rendered", StringComparison.OrdinalIgnoreCase));
        }

        e.Handled = true;
    }

    private void OnRateLimitsContextMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu contextMenu)
        {
            return;
        }

        var placementDataContext = (contextMenu.PlacementTarget as FrameworkElement)?.DataContext;
        contextMenu.DataContext = placementDataContext ?? contextMenu.DataContext;
        if (contextMenu.DataContext is not CodexToolWindowViewModel viewModel)
        {
            return;
        }

        const int staticItemCount = 4;
        while (contextMenu.Items.Count > staticItemCount)
        {
            contextMenu.Items.RemoveAt(contextMenu.Items.Count - 1);
        }

        if (viewModel.HasRateLimitData)
        {
            foreach (var entry in viewModel.RateLimitEntries.Where(item => item is not null && item.HasData))
            {
                contextMenu.Items.Add(CreateRateLimitMenuItem(entry));
            }

            return;
        }

        contextMenu.Items.Add(new MenuItem
        {
            Header = viewModel.Localization.RateLimitsUnavailable,
            IsEnabled = false
        });
    }

    private void OnCloseSidebarClick(object sender, RoutedEventArgs e)
    {
        ExecuteViewModelCommand(_viewModel.CloseSidebarCommand);
        e.Handled = true;
    }

    private void OnSelectSettingsSectionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        var parameter = element.Tag ?? element.DataContext;
        ExecuteViewModelCommand(_viewModel.SelectSettingsSectionCommand, parameter);
        e.Handled = true;
    }

    private void OnPromptTextBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            ExecuteSendShortcut(e);
        }
    }

    private void OnLanguageOptionsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox || listBox.SelectedValue is not string value)
        {
            return;
        }

        _viewModel.SelectedLanguageTag = value;
    }

    private void OnLanguageOptionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string value)
        {
            return;
        }

        _viewModel.SelectedLanguageTag = value;
        if (_viewModel.CloseSidebarCommand.CanExecute(null))
        {
            _viewModel.CloseSidebarCommand.Execute(null);
        }
        e.Handled = true;
    }

    private void OnLanguageOptionPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string value)
        {
            return;
        }

        _viewModel.SelectedLanguageTag = value;
        if (_viewModel.CloseSidebarCommand.CanExecute(null))
        {
            _viewModel.CloseSidebarCommand.Execute(null);
        }
        e.Handled = true;
    }

    private void OnLanguageOptionsPanelIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            LanguageSearchTextBox.Focus();
            Keyboard.Focus(LanguageSearchTextBox);
        }), DispatcherPriority.Input);
    }

    private void OnHistorySearchPanelIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            HistorySearchTextBox.Focus();
            Keyboard.Focus(HistorySearchTextBox);
        }), DispatcherPriority.Input);
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);

        if (!_isSelectingAcrossBubbles || e.LeftButton != MouseButtonState.Pressed || _selectionAnchorElement is null || _selectionAnchorPosition is null)
        {
            return;
        }

        var point = e.GetPosition(ChatContentHost);
        var currentElement = FindChatSelectableElementAtPoint(point);
        if (currentElement is null || ReferenceEquals(currentElement, _selectionAnchorElement))
        {
            return;
        }

        if (Mouse.Captured != ChatContentHost)
        {
            Mouse.Capture(ChatContentHost, CaptureMode.SubTree);
        }

        ExtendSelectionAcrossBubbles(currentElement, e.GetPosition(currentElement));
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);

        _isSelectingAcrossBubbles = false;
        if (Mouse.Captured == ChatContentHost)
        {
            Mouse.Capture(null);
        }
    }

    private void ExtendSelectionAcrossBubbles(FrameworkElement currentElement, Point point)
    {
        if (_selectionAnchorElement is null || _selectionAnchorPosition is null)
        {
            return;
        }

        var orderedElements = GetOrderedChatSelectableElements();
        var anchorPosition = orderedElements.IndexOf(_selectionAnchorElement);
        var currentPosition = orderedElements.IndexOf(currentElement);
        if (anchorPosition < 0 || currentPosition < 0)
        {
            return;
        }

        var currentPositionValue = GetSelectionPoint(currentElement, point);
        if (currentPositionValue is null)
        {
            return;
        }

        ClearChatSelection();

        if (anchorPosition == currentPosition)
        {
            SelectRange(currentElement, _selectionAnchorPosition, currentPositionValue);
            return;
        }

        var forwardSelection = currentPosition > anchorPosition;
        var start = forwardSelection ? anchorPosition : currentPosition;
        var end = forwardSelection ? currentPosition : anchorPosition;

        for (var index = start; index <= end; index++)
        {
            var element = orderedElements[index];
            if (ReferenceEquals(element, _selectionAnchorElement))
            {
                if (forwardSelection)
                {
                    SelectRange(element, _selectionAnchorPosition, GetSelectionEnd(element));
                }
                else
                {
                    SelectRange(element, GetSelectionStart(element), _selectionAnchorPosition);
                }

                continue;
            }

            if (ReferenceEquals(element, currentElement))
            {
                if (forwardSelection)
                {
                    SelectRange(element, GetSelectionStart(element), currentPositionValue);
                }
                else
                {
                    SelectRange(element, currentPositionValue, GetSelectionEnd(element));
                }

                continue;
            }

            SelectAll(element);
        }
    }

    private void OnChatCopyMenuItemClick(object sender, RoutedEventArgs e)
    {
        var selectedChatText = GetSelectedChatText();
        if (string.IsNullOrWhiteSpace(selectedChatText))
        {
            var placementTarget = (sender as FrameworkElement)?.Parent is ContextMenu contextMenu
                ? contextMenu.PlacementTarget as FrameworkElement
                : null;

            var fallbackSelection = placementTarget is null ? string.Empty : GetSelectedText(placementTarget);
            if (!string.IsNullOrWhiteSpace(fallbackSelection))
            {
                Clipboard.SetText(fallbackSelection);
            }

            return;
        }

        Clipboard.SetText(selectedChatText);
    }

    private void OnChatSelectAllMenuItemClick(object sender, RoutedEventArgs e)
    {
        SelectAllChatText();
    }

    private ContextMenu CreateChatContextMenu()
    {
        var contextMenu = new ContextMenu();
        contextMenu.Items.Add(new MenuItem
        {
            Header = _viewModel.Localization.CopyButton
        });
        contextMenu.Items.Add(new MenuItem
        {
            Header = _viewModel.Localization.SelectAllButton
        });

        if (contextMenu.Items[0] is MenuItem copyMenuItem)
        {
            copyMenuItem.Click += OnChatCopyMenuItemClick;
        }

        if (contextMenu.Items[1] is MenuItem selectAllMenuItem)
        {
            selectAllMenuItem.Click += OnChatSelectAllMenuItemClick;
        }

        return contextMenu;
    }

    private MenuItem CreateRateLimitMenuItem(CodexRateLimitWindowSummary entry)
    {
        var menuItem = new MenuItem
        {
            Header = entry,
            HeaderTemplate = TryFindResource("RateLimitPopupEntryTemplate") as DataTemplate,
            IsEnabled = false
        };

        if (TryFindResource("PopupMenuItemStyle") is Style style)
        {
            menuItem.Style = style;
        }

        return menuItem;
    }

    private void SelectAllChatText()
    {
        foreach (var element in GetOrderedChatSelectableElements())
        {
            SelectAll(element);
        }
    }

    private void ClearChatSelection()
    {
        foreach (var element in _chatSelectableElements)
        {
            ClearSelection(element);
        }
    }

    private List<FrameworkElement> GetOrderedChatSelectableElements()
    {
        return _chatSelectableElements
            .Where(element => element.IsLoaded)
            .OrderBy(element => element.TranslatePoint(new Point(0, 0), ChatContentHost).Y)
            .ThenBy(element => element.TranslatePoint(new Point(0, 0), ChatContentHost).X)
            .ToList();
    }

    private FrameworkElement? FindChatSelectableElementAtPoint(Point point)
    {
        var hit = ChatContentHost.InputHitTest(point) as DependencyObject;
        var directMatch = FindSelectableChatTextBox(hit);
        if (directMatch is not null)
        {
            return directMatch;
        }

        FrameworkElement? nearestElement = null;
        var nearestDistance = double.MaxValue;

        foreach (var element in GetOrderedChatSelectableElements())
        {
            var origin = element.TranslatePoint(new Point(0, 0), ChatContentHost);
            var bounds = new Rect(origin, new Size(element.ActualWidth, element.ActualHeight));
            if (bounds.Contains(point))
            {
                return element;
            }

            var deltaX = point.X < bounds.Left
                ? bounds.Left - point.X
                : point.X > bounds.Right
                    ? point.X - bounds.Right
                    : 0d;
            var deltaY = point.Y < bounds.Top
                ? bounds.Top - point.Y
                : point.Y > bounds.Bottom
                    ? point.Y - bounds.Bottom
                    : 0d;
            var distance = (deltaX * deltaX) + (deltaY * deltaY);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestElement = element;
            }
        }

        return nearestElement;
    }

    private string GetSelectedChatText()
    {
        var fragments = GetOrderedChatSelectableElements()
            .Select(GetSelectedText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        return fragments.Count == 0 ? string.Empty : string.Join(Environment.NewLine, fragments);
    }

    private FrameworkElement? GetFocusedChatSelectableElement()
    {
        return _chatSelectableElements.FirstOrDefault(element => element.IsKeyboardFocusWithin || element.IsFocused);
    }

    private static void SelectRange(FrameworkElement element, object start, object end)
    {
        switch (element)
        {
            case TextBox textBox when start is int startIndex && end is int endIndex:
                var boundedAnchor = Math.Max(0, Math.Min(startIndex, textBox.Text.Length));
                var boundedCurrent = Math.Max(0, Math.Min(endIndex, textBox.Text.Length));
                var selectionStart = Math.Min(boundedAnchor, boundedCurrent);
                var selectionLength = Math.Abs(boundedCurrent - boundedAnchor);
                textBox.Select(selectionStart, selectionLength);
                break;
            case RichTextBox richTextBox when start is TextPointer startPointer && end is TextPointer endPointer:
                richTextBox.Selection.Select(startPointer, endPointer);
                break;
        }
    }

    private static object? GetSelectionPoint(FrameworkElement element, Point point)
    {
        switch (element)
        {
            case TextBox textBox:
                var index = textBox.GetCharacterIndexFromPoint(point, true);
                return index >= 0 ? index : point.X <= 0 ? 0 : textBox.Text.Length;
            case RichTextBox richTextBox:
                return richTextBox.GetPositionFromPoint(point, true) ?? richTextBox.Document.ContentEnd;
            default:
                return null;
        }
    }

    private static object GetSelectionStart(FrameworkElement element)
    {
        return element switch
        {
            TextBox => 0,
            RichTextBox richTextBox => richTextBox.Document.ContentStart,
            _ => 0
        };
    }

    private static object GetSelectionEnd(FrameworkElement element)
    {
        return element switch
        {
            TextBox textBox => textBox.Text.Length,
            RichTextBox richTextBox => richTextBox.Document.ContentEnd,
            _ => 0
        };
    }

    private static void ClearSelection(FrameworkElement element)
    {
        switch (element)
        {
            case TextBox textBox:
                textBox.Select(0, 0);
                break;
            case RichTextBox richTextBox:
                richTextBox.Selection.Select(richTextBox.Document.ContentStart, richTextBox.Document.ContentStart);
                break;
        }
    }

    private static void SelectAll(FrameworkElement element)
    {
        switch (element)
        {
            case TextBox textBox:
                textBox.SelectAll();
                break;
            case RichTextBox richTextBox:
                richTextBox.Selection.Select(richTextBox.Document.ContentStart, richTextBox.Document.ContentEnd);
                break;
        }
    }

    private static string GetSelectedText(FrameworkElement element)
    {
        return element switch
        {
            TextBox textBox when textBox.SelectionLength > 0 => textBox.SelectedText,
            RichTextBox richTextBox when !richTextBox.Selection.IsEmpty => new TextRange(richTextBox.Selection.Start, richTextBox.Selection.End).Text.TrimEnd('\r', '\n'),
            _ => string.Empty
        };
    }

    private void ExecuteSendShortcut(KeyEventArgs e)
    {
        if (_viewModel.SendCommand.CanExecute(null))
        {
            _viewModel.SendCommand.Execute(null);
        }

        e.Handled = true;
    }

    private static void ExecuteViewModelCommand(ICommand command, object? parameter = null)
    {
        if (command.CanExecute(parameter))
        {
            command.Execute(parameter);
        }
    }

    private static FrameworkElement? FindSelectableChatTextBox(DependencyObject? origin)
    {
        var current = origin;
        while (current is not null)
        {
            if (current is FrameworkElement element && string.Equals(element.Tag as string, "ChatSelectable", StringComparison.Ordinal))
            {
                return element;
            }

            current = GetParentDependencyObject(current);
        }

        return null;
    }

    private static DependencyObject? GetParentDependencyObject(DependencyObject current)
    {
        if (current is FrameworkContentElement frameworkContentElement)
        {
            return frameworkContentElement.Parent
                ?? ContentOperations.GetParent(frameworkContentElement)
                ?? LogicalTreeHelper.GetParent(frameworkContentElement);
        }

        if (current is ContentElement contentElement)
        {
            return ContentOperations.GetParent(contentElement)
                ?? LogicalTreeHelper.GetParent(contentElement);
        }

        if (current is FrameworkElement frameworkElement)
        {
            return frameworkElement.Parent
                ?? LogicalTreeHelper.GetParent(frameworkElement)
                ?? GetVisualParent(frameworkElement);
        }

        return current switch
        {
            Visual => GetVisualParent(current),
            Visual3D => GetVisualParent(current),
            _ => LogicalTreeHelper.GetParent(current)
        };
    }

    private static DependencyObject? GetVisualParent(DependencyObject current)
    {
        try
        {
            return VisualTreeHelper.GetParent(current);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private void SyncUserInputPromptWindow()
    {
        if (!_viewModel.HasCurrentUserInputPrompt)
        {
            CloseUserInputPromptWindow(suppressCancel: true);
            return;
        }

        if (_userInputPromptWindow is null)
        {
            _userInputPromptWindow = new UserInputPromptWindow
            {
                DataContext = _viewModel
            };
            _userInputPromptWindow.Owner = Window.GetWindow(this);
            _userInputPromptWindow.Closed += OnUserInputPromptWindowClosed;
            _userInputPromptWindow.Show();
            return;
        }

        if (!_userInputPromptWindow.IsVisible)
        {
            _userInputPromptWindow.Show();
        }

        _userInputPromptWindow.Activate();
    }

    private void CloseUserInputPromptWindow(bool suppressCancel)
    {
        if (_userInputPromptWindow is null)
        {
            return;
        }

        _suppressUserInputWindowClosedCancel = suppressCancel;
        _userInputPromptWindow.Close();
        _suppressUserInputWindowClosedCancel = false;
    }

    private void OnUserInputPromptWindowClosed(object? sender, EventArgs e)
    {
        if (_userInputPromptWindow is not null)
        {
            _userInputPromptWindow.Closed -= OnUserInputPromptWindowClosed;
            _userInputPromptWindow = null;
        }

        if (!_suppressUserInputWindowClosedCancel && _viewModel.HasCurrentUserInputPrompt)
        {
            _viewModel.DismissUserInputPrompt();
        }
    }
}
