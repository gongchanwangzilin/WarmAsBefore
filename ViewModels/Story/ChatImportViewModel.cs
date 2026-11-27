using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Controls;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>结果页里「这个文件读出了什么」的一行。</summary>
public sealed partial class ChatImportSourceRow : ObservableObject
{
    public string Name { get; init; } = "";
    public string Detail { get; init; } = "";
    public bool HasError { get; init; }
}

/// <summary>「TA 在这个记录里叫」的一个候选：发言者 + 条数。</summary>
public sealed record ChatImportSpeaker(string Name, string Label, int Count);

/// <summary>
/// 存档开始时的聊天记录导入：多选文件、粘贴一大段、嗅探格式、整理成记忆与初始关系。
///
/// 这一页只做「读进来、算出来、看一眼」，真正写入存档是 NewRunViewModel 在「开始这一局」时
/// 调 <see cref="ChatLogImportService.ApplyAsync"/> —— 新档号还没分配之前写记忆会写错档。
/// 所以这里最后一步是「带回本局」，页尾那句话说的就是这个意思。
/// </summary>
public sealed partial class ChatImportViewModel : ObservableObject
{
    private readonly ChatLogImportService _import;
    private readonly CharacterLibrary _chars;

    private string _characterId = "";
    private string _characterName = "";
    private string _nickname = "";
    private List<ImportedMessage> _messages = new();
    private ChatLogDigest _digest = new();
    private int _skipped;
    private bool _reloadingSpeakers;

    public ObservableCollection<ChatImportSourceRow> Sources { get; } = new();
    public ObservableCollection<ChatImportSpeaker> Speakers { get; } = new();

    [ObservableProperty] private string _characterDisplay = "";
    [ObservableProperty] private string _pastedText = "";
    [ObservableProperty] private string _pasteCount = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyText = "正在读取…";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private bool _hasWarnings;
    [ObservableProperty] private string _warnings = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _initial = "";
    [ObservableProperty] private string _explain = "";
    [ObservableProperty] private string _range = "";
    [ObservableProperty] private bool _showSpeakerPicker;
    [ObservableProperty] private ChatImportSpeaker? _selectedSpeaker;

    public ChatImportViewModel(ChatLogImportService import, CharacterLibrary chars)
    {
        _import = import;
        _chars = chars;
    }

    public async Task LoadAsync(string characterId)
    {
        _characterId = characterId;
        try
        {
            var data = (await _chars.ListAsync()).FirstOrDefault(c => c.Profile.Id == characterId);
            _characterName = data?.Profile.Name ?? "";
            _nickname = data?.Profile.Nickname ?? "";
            CharacterDisplay = _characterName.Length > 0 ? $"这一局与「{_characterName}」" : "这一局";
        }
        catch (Exception ex)
        {
            App.WriteLog("ChatImport.Load -> " + ex.Message);
            CharacterDisplay = "这一局";
        }
    }

    partial void OnPastedTextChanged(string value)
        => PasteCount = string.IsNullOrWhiteSpace(value) ? "" : $"{value.Trim().Length} 字";

    partial void OnSelectedSpeakerChanged(ChatImportSpeaker? value)
    {
        if (_reloadingSpeakers || value is null || _messages.Count == 0) return;
        _digest = _import.RebuildDigest(_messages, _characterName, _nickname, value.Name, _skipped);
        Render();
    }

    // ==================== 读 ====================

    [RelayCommand]
    private async Task PickFiles()
    {
        if (IsBusy) return;
        try
        {
            var files = await FilePicker.Default.PickMultipleAsync(new PickOptions
            {
                PickerTitle = "选择聊天记录（可多选）",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    [DevicePlatform.WinUI] = new[] { ".txt", ".mht", ".mhtml", ".md", ".json", ".csv", ".log" },
                    [DevicePlatform.Android] = new[] { "text/plain", "text/html", "text/markdown", "application/json", "text/csv", "multipart/related" },
                    [DevicePlatform.iOS] = new[] { "public.plain-text", "public.html", "public.json", "public.comma-separated-values-text" },
                })
            });
            if (files is null) return;
            var list = files.ToList();
            if (list.Count == 0) return;

            IsBusy = true;
            HasError = false;
            Status = "";
            var inputs = new List<(string Name, string Text)>();
            for (var i = 0; i < list.Count; i++)
            {
                BusyText = $"正在读取 {i + 1}/{list.Count}：{list[i].FileName}";
                try
                {
                    inputs.Add((list[i].FileName, await ChatLogImportService.ReadTextAsync(list[i])));
                }
                catch (Exception ex)
                {
                    App.WriteLog("ChatImport.Read -> " + ex.Message);
                    inputs.Add((list[i].FileName, ""));
                }
            }

            BusyText = "正在识别格式并整理…";
            var preview = await Task.Run(() => _import.Combine(inputs, _characterName, _nickname));
            ApplyPreview(preview, filesLabel: list.Count == 1 ? list[0].FileName : $"{list.Count} 个文件");
        }
        catch (Exception ex)
        {
            App.WriteLog("ChatImport.Pick -> " + ex);
            ShowError("没能读到文件：" + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ParsePaste()
    {
        if (IsBusy) return;
        var text = (PastedText ?? "").Trim();
        if (text.Length == 0)
        {
            ShowError("先粘贴一段聊天记录，再点解析。");
            return;
        }

        IsBusy = true;
        HasError = false;
        BusyText = "正在识别格式并整理…";
        try
        {
            var preview = await Task.Run(() => _import.Combine(
                new[] { ("粘贴的内容", text) }, _characterName, _nickname));
            ApplyPreview(preview, "粘贴的内容");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ClearPaste()
    {
        PastedText = "";
        Status = "";
    }

    private void ApplyPreview(ChatImportPreview preview, string filesLabel)
    {
        Sources.Clear();
        foreach (var s in preview.Sources)
        {
            var detail = s.Error.Length > 0
                ? s.Error
                : $"{s.Format} · 读出 {s.Messages} 条" + (s.Skipped > 0 ? $" · 跳过 {s.Skipped} 行" : "") +
                  (s.Note.Length > 0 ? $" · {s.Note}" : "");
            Sources.Add(new ChatImportSourceRow { Name = s.Name, Detail = detail, HasError = s.Error.Length > 0 });
        }

        if (!preview.Ok)
        {
            ShowError(preview.Error.Length > 0 ? preview.Error : "没有读出任何消息。");
            return;
        }

        _messages = preview.Messages;
        _digest = preview.Digest;
        _skipped = preview.Digest.Skipped;
        HasError = false;
        HasResult = true;
        Status = $"已从「{filesLabel}」整理出 {preview.Messages.Count} 条对话。";
        Warnings = string.Join("\n", preview.Warnings);
        HasWarnings = preview.Warnings.Count > 0;
        if (preview.DuplicatesDropped > 0)
            Status += $" 去掉重复 {preview.DuplicatesDropped} 条。";
        UiSounds.Confirm();
        Render();
    }

    /// <summary>把一份结果刷到界面上；换「谁是 TA」之后也走这里。</summary>
    private void Render()
    {
        if (!_digest.HasAnything) { HasResult = false; return; }

        Summary = _digest.Messages + " 条对话 · 跳过 " + _digest.Skipped + " 处重复/空行" +
                  (_digest.UserInitiated > 0 || _digest.CharacterInitiated > 0
                      ? $" · 你发起 {_digest.UserInitiatedRatio:P0}"
                      : "");
        var rangeText = _digest.FirstAt is null
            ? "没有时间戳：按文件里的顺序整理"
            : $"{_digest.FirstAt:yyyy-MM-dd} → {_digest.LastAt:yyyy-MM-dd}（{_digest.SpanDays} 天）";
        Range = rangeText;
        Initial = $"初始好感 {WarmAsBefore.Helpers.AffectionScale.TextOf(_digest.Affection)} · 信任 {_digest.Trust}";
        Explain = _digest.Explain();

        _reloadingSpeakers = true;
        Speakers.Clear();
        foreach (var kv in _digest.Speakers)
        {
            Speakers.Add(new ChatImportSpeaker(
                kv.Key,
                $"{kv.Key}（{kv.Value} 条）{(kv.Key == _digest.CharacterSpeaker ? " · TA" : kv.Key == _digest.UserSpeaker ? " · 你" : "")}",
                kv.Value));
        }
        SelectedSpeaker = Speakers.FirstOrDefault(s => s.Name == _digest.CharacterSpeaker) ?? Speakers.FirstOrDefault();
        ShowSpeakerPicker = Speakers.Count > 1;
        _reloadingSpeakers = false;
    }

    private void ShowError(string message)
    {
        HasResult = false;
        HasError = true;
        Error = message;
        Status = "";
        UiSounds.Back();
    }

    // ==================== 带回本局 ====================

    [RelayCommand]
    private async Task BringBack()
    {
        if (IsBusy || !_digest.HasAnything) return;
        try
        {
            var source = Sources.Count == 0
                ? "聊天记录"
                : string.Join(" + ", Sources.Select(s => s.Name).Take(3)) + (Sources.Count > 3 ? $" 等 {Sources.Count} 个" : "");
            _import.Stage(new StagedChatImport
            {
                CharacterId = _characterId,
                Digest = _digest,
                Source = source
            });
            Toast.Success($"已带回 {_digest.Messages} 条对话，开始这一局时写入记忆");
            UiSounds.Confirm();
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            App.WriteLog("ChatImport.BringBack -> " + ex);
            ShowError("没能带回本局：" + ex.Message);
        }
    }

    [RelayCommand]
    private async Task Back()
    {
        UiSounds.Back();
        await Shell.Current.GoToAsync("..");
    }
}
