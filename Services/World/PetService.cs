using System.Runtime.InteropServices;

#if WINDOWS
using Microsoft.UI.Windowing;
using Windows.Graphics;
using WinRT.Interop;
// 分层窗口是个静态类，不能拿变量接住；给它起个别名，下面的「设置 → 桌面」那段才读得下去
using Surface = WarmAsBefore.Platforms.Windows.PetLayeredWindow;
#endif

namespace WarmAsBefore.Services;

/// <summary>
/// 桌宠模式（仅 Windows）：把主窗口收纳隐藏，桌面只保留一个显示角色立绘的置顶小窗（桌宠），
/// 系统托盘出现图标：单击恢复主窗口，右键菜单可恢复/退出。
/// 主窗口最小化时也会自动收纳成桌宠。
/// </summary>
public sealed class PetService
{
    // 这两个平台都要用的字段必须放在 #if 之外：
    // 它们原先声明在 #if WINDOWS 块里，而 Android 分支（#else 之后）也在引用，
    // 于是 Android 目标一直编译不过（_instance / _settings / _petMode 找不到）。
    private static PetService? _instance;
    private readonly SettingsManager _settings;
    private bool _petMode;

    private static readonly System.Text.RegularExpressions.Regex SelfConfigPattern =
        new(@"\{\{\s*pet\s*:\s*(?<body>[^}]*)\}\}", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// 她说的话里如果夹着「自己调桌宠」的指令，就在这里执行并从文字里摘掉。
    /// 只在用户开了「自主配置权限」时生效；可调的只有外观与走动这一小撮，改完立刻落盘——
    /// 她自己的改动也必须活过重启，否则下次进来又变回去，用户只会以为没生效。
    /// </summary>
    public static string ApplySelfConfig(string reply)
        => _instance is { } inst ? inst.ApplySelfConfigInner(reply) : reply;

    private string ApplySelfConfigInner(string reply)
    {
        if (string.IsNullOrEmpty(reply)) return reply;
        var current = _settings.Current;
        if (!current.PetSelfConfig) return SelfConfigPattern.Replace(reply, "").Trim();

        var next = current;
        var changed = false;
        foreach (System.Text.RegularExpressions.Match m in SelfConfigPattern.Matches(reply))
        {
            foreach (var pair in m.Groups["body"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                var key = pair[..eq].Trim().ToLowerInvariant();
                var val = pair[(eq + 1)..].Trim();
                switch (key)
                {
                    case "scale" when double.TryParse(val, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var scale):
                        next = next with { PetScale = Math.Clamp(scale, 0.4, 2.6) }; changed = true; break;
                    case "glow" when double.TryParse(val, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var glow):
                        next = next with { PetGlow = Math.Clamp(glow, 0, 1) }; changed = true; break;
                    case "font" when int.TryParse(val, out var font):
                        next = next with { PetFontSize = Math.Clamp(font, 10, 22) }; changed = true; break;
                    case "fps" when int.TryParse(val, out var fps):
                        next = next with { PetFps = Math.Clamp(fps, 10, 60) }; changed = true; break;
                    case "walking" when val is "off" or "roam":
                        next = next with { PetWalking = val }; changed = true; break;
                }
            }
        }

        var clean = SelfConfigPattern.Replace(reply, "").Trim();
        if (!changed) return clean;
        _settings.Apply(next);
        _ = _settings.Persist();
        return clean;
    }

#if WINDOWS
    private const int WM_USER = 0x0400;
    private const int WM_TRAYICON = WM_USER + 21;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_COMMAND = 0x0111;
    private const uint NIM_ADD = 0;
    private const uint NIM_MODIFY = 1;
    private const uint NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 0x1;
    private const uint NIF_ICON = 0x2;
    private const uint NIF_TIP = 0x4;
    private const int ID_SHOW = 40001;
    private const int ID_EXIT = 40002;
    private const int ID_PET_SETTINGS = 40003;
    private const int ID_TRAY = 1;

    private IntPtr _msgWnd;
    private IntPtr _trayIcon;
    private bool _iconAdded;
    private bool _listening;
    private Window? _petWindow;
    private Views.PetPage? _page;         // 话术层那一页：设置项要直接落到它身上的控件
    private bool _chromeVisible;
    private bool _chromeExpanded;
    private bool _fullscreenHidden;       // 被「全屏时收起」收走ing：这期间不许任何地方把小窗亮回来

    // 头顶小窗的几何：尺寸按 DIP 设计（右下存的是换算后的物理像素，供鼠标命中判定用）
    private const double ChromeWidthDip = 340;
    private const double ChromeRowDip = 56;        // 收起时只有输入行
    private double _chromeRowDip = ChromeRowDip;   // 关掉输入气泡就是 0：小窗只剩气泡那一块
    private const double BubbleGapDip = 6;         // VerticalStackLayout 的行距
    private const double BubblePadDip = 12;        // 气泡上下内边距（Padding="10,6"）
    private const double BubbleMinDip = 40;
    private const double BubbleMaxDip = 300;       // 再长就截断：小窗不该变成一堵盖住半块屏幕的墙
    private const double BubbleInnerWidthDip = ChromeWidthDip - 20;
    private double _bubbleDip = 106;
    private IntPtr _chromeHwnd;
    private int _chromeLoggedH;

    private IDispatcherTimer? _idleTimer;
    private bool _autoPetEntered;   // 是否因闲置自动进入（此时要监听输入自动恢复）
    private bool _idleWatchStarted;

    // ============ 设置项的运行时状态（气泡逐字浮现 / 状态行 / 音效） ============

    private CancellationTokenSource? _typeCts;      // 正在进行的逐字浮现
    private string _typedTarget = "";               // 这一轮要打的那句全文
    private bool _typing;                            // 正在打：防止浮现过程中的高度回写又起一轮
    private long _lastTypeSfxAt;                     // 打字音的最小间隔闸（×6 时会烧出一串机关枪）
    private string _statusOverride = "";            // 「呼噜中」这类临时状态行
    private long _statusOverrideUntil;
    private bool _simulatedPress;                   // 悬停按钮代发的单击：音效由按钮自己出，别响两遍
    private readonly Dictionary<string, string> _sfxPaths = new(StringComparer.Ordinal);
    private bool _missingSfxLogged;

    // 状态行的四种语言：桌宠语言选了哪一种就用哪一种（空=跟随全局）。
    // 只有这一处文本是我自己写的，所以这一项在这里能做真；AI 回话的语言仍归提示词那边管。
    private static readonly Dictionary<string, (string Here, string Walking, string Purring)> StatusWords = new(StringComparer.Ordinal)
    {
        ["zh-CN"] = ("在的", "散步中", "呼噜中"),
        ["zh-TW"] = ("在的", "散步中", "呼嚕中"),
        ["en"] = ("here", "taking a walk", "purring"),
        ["ja"] = ("ここにいる", "おさんぽ中", "ごろごろ"),
    };

    // ============ 闲置检测（GetLastInputInfo）============
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    private static int IdleSeconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref info)
            ? Math.Max(0, (Environment.TickCount - (int)info.dwTime)) / 1000
            : 0;
    }

    /// <summary>开始闲置监听：SetPetWatchIdle（后台 10 秒一次）。挂接一次即可。</summary>
    public void WatchIdle()
    {
        if (_idleWatchStarted) return;
        _idleWatchStarted = true;
        try
        {
            var timer = Application.Current?.Dispatcher.CreateTimer();
            if (timer is null) return;
            _idleTimer = timer;
            timer.Interval = TimeSpan.FromSeconds(10);
            timer.Tick += (_, _) => CheckIdle();
            timer.Start();
            App.WriteLog("PetService: idle watch started");
        }
        catch (Exception ex)
        {
            App.WriteLog("PetService.WatchIdle -> " + ex);
        }
    }

    private void CheckIdle()
    {
        try
        {
            // 姿态先看：这条与下面「闲置自动进入桌宠」的开关无关，她已经在桌面上时也该有动静
            if (_petMode) PoseForIdle();
            var minutes = _settings.Current.PetIdleMinutes;
            if (minutes <= 0) { _autoPetEntered = false; return; }
            var idle = IdleSeconds();

            if (_petMode)
            {
                // 闲置自动进入的桌宠：用户恢复使用（回到屏幕前）应立即回到主窗口
                if (_autoPetEntered && idle < 60)
                {
                    _autoPetEntered = false;
                    ShowMainWindow();
                }
                return;
            }

            if (idle >= minutes * 60)
            {
                // 达到闲置时长：自动收纳为桌宠（仅 Windows；托盘已就绪可恢复）
                _autoPetEntered = true;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_petMode) return;
                    EnterPetMode();
                });
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("PetService.CheckIdle -> " + ex);
        }
    }

#if WINDOWS
    // ============ Win32 P/Invoke ============
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uVersion;
        public int uTimeout;
        public long hBalloonIcon;
    }

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASS
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string lpszMenuName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string lpszClassName;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern ushort RegisterClassW(ref WNDCLASS lpWndClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(int exStyle, string className, string windowName, int style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);
    [DllImport("user32.dll")]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string exeName, int iconIndex);
    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, string lpIconName);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);
    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    // ============ 桌宠窗口表面（背景做不到透明，见下方注释） ============

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;   // Windows 11 才有
    private const int DWMWCP_ROUND = 2;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_FRAMECHANGED = 0x0020;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, ref int value, int size);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr newLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    /// <summary>小窗的尺寸按 DIP 设计，但 AppWindow.Move/Resize 吃物理像素：换算比例只能问窗口自己。</summary>
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    /// <summary>整个虚拟桌面（所有显示器拼起来的包围盒）的左/上/宽/高，物理像素。</summary>
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

    [StructLayout(LayoutKind.Sequential)]
    private struct WinRect { public int left, top, right, bottom; }

    /// <summary>取窗口的真实物理矩形——小窗的坐标系和鼠标坐标系不是一个尺度，只能这样对齐。</summary>
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out WinRect lpRect);

    /// <summary>
    /// 桌宠话术层（头顶小窗）的表面处理：圆角 + 从 Alt+Tab 里消失。
    /// 背景「透明」这件事在 WinUI3 桌面上做不到，别再试了：窗口的组合表面是不透明的，
    /// 它盖在 DWM 画的东西上面，所以 DWM 后面铺什么都是白搭。三条路全部实测过（截图为证）：
    /// 1) WS_EX_LAYERED + SetLayeredWindowAttributes(alpha=255)：没开色键，255 等于不透明。
    /// 2) DwmEnableBlurBehindWindow(空区域) + DwmExtendFrameIntoClientArea(-1)：两个都返回 S_OK，
    ///    白板纹丝不动；顺手把可视树从 WindowRootViewContainer 到页面宿主 Border 全打了一遍，
    ///    背景清一色 #00FFFFFF —— 白不是 XAML 铺的，是那层组合表面。
    /// 3) WS_EX_LAYERED + 色键品红：exStyle 确实带上了 0x80000、函数返回 True，
    ///    截图里整窗变成纯品红，一个像素都没被抠掉。分层属性作用于 DWM 的重定位位图，
    ///    组合内容根本不走那条路。
    /// 所以立绘改由自建 Win32 分层窗口贴图（见 Platforms/Windows/PetLayeredWindow）：
    /// 角色能透，四周和脚下不再有矩形。这条小窗只装输入框和气泡，收起来时根本不存在。
    /// </summary>
    private static void StylePetWindowSurface(Microsoft.UI.Xaml.Window xw)
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(xw);
            var pref = DWMWCP_ROUND;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));

            var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            if ((ex & WS_EX_TOOLWINDOW) == 0)
            {
                SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex | WS_EX_TOOLWINDOW));
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                    SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_FRAMECHANGED);
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("PetService.StylePetWindowSurface -> " + ex.Message);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private const uint MF_STRING = 0x0;
    private const uint TPM_RIGHTBUTTON = 0x2;
    private const uint TPM_RETURNCMD = 0x100;

    private static WndProc? _procDelegate;

    public PetService(SettingsManager settings)
    {
        _instance = this;
        _settings = settings;
        // 设置页每一次改动都推一遍运行时：Apply 只改内存，生效与否全看这里。
        settings.Applied += PushRuntimeSettings;
        // 双击她一下到底干什么，由设置里的 PetDoubleClick 说；分层窗口自己不认识设置。
        Platforms.Windows.PetLayeredWindow.DoubleClickRouter = () => settings.Current.PetDoubleClick;
        Platforms.Windows.PetLayeredWindow.Moved += OnSpriteMoved;
        Platforms.Windows.PetLayeredWindow.HoverButtonPressed += RunHoverAction;
        Platforms.Windows.PetLayeredWindow.FullscreenHidden += OnFullscreenHidden;
        Platforms.Windows.PetLayeredWindow.PositionSynced += () => Helpers.Ui.Post(RepositionChrome);
        // 单击的音效挂在这里：PetPage 自己那一套订阅（单击=嘀咕一句）不动，音效是我这一侧的旁听。
        Platforms.Windows.PetLayeredWindow.Pressed += () => { if (!_simulatedPress) PlayPetSfx("tap"); };
    }

    /// <summary>静态入口：桌宠页双击回到主窗口。</summary>
    public static void ShowMainWindowStatic() => _instance?.ShowMainWindow();

    /// <summary>静态入口：进入/退出桌宠模式。</summary>
    public static void TogglePetModeStatic() => _instance?.TogglePetMode();

    /// <summary>进入/退出桌宠模式：收纳主窗口并显示桌宠（或相反）。</summary>
    public void TogglePetMode()
    {
        if (_petMode) { ShowMainWindow(); return; }
        _autoPetEntered = false;   // 手动进入：不参与闲置自动恢复
        EnterPetMode();
    }

    private void EnterPetMode()
    {
        try
        {
            var main = GetMainWinUIWindow();
            if (main is null) return;
            EnsureTray();
            EnsurePetWindow();
            // 先置位再隐藏：Hide() 会同步触发 AppWindow.Changed，那个回调里的「闲置自动进入」
            // 看到 _petMode 还是 false，就会把 EnterPetMode() 再跑一遍。
            _petMode = true;
            main.AppWindow.Hide();
            App.WriteLog("PetService: pet mode on");
        }
        catch (Exception ex)
        {
            App.WriteLog("PetService.EnterPetMode -> " + ex);
        }
    }

    /// <summary>从桌宠/收纳状态回到主窗口。</summary>
    public void ShowMainWindow()
    {
        try
        {
            var main = GetMainWinUIWindow();
            if (main is not null)
            {
                main.AppWindow.Show();
                main.Activate();
            }
            ClosePetWindow();
            _petMode = false;
            _autoPetEntered = false;
            App.WriteLog("PetService: main window restored");
        }
        catch (Exception ex)
        {
            App.WriteLog("PetService.ShowMainWindow -> " + ex);
        }
    }

    /// <summary>
    /// 主窗口最小化时自动收纳为桌宠（挂接一次监听）。
    /// 注意：窗口此时可能还没建好（VM 构造期就调用），所以必须**确认挂上之后再置位**，
    /// 否则一次过早的调用就会把标志位吃掉，最小化再也不会进桌宠（曾经的 bug）。
    /// </summary>
    public void WatchMinimize()
    {
        if (_listening) return;
        var main = GetMainWinUIWindow();
        if (main is null)
        {
            // 窗口还没就绪：稍后再试，最多试 20 次（约 4 秒）
            if (_watchRetries++ >= 20) return;
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(200);
                WatchMinimize();
            });
            return;
        }
        _listening = true;
        main.AppWindow.Changed += (aw, _) =>
        {
            if (aw.Presenter is OverlappedPresenter p && p.State == OverlappedPresenterState.Minimized)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_petMode) return;
                    EnterPetMode();
                });
            }
        };
    }

    private int _watchRetries;

    private static Microsoft.UI.Xaml.Window? GetMainWinUIWindow()
    {
        // 显式排除桌宠窗：MAUI 窗口集合按创建顺序排，一旦桌宠窗成为第一个，
        // 「隐藏主窗口」就会把桌宠自己藏起来。
        var win = Application.Current?.Windows.FirstOrDefault(w => !ReferenceEquals(w, _instance?._petWindow) && w.Handler is not null
            && w is not WarmAsBefore.Views.ToolWindow);
        return win?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
    }

    /// <summary>
    /// 桌宠的「话术层」：一条浮在头顶的小窗（输入框 + 回复气泡），默认收起。
    /// 立绘不在这里——立绘走 PetLayeredWindow 的按像素 alpha 贴图，XAML 窗口做不到透明。
    /// 这条小窗只能是矩形，所以让它小到只剩内容本身，靠圆角和深色底读成一个对话气泡。
    /// </summary>
    private void EnsurePetWindow()
    {
        if (_petWindow is not null && Application.Current.Windows.Contains(_petWindow)) return;
        var services = Application.Current?.Handler?.MauiContext?.Services;
        if (services is null) return;
        var page = services.GetService(typeof(Views.PetPage)) as Views.PetPage;
        if (page is null) return;
        _page = page;
        var win = new Window(page)
        {
            Title = "温暖如初 · 桌宠",
            Width = 340,
            Height = 56
        };
        Application.Current.OpenWindow(win);
        _petWindow = win;
        _chromeVisible = false;
        _chromeExpanded = false;
        if (win.Handler?.PlatformView is Microsoft.UI.Xaml.Window xw && xw.AppWindow is { } app)
        {
            _chromeHwnd = WindowNative.GetWindowHandle(xw);
            if (app.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsAlwaysOnTop = true;
                // 双击顶沿最大化、拖边缘改尺寸、贴屏幕右缘分屏——这些都是「窗口」的手感，桌宠不该有。
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
            }
            StylePetWindowSurface(xw);
            app.Hide();
        }
        // 建好就照设置摆一次：字号、输入条在不在，都得先跟上用户上一次的选择
        ApplyChromeSettings();
    }

    // ============ 立绘与头顶小窗的联动（供 PetPage 调用） ============

    /// <summary>把角色立绘贴到桌面上（默认右下角、脚踩工作区底沿；记过位置就回那个位置），并定位头顶小窗。</summary>
    public static void AttachSprite(string absPath)
    {
        var inst = _instance;
        if (inst is null) return;
        try
        {
            var area = inst.GetPetDisplayArea();
            var s = inst._settings.Current;
            Platforms.Windows.PetLayeredWindow.Open(absPath,
                area.X + area.Width - 28, area.Y + area.Height - 8,
                BodyHeightFor(area.Height, s.PetScale));
            // 记住位置：拖到哪儿下次还在哪儿。锚点存的是虚拟桌面物理像素，与分层窗口同一套坐标。
            if (s.PetRememberPosition && s.PetAnchorX is int ax && s.PetAnchorY is int ay)
                Platforms.Windows.PetLayeredWindow.MoveTo(ax, ay);
            inst.PushRuntimeSettings();
            inst.PlaceChrome();
        }
        catch (Exception ex)
        {
            App.WriteLog("PetService.AttachSprite -> " + ex);
        }
    }

    /// <summary>
    /// 本体高度 = 工作区高的 42% × 用户体型倍率。
    /// 倍率必须乘在 240/900 这层夹值之外：否则在 4K 屏上把滑杆拖到 50%，乘完仍被下限顶回 240，看着就是「滑杆没反应」。
    /// </summary>
    private static int BodyHeightFor(int areaHeightPx, double scale)
        => Math.Clamp((int)(areaHeightPx * 0.42 * Math.Clamp(scale, 0.4, 2.6)), 120, 4000);

    /// <summary>设置里改体型时立刻重贴：角色已经在桌面上，不能等下次进桌宠才生效。</summary>
    public static void ApplyScale()
    {
        var inst = _instance;
        if (inst is null || !Platforms.Windows.PetLayeredWindow.Exists) return;
        try
        {
            // 只交本体高度，右沿与地面由分层窗口按现在的位置自己保——
            // 用户可能早把角色拖到屏幕中间，按显示区重算会把她拽回右下角。
            Platforms.Windows.PetLayeredWindow.SetBodyHeight(
                BodyHeightFor(inst.GetPetDisplayArea().Height, inst._settings.Current.PetScale));
            inst.PlaceChrome();
        }
        catch (Exception ex)
        {
            App.WriteLog("PetService.ApplyScale -> " + ex);
        }
    }

    /// <summary>
    /// 滚轮缩放之后把新体型写回设置。不写盘就只是这一次进桌宠有效：
    /// 下次 AttachSprite 又按基准摆回去，用户看到的正是「刚调小，怎么又巨大」。
    /// 落盘延到停手之后一次做，滚一格 wheel 就 Apply 会把 Applied 的订阅者刷屏。
    /// </summary>
    public static void CommitScale()
    {
        var inst = _instance;
        if (inst is null || !Platforms.Windows.PetLayeredWindow.Exists) return;
        var baseH = inst.GetPetDisplayArea().Height * 0.42;
        if (baseH <= 0) return;
        inst.QueueScaleSave(Math.Clamp(Platforms.Windows.PetLayeredWindow.BodyHeight / baseH, 0.4, 2.6));
    }

    private double _pendingScale = -1;
    private System.Threading.Timer? _scaleSaveTimer;

    private void QueueScaleSave(double scale)
    {
        _pendingScale = scale;
        _scaleSaveTimer ??= new System.Threading.Timer(
            _ => Helpers.Ui.Post(SaveScaleNow), null, Timeout.Infinite, Timeout.Infinite);
        _scaleSaveTimer.Change(450, Timeout.Infinite);
    }

    private void SaveScaleNow()
    {
        if (_pendingScale < 0) return;
        try
        {
            _settings.Apply(_settings.Current with { PetScale = Math.Round(_pendingScale, 3) });
            _pendingScale = -1;
            _ = _settings.Persist();
        }
        catch (Exception ex)
        {
            App.WriteLog("PetService.SaveScale -> " + ex);
        }
    }

    // ============ AI 做的 Q 版素材包 → 帧动画 ============

    private PetStudioService? _studio;
    private Models.PetAnimationPack? _pack;
    private string _packCharId = "";
    private string _emotion = "";
    private string _poseId = "";       // 桌面上正在播的那一态（"" = 没挂动画，用的还是静态立绘）
    private DateTime _poseStamp;       // 播的是哪一版包：工作室重出过就得重新交帧
    private bool _poseTransient;
    private long _poseUntil;
    private string _idlePose = "";     // 因为没人理而摆的姿态，用来判断「人回来了」要不要站起来
    private System.Threading.Timer? _poseRevert;

    /// <summary>
    /// 把 TA 的 Q 版素材包挂上桌宠。没给这个角色做过桌宠就什么也不动——
    /// 静态立绘本来就是能用的，硬套一个空包只会让她变成一块透明矩形。
    /// </summary>
    public static void AttachAnimation(string characterId, string emotion)
    {
        var inst = _instance;
        if (inst is null) return;
        inst._emotion = emotion ?? "";
        _ = inst.LoadPackAsync(characterId);
    }

    /// <summary>聊完一轮之后心情可能变了：桌宠跟着换表情（演到一半的动作不打断，等它自己落回去）。</summary>
    public static void SetEmotion(string emotion)
    {
        var inst = _instance;
        if (inst is null) return;
        inst._emotion = emotion ?? "";
        inst.RefreshPose();
    }

    /// <summary>工作室刚出完货：正在桌面上的她立刻换上新一套帧。</summary>
    public static void ReloadAnimation()
    {
        var inst = _instance;
        if (inst is null || string.IsNullOrEmpty(inst._packCharId)) return;
        _ = inst.LoadPackAsync(inst._packCharId);
    }

    /// <summary>
    /// 演一态若干秒后自己回落常驻档；seconds &lt;= 0 表示一直演着，等下一次有人动她。
    /// 包里没这一态就整段不动：宁可她一直站着，也别一有反应就闪回静态立绘——那一下比不动更难看。
    /// </summary>
    public static void ShowPoseFor(string stateId, double seconds)
    {
        var inst = _instance;
        if (inst is null || inst._pack is null || !Surface.Exists) return;
        inst.PlayPose(stateId);
        if (inst._poseId != stateId) return;   // 没帧＝没演，也就不必起那张回退的表
        inst._poseTransient = seconds > 0;
        inst._poseUntil = seconds > 0 ? Environment.TickCount64 + (long)(seconds * 1000) : long.MaxValue;
        if (seconds <= 0) return;
        inst._poseRevert ??= new System.Threading.Timer(
            _ => Helpers.Ui.Post(() => _instance?.RevertPose()), null, Timeout.Infinite, Timeout.Infinite);
        inst._poseRevert.Change((int)Math.Ceiling(seconds * 1000), Timeout.Infinite);
    }

    private async Task LoadPackAsync(string characterId)
    {
        try
        {
            _packCharId = characterId ?? "";
            var studio = Resolve<PetStudioService>();
            _studio = studio;
            _pack = studio is null || string.IsNullOrWhiteSpace(characterId)
                ? null
                : await studio.LoadPackAsync(characterId);
            _poseId = "";      // 换包＝重演一次，别让下面的「已经在播」判定把新帧挡掉
            _idlePose = "";
            RevertPose();
        }
        catch (Exception ex)
        {
            App.WriteLog("PetService.LoadPack -> " + ex.Message);
        }
    }

    /// <summary>常驻那一档：开着散步就播走动循环，否则按现在的心情挑一态。</summary>
    private string BasePoseId()
    {
        var pack = _pack;
        if (pack is null) return "";
        if (_settings.Current.PetWalking == "roam" && pack.HasFrames("walk")) return "walk";
        return StateOfEmotion(_emotion);
    }

    /// <summary>
    /// 心情 → 状态。两边都要认：内置心情是英文小写键（happy/shy…），角色素材包里的可能是中文。
    /// 越具体的排越前——「又开心又害羞」该走 happy，不该被「羞」字领走。
    /// </summary>
    private string StateOfEmotion(string emotion)
    {
        var pack = _pack;
        if (pack is null) return "";
        var e = (emotion ?? "").Trim().ToLowerInvariant();
        var table = new (string[] Keys, string Id)[]
        {
            (new[] { "爱", "恋", "喜欢", "心动", "love" }, "love"),
            (new[] { "开心", "高兴", "愉快", "兴奋", "笑", "happy", "excited", "shy" }, "happy"),
            (new[] { "生气", "恼", "怒", "烦", "angry" }, "angry"),
            (new[] { "哭", "难过", "伤心", "委屈", "低落", "sad" }, "cry"),
            (new[] { "惊", "吓", "surprised" }, "surprised"),
            // 困/累只让她坐下歇着：真睡着是「没人陪」的事，不该由心情触发
            (new[] { "困", "累", "疲惫", "无聊", "tired", "bored" }, "sit"),
        };
        foreach (var (keys, id) in table)
        {
            foreach (var k in keys)
                if (e.Contains(k, StringComparison.Ordinal) && pack.HasFrames(id)) return id;
        }
        return pack.HasFrames("normal") ? "normal" : "";
    }

    /// <summary>把一态的帧交给分层窗口。绝对化与「文件还在不在」都在这一处，包被搬走过期只会不演，不会崩。</summary>
    private void PlayPose(string stateId)
    {
        var pack = _pack;
        if (pack is null || stateId.Length == 0 || !Surface.Exists) return;
        if (stateId == _poseId && pack.GeneratedAt == _poseStamp) return;   // 已经在播这一态：别白解一遍图
        if (!pack.HasFrames(stateId)) return;
        var rel = pack.FramesOf(stateId);
        var paths = new List<string>(rel.Count);
        foreach (var r in rel)
        {
            var abs = _studio is null ? "" : _studio.ResolveAbs(r);
            if (abs.Length > 0 && System.IO.File.Exists(abs)) paths.Add(abs);
        }
        if (paths.Count == 0) return;
        _poseId = stateId;
        _poseStamp = pack.GeneratedAt;
        Surface.SetFrames(paths, pack.FpsOf(stateId));
    }

    /// <summary>设置或心情变了之后重算常驻档；正演到一半的动作让它先演完。</summary>
    private void RefreshPose()
    {
        if (_poseTransient && Environment.TickCount64 < _poseUntil) return;
        PlayPose(BasePoseId());
    }

    private void RevertPose()
    {
        _poseTransient = false;
        _poseUntil = 0;
        _idlePose = "";
        PlayPose(BasePoseId());
    }

    /// <summary>
    /// 没人陪的这几分钟让她自己待着：先坐下，再睡；人回来了站起来。
    /// 与「闲置自动进入桌宠」那条设置无关——那是切模式，这是她本来就站在桌面上时的姿态。
    /// </summary>
    private void PoseForIdle()
    {
        if (_pack is null || !Surface.Exists) return;
        if (BasePoseId() == "walk") return;   // 正散步：坐着睡着都和「脚下在动」打架
        var idle = IdleSeconds();
        if (idle >= 600)
        {
            if (_idlePose != "sleep") { _idlePose = "sleep"; ShowPoseFor("sleep", 0); }
            return;
        }
        if (idle >= 180)
        {
            if (_idlePose != "sit") { _idlePose = "sit"; ShowPoseFor("sit", 0); }
            return;
        }
        if (_idlePose.Length == 0) return;
        _idlePose = "";
        ShowPoseFor("stand", 1.6);
    }

    // ============ 设置 → 桌面（这一页每一项的真实出口都在这里） ============

    /// <summary>
    /// SettingsManager.Applied 的出口：把内存里的设置推成分层窗口的绘制参数与小窗的控件状态。
    /// 全部走 Helpers.Ui.Post——设置可能从任何线程 Apply，而这里要动 XAML 控件、要往 UI 线程的
    /// 窗口上贴位图。Post 在「已经在 UI 线程」时是同步执行的，所以拖动滑杆不会有延迟。
    /// </summary>
    private void PushRuntimeSettings() => Helpers.Ui.Post(PushRuntimeNow);

    private void PushRuntimeNow()
    {
        try
        {
            var s = _settings.Current;
            _sfxPaths.Clear();   // 音效卡槽可能在音效库里被换过，缓存跟着设置一起失效
            if (Surface.Exists)
            {
                var wantH = BodyHeightFor(GetPetDisplayArea().Height, s.PetScale);
                // 只在真的变了才重贴：SetBodyHeight 会重建缓存位图，滚轮/滑杆每帧都推会白烧一次大图
                if (Math.Abs(wantH - Surface.BodyHeight) >= 2) Surface.SetBodyHeight(wantH);
                Surface.SetGlow(s.PetGlow);
                Surface.SetMotion(Math.Clamp(s.PetFps, 10, 60), s.PetFpsLock);
                Surface.SetWalking(s.PetWalking);
                Surface.SetHoverButtons(s.PetHoverButtons ?? new List<string>());
                Surface.SetFontDip(s.PetFontSize);
                Surface.SetFullscreenGuard(s.PetHideOnFullscreen);
                Surface.SetStatusText(s.PetStatusBubble ? CurrentStatusLine() : "");
                // 散步开关改的就是常驻档该演哪一态；其余项不关心动画，被「已经在播」判定挡掉
                RefreshPose();
            }
            ApplyChromeSettings();
        }
        catch (Exception ex)
        {
            App.WriteLog("PetService.PushRuntime -> " + ex);
        }
    }

    /// <summary>
    /// 头顶小窗里那些控件的真实状态：字号、输入条在不在、要不要继续亮着。
    /// 用的是 FindByName：那一页不归这里改，可它身上的控件状态又确实归设置管。
    /// </summary>
    private void ApplyChromeSettings()
    {
        var s = _settings.Current;
        _chromeRowDip = s.PetInputBubble ? ChromeRowDip : 0;
        var page = _page;
        if (page is not null)
        {
            if (page.FindByName("ReplyLabel") is Microsoft.Maui.Controls.Label reply
                && Math.Abs(reply.FontSize - s.PetFontSize) > 0.4)
                reply.FontSize = s.PetFontSize;
            if (page.FindByName("InputEntry") is Microsoft.Maui.Controls.Entry entry
                && Math.Abs(entry.FontSize - s.PetFontSize) > 0.4)
                entry.FontSize = s.PetFontSize;
            if (page.FindByName("InputBar") is Microsoft.Maui.Controls.View bar && bar.IsVisible != s.PetInputBubble)
                bar.IsVisible = s.PetInputBubble;
        }
        if (_chromeVisible) PlaceChrome();
        // 现在的内容还够不够撑一条小窗：输入条关了又没气泡在屏，就把白板收掉
        SetChrome(_chromeVisible, _chromeExpanded);
        if (Math.Abs(_lastFontDip - s.PetFontSize) > 0.4)
        {
            _lastFontDip = s.PetFontSize;
            // 字号变了，旧的高度就量错了：等这一帧布局走完按新字号重量一次
            page?.Dispatcher.Dispatch(RefitBubble);
        }
    }

    private double _lastFontDip = -1;

    /// <summary>按现在的字号重新量一次气泡高度（不动文字，所以也不会重启逐字浮现）。</summary>
    private void RefitBubble()
    {
        if (_typing) return;   // 正在逐字浮现：半截文字量出来的高度会把整句挤没
        if (_page?.FindByName("ReplyLabel") is not Microsoft.Maui.Controls.Label reply) return;
        var text = reply.Text ?? "";
        if (text.Length == 0 || reply.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.TextBlock tb) return;
        tb.Text = text;
        tb.Measure(new Windows.Foundation.Size((float)BubbleInnerWidthDip, float.PositiveInfinity));
        var h = (double)tb.DesiredSize.Height;
        SetBubbleHeightRaw(h > BubbleMaxDip ? BubbleMaxDip : h + BubblePadDip);
    }

    /// <summary>
    /// 气泡逐字浮现。触发点是 SetBubbleHeight——那是 PetPage 把整句文字放进气泡之后必经的一站，
    /// 于是「浮现速度」「打字音」这两项不用改那一页也能真的生效。
    /// 高按整句量好一次定死，只有文字在长：小窗不会因为多打一个字就上下跳。
    /// </summary>
    private void MaybeTypewriter()
    {
        var s = _settings.Current;
        var label = _page?.FindByName("ReplyLabel") as Microsoft.Maui.Controls.Label;
        if (label is null) return;
        var full = label.Text ?? "";
        if (full == _typedTarget) return;   // 就是这一句，正打到一半：别从头再来
        _typeCts?.Cancel();
        if (full.Length < 2)
        {
            _typedTarget = "";
            _typing = false;
            return;
        }
        var per = Math.Clamp(60.0 / Math.Clamp(s.PetBubbleSpeed, 0.2, 6.0), 8, 320);
        if (full.Length * per > 6000) per = 6000.0 / full.Length;   // 再慢也不许把气泡吊在屏幕上十几秒
        _typedTarget = full;
        _typing = true;
        var cts = _typeCts = new CancellationTokenSource();
        label.Text = full[..1];
        _ = RunTypewriterAsync(label, full, per, cts.Token, s.PetTypingSound);
    }

    private async Task RunTypewriterAsync(Microsoft.Maui.Controls.Label label, string full,
        double perMs, CancellationToken token, bool typingSound)
    {
        try
        {
            for (var i = 2; i <= full.Length; i++)
            {
                await Task.Delay((int)Math.Round(perMs), token);
                if (token.IsCancellationRequested) return;
                var k = i;
                Helpers.Ui.Post(() =>
                {
                    if (!token.IsCancellationRequested) label.Text = full[..k];
                });
                // 打字音不是每个字都响：×6 时那就是十几毫秒一下的机关枪
                if (typingSound && k % 3 == 0 && Environment.TickCount64 - _lastTypeSfxAt > 60)
                {
                    _lastTypeSfxAt = Environment.TickCount64;
                    PlayPetSfx("typing");
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { App.WriteLog("PetService.Typewriter -> " + ex.Message); }
        finally
        {
            // 被下一句顶掉的轮子不收尾：那句自己会负责把文字摆正
            if (!token.IsCancellationRequested)
                Helpers.Ui.Post(() =>
                {
                    label.Text = full;
                    _typing = false;
                    _typedTarget = "";
                });
        }
    }

    /// <summary>状态行：临时状态（呼噜中/散步中）优先，其次是现在到底在做什么。</summary>
    private string CurrentStatusLine()
    {
        if (_statusOverride.Length > 0 && Environment.TickCount64 < _statusOverrideUntil) return _statusOverride;
        _statusOverride = "";
        return _settings.Current.PetWalking == "roam" ? StatusLine("walking") : StatusLine("here");
    }

    /// <summary>桌宠语言（空=跟随全局语言）：这一行字是我自己写的，所以这一项在这里能做真。</summary>
    private string StatusLine(string which)
    {
        var s = _settings.Current;
        var key = string.IsNullOrWhiteSpace(s.PetLanguage) ? s.Lang : s.PetLanguage;
        if (!StatusWords.TryGetValue(key, out var w)) w = StatusWords["zh-CN"];
        return which switch { "walking" => w.Walking, "purring" => w.Purring, _ => w.Here };
    }

    private void SetStatusFor(string line, double seconds)
    {
        _statusOverride = line;
        _statusOverrideUntil = Environment.TickCount64 + (long)Math.Max(0, seconds) * 1000;
        Platforms.Windows.PetLayeredWindow.SetStatusText(_settings.Current.PetStatusBubble ? line : "");
        _ = ClearStatusAfterAsync(seconds + 0.4);
    }

    private async Task ClearStatusAfterAsync(double seconds)
    {
        try { await Task.Delay((int)Math.Max(0, seconds) * 1000); } catch { }
        if (_statusOverride.Length == 0) return;
        _statusOverride = "";
        Platforms.Windows.PetLayeredWindow.SetStatusText(_settings.Current.PetStatusBubble ? CurrentStatusLine() : "");
    }

    /// <summary>拖完角色：开了「记住位置」就把这一步存进设置，下次进桌宠回到这里。</summary>
    private void OnSpriteMoved()
    {
        var s = _settings.Current;
        if (!s.PetRememberPosition) return;
        var l = Platforms.Windows.PetLayeredWindow.Left;
        var t = Platforms.Windows.PetLayeredWindow.Top;
        if (s.PetAnchorX == l && s.PetAnchorY == t) return;
        _settings.Apply(s with { PetAnchorX = l, PetAnchorY = t });
        _ = _settings.Persist();
    }

    /// <summary>设置页的「记住当前位置」：角色没在桌面上就返回 false，让那一页说人话。</summary>
    public static bool CaptureAnchor()
    {
        var inst = _instance;
        if (inst is null || !Platforms.Windows.PetLayeredWindow.Exists) return false;
        var s = inst._settings.Current;
        inst._settings.Apply(s with
        {
            PetAnchorX = Platforms.Windows.PetLayeredWindow.Left,
            PetAnchorY = Platforms.Windows.PetLayeredWindow.Top,
            PetRememberPosition = true,
        });
        _ = inst._settings.Persist();
        App.WriteLog($"PetService: 锚点 {(s.PetAnchorX)},{(s.PetAnchorY)} → " +
            $"{Platforms.Windows.PetLayeredWindow.Left},{Platforms.Windows.PetLayeredWindow.Top}");
        return true;
    }

    /// <summary>悬停按钮按下：id 与设置里那份目录同源。</summary>
    private void RunHoverAction(string id)
    {
        var s = _settings.Current;
        PlayPetSfx("tap");
        switch (id)
        {
            case "talk":
            case "think":
                // 「答一句」的整条链（提示词 → AI → 气泡）都在 PetPage 手上，这里只代发一次单击
                _simulatedPress = true;
                try { Surface.SimulatePressed(); } finally { _simulatedPress = false; }
                break;
            case "pet":
                // 被摸头蹦一下，然后接着演她本来在演的那一态
                ShowPoseFor("jump", 1.6);
                break;
            case "purr":
                Surface.StartPurr(s.PetPurrSeconds);
                SetStatusFor(StatusLine("purring"), s.PetPurrSeconds);
                PlayPetSfx("purr");
                ShowPoseFor("love", s.PetPurrSeconds);
                break;
            case "walk":
                Surface.StartWalkBurst(12);
                SetStatusFor(StatusLine("walking"), 12);
                ShowPoseFor("walk", 12);
                break;
            case "settings":
                ShowMainWindow();
                _ = OpenPetSettingsAsync();
                break;
            case "home":
                ShowMainWindow();
                break;
        }
    }

    /// <summary>回主窗口之后才导航：桌宠窗正在关，这时切页会撞在一起。</summary>
    private static async Task OpenPetSettingsAsync()
    {
        try
        {
            await Task.Delay(180);
            await Shell.Current.GoToAsync("petsettings");
        }
        catch (Exception ex) { App.WriteLog("PetService: 打不开桌宠设置 -> " + ex.Message); }
    }

    /// <summary>全屏收起：小窗一起收走——一块矩形白板压在全屏画面上，比角色露在旁边更糟。</summary>
    private void OnFullscreenHidden(bool hidden)
    {
        _fullscreenHidden = hidden;
        Helpers.Ui.Post(() =>
        {
            if (hidden) SetChrome(false, _chromeExpanded);
            else RepositionChrome();   // 回来后显隐交给页面的靠近轮询，这里只把位置对齐
        });
    }

    // ============ 桌宠自己的音效通道（按类别音量播） ============

    /// <summary>
    /// 播一个桌宠音效：类别音量（PetSfxLevels）× 全局音效音量，素材走「声音分配」里那两格。
    /// 设置页的「试听」也走这一个口，听到的就是桌宠上真正会响的东西。
    /// </summary>
    public static void PlayPetSfx(string category)
    {
        var inst = _instance;
        if (inst is null || string.IsNullOrWhiteSpace(category)) return;
        _ = inst.PlaySfxAsync(category);
    }

    private async Task PlaySfxAsync(string category)
    {
        try
        {
            var levels = _settings.Current.PetSfxLevels;
            var level = levels is not null && levels.TryGetValue(category, out var v) ? Math.Clamp(v, 0, 1) : 1.0;
            if (level <= 0.01) return;   // 这一档拧到 0 就是没有
            var file = await ResolveSfxPathAsync(category);
            if (string.IsNullOrEmpty(file)) return;
            double global = 0.8;
            if (Resolve<AudioController>() is { } ac) global = ac.Sfx;
            PlayFileWithVolume(file, level * global);
        }
        catch (Exception ex)
        {
            App.WriteLog("PetService.PlaySfx(" + category + ") -> " + ex.Message);
        }
    }

    /// <summary>从 DI 里要一个服务：拿不到就返回 null，调用方自己兜着（桌宠不该因为服务没注册而炸）。</summary>
    private static T? Resolve<T>() where T : class
    {
        var services = Application.Current?.Handler?.MauiContext?.Services;
        return services?.GetService(typeof(T)) as T;
    }

    /// <summary>类别 → 素材绝对路径。查过一次就记住，别每敲一个字都去翻一遍音效库。</summary>
    private async Task<string?> ResolveSfxPathAsync(string category)
    {
        if (_sfxPaths.TryGetValue(category, out var cached)) return cached.Length == 0 ? null : cached;
        if (Resolve<MaterialLibrary>() is not MaterialLibrary mats) return null;
        await mats.EnsureLoadedAsync();
        var a = mats.Assign;
        var id = category switch
        {
            "tap" => string.IsNullOrWhiteSpace(a.LeftClickId) ? a.KeyPressId : a.LeftClickId,
            "typing" => a.KeyPressId,
            "purr" => a.PurrId,
            // speak 走的是 TTS，不在音效库里另设卡槽
            _ => "",
        };
        var abs = "";
        if (!string.IsNullOrWhiteSpace(id))
        {
            var item = await mats.FindMusicAsync(id);
            if (item is not null) abs = mats.ResolveAbs(item.RelPath);
        }
        else if (!_missingSfxLogged && category == "speak")
        {
            _missingSfxLogged = true;
            App.WriteLog("PetService: 「speak」没有音效卡槽——这一档走的是 TTS");
        }
        _sfxPaths[category] = abs;
        return abs.Length == 0 ? null : abs;
    }

    private static void PlayFileWithVolume(string file, double volume)
    {
        if (string.IsNullOrEmpty(file) || !File.Exists(file)) return;
        try
        {
            var p = new Windows.Media.Playback.MediaPlayer { Volume = Math.Clamp(volume, 0, 1) };
            p.MediaEnded += (_, _) => { try { p.Dispose(); } catch { } };
            p.MediaFailed += (_, _) => { try { p.Dispose(); } catch { } };
            p.Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(file));
            p.Play();
        }
        catch (Exception ex) { App.WriteLog("PetService.PlayFile -> " + ex.Message); }
    }

    /// <summary>小窗显隐；expanded=true 时给回复气泡腾出高度（气泡向上长，输入行位置不动）。</summary>
    public static void SetChrome(bool visible, bool expanded)
    {
        var inst = _instance;
        if (inst is null) return;
        if (inst._chromeExpanded != expanded) { inst._chromeExpanded = expanded; inst.PlaceChrome(); }
        // 「关掉输入气泡」= 小窗里一个可点的东西都没有；「全屏收起」= 人不在这块屏幕上。
        // 这两种情况下都不该有一块矩形白板浮在桌面上——桌宠不是一个窗口。
        if (visible && (inst._fullscreenHidden || (!inst._settings.Current.PetInputBubble && !expanded)))
            visible = false;
        if (inst._chromeVisible == visible) return;
        inst._chromeVisible = visible;
        try
        {
            if (visible)
            {
                inst.PlaceChrome();
                if (inst._petWindow?.Handler?.PlatformView is Microsoft.UI.Xaml.Window xw) xw.AppWindow.Show();
            }
            else if (inst._petWindow?.Handler?.PlatformView is Microsoft.UI.Xaml.Window hidden) hidden.AppWindow.Hide();
        }
        catch (Exception ex)
        {
            App.WriteLog("PetService.SetChrome -> " + ex.Message);
        }
    }

    /// <summary>角色被拖走或缩放后，小窗要跟着回到头顶。</summary>
    public static void RepositionChrome() => _instance?.PlaceChrome();

    /// <summary>
    /// 气泡内容实测出来的高度（DIP，含自身内边距）。小窗展开后的高度跟着它长：
    /// 以前固定 168 DIP + MaxLines=4，长回复被静悄悄切成四行，读的人以为 AI 只说了这么多。
    /// </summary>
    public static void SetBubbleHeight(double dip)
    {
        var inst = _instance;
        if (inst is null) return;
        inst.SetBubbleHeightRaw(dip);
        // 高度是按整句量出来的，文字却可以一个字一个字浮出来（气泡浮现速度 + 打字音）
        inst.MaybeTypewriter();
    }

    private void SetBubbleHeightRaw(double dip)
    {
        var next = Math.Clamp(dip, BubbleMinDip, BubbleMaxDip);
        if (Math.Abs(next - _bubbleDip) < 1) return;
        _bubbleDip = next;
        if (_chromeVisible) PlaceChrome();
    }

    /// <summary>
    /// 鼠标停在小窗自己身上（正在读长回复、正在打字）——这时候不许收起来。
    /// 用的是 GetWindowRect 的真实物理矩形，不是 PlaceChrome 里算出来的那份：小窗是 MAUI 建的，
    /// 它自己活在被 DWM 缩放过的坐标系里（GetDpiForWindow 回 96，屏幕却是 125%），
    /// 两套坐标一比大小，等于永远比不中。
    /// </summary>
    public static bool IsCursorOnChrome()
    {
        var inst = _instance;
        if (inst is null || !inst._chromeVisible || inst._chromeHwnd == IntPtr.Zero) return false;
        if (!GetWindowRect(inst._chromeHwnd, out var r)) return false;
        if (!GetCursorPos(out var pt)) return false;
        return pt.X >= r.left && pt.X < r.right && pt.Y >= r.top && pt.Y < r.bottom;
    }

    /// <summary>显隐翻转时打一行探针：小窗真实矩形 + 鼠标点位。这两套坐标系不一致时，只有这条能看出来。</summary>
    public static void LogChromeProbe(string why)
    {
        var inst = _instance;
        if (inst is null) return;
        var rect = "no-window";
        if (inst._chromeHwnd != IntPtr.Zero && GetWindowRect(inst._chromeHwnd, out var r))
            rect = $"{r.left},{r.top} {r.right - r.left}x{r.bottom - r.top}";
        var cur = GetCursorPos(out var p) ? $"{p.X},{p.Y}" : "n/a";
        App.WriteLog($"PetService: 小窗{why} visible={inst._chromeVisible} rect={rect} cursor={cur}");
    }

    /// <summary>鼠标是否靠近角色头顶（窗口外一段也算）。分层窗口的矩形就是轮廓的包围盒，够用。</summary>
    public static bool IsCursorNearHead(int bandPx)
    {
        try
        {
            if (!Platforms.Windows.PetLayeredWindow.Exists) return false;
            if (!GetCursorPos(out var pt)) return false;
            var l = Platforms.Windows.PetLayeredWindow.Left;
            var r = l + Platforms.Windows.PetLayeredWindow.Width;
            var top = Platforms.Windows.PetLayeredWindow.Top;
            return pt.X >= l - bandPx && pt.X <= r + bandPx && pt.Y >= top - bandPx && pt.Y <= top + bandPx;
        }
        catch (Exception ex)
        {
            App.WriteLog("PetService.IsCursorNearHead -> " + ex.Message);
            return false;
        }
    }

    private RectInt32 GetPetDisplayArea()
    {
        if (_petWindow?.Handler?.PlatformView is Microsoft.UI.Xaml.Window xw)
        {
            var area = DisplayArea.GetFromWindowId(xw.AppWindow.Id, DisplayAreaFallback.Nearest);
            return area.WorkArea;
        }
        return DisplayArea.Primary.WorkArea;
    }

    private void PlaceChrome()
    {
        if (_petWindow?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window xw) return;
        var app = xw.AppWindow;
        var scale = GetDpiForWindow(WindowNative.GetWindowHandle(xw)) / 96f;
        if (scale <= 0f) scale = 1f;
        var w = (int)(ChromeWidthDip * scale);
        var h = (int)((_chromeRowDip + (_chromeExpanded
            ? BubbleGapDip + Math.Clamp(_bubbleDip, BubbleMinDip, BubbleMaxDip) : 0)) * scale);
        app.Resize(new SizeInt32(w, h));

        var area = GetChromeBounds();
        var top = Platforms.Windows.PetLayeredWindow.Exists
            ? Platforms.Windows.PetLayeredWindow.Top - (int)(10 * scale) - h
            : area.Y + 20;
        var left = Platforms.Windows.PetLayeredWindow.Exists
            ? Platforms.Windows.PetLayeredWindow.CenterX - w / 2
            : area.X + area.Width - w - 28;
        // 头顶不够位置（角色贴着屏幕顶）时，把小窗压回可用区，别飘到看不见的地方
        left = Math.Clamp(left, area.X + 8, Math.Max(area.X + 8, area.X + area.Width - w - 8));
        top = Math.Max(area.Y + 8, top);
        app.Move(new PointInt32(left, top));
        if (h != _chromeLoggedH)
        {
            // 只在高度变化时写：拖拽时每帧都会重排，全记会把日志刷爆。
            _chromeLoggedH = h;
            App.WriteLog($"PetService: 小窗 {left},{top} {w}x{h}（scale={scale:F2} bubble={_bubbleDip:F0} expanded={_chromeExpanded}）");
        }
    }

    /// <summary>
    /// 小窗允许待的范围。关掉「跨屏跟随」时就是角色那块屏的工作区——角色贴着屏缝时，
    /// 小窗折回本屏，而不是跨过缝跑到另一块屏上去（那块屏上没人看着它）；
    /// 打开时按整个虚拟桌面算，角色拖到副屏，小窗就跟着过去。
    /// 虚拟桌面没有工作区概念（副屏一般没有任务栏），这里直接用整块包围盒。
    /// </summary>
    private RectInt32 GetChromeBounds()
    {
        if (_settings.Current.PetChromeCrossScreen)
        {
            var x = GetSystemMetrics(SM_XVIRTUALSCREEN);
            var y = GetSystemMetrics(SM_YVIRTUALSCREEN);
            var cw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            var ch = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            if (cw > 0 && ch > 0) return new RectInt32(x, y, cw, ch);
        }
        return GetPetDisplayArea();
    }

    private void ClosePetWindow()
    {
        Platforms.Windows.PetLayeredWindow.Close();
        _typeCts?.Cancel();
        _typedTarget = "";
        _typing = false;
        if (_petWindow is not null && Application.Current.Windows.Contains(_petWindow))
        {
            try { Application.Current.CloseWindow(_petWindow); } catch { }
        }
        _petWindow = null;
        _page = null;
        _chromeVisible = false;
        _chromeExpanded = false;
    }

    /// <summary>主窗口关闭时调用：收起桌宠窗口并移除托盘图标，确保干净退出。</summary>
    public void Shutdown()
    {
        try
        {
            _poseRevert?.Change(Timeout.Infinite, Timeout.Infinite);   // 进程正在收：别让回退回调对着已关的窗口贴帧
            _pack = null;
            ClosePetWindow();
            RemoveTray();
            _petMode = false;
        }
        catch (Exception ex)
        {
            App.WriteLog("PetService.Shutdown -> " + ex);
        }
    }

    // ============ 托盘 ============
    private void EnsureTray()
    {
        if (_iconAdded) return;
        _procDelegate ??= WndProcHandler;
        var hInstance = GetModuleHandle(null);

        var wc = new WNDCLASS
        {
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_procDelegate),
            hInstance = hInstance,
            lpszClassName = "WarmAsBeforeTrayWnd"
        };
        _ = RegisterClassW(ref wc);
        _msgWnd = CreateWindowExW(0, "WarmAsBeforeTrayWnd", "WarmAsBeforeTray", 0,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (_msgWnd == IntPtr.Zero) return;

        _trayIcon = ExtractIcon(hInstance, Environment.ProcessPath ?? "", 0);
        if (_trayIcon == IntPtr.Zero) _trayIcon = LoadIcon(IntPtr.Zero, "APPICON");

        var data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _msgWnd,
            uID = ID_TRAY,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _trayIcon,
            szTip = "温暖如初 · 点击恢复窗口"
        };
        Shell_NotifyIcon(NIM_ADD, ref data);
        _iconAdded = true;
    }

    private IntPtr WndProcHandler(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAYICON)
        {
            var evt = lParam.ToInt32() & 0xFFFF;
            switch (evt)
            {
                case WM_LBUTTONUP:
                    Helpers.Ui.Post(ShowMainWindow);
                    return IntPtr.Zero;
                case WM_LBUTTONDBLCLK:
                    Helpers.Ui.Post(ShowMainWindow);
                    return IntPtr.Zero;
                case WM_RBUTTONUP:
                    ShowMenu();
                    return IntPtr.Zero;
            }
        }
        else if (msg == WM_COMMAND && wParam.ToInt32() == ID_EXIT)
        {
            Helpers.Ui.Post(() => { RemoveTray(); Application.Current?.Quit(); });
            return IntPtr.Zero;
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowMenu()
    {
        try
        {
            var menu = CreatePopupMenu();
            _ = AppendMenu(menu, MF_STRING, ID_SHOW, "显示主窗口");
            _ = AppendMenu(menu, MF_STRING, ID_PET_SETTINGS, "桌宠设置");
            _ = AppendMenu(menu, MF_STRING, ID_EXIT, "退出游戏");
            _ = GetCursorPos(out var pt);
            var cmd = TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, 0, _msgWnd, IntPtr.Zero);
            _ = DestroyMenu(menu);
            if (cmd == (uint)ID_SHOW)
                Helpers.Ui.Post(ShowMainWindow);
            else if (cmd == (uint)ID_PET_SETTINGS)
            {
                Helpers.Ui.Post(ShowMainWindow);
                _ = OpenPetSettingsAsync();
            }
            else if (cmd == (uint)ID_EXIT)
                Helpers.Ui.Post(() => { RemoveTray(); Application.Current?.Quit(); });
        }
        catch (Exception ex)
        {
            App.WriteLog("PetService.ShowMenu -> " + ex);
        }
    }

    private void RemoveTray()
    {
        if (!_iconAdded) return;
        var data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _msgWnd,
            uID = ID_TRAY
        };
        Shell_NotifyIcon(NIM_DELETE, ref data);
        _iconAdded = false;
        if (_msgWnd != IntPtr.Zero) { _ = DestroyWindow(_msgWnd); _msgWnd = IntPtr.Zero; }
    }
#endif
#else
    // 移动端：桌宠模式 = 全屏问答模式（进入PetPage）
    private readonly Shell _shell;
    
    public PetService(SettingsManager settings, Shell shell)
    {
        _instance = this;
        _settings = settings;
        _shell = shell;
    }
    
    /// <summary>静态入口：进入/退出全屏问答模式。</summary>
    public static void TogglePetModeStatic() => _instance?.TogglePetMode();
    public static void ShowMainWindowStatic() => _instance?.ShowMainWindow();
    
    public void TogglePetMode()
    {
        if (_petMode) { ShowMainWindow(); return; }
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await _shell.GoToAsync("pet");
                _petMode = true;
                App.WriteLog("PetService: pet mode (fullscreen chat) on");
            }
            catch (Exception ex)
            {
                App.WriteLog("PetService.TogglePetMode -> " + ex);
            }
        });
    }
    
    public void ShowMainWindow()
    {
        if (!_petMode) return;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await _shell.GoToAsync("main");
                _petMode = false;
                App.WriteLog("PetService: main window restored");
            }
            catch (Exception ex)
            {
                App.WriteLog("PetService.ShowMainWindow -> " + ex);
            }
        });
    }
    
    public void WatchMinimize() { }
    public void WatchIdle() { }
    public void Shutdown() { }

    /// <summary>
    /// 移动端没有分层窗口可重贴：体型就是 PetPage 里那张立绘的 Scale，
    /// 页面自己在 OnAppearing 与捏合结束时读写设置。这里留同名空实现，设置页才能不分平台地调它。
    /// </summary>
    public static void ApplyScale() { }

    /// <summary>移动端没有「站在桌面上的角色」，也就没有锚点可记：返回 false，让那一页说人话。</summary>
    public static bool CaptureAnchor() => false;

    /// <summary>移动端试听：直接走「声音分配」里那格（全局音效音量之下，没有按类别的音量这一层）。
    /// 设置页上那几档音量只在桌面端可见，所以这里不会让人误以为手机上能调。</summary>
    public static void PlayPetSfx(string category)
    {
        if (string.IsNullOrWhiteSpace(category)) return;
        if (Application.Current?.Handler?.MauiContext?.Services?.GetService(typeof(AudioController))
            is not AudioController ac) return;
        ac.PlayAssigned(category switch { "typing" => "key", "purr" => "purr", _ => "left" });
    }

    // 帧动画这一套要有一个「按像素贴到桌面、还能逐帧换」的表面才成立；手机上是 XAML 里那张 Image，
    // 没有分层窗口，所以工作室生成的包在手机上先只是存着。留同名空实现，页面才能不分平台地叫。
    public static void AttachAnimation(string characterId, string emotion) { }
    public static void SetEmotion(string emotion) { }
    public static void ReloadAnimation() { }
    public static void ShowPoseFor(string stateId, double seconds) { }
#endif
}
