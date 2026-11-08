using WarmAsBefore.Controls;
using WarmAsBefore.Modules.MinecraftBridge;
using WarmAsBefore.Services;

namespace WarmAsBefore.Views;

/// <summary>
/// 原生插件设置页（settings.kind = native）：按 plugin.json 里的 fields 现场生成表单。
/// 不认识的 type 一律当 text，插件写错了也不会把设置页弄崩。
/// </summary>
[QueryProperty(nameof(PluginId), "id")]
public partial class PluginSettingsPage : ContentPage
{
    private readonly PluginHost _host;
    private readonly MaidBridgeService _bridge;
    private readonly List<(string Key, Func<string> Read)> _getters = new();
    private PluginEntry? _entry;
    private bool _loaded;
    /// <summary>页面打开那一刻各字段的值：保存时只写「用户真正改过」的键，
    /// 不然这份可能开了一整晚的表单会把游戏里/别处刚保存的新值整表冲掉。</summary>
    private Dictionary<string, string> _initial = new(StringComparer.Ordinal);

    public PluginSettingsPage(PluginHost host, MaidBridgeService bridge)
    {
        InitializeComponent();
        _host = host;
        _bridge = bridge;
    }

    public string PluginId { get; set; } = "";

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        _loaded = true;
        _ = BuildAsync();
    }

    private async Task BuildAsync()
    {
        try
        {
            _entry = await _host.FindAsync(Uri.UnescapeDataString(PluginId));
            if (_entry is null)
            {
                DescLabel.Text = "找不到这个插件。";
                SaveButton.IsVisible = false;
                return;
            }
            TitleLabel.Text = _entry.Manifest.Name + " · 设置";
            DescLabel.Text = _entry.Manifest.Description;
            if (!string.Equals(_entry.Manifest.Settings.Kind, "native", StringComparison.OrdinalIgnoreCase))
            {
                DescLabel.Text = "这个插件没有使用应用内设置页（声明为 " + _entry.Manifest.Settings.Kind + "）。";
                SaveButton.IsVisible = false;
                return;
            }

            // 桥插件多两颗入口：向导是唯一「手动配置女仆」的地方，实时面板是详细数值页。
            BridgeActions.IsVisible = string.Equals(_entry.Manifest.Id, MaidBridgeService.PluginId,
                StringComparison.OrdinalIgnoreCase);

            var values = await _host.FieldValuesAsync(_entry);
            _initial = new Dictionary<string, string>(values, StringComparer.Ordinal);
            foreach (var f in _entry.Manifest.Settings.Fields)
            {
                if (f is null || string.IsNullOrWhiteSpace(f.Key)) continue;
                FieldsHost.Add(BuildField(f, values.TryGetValue(f.Key, out var v) ? v : f.Default));
            }
            if (FieldsHost.Children.Count == 0)
                DescLabel.Text = (DescLabel.Text ?? "") + "\n（插件没有声明任何字段，保存会把空设置写回。）";
        }
        catch (Exception ex)
        {
            DescLabel.Text = "读取插件设置失败：" + ex.Message;
            App.WriteLog("PluginSettingsPage.Build -> " + ex);
        }
    }

    private async void OnBindWizardClicked(object sender, EventArgs e)
    {
        var (ok, message) = await _bridge.RequestBindWizardAsync();
        if (!ok) Toast.Error(message);
    }

    private async void OnOpenPanelClicked(object sender, EventArgs e)
    {
        var entry = await _host.FindAsync(MaidBridgeService.PluginId);
        if (entry is null) return;
        var index = 0;
        for (var i = 0; i < entry.Manifest.Pages.Count; i++)
        {
            if ((entry.Manifest.Pages[i].Title ?? "").Contains("实时", StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }
        var (ok, message, url) = await _host.GetPageUrlAsync(MaidBridgeService.PluginId, index);
        if (!ok) { Toast.Error(message); return; }
        await Shell.Current.GoToAsync(
            $"pluginWeb?url={Uri.EscapeDataString(url)}&title={Uri.EscapeDataString("Minecraft 女仆桥 · 实时面板")}");
    }

    private View BuildField(PluginSettingField f, string value)
    {
        var box = new VerticalStackLayout { Spacing = 4 };
        box.Add(new Label { Text = string.IsNullOrWhiteSpace(f.Label) ? f.Key : f.Label, FontSize = 13 });
        if (!string.IsNullOrWhiteSpace(f.Help))
            box.Add(new Label { Text = f.Help, FontSize = 11, Opacity = 0.7 });

        var type = (f.Type ?? "text").Trim().ToLowerInvariant();
        if (type == "switch")
        {
            var sw = new Switch
            {
                IsToggled = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1",
                HorizontalOptions = LayoutOptions.Start
            };
            box.Add(sw);
            _getters.Add((f.Key, () => sw.IsToggled ? "true" : "false"));
        }
        else if (type == "select" && f.Options.Count > 0)
        {
            var picker = new Picker { FontSize = 13, HorizontalOptions = LayoutOptions.Fill };
            foreach (var o in f.Options) picker.Items.Add(o);
            picker.SelectedItem = f.Options.Contains(value) ? value : f.Options[0];
            box.Add(picker);
            _getters.Add((f.Key, () => picker.SelectedItem?.ToString() ?? ""));
        }
        else
        {
            var entry = new Entry
            {
                Text = value,
                IsPassword = type == "secret",
                Keyboard = type == "number" ? Keyboard.Numeric : Keyboard.Default,
                FontSize = 13,
                BackgroundColor = (Color)Application.Current!.Resources["InputBg"],
                TextColor = (Color)Application.Current.Resources["SurfaceFg"]
            };
            box.Add(entry);
            _getters.Add((f.Key, () => entry.Text ?? ""));
        }
        return box;
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (_entry is null) return;
        try
        {
            // 只写「用户在页面上真正改过的键」：这份表单可能开了一整晚，期间游戏里改过的值
            // 不该被它整表冲掉——以前是全量回写，谁后保存谁说了算，游戏侧的修改就这样被无声覆盖。
            var dirty = new Dictionary<string, string>();
            foreach (var g in _getters)
            {
                var now = g.Read();
                _initial.TryGetValue(g.Key, out var was);
                if (!string.Equals(now, was, StringComparison.Ordinal)) dirty[g.Key] = now;
            }
            if (dirty.Count == 0)
            {
                StatusLabel.Text = "没有改动，什么都不用写。";
                StatusLabel.IsVisible = true;
                return;
            }
            await _host.SaveSettingsAsync(_entry.Manifest.Id, dirty);
            MaidBridgeService.Diag("应用内设置页保存（只写脏键 " + dirty.Count + " 个）: " + string.Join(",", dirty.Keys));
            foreach (var (k, v) in dirty) _initial[k] = v;
            StatusLabel.Text = "已保存，下一次用到钩子时生效。";
            StatusLabel.IsVisible = true;
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "保存失败：" + ex.Message;
            StatusLabel.IsVisible = true;
        }
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        try { await Shell.Current.GoToAsync(".."); }
        catch (Exception ex) { App.WriteLog("PluginSettingsPage.Back -> " + ex.Message); }
    }
}
