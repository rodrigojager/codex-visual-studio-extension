using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using CodexVsix.ViewModels;

namespace CodexVsix;

public partial class CodexSettingsToolWindowControl : UserControl
{
    private readonly CodexToolWindowViewModel _viewModel;

    public CodexSettingsToolWindowControl()
    {
        InitializeComponent();
        _viewModel = CodexViewModelHost.GetOrCreate();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        RefreshSectionContent();
        UpdateSectionVisibility();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName)
            || string.Equals(e.PropertyName, nameof(CodexToolWindowViewModel.SelectedSettingsSection), System.StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(CodexToolWindowViewModel.Localization), System.StringComparison.Ordinal))
        {
            RefreshSectionContent();
            UpdateSectionVisibility();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Unloaded -= OnUnloaded;
    }

    private void RefreshSectionContent()
    {
        if (_viewModel.IsMcpSectionSelected || _viewModel.IsSkillsSectionSelected)
        {
            if (_viewModel.RefreshIntegrationsCommand.CanExecute(null))
            {
                _viewModel.RefreshIntegrationsCommand.Execute(null);
            }
        }
        else if (_viewModel.RefreshCodexStatusCommand.CanExecute(null))
        {
            _viewModel.RefreshCodexStatusCommand.Execute(null);
        }
    }

    private void UpdateSectionVisibility()
    {
        SettingsPlaceholderText.Visibility = _viewModel.IsSettingsDetailPanelVisible ? Visibility.Collapsed : Visibility.Visible;
        AccountSectionPanel.Visibility = _viewModel.IsAccountSectionSelected ? Visibility.Visible : Visibility.Collapsed;
        CodexSectionPanel.Visibility = _viewModel.IsCodexSectionSelected ? Visibility.Visible : Visibility.Collapsed;
        McpSectionPanel.Visibility = _viewModel.IsMcpSectionSelected ? Visibility.Visible : Visibility.Collapsed;
        SkillsSectionPanel.Visibility = _viewModel.IsSkillsSectionSelected ? Visibility.Visible : Visibility.Collapsed;
    }
}
