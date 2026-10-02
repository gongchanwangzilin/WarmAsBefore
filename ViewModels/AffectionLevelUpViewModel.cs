using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WarmAsBefore.ViewModels;

/// <summary>好感等级提升动画页数据。</summary>
public sealed partial class AffectionLevelUpViewModel : ObservableObject
{
    [ObservableProperty] private int _level = 1;
    [ObservableProperty] private string _title = "初识";
    [ObservableProperty] private string _characterName = "";
    [ObservableProperty] private string? _spritePath;
    [ObservableProperty] private string? _backgroundPath;

    public string LevelText => $"Lv.{Level}";
    public string TitleLine => $"「{Title}」 · {CharacterName}";
    public bool HasSprite => !string.IsNullOrWhiteSpace(SpritePath);
    public bool HasBackground => !string.IsNullOrWhiteSpace(BackgroundPath);

    /// <summary>载荷就位后通知全部计算结果。</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(LevelText));
        OnPropertyChanged(nameof(TitleLine));
        OnPropertyChanged(nameof(HasSprite));
        OnPropertyChanged(nameof(HasBackground));
    }

    [RelayCommand]
    private async Task Close()
        => await Microsoft.Maui.Controls.Shell.Current.GoToAsync("..");
}