using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CodexVsix.Models;

public sealed class ChatMessage : INotifyPropertyChanged
{
    private string _text;
    private string? _title;
    private string? _detail;

    public ChatMessage(bool isUser, string text, bool isEvent = false, string? title = null, string? detail = null)
    {
        IsUser = isUser;
        IsEvent = isEvent;
        _title = title;
        _detail = detail;
        _text = text;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsUser { get; }

    public bool IsEvent { get; }

    public string? Title
    {
        get => _title;
        set
        {
            _title = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasTitle));
        }
    }

    public string? Detail
    {
        get => _detail;
        set
        {
            _detail = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDetail));
        }
    }

    public bool HasTitle => !string.IsNullOrWhiteSpace(Title);

    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

    public string Text
    {
        get => _text;
        set
        {
            _text = value;
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
