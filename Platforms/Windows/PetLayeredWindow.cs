using System.Runtime.InteropServices;
using SkiaSharp;

namespace WarmAsBefore.Platforms.Windows;

/// <summary>
/// 桌宠立绘的宿主：自建 Win32 分层窗口 + UpdateLayeredWindow 按像素 alpha 贴图。
/// 这是「立绘直接站在桌面上」唯一走得通的实现——WinUI3 桌面窗口的组合表面是不透明的
/// （三条路实测过，结论记在 PetService.StylePetWindowSurface 的注释里），XAML 里怎么写
/// 透明都会先跟那层表面混合。这里不挂 XAML，只贴一张预乘 BGRA 的位图：
/// alpha=0 的像素既不显示也不吃鼠标，所以角色的轮廓之外就是桌面本身。
/// 立绘自带的透明余量（标准画布 1080×1920 四周的空边）会被裁掉，窗口紧贴角色轮廓，
/// 所以「脚下」不会有看不见的空板压住桌面上的图标。
/// </summary>
internal static class PetLayeredWindow
{
    /// <summary>单击角色（已排除拖拽与双击）。</summary>
    public static event Action? Pressed;
    /// <summary>双击角色。</summary>
    public static event Action? DoublePressed;
    /// <summary>滚轮：正=放大。</summary>
    public static event Action<int>? WheelTurned;
    /// <summary>拖拽结束（位置已改）。</summary>
    public static event Action? Moved;

    /// <summary>头顶留白（逻辑像素）：输入条按这条线定位，角色头发不会被它压住。</summary>
    const int HeadroomDip = 40;
    /// <summary>头顶留白（物理像素，按本窗口 DPI 换算）。</summary>
    static int _headroom = HeadroomDip;
    public static int Headroom => _headroom;

    public static bool Exists => _hwnd != IntPtr.Zero;
    public static int Left => _x;
    public static int Top => _y;
    public static int Width => _w;
    public static int Height => _h;
    public static int CenterX => _x + _w / 2;
    /// <summary>角色脚底所在的屏幕纵坐标（物理像素）。</summary>
    public static int BottomY => _y + _h;

    static IntPtr _hwnd;
    static WndProc? _proc;
    static bool _classRegistered;
    static SKBitmap? _src;
    static SKRectI _body;
    static int _x, _y, _w, _h;
    static int _bodyH;
    static string _path = "";
    static bool _dragging;
    static int _grabX, _grabY, _originX, _originY;
    // 双击判定的上一击：系统中意的第二次按下会换成 WM_LBUTTONDBLCLK 送进来，
    // 但这个分层窗口收不到那条消息（实测），只能自己按「双击时长 + 双击容差矩形」配对。
    // 记下第一次松手的时刻与位置，下一次按下落在窗口内就算双击的前半。
    static uint _lastUpTick;
    static int _lastUpX, _lastUpY;
    static bool _swallowNextUp;

    // ============ 对外几何 ============

    /// <summary>把角色摆在右下角：给定右沿与地面纵坐标（物理像素），以及角色本体高度。</summary>
    public static void Open(string pngPath, int rightEdgeX, int groundY, int bodyHeightPx)
    {
        if (!Load(pngPath)) return;
        EnsureWindow();
        _bodyH = Math.Clamp(bodyHeightPx, 120, 4000);
        Measure();
        _x = rightEdgeX - _w;
        _y = groundY - _h;
        ApplyPosition();
    }

    /// <summary>按倍率缩放：右脚与地面都不动，角色只往上、往左长。</summary>
    public static void Zoom(double factor)
    {
        if (_hwnd == IntPtr.Zero) return;
        var right = _x + _w;
        var ground = _y + _h;
        _bodyH = Math.Clamp((int)Math.Round(_bodyH * factor), 120, 4000);
        Measure();
        _x = right - _w;
        _y = ground - _h;
        ApplyPosition();
    }

    public static void Close()
    {
        // 手势判定状态跟着窗口一起清掉：双击的收尾松手可能落在窗口销毁之后才到，
        // 于是 _swallowNextUp 会一路留到下一次进桌宠，白吃掉那之后的第一下单击。
        _lastUpTick = 0; _swallowNextUp = false; _dragging = false;
        if (_hwnd != IntPtr.Zero) { _ = DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
        _src?.Dispose(); _src = null;
        _path = "";
    }

    // ============ 位图 ============

    static bool Load(string pngPath)
    {
        if (string.Equals(_path, pngPath, StringComparison.OrdinalIgnoreCase) && _src is not null) return true;
        try
        {
            var bmp = SKBitmap.Decode(pngPath);
            if (bmp is null) { App.WriteLog($"PetLayeredWindow: 解码失败 {pngPath}"); return false; }
            _src?.Dispose();
            _src = bmp;
            _path = pngPath;
            _body = OpaqueBounds(bmp);
            if (_body.Width <= 0 || _body.Height <= 0)
            {
                App.WriteLog($"PetLayeredWindow: 立绘全透明 {pngPath}");
                _body = new SKRectI(0, 0, bmp.Width, bmp.Height);
            }
            return true;
        }
        catch (Exception ex)
        {
            App.WriteLog("PetLayeredWindow.Load -> " + ex.Message);
            return false;
        }
    }

    /// <summary>量出真正有像素的矩形：标准画布四周的透明余量不该占着窗口，更不该挡住桌面。</summary>
    static SKRectI OpaqueBounds(SKBitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height, rb = bmp.RowBytes;
        var buf = new byte[rb * h];
        Marshal.Copy(bmp.GetPixels(), buf, 0, buf.Length);
        // 预乘与非预乘、BGRA 与 RGBA 的内存排布里，alpha 都是每像素的第 4 个字节
        const int aOff = 3;
        int l = int.MaxValue, t = int.MaxValue, r = -1, b = -1;
        for (var y = 0; y < h; y++)
        {
            var row = y * rb;
            var rowHit = false;
            for (var x = 0; x < w; x++)
            {
                if (buf[row + x * 4 + aOff] <= 8) continue;
                rowHit = true;
                if (x < l) l = x;
                if (x > r) r = x;
            }
            if (rowHit) { if (y < t) t = y; if (y > b) b = y; }
        }
        if (r < 0) return SKRectI.Empty;
        return new SKRectI(l, t, r + 1, b + 1);
    }

    static void Measure()
    {
        if (_src is null) return;
        var aspect = (float)_body.Width / _body.Height;
        _w = Math.Max(16, (int)Math.Round(_bodyH * aspect));
        _h = _bodyH + Headroom;
    }

    /// <summary>挪动/改尺寸只需要 SetWindowPos；位图仍由上一次 UpdateLayeredWindow 持有。</summary>
    static void ApplyPosition()
    {
        _ = SetWindowPos(_hwnd, IntPtr.Zero, _x, _y, _w, _h, SWP_NOZORDER | SWP_NOACTIVATE);
        Render();
    }

    static void Render()
    {
        if (_hwnd == IntPtr.Zero || _src is null || _w <= 0 || _h <= 0) return;
        SKBitmap? pixels = null;
        IntPtr screen = IntPtr.Zero, mem = IntPtr.Zero, dib = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            pixels = new SKBitmap(new SKImageInfo(_w, _h, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(pixels))
            {
                canvas.Clear(SKColors.Transparent);
                // 本体贴在下沿，头顶留出 Headroom：那段空白只给对话小窗让位，不写提示字
                var dest = new SKRect(0, Headroom, _w, _h);
                var srcRect = new SKRect(_body.Left, _body.Top, _body.Right, _body.Bottom);
                using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
                    canvas.DrawBitmap(_src, srcRect, dest, paint);
                canvas.Flush();
            }

            screen = GetDC(_hwnd);
            mem = CreateCompatibleDC(screen);
            var bmi = new BITMAPINFO();
            bmi.Header.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
            bmi.Header.biWidth = _w;
            bmi.Header.biHeight = -_h;          // 负值 = 自上而下，和 Skia 的行序一致，不必翻图
            bmi.Header.biPlanes = 1;
            bmi.Header.biBitCount = 32;
            bmi.Header.biCompression = BI_RGB;
            dib = CreateDIBSection(screen, ref bmi, DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero) return;
            old = SelectObject(mem, dib);

            var stride = _w * 4;
            var rowBytes = pixels.RowBytes;
            var ptr = pixels.GetPixels();
            for (var y = 0; y < _h; y++)
                CopyMemory(bits + y * stride, ptr + y * rowBytes, (UIntPtr)stride);

            var dst = new POINT { X = _x, Y = _y };
            var size = new SIZE { CX = _w, CY = _h };
            var srcOrigin = new POINT { X = 0, Y = 0 };
            var blend = new BLENDFUNCTION
            {
                BlendOp = AC_SRC_OVER,
                SourceConstantAlpha = 255,       // 整窗不额外压透明度，逐像素 alpha 由位图带
                AlphaFormat = AC_SRC_ALPHA,
                BlendFlags = 0,
            };
            var ok = UpdateLayeredWindow(_hwnd, screen, ref dst, ref size, mem, ref srcOrigin, 0, ref blend, ULW_ALPHA);
            if (!ok) App.WriteLog($"PetLayeredWindow: UpdateLayeredWindow 失败 gle={Marshal.GetLastWin32Error()}");
        }
        catch (Exception ex)
        {
            App.WriteLog("PetLayeredWindow.Render -> " + ex.Message);
        }
        finally
        {
            pixels?.Dispose();
            if (old != IntPtr.Zero && mem != IntPtr.Zero) SelectObject(mem, old);
            if (dib != IntPtr.Zero) DeleteObject(dib);
            if (mem != IntPtr.Zero) DeleteDC(mem);
            if (screen != IntPtr.Zero) ReleaseDC(_hwnd, screen);
        }
    }

    // ============ 窗口 ============

    static void EnsureWindow()
    {
        if (_hwnd != IntPtr.Zero) return;
        var hinst = GetModuleHandle(null);
        if (!_classRegistered)
        {
            // 委托必须常驻：窗口类存的是函数指针，类登记一次就再也不会更新，
            // 一旦这个委托被 GC 收掉，下一次消息打进来撞的是已释放的桩。
            _proc = WndProcHandler;
            var wc = new WNDCLASS
            {
                style = CS_DBLCLKS,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
                hInstance = hinst,
                hCursor = LoadCursorW(IntPtr.Zero, IDC_ARROW),
                lpszClassName = ClassName,
            };
            var atom = RegisterClassW(ref wc);
            if (atom == 0)
                App.WriteLog($"PetLayeredWindow: RegisterClassW 失败 gle={Marshal.GetLastWin32Error()}");
            _classRegistered = true;
        }
        _hwnd = CreateWindowExW(
            WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
            ClassName, "温暖如初 · 桌宠", WS_POPUP,
            0, 0, 64, 64, IntPtr.Zero, IntPtr.Zero, hinst, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
        {
            App.WriteLog($"PetLayeredWindow: CreateWindowExW 失败 gle={Marshal.GetLastWin32Error()}");
            return;
        }
        // SW_SHOWNA：显示但不抢焦点——一个会抢键盘的角色，用户第一个动作就是嫌它。
        _ = ShowWindow(_hwnd, SW_SHOWNA);
        // 留白按这个窗口所在显示器的 DPI 换算：150% 屏上 40 物理像素只有 27 逻辑像素，
        // 输入条会压到头发上。
        var dpi = GetDpiForWindow(_hwnd);
        if (dpi > 0) _headroom = Math.Max(HeadroomDip, (int)Math.Round(HeadroomDip * dpi / 96f));
    }

    static IntPtr WndProcHandler(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (msg)
            {
                case WM_LBUTTONDOWN:
                    _dragging = false;
                    _ = SetCapture(hWnd);
                    if (GetCursorPos(out var p)) { _grabX = p.X; _grabY = p.Y; _originX = _x; _originY = _y; }
                    if (IsSecondOfDouble(p.X, p.Y))
                    {
                        _lastUpTick = 0;
                        _swallowNextUp = true; // 这一击的松手是双击的收尾，不再算一次单击
                        DoublePressed?.Invoke();
                    }
                    return IntPtr.Zero;
                case WM_MOUSEMOVE:
                    if (GetCapture() != hWnd) break;
                    if (!GetCursorPos(out var c)) break;
                    var dx = c.X - _grabX; var dy = c.Y - _grabY;
                    // 6px 之内不算拖：不然每一次「其实没打算移动」的单击都会把角色挪走半格。
                    // 一旦确认在拖，后面每一帧都要跟着走——早先这里写的是 if (_dragging) break，
                    // 结果角色只跟着第一步动，剩下 240px 的手势全被 DefWindowProc 吃掉了。
                    if (!_dragging && Math.Abs(dx) + Math.Abs(dy) < 6) break;
                    _dragging = true;
                    _lastUpTick = 0; // 拖过就不可能是双击的后半
                    _x = _originX + dx; _y = _originY + dy;
                    _ = SetWindowPos(hWnd, IntPtr.Zero, _x, _y, _w, _h, SWP_NOZORDER | SWP_NOACTIVATE);
                    return IntPtr.Zero;
                case WM_LBUTTONUP:
                    _ = ReleaseCapture();
                    if (_dragging) { _dragging = false; _lastUpTick = 0; _swallowNextUp = false; Moved?.Invoke(); return IntPtr.Zero; }
                    if (_swallowNextUp) { _swallowNextUp = false; return IntPtr.Zero; }
                    if (GetCursorPos(out var u)) { _lastUpTick = GetTickCount(); _lastUpX = u.X; _lastUpY = u.Y; }
                    Pressed?.Invoke();
                    return IntPtr.Zero;
                case WM_LBUTTONDBLCLK:
                    _dragging = false;
                    _lastUpTick = 0;
                    _swallowNextUp = true; // 双击后紧跟的那次松手不算第三次单击
                    DoublePressed?.Invoke();
                    return IntPtr.Zero;
                case WM_MOUSEWHEEL:
                    var delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
                    if (delta != 0) WheelTurned?.Invoke(delta);
                    return IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("PetLayeredWindow.WndProc -> " + ex.Message);
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    /// <summary>
    /// 这一击是不是双击的第二次按下。系统只在窗口类带 CS_DBLCLKS 且消息能正常派发时才送
    /// WM_LBUTTONDBLCLK；这个不激活、不抢焦点的分层窗口实测收不到那条消息，于是自己配对：
    /// 上一次松手的时刻 + 位置，落进系统的双击时长与容差矩形里就算双击。
    /// </summary>
    static bool IsSecondOfDouble(int x, int y)
    {
        if (_lastUpTick == 0) return false;
        if (GetTickCount() - _lastUpTick > GetDoubleClickTime()) { _lastUpTick = 0; return false; }
        var tolX = Math.Max(2, GetSystemMetrics(SM_CXDOUBLECLK) / 2);
        var tolY = Math.Max(2, GetSystemMetrics(SM_CYDOUBLECLK) / 2);
        var inside = Math.Abs(x - _lastUpX) <= tolX && Math.Abs(y - _lastUpY) <= tolY;
        if (!inside) _lastUpTick = 0;
        return inside;
    }

    // ============ P/Invoke ============

    const uint CS_DBLCLKS = 0x00000001;
    const uint WM_MOUSEMOVE = 0x0200;
    const uint WM_LBUTTONDOWN = 0x0201;
    const uint WM_LBUTTONUP = 0x0202;
    const uint WM_LBUTTONDBLCLK = 0x0203;
    const uint WM_MOUSEWHEEL = 0x020A;
    const int SM_CXDOUBLECLK = 36;
    const int SM_CYDOUBLECLK = 37;
    const int WS_POPUP = unchecked((int)0x80000000);
    const uint WS_EX_LAYERED = 0x00080000;
    const uint WS_EX_TOPMOST = 0x00000008;
    const uint WS_EX_TOOLWINDOW = 0x00000080;
    const uint WS_EX_NOACTIVATE = 0x08000000;
    const uint SWP_NOZORDER = 0x0004;
    const uint SWP_NOACTIVATE = 0x0010;
    const int SW_SHOWNA = 8;
    const int IDC_ARROW = 32512;
    const uint BI_RGB = 0;
    const uint DIB_RGB_COLORS = 0;
    const uint ULW_ALPHA = 0x00000002;
    const byte AC_SRC_OVER = 0x00;
    const byte AC_SRC_ALPHA = 0x01;
    const string ClassName = "WarmAsBeforePetSprite";

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int CX, CY; }

    [StructLayout(LayoutKind.Sequential)]
    struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFO { public BITMAPINFOHEADER Header; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASS
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        // LPWSTR，不是内联字符数组：写成 ByValTStr 会让结构体在系统期待指针的地方多出 512 字节，
        // RegisterClassW 于是读到一堆垃圾当类名，注册静默失败，后面 CreateWindowExW 找不到类。
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszClassName;
    }

    delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string? lpModuleName);
    [DllImport("user32.dll", SetLastError = true)] static extern ushort RegisterClassW(ref WNDCLASS lpWndClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateWindowExW(
        uint exStyle, string className, string windowName, int style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern IntPtr SetCapture(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr GetCapture();
    [DllImport("user32.dll")] static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("kernel32.dll")] static extern uint GetTickCount();
    [DllImport("user32.dll")] static extern IntPtr LoadCursorW(IntPtr hInstance, int id);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
    [DllImport("user32.dll", SetLastError = true)] static extern bool UpdateLayeredWindow(
        IntPtr hWnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc,
        ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage,
        out IntPtr ppvBits, IntPtr section, uint offset);
    [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")] static extern void CopyMemory(IntPtr dest, IntPtr src, UIntPtr count);
}
