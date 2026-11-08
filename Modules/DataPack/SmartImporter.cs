using System.IO.Compression;
using WarmAsBefore.Modules.SaveSystem;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.DataPack;

/// <summary>应用能识别的包类型。</summary>
public enum WabPackageKind
{
    Unknown,
    /// <summary>整包数据包（datapack.json 标记，整体替换数据目录）。</summary>
    DataPack,
    /// <summary>角色包（角色/&lt;名&gt;/character.json 或 角色主设定.txt）。</summary>
    CharacterPack,
    /// <summary>存档包（.wbsave：save.json [+ plugins/]）。</summary>
    SaveBundle,
    /// <summary>插件包（plugin.json）。</summary>
    PluginPack,
    /// <summary>素材包（manifest.json + 背景/音频/CG/地图）。</summary>
    MaterialPack,
}

public sealed record SmartImportResult(bool Ok, WabPackageKind Kind, string KindName, string Message);

/// <summary>
/// 统一的「ZIP 自动识别 + 自动加载」入口。
///
/// 此前四个导入入口各认各的格式：存档页只认 save.json、插件中心只认 plugin.json、
/// 角色页只认角色包、设置页只认数据包——拿错入口只能收获一句「导入失败」。
/// 现在**先偷看包内结构再路由**，任何入口、任何包（包括桌面直接拖进窗口的 zip）
/// 都会被送到正确的导入器。
///
/// 识别只读 zip 中央目录，不解压：一个几 KB 的 OpenRead 就能定类型，代价可忽略。
/// </summary>
public sealed class SmartImporter
{
    private readonly CharacterLibrary _library;
    private readonly SaveManager _save;
    private readonly PluginHost _plugins;
    private readonly PackImporter _packs;
    private readonly SceneCalibrationService _cal;

    public SmartImporter(CharacterLibrary library, SaveManager save, PluginHost plugins,
        PackImporter packs, SceneCalibrationService cal)
    {
        _library = library;
        _save = save;
        _plugins = plugins;
        _packs = packs;
        _cal = cal;
    }

    /// <summary>偷看包内结构判断类型。损坏的 zip 返回 Unknown，不抛异常。</summary>
    public static WabPackageKind Detect(string zipPath)
    {
        try
        {
            if (!File.Exists(zipPath)) return WabPackageKind.Unknown;
            using var zip = ZipFile.OpenRead(zipPath);
            var names = zip.Entries
                .Select(e => e.FullName.Replace('\\', '/'))
                .Where(n => n.Length > 0 && !n.EndsWith("/", StringComparison.Ordinal))
                .ToList();
            if (names.Count == 0) return WabPackageKind.Unknown;

            // 根级（或旧版数据包多套的 WarmAsBefore/ 一层下的）标记文件最可靠
            if (names.Any(n => n.Equals("datapack.json", StringComparison.OrdinalIgnoreCase)
                || n.Equals("WarmAsBefore/datapack.json", StringComparison.OrdinalIgnoreCase)))
                return WabPackageKind.DataPack;

            if (names.Any(n => n.Equals("save.json", StringComparison.OrdinalIgnoreCase)
                || n.EndsWith("/save.json", StringComparison.OrdinalIgnoreCase)))
                return WabPackageKind.SaveBundle;

            if (names.Any(n => n.Equals("plugin.json", StringComparison.OrdinalIgnoreCase)
                || names.Any(m => m.EndsWith("/plugin.json", StringComparison.OrdinalIgnoreCase)
                    && m.IndexOf('/') == m.LastIndexOf('/'))))   // 顶层一个目录 = 标准插件包
                return WabPackageKind.PluginPack;

            // 角色包：角色/<名>/character.json（或 角色主设定.txt），与素材包同用「角色/」目录，
            // 靠「有没有角色资料文件」区分
            if (names.Any(n => n.StartsWith("角色/", StringComparison.Ordinal)
                && (n.EndsWith("/character.json", StringComparison.OrdinalIgnoreCase)
                    || n.EndsWith("/角色主设定.txt", StringComparison.Ordinal))))
                return WabPackageKind.CharacterPack;

            var topDirs = names
                .Where(n => n.Contains('/'))
                .Select(n => n[..n.IndexOf('/')])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet();

            // 素材包：manifest.json + 素材目录（音频/CG/背景/地图 至少其一）
            if (names.Any(n => n.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                && topDirs.Overlaps(new[] { "背景", "音频", "CG", "地图" }))
                return WabPackageKind.MaterialPack;

            // 兜底：单文件 character.json（极简角色包）
            if (names.Any(n => n.Equals("character.json", StringComparison.OrdinalIgnoreCase)))
                return WabPackageKind.CharacterPack;

            return WabPackageKind.Unknown;
        }
        catch (Exception ex)
        {
            App.WriteLog("SmartImporter.Detect -> " + ex.Message);
            return WabPackageKind.Unknown;
        }
    }

    /// <summary>人类可读的类型名。</summary>
    public static string KindNameOf(WabPackageKind kind) => kind switch
    {
        WabPackageKind.DataPack => "整包数据包",
        WabPackageKind.CharacterPack => "角色包",
        WabPackageKind.SaveBundle => "存档包",
        WabPackageKind.PluginPack => "插件包",
        WabPackageKind.MaterialPack => "素材包",
        _ => "未知格式",
    };

    /// <summary>识别并加载。DataPack 带确认弹窗（整体替换数据目录属破坏性操作，绝不静默执行）。</summary>
    public async Task<SmartImportResult> ImportAsync(string zipPath)
    {
        var kind = Detect(zipPath);
        try
        {
            switch (kind)
            {
                case WabPackageKind.DataPack:
                    return await ImportDataPackAsync(zipPath);

                case WabPackageKind.CharacterPack:
                {
                    var (ok, message) = await _library.ImportFromZipAsync(zipPath);
                    return new(ok, kind, KindNameOf(kind), message);
                }

                case WabPackageKind.SaveBundle:
                {
                    var (ok, message) = await _save.Import(zipPath);
                    return new(ok, kind, KindNameOf(kind), message);
                }

                case WabPackageKind.PluginPack:
                {
                    // 自动入口只做「补缺」安装，不覆盖已装版本；要更新请走插件中心的「重装」。
                    var (ok, message) = await _plugins.InstallFromZipAsync(zipPath, overwrite: false);
                    return new(ok, kind, KindNameOf(kind), message);
                }

                case WabPackageKind.MaterialPack:
                {
                    var result = await _packs.ImportWithReport(zipPath);
                    if (!result.Ok)
                        return new(false, kind, KindNameOf(kind),
                            "素材包导入失败：" + string.Join("；", result.Messages.Take(3)));

                    var message = "素材包导入完成。";

                    // 统一标定（任何入口导入素材包都走这里）：素材包里的地图必须走与
                    // 手画完全相同的标定流程，导入完立即询问是否开始，不再由各页面自己弹。
                    if (result.PendingCalibration.Count > 0)
                    {
                        message += $"有 {result.PendingCalibration.Count} 张地图待标定。";
                        var go = await Shell.Current.DisplayAlert("地图标定",
                            $"这个素材包带进来 {result.PendingCalibration.Count} 张地图背景，" +
                            "需要依次标定（落脚点 + 缩放）。现在开始吗？",
                            "开始标定", "稍后再说");
                        if (go)
                            await _cal.DrainAsync(SceneCalibrationService.DefaultTargetAspect);
                    }
                    return new(true, kind, KindNameOf(kind), message);
                }

                default:
                {
                    // 识别不出结构的文件：按老行为当存档试一次（.json 裸存档仍走这条路），
                    // 失败就给出明确的「都不认识」，不再让用户猜哪里能喂。
                    var (ok, message) = await _save.Import(zipPath);
                    if (ok) return new(true, WabPackageKind.SaveBundle, KindNameOf(WabPackageKind.SaveBundle), message);
                    return new(false, WabPackageKind.Unknown, KindNameOf(WabPackageKind.Unknown),
                        "识别不出这是什么包（需要 数据包 / 角色包 / 存档包 / 插件包 / 素材包）。" +
                        (message.Length > 0 ? " 按存档导入的报错：" + message : ""));
                }
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("SmartImporter.Import(" + KindNameOf(kind) + ") -> " + ex);
            return new(false, kind, KindNameOf(kind), "导入失败：" + ex.Message);
        }
    }

    /// <summary>
    /// 整包数据包：与设置页同一套「解压校验 → 列内容 → 确认 → 整体替换」流程。
    /// 这里补弹确认框，保证从自动入口进来也不会跳过用户授权。
    /// </summary>
    private static async Task<SmartImportResult> ImportDataPackAsync(string zipPath)
    {
        string? tempDir = null;
        try
        {
            tempDir = await DataPackageService.UnpackForImportAsync(zipPath, null);
            var items = await Task.Run(() => DataPackageService.ListImportTargets(tempDir));
            var summary = items.Count == 0
                ? "（空包）"
                : string.Join("、", items.Take(8).Select(i =>
                    i.Size < 0 ? i.Name : $"{i.Name}（{DataPackageService.FormatSize(i.Size)}）"));

            var ok = await Shell.Current.DisplayActionSheet(
                $"这是整包数据包：将整体替换当前全部数据（共 {items.Count} 项：{summary}），完成后需要重启应用。",
                "取消", null, "确认导入") == "确认导入";
            if (!ok) return new(false, WabPackageKind.DataPack, "整包数据包", "已取消导入。");

            var (success, message) = await Task.Run(() => DataPackageService.ReplaceDataDir(tempDir!));
            if (success)
            {
                tempDir = null;   // ReplaceDataDir 已接管临时目录，不能再清
                return new(true, WabPackageKind.DataPack, "整包数据包", "数据已导入，请完全关闭并重新打开应用生效。");
            }
            return new(false, WabPackageKind.DataPack, "整包数据包", "导入失败：" + message);
        }
        finally
        {
            if (tempDir is not null) DataPackageService.CleanupImport(tempDir);
        }
    }
}
