using System.Runtime.InteropServices;

#if WINDOWS
using Microsoft.UI.Windowing;
using Windows.Graphics;
using WinRT.Interop;
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
    private const int ID_TRAY = 1;

    private IntPtr _msgWnd;
    private IntPtr _trayIcon;
    private bool _iconAdded;
    private bool _listening;
    private Window? _petWindow;
    private bool _chromeVisible;
    private bool _chromeExpanded;

    // 头顶小窗的几何：尺寸按 DIP 设计（右下存的是换算后的物理像素，供鼠标命中判定用）
    private const double ChromeWidthDip = 340;
    private const double ChromeRowDip = 56;        // 收起时只有输入行
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
    }

    // ============ 立绘与头顶小窗的联动（供 PetPage 调用） ============

    /// <summary>把角色立绘贴到桌面上（右下角、脚踩工作区底沿），并定位头顶小窗。</summary>
    public static void AttachSprite(string absPath)
    {
        var inst = _instance;
        if (inst is null) return;
        try
        {
            var area = inst.GetPetDisplayArea();
            var bodyH = Math.Clamp((int)(area.Height * 0.42), 240, 900);
            Platforms.Windows.PetLayeredWindow.Open(absPath,
                area.X + area.Width - 28, area.Y + area.Height - 8, bodyH);
            inst.PlaceChrome();
        }
        catch (Exception ex)
        {
            App.WriteLog("PetService.AttachSprite -> " + ex);
        }
    }

    /// <summary>小窗显隐；expanded=true 时给回复气泡腾出高度（气泡向上长，输入行位置不动）。</summary>
    public static void SetChrome(bool visible, bool expanded)
    {
        var inst = _instance;
        if (inst is null) return;
        if (inst._chromeExpanded != expanded) { inst._chromeExpanded = expanded; inst.PlaceChrome(); }
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
        var next = Math.Clamp(dip, BubbleMinDip, BubbleMaxDip);
        if (Math.Abs(next - inst._bubbleDip) < 1) return;
        inst._bubbleDip = next;
        if (inst._chromeVisible) inst.PlaceChrome();
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
        var h = (int)((ChromeRowDip + (_chromeExpanded
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
        if (_petWindow is not null && Application.Current.Windows.Contains(_petWindow))
        {
            try { Application.Current.CloseWindow(_petWindow); } catch { }
        }
        _petWindow = null;
        _chromeVisible = false;
        _chromeExpanded = false;
    }

    /// <summary>主窗口关闭时调用：收起桌宠窗口并移除托盘图标，确保干净退出。</summary>
    public void Shutdown()
    {
        try
        {
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
                    MainThread.BeginInvokeOnMainThread(ShowMainWindow);
                    return IntPtr.Zero;
                case WM_LBUTTONDBLCLK:
                    MainThread.BeginInvokeOnMainThread(ShowMainWindow);
                    return IntPtr.Zero;
                case WM_RBUTTONUP:
                    ShowMenu();
                    return IntPtr.Zero;
            }
        }
        else if (msg == WM_COMMAND && wParam.ToInt32() == ID_EXIT)
        {
            MainThread.BeginInvokeOnMainThread(() => { RemoveTray(); Application.Current?.Quit(); });
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
            _ = AppendMenu(menu, MF_STRING, ID_EXIT, "退出游戏");
            _ = GetCursorPos(out var pt);
            var cmd = TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, 0, _msgWnd, IntPtr.Zero);
            _ = DestroyMenu(menu);
            if (cmd == (uint)ID_SHOW)
                MainThread.BeginInvokeOnMainThread(ShowMainWindow);
            else if (cmd == (uint)ID_EXIT)
                MainThread.BeginInvokeOnMainThread(() => { RemoveTray(); Application.Current?.Quit(); });
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
#endif
}
