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
    private bool _petMode;
    private bool _listening;
    private Window? _petWindow;

    private readonly SettingsManager _settings;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private const uint MF_STRING = 0x0;
    private const uint TPM_RIGHTBUTTON = 0x2;
    private const uint TPM_RETURNCMD = 0x100;
    private const int SW_RESTORE = 9;

    private static PetService? _instance;
    private static WndProc? _procDelegate;

    public PetService(SettingsManager settings)
    {
        _instance = this;
        _settings = settings;
    }

    /// <summary>静态入口：桌宠页双击回到主窗口。</summary>
    public static void ShowMainWindowStatic() => _instance?.ShowMainWindow();

    public static void BeginDrag(Microsoft.Maui.Controls.Page? page)
    {
        if (page?.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.Panel) return;
        var win = page.Window;
        if (win?.Handler?.PlatformView is Microsoft.UI.Xaml.Window xw)
        {
            var hwnd = WindowNative.GetWindowHandle(xw);
            ReleaseCapture();
            _ = PostMessage(hwnd, 0x00A1 /* WM_NCLBUTTONDOWN */, new IntPtr(2 /* HTCAPTION */), IntPtr.Zero);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

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
            main.AppWindow.Hide();
            _petMode = true;
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

    /// <summary>主窗口最小化时自动收纳为桌宠（挂接一次监听）。</summary>
    public void WatchMinimize()
    {
        if (_listening) return;
        _listening = true;
        var main = GetMainWinUIWindow();
        if (main is null) return;
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

    private static Microsoft.UI.Xaml.Window? GetMainWinUIWindow()
    {
        var win = Application.Current?.Windows.FirstOrDefault(w => w.Handler is not null);
        return win?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
    }

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
            Width = 320,
            Height = 460
        };
        Application.Current.OpenWindow(win);
        _petWindow = win;
        // 无边框 + 置顶 + 右下角摆放
        if (win.Handler?.PlatformView is Microsoft.UI.Xaml.Window xw && xw.AppWindow is { } app)
        {
            app.Resize(new SizeInt32(320, 460));
            var area = DisplayArea.GetFromWindowId(app.Id, DisplayAreaFallback.Nearest);
            app.Move(new PointInt32(area.WorkArea.X + area.WorkArea.Width - 340,
                area.WorkArea.Y + area.WorkArea.Height - 480));
            if (app.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsAlwaysOnTop = true;
            }
        }
    }

    private void ClosePetWindow()
    {
        if (_petWindow is not null && Application.Current.Windows.Contains(_petWindow))
        {
            try { Application.Current.CloseWindow(_petWindow); } catch { }
        }
        _petWindow = null;
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
    public static void BeginDrag(Microsoft.Maui.Controls.Page? page) { }
#endif
}
