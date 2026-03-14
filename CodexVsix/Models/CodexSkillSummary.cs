using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CodexVsix.Models;

public sealed class CodexSkillSummary : INotifyPropertyChanged
{
    private bool _isEnabled;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string ShortDescription { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
            {
                return;
            }

            _isEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool IsSystem { get; set; }

    public string ScopeLabel { get; set; } = string.Empty;

    public string DisplayTitle => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName;

    public string Summary => !string.IsNullOrWhiteSpace(ShortDescription) ? ShortDescription : Description;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
