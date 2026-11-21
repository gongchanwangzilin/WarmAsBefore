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

public sealed class DialogueMessage
{
    public string Role { get; set; } = "";
    public string Text { get; set; } = "";
    public string Time { get; set; } = "";
    public bool IsUser => Role == "user";
    /// <summary>#12：这是一条表情消息时，指向表情图片的路径；普通文字消息为 null。</summary>
    public string? StickerPath { get; set; }
    /// <summary>是否显示为表情（模板里用它切换显示形态）。</summary>
    public bool HasSticker => !string.IsNullOrEmpty(StickerPath);
    /// <summary>文本气泡的显示条件（表情消息不显示空气泡）。</summary>
    public bool ShowBubbleMine => IsUser && !HasSticker;
    public bool ShowBubbleTheirs => !IsUser && !HasSticker;
    /// <summary>表情图片的显示条件（用户发的靠右、AI 发的靠左）。</summary>
    public bool ShowStickerMine => IsUser && HasSticker;
    public bool ShowStickerTheirs => !IsUser && HasSticker;
}
