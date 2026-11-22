using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Helpers;

namespace WarmAsBefore.ViewModels;

/// <summary>好感等级提升动画页数据。</summary>
public sealed partial class AffectionLevelUpViewModel : ObservableObject
{
    [ObservableProperty] private int _level = 1;
    /// <summary>升到这一级时的绝对点数（0–1000）。</summary>
    [ObservableProperty] private int _points;
    [ObservableProperty] private string _title = "初识";
    [ObservableProperty] private string _characterName = "";
    [ObservableProperty] private string? _spritePath;
    [ObservableProperty] private string? _backgroundPath;

    public string LevelText => $"Lv.{Level}";
    /// <summary>「300/1000」：和主界面同一套刻度，别让动画页自成一套说法。</summary>
    public string PointsText =>
        $"{Helpers.AffectionScale.Clamp(Points)}/{Helpers.AffectionScale.MaxPoints} · 跨过了 {Helpers.AffectionScale.CapFor(Level)} 点";
    partial void OnPointsChanged(int value) => OnPropertyChanged(nameof(PointsText));
    partial void OnLevelChanged(int value) => OnPropertyChanged(nameof(PointsText));
    public string TitleLine => $"「{Title}」 · {CharacterName}";
    public bool HasSprite => !string.IsNullOrWhiteSpace(SpritePath);
    public bool HasBackground => !string.IsNullOrWhiteSpace(BackgroundPath);

    /// <summary>载荷就位后通知全部计算结果。</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(LevelText));
        OnPropertyChanged(nameof(PointsText));
        OnPropertyChanged(nameof(TitleLine));
        OnPropertyChanged(nameof(HasSprite));
        OnPropertyChanged(nameof(HasBackground));
    }

    [RelayCommand]
    private async Task Close()
        => await Microsoft.Maui.Controls.Shell.Current.GoToAsync("..");
}