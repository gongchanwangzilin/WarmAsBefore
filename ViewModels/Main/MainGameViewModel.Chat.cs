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

public sealed partial class MainGameViewModel
{
    /// <summary>
    /// #10 待发送的文件附件（用户点了"带文件"后先挂在这里，随下一条消息一起发出）。
    /// 为空时发送路径与旧行为完全一致。
    /// </summary>
    public ObservableCollection<Models.ChatAttachment> PendingAttachments { get; } = new();
    /// <summary>待发附件数量标签（"带文件 · 2"），0 时隐藏。</summary>
    [ObservableProperty] private string _pendingAttachmentsLabel = "";
    /// <summary>是否有待发附件（控制按钮高亮/清除按钮显隐）。</summary>
    [ObservableProperty] private bool _hasPendingAttachments;

    // ============ 送礼 / 使用面板（主界面聊天时显示） ============
    [ObservableProperty] private bool _isGiftPanelVisible;
    [ObservableProperty] private string _giftPanelTitle = "送给小雨";

    // ===== 主动外发确认（角色要给你的手机发消息） =====

    /// <summary>待确认的主动消息文本（空 = 没有待确认的）。</summary>
    [ObservableProperty] private string _outboundPendingText = "";
    private string _outboundPendingId = "";
    public bool HasOutboundPending => !string.IsNullOrEmpty(OutboundPendingText);
    partial void OnOutboundPendingTextChanged(string value) => OnPropertyChanged(nameof(HasOutboundPending));

    private void RefreshOutbound()
    {
        var first = _bridge.PendingOutbound.FirstOrDefault();
        _outboundPendingId = first?.Id ?? "";
        OutboundPendingText = first?.Text ?? "";
    }

    [RelayCommand]
    private async Task ConfirmOutbound()
    {
        var id = _outboundPendingId;
        if (string.IsNullOrEmpty(id)) return;
        var ok = await _bridge.ConfirmOutboundAsync(id);
        if (!ok) Toast.Warn("发送失败：可能是超过 48 小时没互动，QQ 不允许机器人主动发起会话；让对方先给机器人发条消息即可。");
        RefreshOutbound();
    }

    [RelayCommand]
    private void CancelOutbound()
    {
        if (string.IsNullOrEmpty(_outboundPendingId)) return;
        _bridge.CancelOutbound(_outboundPendingId);
        RefreshOutbound();
    }

    // ============ #10 对话携带文件 ============

    /// <summary>选一个文件挂到「待发附件」，下一条消息会带着它一起发给 AI。</summary>
    [RelayCommand]
    private async Task PickAttachment()
    {
        if (IsCalibrationBlocked) return;
        try
        {
            var pick = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "选择要带给 TA 的文件",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".txt", ".md", ".json", ".csv", ".log", ".xml", ".pdf",
                        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".mp3", ".wav", ".mp4", "*" } },
                    { DevicePlatform.Android, new[] { "*/*" } },
                    { DevicePlatform.iOS, new[] { "public.item" } }
                })
            });
            if (pick is null) return;
            var att = Modules.AiChat.ChatEngine.BuildAttachmentFromPath(pick.FullPath);
            PendingAttachments.Add(att);
            RefreshAttachmentState();
        }
        catch (Exception ex)
        {
            App.WriteLog("MainGameViewModel.PickAttachment -> " + ex);
        }
    }

    /// <summary>清空待发附件。</summary>
    [RelayCommand]
    private void ClearAttachments() => ClearAttachmentsInternal();

    private void ClearAttachmentsInternal()
    {
        PendingAttachments.Clear();
        RefreshAttachmentState();
    }

    /// <summary>刷新待发附件相关的展示状态（数量标签、按钮高亮）。</summary>
    private void RefreshAttachmentState()
    {
        var n = PendingAttachments.Count;
        HasPendingAttachments = n > 0;
        PendingAttachmentsLabel = n > 0 ? $"带文件 · {n}" : "";
    }

    /// <summary>#12：挑一个已导入的表情发出去（用户侧）。</summary>
    [RelayCommand]
    private async Task SendSticker()
    {
        if (IsCalibrationBlocked) return;
        await _stickers.EnsureLoadedAsync();
        var items = _stickers.Items;
        if (items.Count == 0)
        {
            Toast.Info("还没有导入表情。到「设置 → 表情包库」导入几张吧。");
            return;
        }
        var names = items.Select(i => i.Name).Distinct().Take(24).ToArray();
        var choice = await Shell.Current.DisplayActionSheet("发一个表情", "取消", null, names);
        if (string.IsNullOrEmpty(choice) || choice == "取消") return;
        var item = _stickers.Find(choice);
        if (item is null) return;
        // 界面上先把自己发的表情挂出来
        AddMessage("user", "", item.Path);
        var charId = _engine.State.CharacterId;
        if (string.IsNullOrEmpty(charId)) return;
        IsThinking = true;
        var stickerNames = new List<string>();
        try
        {
            _chat.SetStickerContext(_stickers.BuildContextForAi());
            var desc = string.IsNullOrWhiteSpace(item.Description) ? "" : $"，{item.Description}";
            var prompt = $"(用户发来一个表情包：「{item.Name}」{desc})";
            var reply = await _chat.Send(charId, prompt);
            var rawForStats = reply;
            var (replyClean, found) = Services.StickerLibrary.SplitMarkers(reply);
            reply = replyClean;
            stickerNames.AddRange(found);
            var (segs, _) = await ProcessReplyAsync(reply);
            if (segs.Count == 0) segs.Add(reply.Trim());
            RefreshCharacterName();
            foreach (var seg in segs) AddMessage("assistant", seg);
            foreach (var sn in stickerNames)
            {
                var st = _stickers.Find(sn);
                if (st is not null) AddMessage("assistant", "", st.Path);
            }
            _ = ApplyChatAffectionAsync(charId, prompt, reply, string.Join(" ", segs));
            _ = AutoSave();
        }
        catch (Exception ex)
        {
            App.WriteLog("MainGameViewModel.SendSticker -> " + ex);
        }
        finally { IsThinking = false; }
    }

    // ============ 送礼 / 使用面板 ============

    /// <summary>展开/收起送礼面板，打开时刷新已购商品。</summary>
    [RelayCommand]
    private void ToggleGiftPanel()
    {
        IsGiftPanelVisible = !IsGiftPanelVisible;
        if (IsGiftPanelVisible)
            OnPropertyChanged(nameof(GiftItems));
    }

    /// <summary>送礼面板模式：true=送礼给小雨，false=自己使用。</summary>
    [ObservableProperty] private bool _isGiftMode = true;

    /// <summary>切换送礼面板模式（送礼 ⇄ 使用）。</summary>
    [RelayCommand]
    private void ToggleGiftMode() => IsGiftMode = !IsGiftMode;

    /// <summary>已购商品（面板数据源）。</summary>
    public IReadOnlyList<Models.ShopItem> GiftItems => _gifts.OwnedItems;

    /// <summary>是否已有已购商品（空态提示用）。</summary>
    public bool HasGiftItems => GiftItems.Count > 0;

    /// <summary>送礼：消耗库存，小雨回应显示为一条聊天消息。</summary>
    [RelayCommand]
    private async Task GiftItem(Models.ShopItem item)
    {
        if (item is null) return;
        IsGiftPanelVisible = false;
        AddMessage("user", $"送给你：{item.Emoji} {item.Name}");
        IsThinking = true;
        try
        {
            var charId = _engine.State.CharacterId;
            var raw = await _gifts.GiftAsync(item);
            raw = await ExecuteMoveMarkersAsync(raw);
            var clean = StripStatDirective(raw);
            AddMessage("assistant", clean);
            _ = ApplyChatAffectionAsync(charId, $"你送 TA 了一份「{item.Name}」", raw, clean);
            CaptureMoment(charId, item.Price >= 50 ? 5 : 3, $"送礼：{item.Name}");
            var e = ResolveEmotion(clean);
            SetEmotion(e ?? RandomEmotion() ?? _defaultEmotion);
            _ = _speech.Speak(clean);
            _ = AutoSave();
        }
        catch (Exception ex)
        {
            App.WriteLog("MainGame.GiftItem -> " + ex);
            AddMessage("assistant", "……");
        }
        finally { IsThinking = false; }
    }

    /// <summary>使用：消耗库存，小雨回应显示为一条聊天消息。</summary>
    [RelayCommand]
    private async Task UseItem(Models.ShopItem item)
    {
        if (item is null) return;
        IsGiftPanelVisible = false;
        AddMessage("user", $"我用了：{item.Emoji} {item.Name}");
        IsThinking = true;
        try
        {
            var charId = _engine.State.CharacterId;
            var raw = await _gifts.UseAsync(item);
            raw = await ExecuteMoveMarkersAsync(raw);
            var clean = StripStatDirective(raw);
            AddMessage("assistant", clean);
            _ = ApplyChatAffectionAsync(charId, $"你自己用了「{item.Name}」。", raw, clean);
            var e = ResolveEmotion(clean);
            SetEmotion(e ?? RandomEmotion() ?? _defaultEmotion);
            _ = _speech.Speak(clean);
            _ = AutoSave();
        }
        catch (Exception ex)
        {
            App.WriteLog("MainGame.UseItem -> " + ex);
            AddMessage("assistant", "……");
        }
        finally { IsThinking = false; }
    }

    // ============ 多段回答：按工具标记 / 动作标记切段 ============

    private static readonly HashSet<string> ActionMarkerNames = new() { "下沉", "雀跃", "颤抖" };

    private static readonly Regex ReplyMarkersRegex = new(@"【(移动:[^】]+|CG:[^】]+|状态:[^】]+|下沉|雀跃|颤抖)】", RegexOptions.Compiled);

    private static readonly Regex ActionMarkerRegex = new(@"【(下沉|雀跃|颤抖)】", RegexOptions.Compiled);

    /// <summary>数值指令标记：给机器读的（送礼/自用等直接 AddMessage 的路径要用它把标记从展示文本里剥掉）。</summary>
    private static readonly Regex StatDirectiveRegex = new(@"【\s*状态[^】]*】", RegexOptions.Compiled);

    /// <summary>
    /// 把 AI 回复拆成多段：正文按标记位置切段，移动/CG 工具调用就地执行（各自算一段），
    /// 动作标记直接执行动画；开发者展示模式保留动作标记文本，普通模式隐藏。
    /// </summary>
    private async Task<(List<string> Segments, bool HasReaction)> ProcessReplyAsync(string reply)
    {
        // 架构流程图打点：模型文本进入后处理管线（第5层 eng）。
        Services.Core.FlowTracer.Trace("eng.postprocess", _engine?.State?.CharacterId);
        if (string.IsNullOrWhiteSpace(reply))
            return (new List<string>(), false);

        var matches = ReplyMarkersRegex.Matches(reply).Cast<Match>().ToList();
        var hasReaction = matches.Any(m => ActionMarkerNames.Contains(m.Groups[1].Value));
        var segments = new List<string>();
        if (matches.Count == 0)
        {
            segments.Add(reply.Trim());
            return (segments, false);
        }

        var devShow = _settings.Current.DeveloperShowcaseUnlocked;
        var last = 0;
        foreach (var m in matches)
        {
            var head = reply[last..m.Index];
            if (!string.IsNullOrWhiteSpace(head)) segments.Add(head.Trim());
            last = m.Index + m.Length;
            var inner = m.Groups[1].Value;

            if (inner.StartsWith("移动:", StringComparison.Ordinal))
            {
                var note = await ExecuteSingleMoveAsync(inner[3..].Trim());
                if (!string.IsNullOrWhiteSpace(note)) segments.Add(note.Trim());
            }
            else if (inner.StartsWith("CG:", StringComparison.Ordinal))
            {
                await TriggerSingleCgAsync(inner[3..].Trim());
            }
            else if (ActionMarkerNames.Contains(inner))
            {
                RunActionMarker(inner);
                if (devShow)
                {
                    if (segments.Count == 0) segments.Add($"【{inner}】");
                    else segments[^1] += $"【{inner}】";
                }
            }
        }
        var tail = reply[last..];
        if (!string.IsNullOrWhiteSpace(tail)) segments.Add(tail.Trim());
        return (segments, hasReaction);
    }
}
