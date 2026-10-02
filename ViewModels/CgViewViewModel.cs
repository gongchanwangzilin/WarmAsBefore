using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WarmAsBefore.ViewModels;

/// <summary>CG 全屏播放页数据。</summary>
public sealed partial class CgViewViewModel : ObservableObject
{
    [ObservableProperty] private string _imagePath = "";
    [ObservableProperty] private string _title = "";

    public bool HasImage => !string.IsNullOrWhiteSpace(ImagePath);

    public void Refresh() => OnPropertyChanged(nameof(HasImage));

    [RelayCommand]
    private async Task Skip()
        => await Microsoft.Maui.Controls.Shell.Current.GoToAsync("..");
}