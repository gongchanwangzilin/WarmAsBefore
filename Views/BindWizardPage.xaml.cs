using WarmAsBefore.Controls;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.MinecraftBridge;
using WarmAsBefore.Modules.SaveSystem;
using WarmAsBefore.Services;

namespace WarmAsBefore.Views;

/// <summary>
/// 首次连进一个 Minecraft 世界时的绑定向导。
/// 桥只负责把「这个世界还没接进 WAB」说出来并弹出这一页；选存档、建存档、选角色、
/// 绑定女仆都走 WAB 自己的内部函数，样式也跟存档页/角色库同一套。
/// </summary>
public partial class BindWizardPage : ContentPage
{
    private readonly SaveManager _save;
    private readonly CharacterLibrary _characters;
    private readonly GameEngine _engine;
    private readonly MaidBridgeService _bridge;

    private List<SaveSlot> _slots = new();
    private List<CharacterData> _charactersList = new();
    private List<(string Id, string Name)> _maids = new();

    private string _worldName = "";
    private string _ownerName = "";
    private bool _newSave;
    private string _selectedSaveId = "";
    private string _selectedCharId = "";
    private string _selectedMaidId = "";
    private bool _createCharFromMaid;
    private bool _busy;
    private bool _loaded;

    public BindWizardPage(SaveManager save, CharacterLibrary characters, GameEngine engine, MaidBridgeService bridge)
    {
        InitializeComponent();
        _save = save;
        _characters = characters;
        _engine = engine;
        _bridge = bridge;
    }

    public void Configure(string worldName, string ownerName, IReadOnlyList<(string Id, string Name)> maids, string currentSaveId)
    {
        _worldName = string.IsNullOrWhiteSpace(worldName) ? "Minecraft 世界" : worldName;
        _ownerName = ownerName ?? "";
        _maids = maids.ToList();
        if (_maids.Count > 0) _selectedMaidId = _maids[0].Id;
        if (!string.IsNullOrEmpty(currentSaveId) && _slots.Any(s => s.Id == currentSaveId))
            _selectedSaveId = currentSaveId;
        SubtitleLabel.Text = $"{_worldName} · 主人 {(_ownerName.Length > 0 ? _ownerName : "未知")} · {_maids.Count} 只女仆";
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        _loaded = true;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _slots = await _save.List();
            _charactersList = await _characters.ListAsync();
        }
        catch (Exception ex)
        {
            App.WriteLog("BindWizard.Load -> " + ex.Message);
        }
        if (_selectedSaveId.Length == 0)
        {
            var active = _engine.CurrentSaveId;
            if (!string.IsNullOrEmpty(active) && _slots.Any(s => s.Id == active)) _selectedSaveId = active;
        }
        // 默认落在「最省事且不会丢东西」的那一项：有能接的旧档就先选旧档，一个都没有才默认新建。
        if (_selectedSaveId.Length == 0)
            _selectedSaveId = _slots.FirstOrDefault(s => !string.IsNullOrEmpty(s.Character))?.Id ?? "";
        _newSave = _selectedSaveId.Length == 0;
        if (_selectedCharId.Length == 0 && _charactersList.Count > 0)
            _selectedCharId = _charactersList[0].Profile.Id;
        RenderWorld();
        RenderSaves();
        RenderCharacters();
        RenderMaids();
        UpdateFooter();
    }

    private void RenderWorld()
    {
        WorldHost.Children.Clear();
        AddLine("世界", _worldName, strong: true);
        AddLine("主人", _ownerName.Length > 0 ? _ownerName : "未知");
        AddLine("女仆", _maids.Count == 0 ? "还没看到女仆" : string.Join("、", _maids.Select(m => m.Name)));

        void AddLine(string key, string value, bool strong = false)
        {
            var row = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) }, ColumnSpacing = 10 };
            var k = new Label { Text = key, FontSize = 12, VerticalOptions = LayoutOptions.Center };
            k.SetDynamicResource(Label.TextColorProperty, "WarmMuted");
            var v = new Label
            {
                Text = value,
                FontSize = strong ? 14 : 13,
                FontAttributes = strong ? FontAttributes.Bold : FontAttributes.None,
                LineBreakMode = LineBreakMode.TailTruncation
            };
            v.SetDynamicResource(Label.TextColorProperty, strong ? "WarmDark" : "WarmBody");
            Grid.SetColumn(v, 1);
            row.Add(k);
            row.Add(v);
            WorldHost.Children.Add(row);
        }
    }

    private void RenderSaves()
    {
        SaveHost.Children.Clear();
        foreach (var slot in _slots)
        {
            var charName = _charactersList.FirstOrDefault(c => c.Profile.Id == slot.Character)?.Profile.Name;
            var desc = string.IsNullOrEmpty(slot.Character)
                ? "这一档没有绑定角色，不能直接接入"
                : $"角色 {charName ?? slot.Character} · {slot.SavedAt.ToLocalTime():MM-dd HH:mm}";
            SaveHost.Children.Add(PickRow(
                string.IsNullOrWhiteSpace(slot.Label) ? "未命名存档" : slot.Label,
                desc,
                selected: !_newSave && _selectedSaveId == slot.Id,
                enabled: !string.IsNullOrEmpty(slot.Character),
                onTap: () =>
                {
                    _newSave = false;
                    _selectedSaveId = slot.Id;
                    RenderSaves();
                    RenderCharacters();
                    UpdateFooter();
                }));
        }

        SaveHost.Children.Add(PickRow(
            "新建一份存档",
            $"为「{_worldName}」开一份新档，并绑定下面选的角色",
            selected: _newSave,
            enabled: true,
            onTap: () =>
            {
                _newSave = true;
                _selectedSaveId = "";   // 别留着旧档 id，免得后面误判成「已经选中一份档」
                RenderSaves();
                RenderCharacters();
                UpdateFooter();
            }));
    }

    private void RenderCharacters()
    {
        CharCard.IsVisible = _newSave;
        if (!_newSave) return;
        CharHost.Children.Clear();
        foreach (var ch in _charactersList)
        {
            CharHost.Children.Add(PickRow(
                ch.Profile.Name,
                string.IsNullOrWhiteSpace(ch.Profile.Personality) ? "性格未知" : ch.Profile.Personality,
                selected: !_createCharFromMaid && _selectedCharId == ch.Profile.Id,
                enabled: true,
                onTap: () =>
                {
                    _createCharFromMaid = false;
                    _selectedCharId = ch.Profile.Id;
                    RenderCharacters();
                    UpdateFooter();
                }));
        }
        var maidName = _maids.FirstOrDefault(m => m.Id == _selectedMaidId).Name ?? "女仆";
        CharHost.Children.Add(PickRow(
            $"用「{maidName}」的名字新建角色",
            "按女仆名建一个默认角色（温柔可爱），之后可以在角色库里慢慢改",
            selected: _createCharFromMaid,
            enabled: true,
            onTap: () =>
            {
                _createCharFromMaid = true;
                RenderCharacters();
                UpdateFooter();
            }));
        if (_charactersList.Count == 0 && !_createCharFromMaid)
            _createCharFromMaid = true;
    }

    private void RenderMaids()
    {
        MaidHost.Children.Clear();
        if (_maids.Count == 0)
        {
            var label = new Label { Text = "游戏里还没有女仆：先训服一只，再回来连接。", FontSize = 12 };
            label.SetDynamicResource(Label.TextColorProperty, "WarmMuted");
            MaidHost.Children.Add(label);
            return;
        }
        foreach (var maid in _maids)
        {
            MaidHost.Children.Add(PickRow(
                maid.Name.Length > 0 ? maid.Name : "未命名女仆",
                maid.Id,
                selected: _selectedMaidId == maid.Id,
                enabled: true,
                onTap: () =>
                {
                    _selectedMaidId = maid.Id;
                    RenderMaids();
                    if (_newSave) RenderCharacters();
                    UpdateFooter();
                }));
        }
    }

    private View PickRow(string title, string desc, bool selected, bool enabled, Action onTap)
    {
        var stack = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
        var titleLabel = new Label { Text = title, FontSize = 14, FontAttributes = FontAttributes.Bold };
        titleLabel.SetDynamicResource(Label.TextColorProperty, enabled ? "WarmDark" : "WarmMuted");
        stack.Add(titleLabel);
        if (!string.IsNullOrWhiteSpace(desc))
        {
            var descLabel = new Label { Text = desc, FontSize = 12, LineBreakMode = LineBreakMode.WordWrap };
            descLabel.SetDynamicResource(Label.TextColorProperty, "WarmMuted");
            stack.Add(descLabel);
        }

        var bar = new BoxView { WidthRequest = 3, CornerRadius = 2, IsVisible = selected, VerticalOptions = LayoutOptions.Fill };
        bar.SetDynamicResource(BoxView.ColorProperty, "AccentWarmFg");

        var grid = new Grid
        {
            ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) },
            ColumnSpacing = 10
        };
        grid.Add(bar);
        Grid.SetColumn(stack, 1);
        grid.Add(stack);

        var border = new Border
        {
            Padding = new Thickness(12, 9),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Content = grid,
            Opacity = enabled ? 1 : 0.55
        };
        border.SetDynamicResource(Border.StrokeProperty, "WEdgeBrush");
        if (selected) border.SetDynamicResource(VisualElement.BackgroundColorProperty, "BeigeAccent50");
        if (enabled)
            border.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(onTap) });
        return border;
    }

    private void UpdateFooter()
    {
        var hasMaid = _selectedMaidId.Length > 0;
        var hasChar = _newSave
            ? (_createCharFromMaid || _selectedCharId.Length > 0)
            : _slots.FirstOrDefault(s => s.Id == _selectedSaveId) is { } slot && !string.IsNullOrEmpty(slot.Character);
        BindButton.IsEnabled = hasMaid && hasChar && !_busy;
        BindButton.Text = _newSave ? "新建存档并绑定" : "开始绑定";
        if (_busy) return;
        if (StatusLabel.IsVisible) return;
        HintLabel.Text = !hasMaid
            ? "游戏里还没有可绑定的女仆"
            : !hasChar
                ? "先选一份存档，或为这份新档选一个角色"
                : "";
    }

    private async void OnBindClicked(object sender, EventArgs e)
    {
        if (_busy) return;
        _busy = true;
        BindButton.IsEnabled = false;
        StatusLabel.IsVisible = false;
        HintLabel.Text = "正在绑定…";
        try
        {
            var maidId = _selectedMaidId;
            string charId;
            if (_newSave)
            {
                var maidName = _maids.FirstOrDefault(m => m.Id == maidId).Name ?? "女仆";
                if (_createCharFromMaid)
                {
                    var created = _characters.CreateDefault(maidName, "女", "温柔可爱");
                    var added = await _characters.AddAsync(created);
                    if (!added) { Fail("新建角色失败：角色库可能读取异常。"); return; }
                    charId = created.Profile.Id;
                }
                else charId = _selectedCharId;

                if (string.IsNullOrEmpty(charId) || !_engine.Roster.ContainsKey(charId))
                {
                    Fail("这份新档还没选定角色，先在上面挑一个。");
                    return;
                }

                // 新建存档交给 WAB 自己的四步向导（世界 → 人设 → 细则 → 开始）：
                // Boot、人格记忆初始化、落盘都在那一套里，不用这里硬写一份结构不全的档。
                // 它走完会关掉自己，这里再拿新档继续绑定。
                IServiceProvider? services = Microsoft.Maui.IPlatformApplication.Current?.Services
                    ?? Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services;
                if (services?.GetService(typeof(NewRunPage)) is not NewRunPage page)
                {
                    Fail("新建存档向导没能打开（页面没注册）。");
                    return;
                }
                page.Character = charId;
                page.Edit = "";
                page.Wizard = "1";
                var closed = new TaskCompletionSource<bool>();
                page.Disappearing += (_, _) => closed.TrySetResult(true);
                HintLabel.Text = "正在等你走完新建存档的四步向导…";
                try { await Shell.Current.Navigation.PushModalAsync(page); }
                catch (Exception ex)
                {
                    App.WriteLog("BindWizard.NewRunPush -> " + ex);
                    Fail("新建存档向导没能打开：" + ex.Message);
                    return;
                }
                await closed.Task;
                if (!page.WizardSucceeded)
                {
                    StatusLabel.Text = "新建存档没有走完，绑定没有改动；可以再点一次重新来。";
                    StatusLabel.IsVisible = true;
                    Toast.Info("新建存档已取消，绑定没有改动。");
                    return;
                }
                charId = _engine.State.CharacterId;
                if (string.IsNullOrEmpty(charId) || string.IsNullOrEmpty(_engine.CurrentSaveId))
                {
                    Fail("新存档没能打开，先没有绑定。");
                    return;
                }
            }
            else
            {
                var slot = _slots.FirstOrDefault(s => s.Id == _selectedSaveId);
                if (slot is null) { Fail("没有找到选中的存档。"); return; }
                charId = slot.Character;
                if (!await _save.Load(slot.Id))
                {
                    Fail("读取存档失败。");
                    return;
                }
            }

            var (ok, message) = await _bridge.BindFromWizardAsync(maidId, charId);
            if (!ok) { Fail(message); return; }
            await Shell.Current.Navigation.PopModalAsync();
            Toast.Info(message);
        }
        catch (Exception ex)
        {
            App.WriteLog("BindWizard.Bind -> " + ex);
            Fail("绑定失败：" + ex.Message);
        }
        finally
        {
            _busy = false;
            UpdateFooter();
        }
    }

    private void Fail(string message)
    {
        StatusLabel.Text = message;
        StatusLabel.IsVisible = true;
        HintLabel.Text = message;
        Toast.Error(message);
    }
}
