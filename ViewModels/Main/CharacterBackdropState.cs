using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Dispatching;
using WarmAsBefore.Controls;
using WarmAsBefore.Models;
using WarmAsBefore.Models.Background;
using WarmAsBefore.Modules.AiChat;
using WarmAsBefore.Modules.Automation;
using WarmAsBefore.Modules.Outfit;
using WarmAsBefore.Modules.RealWorld;
using WarmAsBefore.Modules.SaveSystem;
using WarmAsBefore.Services;
using RealTimeProvider = WarmAsBefore.Modules.RealWorld.TimeProvider;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 舞台要显示的角色背景：路径 + 适配方式 + 归一化裁剪框。
/// 单独成一条记录，视图层只在它真正变化时切一场，而不是每次刷新都重放。
/// </summary>
public sealed record CharacterBackdropState(
    string Path,
    string Fit,
    double CropX,
    double CropY,
    double CropW,
    double CropH,
    bool IsVideo);
