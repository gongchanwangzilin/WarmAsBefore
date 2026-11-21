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

public sealed record CharacterBackdropState(
    string Path,
    string Fit,
    double CropX,
    double CropY,
    double CropW,
    double CropH,
    bool IsVideo);
