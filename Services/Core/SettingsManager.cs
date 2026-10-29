using System.Text.Json;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;


public sealed class SettingsManager
{
    private readonly StorageProvider _store;
    private UserSettings _current = new();

    /// <summary>上一次读 settings.json 失败：这时内存里是默认值，写盘等于把用户的设置整份盖掉。</summary>
    private bool _loadFaulted;

    public UserSettings Current => _current;
    public event Action? Applied;

    public SettingsManager(StorageProvider store) => _store = store;

    public void Apply(UserSettings s)
    {
        _current = s;
        Applied?.Invoke();
    }

    public async Task Persist()
    {
        // 上一次没读出来 → 内存里是默认值，这时候写盘等于把用户的设置整份盖掉。先试着重读，读不通就不写。
        if (!await RetryRestoreAsync())
        {
            App.WriteLog("SettingsManager.Persist -> 已跳过落盘：settings.json 这次读不出来，不拿默认值盖用户的设置");
            return;
        }
        await _store.Save("settings", _current);
    }

    public async Task Restore()
    {
        try
        {
            var s = await _store.Load<UserSettings>("settings");
            if (s is not null) _current = s;
            _loadFaulted = false;
        }
        catch (Exception ex)
        {
            _loadFaulted = true;
            App.WriteLog("SettingsManager.Restore -> " + ex.Message);
        }
        await MigrateLegacyAsync();
    }

    /// <summary>读盘失败过就惰性重试：读回来了才允许写盘（重试成功时以磁盘那份为准）。</summary>
    private async Task<bool> RetryRestoreAsync()
    {
        if (!_loadFaulted) return true;
        try
        {
            var retry = await _store.Load<UserSettings>("settings");
            if (retry is not null) _current = retry;
            _loadFaulted = false;
            App.WriteLog("SettingsManager: settings.json 这次读出来了，恢复落盘");
            return true;
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsManager.RetryRestore -> " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 同步读取设置：必须在构建窗口/界面之前调用。
    /// 原因是标题页要靠 <see cref="UserSettings.DisclaimerAcknowledged"/> 决定弹不弹免责声明，
    /// 异步读会晚于首帧，导致「已确认过也每次启动都弹」。文件很小，阻塞时间可忽略。
    /// </summary>
    public void RestoreBlocking()
    {
        try { Task.Run(async () => await Restore()).GetAwaiter().GetResult(); }
        catch (Exception ex) { App.WriteLog("SettingsManager.RestoreBlocking -> " + ex.Message); }
    }

    /// <summary>旧版单一 GlassStyle → 新的三个独立开关；毛玻璃隐含磨砂。</summary>
    private async Task MigrateLegacyAsync()
    {
        // 上一次没读成功时 _current 是默认值：这时候迁移完回写，等于把用户全部设置换成出厂值。
        if (_loadFaulted) return;
        var c = _current;
        var changed = false;

        if (!(string.IsNullOrEmpty(c.GlassStyle) || c.FrostEnabled || c.GlassEnabled || c.LiquidEnabled))
        {
            c = c with
            {
                FrostEnabled = c.GlassStyle is "frost" or "glass",
                GlassEnabled = c.GlassStyle == "glass",
                LiquidEnabled = c.GlassStyle == "liquid",
                GlassStyle = ""
            };
            changed = true;
        }

        // 旧的「一条档案 = 厂商+模型+密钥」迁移成「厂商 + 多模型」：
        // 同端点同密钥的档案合并成一家厂商，各自的模型并进这家厂商的模型列表。
        if (c.AiProviders.Count == 0 && c.AiProfiles.Count > 0)
        {
            var providers = new List<AiProvider>();
            foreach (var p in c.AiProfiles)
            {
                var normUrl = (p.Url ?? "").Trim();
                var keys = (p.Keys ?? new List<string>()).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
                var match = providers.FirstOrDefault(v =>
                    string.Equals(v.Url, normUrl, StringComparison.OrdinalIgnoreCase)
                    && v.Keys.SequenceEqual(keys, StringComparer.Ordinal));

                if (match is not null)
                {
                    var idx = providers.IndexOf(match);
                    providers[idx] = match with
                    {
                        Models = match.Models.Concat(new[] { new AiProviderModel { Model = (p.Model ?? "").Trim(), Label = p.Name ?? "" } })
                            .Where(m => !string.IsNullOrWhiteSpace(m.Model))
                            .GroupBy(m => m.Model, StringComparer.OrdinalIgnoreCase)
                            .Select(g => g.First())
                            .ToList()
                    };
                }
                else
                {
                    providers.Add(new AiProvider
                    {
                        Name = string.IsNullOrWhiteSpace(p.Name) ? (p.Model ?? "") : p.Name!,
                        Url = normUrl,
                        Keys = keys,
                        Enabled = p.Enabled,
                        TimeoutSeconds = p.TimeoutSeconds,
                        Models = string.IsNullOrWhiteSpace(p.Model)
                            ? new List<AiProviderModel>()
                            : new List<AiProviderModel> { new() { Model = p.Model.Trim() } }
                    });
                }
            }
            if (providers.Count > 0)
            {
                c = c with { AiProviders = providers };
                changed = true;
                App.WriteLog($"Settings.Migrate: {c.AiProfiles.Count} 条端点档案 → {providers.Count} 家厂商");
            }
        }

        // 旧的单一周期设置 → 按性别分开。判据是「字典为空」而不是「值为默认」：
        // 老存档里根本没有 CycleRules 这个键，反序列化后就是空字典，新存档则由设置页写满。
        if (c.CycleRules.Count == 0)
        {
#pragma warning disable CS0618   // 这两个属性就是为了这次搬家才留在模型里的
            var fem = new CycleRule { Enabled = true, CycleLength = c.CycleLength, PeriodLength = c.PeriodLength };
#pragma warning restore CS0618
            c = c with
            {
                CycleRules = new Dictionary<string, CycleRule>(StringComparer.Ordinal)
                {
                    ["女"] = fem,
                    ["男"] = new CycleRule { Enabled = false },
                    ["其他"] = new CycleRule { Enabled = false },
                }
            };
            changed = true;
            App.WriteLog($"Settings.Migrate: 单一周期 → 按性别（女 {fem.CycleLength}/{fem.PeriodLength}，男/其他无周期）");
        }

        if (!changed) return;
        _current = c;
        await _store.Save("settings", _current);
    }
}
