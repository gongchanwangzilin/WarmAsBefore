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
    private readonly List<(string Key, Func<string> Read)> _getters = new();
    private PluginEntry? _entry;
    private bool _loaded;

    public PluginSettingsPage(PluginHost host)
    {
        InitializeComponent();
        _host = host;
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

            var values = await _host.FieldValuesAsync(_entry);
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
            var values = _getters.ToDictionary(g => g.Key, g => g.Read());
            await _host.SaveSettingsAsync(_entry.Manifest.Id, values);
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
