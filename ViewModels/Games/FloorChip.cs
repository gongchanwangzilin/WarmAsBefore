using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Dispatching;
using WarmAsBefore.Controls;
using WarmAsBefore.DesignSystem.Theme;
using WarmAsBefore.Drawables;
using WarmAsBefore.Helpers;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>楼层条上的一枚档：点它切到这层；「全部」或再点当前层 = 退出分层视图。</summary>
public sealed partial class FloorChip : ObservableObject
{
    public string Label { get; }
    public int? Floor { get; }
    public bool IsAll { get; }
    public Command TapCommand { get; }

    [ObservableProperty] private bool _isCurrent;

    internal FloorChip(MapViewModel owner, string label, int? floor, bool isAll)
    {
        Label = label;
        Floor = floor;
        IsAll = isAll;
        TapCommand = new Command(() => owner.TapFloor(this));
    }
}
