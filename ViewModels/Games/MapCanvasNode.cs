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

/// <summary>画布上的一张场景卡片（位置由 Scene.X/Y 持久化），按地点分组配色。</summary>
public sealed partial class MapCanvasNode : ObservableObject
{
    public MapScene Scene { get; }
    public MapLocation Location { get; }

    public string GroupKey => Location.Id;
    public string Id => Scene.Id;
    public string SceneName => Scene.Name;
    public string LocationName => Location.Name;

    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isCurrent;

    public MapCanvasNode(MapScene scene, MapLocation location)
    {
        Scene = scene;
        Location = location;
    }
}
