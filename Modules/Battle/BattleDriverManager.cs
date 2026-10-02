using System.Text.Json;
using WarmAsBefore.Modules.Tools;

namespace WarmAsBefore.Modules.Battle;

/// <summary>
/// 战斗驱动管理器：决定使用哪一套战斗系统。
///  1) 扫描 {root}/battles/*/battle.json —— 找到即用外部驱动（Python/Java 可视化脚本）；
///  2) 否则使用内置驱动（进程内 C# 回合制，保证开箱即用）。
/// 同样的「外部优先、内置兜底」策略与工具模式一致。
/// </summary>
public sealed class BattleDriverManager
{
    private readonly RuntimeManager _runtimes;

    public BattleDriverManager(RuntimeManager runtimes) => _runtimes = runtimes;

    public static string BattlesRoot => Path.Combine(App.RootDirectory, "battles");

    /// <summary>全部可用的外部战斗驱动清单。</summary>
    public List<ExternalBattleManifest> ExternalManifests { get; } = new();

    public void ScanExternals()
    {
        ExternalManifests.Clear();
        try
        {
            if (!Directory.Exists(BattlesRoot)) return;
            foreach (var dir in Directory.GetDirectories(BattlesRoot))
            {
                var manifestPath = Path.Combine(dir, "battle.json");
                if (!File.Exists(manifestPath)) continue;
                try
                {
                    ExternalBattleManifest? m = JsonSerializer.Deserialize<ExternalBattleManifest>(File.ReadAllText(manifestPath));
                    if (m is null || string.IsNullOrWhiteSpace(m.Name)) continue;
                    m.Name = m.Name.Trim();
                    ExternalManifests.Add(m);
                }
                catch (Exception ex)
                {
                    App.WriteLog("BattleDriverManager.ScanExternals -> " + ex);
                }
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("BattleDriverManager.ScanExternals -> " + ex);
        }
    }

    /// <summary>
    /// 创建一个战斗驱动：优先外部（目录名 {name}），运行时缺失则回退内置；
    /// 无外部清单时直接用内置驱动。
    /// </summary>
    public async Task<IBattleDriver> CreateDefaultAsync(BattleBootData boot)
    {
        ScanExternals();
        foreach (var m in ExternalManifests)
        {
            var workDir = Path.Combine(BattlesRoot, m.Name);
            var ext = await ExternalBattleDriver.StartAsync(m, workDir, _runtimes);
            if (ext is not null) return ext;
            App.WriteLog($"BattleDriverManager 外部驱动 {m.Name} 拉取失败（运行时不可用？），回退内置");
        }
        return new BuiltinBattleDriver(boot);
    }

    /// <summary>创建一个使用内置驱动的实例（供此会话单独使用）。</summary>
    public IBattleDriver CreateBuiltin(BattleBootData? boot = null) => new BuiltinBattleDriver(boot);
}