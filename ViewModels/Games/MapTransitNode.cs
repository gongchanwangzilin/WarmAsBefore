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

/// <summary>画布上的一个临时节点（可双击为连线端点），坐标由 MapTransit.X/Y 持久化。</summary>
public sealed partial class MapTransitNode : ObservableObject
{
    public MapTransit Transit { get; }

    public string Id => Transit.Id;
    public string Name => Transit.Name;
    public string Note => Transit.Note;

    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;

    public MapTransitNode(MapTransit transit)
    {
        Transit = transit;
        _x = transit.X;
        _y = transit.Y;
    }
}
