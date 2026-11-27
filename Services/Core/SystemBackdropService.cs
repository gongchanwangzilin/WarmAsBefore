namespace WarmAsBefore.Services;

/// <summary>
/// 窗口透视档的系统 backdrop：让 Windows 自己模糊桌面，而不是我们糊一张静态图。
///
/// 为什么不是「全站都靠它」：它只负责窗口背后（桌面/其它窗口），对**应用内**的背景图无能为力 ——
/// 自家壁纸画在窗口内容层里，系统 backdrop 根本看不见。所以图片档与纯色档用
/// <see cref="FrostImageService"/> 的糊底，只有「看穿到桌面」这一档才把活交给系统。
///
/// 三条纪律：
///   1. 能力探测先做。DesktopAcrylicController.IsSupported() / MicaController.IsSupported()
///      答不上来（类型找不到、老系统）就不启用 —— 系统 backdrop 不支持时是**静默无效**的，
///      如果我们以为它生效了，页面就只剩一层薄纱压在看不清的桌面上，字直接不可读。
///   2. 反射拿类型与属性。WinAppSDK 的版本跟着 MAUI 走，类型名在 1.3~1.7 之间挪过位置；
///      反射失败就当没有，绝不让编译期依赖把整个工程钉死在某个 SDK 小版本上。
///   3. 全程 try/catch。backdrop 是装饰，失败必须静默回落到第一层（自家糊底）。
/// </summary>
public static class SystemBackdropService
{
    /// <summary>系统 backdrop 此刻是否真的生效（决定页面薄纱的浓度）。</summary>
    public static bool IsActive { get; private set; }

    /// <summary>上一次探测/启用的结论，写日志用。</summary>
    public static string Status { get; private set; } = "未尝试";

    private static bool _resolved;
    private static Type? _backdropType;

    /// <summary>
    /// 给当前主窗口挂上系统 backdrop。返回 true 表示「现在真的有一层系统级模糊在窗口背后」。
    /// 已经生效时直接返回 true；硬失败（系统不支持/类型不存在）之后不再重试。
    /// </summary>
    public static bool TryApply(Color fallback)
    {
        if (IsActive) return true;
        if (_resolved && _backdropType is null) return false;

#if WINDOWS
        try
        {
            var win = Application.Current?.Windows
                .FirstOrDefault(w => w.Handler is not null && w is not Views.ToolWindow);
            if (win?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window w)
            {
                // 窗口还没建好：不算失败，下一次 Repaint 再来
                Status = "窗口未就绪";
                return false;
            }

            _backdropType ??= Resolve();
            if (_backdropType is null)
            {
                _resolved = true;
                Status = "系统不支持（无 backdrop 类型 / 控制器 IsSupported=false）";
                return false;
            }

            var prop = w.GetType().GetProperty("SystemBackdrop");
            if (prop is null || !prop.CanWrite)
            {
                _resolved = true;
                _backdropType = null;
                Status = "SystemBackdrop 属性不存在";
                return false;
            }

            var backdrop = Activator.CreateInstance(_backdropType);
            if (backdrop is null) { Status = "backdrop 实例化失败"; return false; }

            // 不支持时的兜底色：设成当前世界的页面底，比系统默认的黑/灰更接近用户的界面
            TrySet(backdrop, "FallbackColor", ToNative(fallback));

            prop.SetValue(w, backdrop);
            IsActive = prop.GetValue(w) is not null;
            Status = IsActive ? "系统 backdrop 已启用（" + _backdropType.Name + "）" : "属性写入后被忽略";
            App.WriteLog("SystemBackdrop: " + Status);
            return IsActive;
        }
        catch (Exception ex)
        {
            IsActive = false;
            Status = "反射失败：" + ex.GetType().Name;
            App.WriteLog("SystemBackdrop.TryApply -> " + ex);
            return false;
        }
#else
        Status = "非 Windows";
        return false;
#endif
    }

    /// <summary>撤掉系统 backdrop（切回图片档/纯色档时，别让桌面继续透）。</summary>
    public static void Clear()
    {
        if (!IsActive && !_resolved) { Status = "未启用"; return; }
        var was = IsActive;
        IsActive = false;
#if WINDOWS
        if (!was) return;
        try
        {
            var win = Application.Current?.Windows
                .FirstOrDefault(w => w.Handler is not null && w is not Views.ToolWindow);
            if (win?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window w) return;
            w.GetType().GetProperty("SystemBackdrop")?.SetValue(w, null);
            Status = "已撤下";
        }
        catch (Exception ex) { App.WriteLog("SystemBackdrop.Clear -> " + ex.Message); }
#endif
    }

#if WINDOWS
    /// <summary>
    /// 挑一个这套系统真能用的 backdrop：
    ///   DesktopAcrylic —— Win11 22H2+（22621），模糊 + 噪点 + 色调，最接近「磨砂」；
    ///   Mica           —— Win11 21H2+（22000），只取桌面壁纸主色，个性差一点但也是系统级模糊。
    /// 判定靠 WinAppSDK 自己的控制器，不靠版本号猜：版本号猜错会得到一个「透明但没糊」的窗口。
    /// </summary>
    private static Type? Resolve()
    {
        if (ControllerSupported("Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController"))
            return FindType("Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop");
        if (ControllerSupported("Microsoft.UI.Composition.SystemBackdrops.MicaController"))
            return FindType("Microsoft.UI.Xaml.Media.MicaBackdrop");
        return null;
    }

    private static bool ControllerSupported(string typeName)
    {
        try
        {
            var t = FindType(typeName);
            var probe = t?.GetMethod("IsSupported", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            return probe?.Invoke(null, null) is true;
        }
        catch (Exception ex)
        {
            App.WriteLog("SystemBackdrop.Probe(" + typeName + ") -> " + ex.GetType().Name);
            return false;
        }
    }

    private static Type? FindType(string name)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                if (asm.IsDynamic) continue;
                var t = asm.GetType(name, false);
                if (t is not null) return t;
            }
            catch { /* 故意忽略：某个动态程序集查不了，继续找下一个 */ }
        }
        // 还没被 JIT 加载过的 WinAppSDK 程序集：按名字拉一次
        foreach (var asmName in new[] { "Microsoft.InteractiveExperiences", "Microsoft.UI.Composition" })
        {
            try
            {
                var t = System.Reflection.Assembly.Load(asmName).GetType(name, false);
                if (t is not null) return t;
            }
            catch { /* 故意忽略：程序集不在，说明这套 SDK 没有该能力 */ }
        }
        return null;
    }

    private static void TrySet(object target, string property, object? value)
    {
        try
        {
            var p = target.GetType().GetProperty(property);
            if (p is null || !p.CanWrite) return;
            if (value is not null && !p.PropertyType.IsInstanceOfType(value)) return;
            p.SetValue(target, value);
        }
        catch { /* 故意忽略：FallbackColor 是锦上添花 */ }
    }

    private static Windows.UI.Color ToNative(Color c) =>
        Windows.UI.Color.FromArgb((byte)(c.Alpha * 255), (byte)(c.Red * 255), (byte)(c.Green * 255), (byte)(c.Blue * 255));
#endif
}
