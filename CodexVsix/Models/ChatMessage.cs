using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace CodexVsix.Models;

public sealed class ChatMessage : INotifyPropertyChanged
{
    private string _text;
    private string _displayText;
    private string? _title;
    private string? _detail;
    private bool _hasCustomDisplayText;
    private bool _renderMarkdown;

    public ChatMessage(
        bool isUser,
        string text,
        bool isEvent = false,
        string? title = null,
        string? detail = null,
        bool? supportsMarkdownText = null,
        bool supportsMarkdownDetail = false)
    {
        IsUser = isUser;
        IsEvent = isEvent;
        SupportsMarkdownText = supportsMarkdownText ?? (!isUser && !isEvent);
        SupportsMarkdownDetail = supportsMarkdownDetail;
        _title = title;
        _detail = detail;
        _text = text;
        _displayText = text;
        _renderMarkdown = SupportsMarkdownText || SupportsMarkdownDetail;
        PromptSkillNames.CollectionChanged += HandlePromptSkillNamesChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsUser { get; }

    public bool IsEvent { get; }

    public bool SupportsMarkdownText { get; }

    public bool SupportsMarkdownDetail { get; }

    public string? Title
    {
        get => _title;
        set
        {
            _title = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasTitle));
            OnPropertyChanged(nameof(HasHeader));
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
            OnPropertyChanged(nameof(CanToggleMarkdownView));
            OnPropertyChanged(nameof(HasHeader));
            OnPropertyChanged(nameof(ShowMarkdownDetail));
            OnPropertyChanged(nameof(ShowPlainDetail));
        }
    }

    public bool HasTitle => !string.IsNullOrWhiteSpace(Title);

    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

    public bool HasHeader => HasTitle || CanToggleMarkdownView;

    public ObservableCollection<string> PromptSkillNames { get; } = new();

    public bool HasPromptSkillNames => PromptSkillNames.Count > 0;

    public string DisplayText
    {
        get => _displayText;
        private set
        {
            _displayText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDisplayText));
        }
    }

    public bool HasDisplayText => !string.IsNullOrWhiteSpace(DisplayText);

    public bool RenderMarkdown
    {
        get => _renderMarkdown;
        set
        {
            if (_renderMarkdown == value)
            {
                return;
            }

            _renderMarkdown = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsTextMode));
            OnPropertyChanged(nameof(IsRenderedMode));
            OnPropertyChanged(nameof(ShowMarkdownText));
            OnPropertyChanged(nameof(ShowPlainDetail));
            OnPropertyChanged(nameof(ShowMarkdownDetail));
        }
    }

    public bool IsTextMode => !RenderMarkdown;

    public bool IsRenderedMode => RenderMarkdown;

    public bool CanToggleMarkdownView =>
        (SupportsMarkdownText && !string.IsNullOrWhiteSpace(Text))
        || (SupportsMarkdownDetail && HasDetail);

    public bool ShowMarkdownText => SupportsMarkdownText && RenderMarkdown;

    public bool ShowMarkdownDetail => SupportsMarkdownDetail && HasDetail && RenderMarkdown;

    public bool ShowPlainDetail => HasDetail && !ShowMarkdownDetail;

    public string Text
    {
        get => _text;
        set
        {
            _text = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanToggleMarkdownView));
            OnPropertyChanged(nameof(HasHeader));
            OnPropertyChanged(nameof(ShowMarkdownText));
            if (!_hasCustomDisplayText)
            {
                DisplayText = value;
            }
        }
    }

    public void ApplyPromptSkillDisplay(System.Collections.Generic.IEnumerable<string> skillNames, string? displayText)
    {
        _hasCustomDisplayText = false;
        PromptSkillNames.Clear();

        foreach (var skillName in skillNames.Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            PromptSkillNames.Add(skillName);
        }

        var normalizedDisplayText = displayText ?? string.Empty;
        _hasCustomDisplayText = PromptSkillNames.Count > 0 || !string.Equals(normalizedDisplayText, _text, System.StringComparison.Ordinal);
        DisplayText = _hasCustomDisplayText ? normalizedDisplayText : _text;
    }

    private void HandlePromptSkillNamesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasPromptSkillNames));
    }

    public void ToggleMarkdownView()
    {
        if (!CanToggleMarkdownView)
        {
            return;
        }

        RenderMarkdown = !RenderMarkdown;
    }

    public void SetMarkdownView(bool renderMarkdown)
    {
        if (!CanToggleMarkdownView)
        {
            return;
        }

        RenderMarkdown = renderMarkdown;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
