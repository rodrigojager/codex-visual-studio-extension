using System.Collections.ObjectModel;

namespace CodexVsix.Models;

public sealed class UserInputPromptViewModel
{
    public string Title { get; set; } = string.Empty;

    public ObservableCollection<UserInputQuestionViewModel> Questions { get; } = new();
}
