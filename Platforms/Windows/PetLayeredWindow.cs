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
///
/// 设置页的每一项在这里都有对应的真实出口：
/// · 外发光 → 贴图前先在轮廓上铺一层同形柔光（SKImageFilter 模糊）；
/// · 帧率/锁定 → 走动与呼噜的 WM_TIMER 抽帧间隔；
/// · 走动 → 沿工作区底沿平移、到边回头（镜像靠重画一次缓存，不逐帧翻转）；
/// · 悬停按钮 → 画在头顶留白带里，鼠标停在角色上才亮，按下发 HoverButtonPressed；
/// · 状态气泡 → 同一留白带的一行小字；
/// · 全屏收起 → 前台窗口盖满整块显示器且不是自家进程时 SW_HIDE，退出再回来。
/// 绘制走「缓存位图 + 每帧上传」：几何/发光/按钮变化才重建缓存，走动与呼噜只平移缓存上传，
/// 不在每帧重做高质量缩放——这是不引入绘制回归的前提。
/// </summary>
internal static class PetLayeredWindow
{
    /// <summary>单击角色（已排除拖拽与双击）。</summary>
    public static event Action? Pressed;
    /// <summary>双击角色——仅在设置里双击动作=回窗口时发出（其余走 Router）。</summary>
    public static event Action? DoublePressed;
    /// <summary>滚轮：正=放大。</summary>
    public static event Action<int>? WheelTurned;
    /// <summary>拖拽结束（位置已改）。</summary>
    public static event Action? Moved;
    /// <summary>悬停按钮被按下（id 见设置里 PetHoverButtons 的目录）。</summary>
    public static event Action<string>? HoverButtonPressed;
    /// <summary>全屏收起翻转：true=被收走了。</summary>
    public static event Action<bool>? FullscreenHidden;
    /// <summary>散步途中小位置变了，头顶小窗该跟上（约每 0.6 秒一次，不是每帧）。</summary>
    public static event Action? PositionSynced;
    /// <summary>双击动作路由：返回设置里的 PetDoubleClick 值。没挂 = 老行为（发 DoublePressed）。</summary>
    public static Func<string>? DoubleClickRouter;

    /// <summary>头顶留白（逻辑像素）：输入条按这条线定位，角色头发不会被它压住。</summary>
    const int HeadroomDip = 44;
    /// <summary>头顶留白（物理像素，按本窗口 DPI 换算）。留白带也是悬停按钮与状态行的位置。</summary>
    static int _headroom = HeadroomDip;
    public static int Headroom => _headroom;

    public static bool Exists => _hwnd != IntPtr.Zero;
    public static int Left => _x;
    public static int Top => _y;
    public static int Width => _w;
    public static int Height => _h;
    /// <summary>角色本体高度（物理像素，不含头顶留白）。滚轮缩放写回设置时要拿它反算倍率。</summary>
    public static int BodyHeight => _bodyH;
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

    // ============ 运行时设置（由 PetService 在 Applied 时推过来） ============

    static double _glow;                       // 0–1 外发光
    static int _fps = 30;                      // 自发动作帧率
    static bool _fpsLock = true;
    static bool _roam;                         // 常驻散步
    static long _walkBurstUntil;               // 「走走」按钮的一次性散步
    static long _purrUntil;                    // 呼噜抖动窗口
    static readonly List<string> _buttonIds = new();   // 悬停按钮 id（≤6，顺序即显示顺序）
    static string _statusText = "";
    static int _fontDip = 12;
    static bool _fsGuard;                      // 全屏自动隐藏开关
    static float _dpiScale = 1f;

    static SKBitmap? _cache;                   // 几何不变时的成品帧（发光+立绘+按钮/状态都已画好）
    static SKBitmap? _frame;                   // 只有平移抖动的合成帧
    static bool _cacheDirty = true;
    static bool _buttonsVisible;
    static long _lastHoverAt;
    static int _dir = 1; static bool _flip;    // 散步方向与镜像
    static double _walkPos;                    // 亚像素步进的累加器
    static long _lastPositionSyncAt;
    static int _motionIntervalMs;              // 当前 WM_TIMER 间隔，变了才重挂
    static bool _fsHidden;

    // ============ 帧动画通道（AI 制作桌宠逐状态生成的帧序列） ============

    const int MaxAnimationFrames = 12;         // 一次最多收的帧数：一帧可能是 1024 见方，收多了就是往桌面上堆内存
    static readonly List<string> _framePaths = new();   // 当前这段动画的绝对路径：改尺寸要按它重解，不再找宿主要
    static readonly List<SKBitmap> _frames = new();     // 后台解码好的逐帧位图，所有权整批在这张表里
    static double _frameFps;                   // 动画自己该走的帧率，并进 MOTION_TIMER 的那一档
    static int _frameIndex;
    static long _nextFrameAt;                  // 下一帧该换的时刻（TickCount64）
    static int _frameWorkH;                    // 这批帧是按多大的显示高度备的货：重解判据拿它跟 _bodyH 比
    static int _framesGeneration;              // 代次：切状态/撤动画后，在飞的解码按它对号，对不上就丢
    static SKBitmap? _staticSrc;               // 进动画前那张静态立绘的引用——撤回时按引用换回，不重新解码
    static SKRectI _staticBody;

    // ============ 对外几何 ============

    /// <summary>把角色摆在右下角：给定右沿与地面纵坐标（物理像素），以及角色本体高度。</summary>
    public static void Open(string pngPath, int rightEdgeX, int groundY, int bodyHeightPx)
    {
        // 只有真的换立绘才交还动画通道：动画在播时 _src 指的是某一帧，直接进 Load 会被当成旧立绘 Dispose 掉。
        // 同一路径重复 Open 只是重摆位置，不该打断正在播的那段动画。
        if (_framePaths.Count > 0 && !string.Equals(pngPath, _path, StringComparison.OrdinalIgnoreCase))
            ReleaseFrames(false);
        if (!Load(pngPath)) return;
        EnsureWindow();
        _bodyH = Math.Clamp(bodyHeightPx, 120, 4000);
        Measure();
        _x = rightEdgeX - _w;
        _y = groundY - _h;
        ApplyPosition();
        if (_fsGuard) StartTimer(FS_TIMER, 2000);
        MaybeRedecodeFrames();
        EnsureMotionTimer();
    }

    /// <summary>把角色本体设成绝对高度：右脚与地面都不动，只往上、往左长。</summary>
    public static void SetBodyHeight(int bodyHeightPx)
    {
        if (_hwnd == IntPtr.Zero) return;
        var right = _x + _w;
        var ground = _y + _h;
        _bodyH = Math.Clamp(bodyHeightPx, 120, 4000);
        Measure();
        _x = right - _w;
        _y = ground - _h;
        ApplyPosition();
        MaybeRedecodeFrames();
    }

    /// <summary>按倍率缩放：右脚与地面都不动，角色只往上、往左长。</summary>
    public static void Zoom(double factor) => SetBodyHeight((int)Math.Round(_bodyH * factor));

    /// <summary>回到记下的锚点（虚拟桌面坐标，物理像素）：越界收回屏幕里，但保证 80px 可见。</summary>
    public static void MoveTo(int left, int top)
    {
        if (_hwnd == IntPtr.Zero) return;
        var vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        _x = Math.Clamp(left, vx - _w + 80, vx + vw - 80);
        _y = Math.Clamp(top, vy - _h + 80, vy + vh - 80);
        _walkPos = _x;
        ApplyPosition();
    }

    public static void Close()
    {
        // 手势判定状态跟着窗口一起清掉：双击的收尾松手可能落在窗口销毁之后才到，
        // 于是 _swallowNextUp 会一路留到下一次进桌宠，白吃掉那之后的第一下单击。
        _lastUpTick = 0; _swallowNextUp = false; _dragging = false;
        if (_hwnd != IntPtr.Zero)
        {
            KillTimer(_hwnd, MOTION_TIMER); KillTimer(_hwnd, FS_TIMER);
            _motionIntervalMs = 0; _fsHidden = false; _walkPos = 0; _purrUntil = 0; _walkBurstUntil = 0;
            _ = DestroyWindow(_hwnd); _hwnd = IntPtr.Zero;
        }
        ReleaseFrames(true);   // 帧连同静态立绘的快照一起清掉：_src 上不留已死的引用，也不留没人认领的活口
        _src?.Dispose(); _src = null;
        _cache?.Dispose(); _cache = null;
        _frame?.Dispose(); _frame = null;
        _cacheDirty = true;
        _path = "";
    }

    // ============ 设置入口（每一项都对应设置页的一行） ============

    public static void SetGlow(double glow)
    {
        var g = Math.Clamp(glow, 0, 1);
        if (Math.Abs(g - _glow) < 0.01) return;
        _glow = g;
        Invalidate();
    }

    public static void SetMotion(int fps, bool fpsLock)
    {
        var f = Math.Clamp(fps, 10, 60);
        if (f == _fps && fpsLock == _fpsLock) return;
        _fps = f; _fpsLock = fpsLock;
        EnsureMotionTimer();   // 走动进行中就当场换拍，滑块才「立刻是那个节奏」
    }

    /// <summary>走动：off=站定 / roam=沿工作区底沿来回走。</summary>
    public static void SetWalking(string mode)
    {
        _roam = mode == "roam";
        if (!_roam) _walkBurstUntil = 0;
        EnsureMotionTimer();
    }

    /// <summary>「走走」按钮：暂时散步几秒后停下。</summary>
    public static void StartWalkBurst(double seconds)
    {
        _walkBurstUntil = Math.Max(_walkBurstUntil, Environment.TickCount64 + (long)seconds * 1000);
        EnsureMotionTimer();
    }

    /// <summary>「摸摸头/呼噜」按钮：抖动 PetPurrSeconds 秒。</summary>
    public static void StartPurr(double seconds)
    {
        _purrUntil = Math.Max(_purrUntil, Environment.TickCount64 + (long)seconds * 1000);
        EnsureMotionTimer();
    }

    public static void SetHoverButtons(IEnumerable<string> ids)
    {
        var next = ids.Take(6).ToList();
        if (next.SequenceEqual(_buttonIds)) return;
        _buttonIds.Clear(); _buttonIds.AddRange(next);
        _buttonsVisible = false;
        Invalidate();
    }

    /// <summary>状态行：空串=不画（设置页关掉「状态气泡」时 PetService 传空进来）。</summary>
    public static void SetStatusText(string text)
    {
        if (text == _statusText) return;
        _statusText = text ?? "";
        Invalidate();
    }

    public static void SetFontDip(int dip)
    {
        var v = Math.Clamp(dip, 9, 24);
        if (v == _fontDip) return;
        _fontDip = v;
        Invalidate();
    }

    /// <summary>全屏自动隐藏总闸：开着就每 2 秒看一眼前台。</summary>
    public static void SetFullscreenGuard(bool on)
    {
        _fsGuard = on;
        if (_hwnd == IntPtr.Zero) return;
        if (on) StartTimer(FS_TIMER, 2000);
        else
        {
            KillTimer(_hwnd, FS_TIMER);
            if (_fsHidden) RestoreFromFullscreen();
        }
    }

    /// <summary>被悬停按钮/双击路由驱动的「假装单击」：让订阅了 Pressed 的页面走一次单击逻辑。</summary>
    public static void SimulatePressed() => Pressed?.Invoke();

    static void Invalidate()
    {
        _cacheDirty = true;
        if (_hwnd != IntPtr.Zero) Render();
    }

    // ============ 帧动画通道 ============

    /// <summary>挂上一段帧动画；framePaths 为绝对路径。传 null/空 或 fps<=0 = 撤掉动画，回到原来那张静态立绘。</summary>
    public static void SetFrames(IReadOnlyList<string>? framePaths, double fps)
    {
        var next = new List<string>();
        if (framePaths is not null && fps > 0)
            for (var i = 0; i < framePaths.Count && next.Count < MaxAnimationFrames; i++)
                if (!string.IsNullOrWhiteSpace(framePaths[i])) next.Add(framePaths[i]);

        // 同一个状态被反复下单（宿主会重复推同一串帧）时不能重解，更不能动代次——
        // 代次一动，正在后台解码的那一批就永远回不来了。
        if (next.Count > 0 && Math.Abs(fps - _frameFps) < 0.01
            && next.SequenceEqual(_framePaths, StringComparer.OrdinalIgnoreCase)) return;

        if (next.Count == 0)
        {
            // 撤动画走同步路径：按引用换回原来那张立绘。宿主每 6 秒就在「比心 → 普通」之间来回，
            // 这里再从盘上解一次就是用户看得见的一顿。
            ReleaseFrames(false);
            EnsureMotionTimer();
            Invalidate();
            return;
        }

        var gen = ++_framesGeneration;
        _framePaths.Clear();
        _framePaths.AddRange(next);
        _frameFps = fps;
        // 快照只在这条通道第一次被占住时拍：切状态只是换一批帧，底片始终是进动画前那一张。
        if (_staticSrc is null && _src is not null) { _staticSrc = _src; _staticBody = _body; }
        DecodeFrames(next, gen, _bodyH);
    }

    /// <summary>
    /// 清掉动画通道。disposeSnapshot=false：静态立绘按引用回到 _src（撤动画走这条）；
    /// true：连快照一起释放，换角色与关窗用它——所有权必须在这一个出口交代干净。
    /// </summary>
    static void ReleaseFrames(bool disposeSnapshot)
    {
        // 代次先走：在飞的后台解码回来对不上号就自己把成果丢掉，别把已经不要的帧塞回显示源
        _framesGeneration++;
        var srcWasFrame = false;
        foreach (var frame in _frames)
        {
            if (ReferenceEquals(frame, _src)) srcWasFrame = true;
            frame.Dispose();
        }
        _frames.Clear();
        _frameIndex = 0; _frameWorkH = 0; _frameFps = 0; _nextFrameAt = 0;
        _framePaths.Clear();
        if (srcWasFrame) _src = null;
        if (_staticSrc is { } snapshot)
        {
            _staticSrc = null;
            _body = _staticBody;
            if (disposeSnapshot)
            {
                if (ReferenceEquals(_src, snapshot)) _src = null;
                snapshot.Dispose();
            }
            else
            {
                _src = snapshot;
                // 立绘和帧的轮廓一般不一样（1080×1920 的主界面 vs 方画布的 Q 版帧）：
                // 交还底片的这一刻按静态那张重新量一次窗口，右沿与脚底不动，
                // 否则角色会被留在动画时的窗口里压扁成一坨。
                if (_hwnd != IntPtr.Zero)
                {
                    var right = _x + _w;
                    var ground = _y + _h;
                    Measure();
                    _x = right - _w;
                    _y = ground - _h;
                }
            }
        }
        _cacheDirty = true;
    }

    /// <summary>
    /// 后台解一批帧：一次可能是六八张 1024 见方，搁主线程解就是整块桌面卡住——流畅度是硬指标。
    /// 解完整批按代次交回 UI 线程换源，中途不碰 _src/_cache。
    /// </summary>
    static void DecodeFrames(List<string> paths, int gen, int workBodyH)
    {
        _frameWorkH = Math.Max(1, workBodyH);
        _ = Task.Run(() =>
        {
            var decoded = new List<SKBitmap>(paths.Count);
            var body = SKRectI.Empty;
            int targetW = 0, targetH = 0;
            float scale = 1f;
            try
            {
                foreach (var path in paths)
                {
                    var bmp = SKBitmap.Decode(path);
                    if (bmp is null) { App.WriteLog($"PetLayeredWindow: 帧解码失败 {path}"); continue; }
                    if (decoded.Count == 0)
                    {
                        // 整段动画的轮廓只在第一张可用帧上量一次：逐帧重量会让角色在桌面上抖，
                        // 还会把标准画布的透明余量重新占回窗口。
                        body = OpaqueBounds(bmp);
                        if (body.Width <= 0 || body.Height <= 0) body = new SKRectI(0, 0, bmp.Width, bmp.Height);
                        // 预缩放到当前显示高度：动画期间每帧都要重建一次成品帧，拿原图做高质量缩放太贵。
                        // 只往下缩——放大不会更清晰，交给 EnsureCache 那一次缩放画就行。
                        scale = (float)workBodyH / body.Height;
                        if (scale >= 0.98f || scale <= 0.01f) scale = 1f;
                        targetW = Math.Max(1, (int)Math.Round(bmp.Width * scale));
                        targetH = Math.Max(1, (int)Math.Round(bmp.Height * scale));
                    }
                    // 尺寸跟首帧不齐的按首帧宽高收进来，共用的那块 _body 才对得上
                    var keep = bmp.Width == targetW && bmp.Height == targetH ? bmp : Resample(bmp, targetW, targetH);
                    if (!ReferenceEquals(keep, bmp)) bmp.Dispose();
                    if (keep is not null) decoded.Add(keep);
                }
                if (decoded.Count > 0 && scale != 1f)
                {
                    body = new SKRectI(
                        Math.Clamp((int)Math.Round(body.Left * scale), 0, targetW - 1),
                        Math.Clamp((int)Math.Round(body.Top * scale), 0, targetH - 1),
                        Math.Clamp((int)Math.Round(body.Right * scale), 1, targetW),
                        Math.Clamp((int)Math.Round(body.Bottom * scale), 1, targetH));
                    if (body.Width <= 1 || body.Height <= 1) body = new SKRectI(0, 0, targetW, targetH);
                }
            }
            catch (Exception ex) { App.WriteLog("PetLayeredWindow.DecodeFrames -> " + ex.Message); }
            Helpers.Ui.Post(() => ApplyFrames(gen, decoded, body));
        });
    }

    /// <summary>缩放整张帧位图；成品固定成预乘 BGRA，跟缓存帧同一套内存排布。</summary>
    static SKBitmap? Resample(SKBitmap src, int w, int h)
    {
        try
        {
            var dst = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var canvas = new SKCanvas(dst);
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { FilterQuality = SKFilterQuality.High };
            canvas.DrawBitmap(src, new SKRect(0, 0, w, h), paint);
            canvas.Flush();
            return dst;
        }
        catch (Exception ex)
        {
            App.WriteLog("PetLayeredWindow.Resample -> " + ex.Message);
            return null;
        }
    }

    /// <summary>把后台解好的一批帧换成当前显示源。</summary>
    static void ApplyFrames(int gen, List<SKBitmap> decoded, SKRectI body)
    {
        if (gen != _framesGeneration)
        {
            // 用户在这期间切了状态或撤了动画：成果没人要，但位图得自己收干净，不然就是一批没人认领的内存
            foreach (var bmp in decoded) bmp.Dispose();
            return;
        }
        if (decoded.Count == 0)
        {
            App.WriteLog("PetLayeredWindow: 动画帧一张都没解出来，退回静态立绘");
            ReleaseFrames(false);
            EnsureMotionTimer();
            Invalidate();
            return;
        }

        foreach (var old in _frames) old.Dispose();   // 上一批整批让位（可能还在播），所有权只认这张表
        _frames.Clear();
        _frames.AddRange(decoded);
        // 中途出过意外时轮廓可能是空的：宁可退回整帧，也不能让 _body 塌成零宽——那等于把角色画没
        if (body.Width <= 0 || body.Height <= 0) body = new SKRectI(0, 0, decoded[0].Width, decoded[0].Height);
        _body = body;
        _frameIndex = 0;
        _src = _frames[0];
        _nextFrameAt = Environment.TickCount64 + FrameIntervalMs();

        // 窗口尺寸只在这批帧换源的当口按新轮廓对齐一次（右沿与脚底不动）；此后每帧只换图、不动尺寸。
        var right = _x + _w;
        var ground = _y + _h;
        Measure();
        _x = right - _w;
        _y = ground - _h;
        ApplyPosition();
        EnsureMotionTimer();
    }

    /// <summary>
    /// 缩放跨度太大就一直拿不合身的帧缩放画：动画期间每帧重建一次成品帧，这笔开销会被放大到每一次抖动。
    /// 判据按「备货高度」而不是「帧高度」比 1.6 倍——原图本来就没那么大时重解也解不出更多像素，
    /// 拿备货高度比过一次就把这条路上限了，滚轮来回滚不会引发连环解码。
    /// </summary>
    static void MaybeRedecodeFrames()
    {
        if (_framePaths.Count == 0 || _frames.Count == 0 || _frameWorkH <= 0) return;
        var need = _bodyH;
        if (need <= 0) return;
        if (_frameWorkH <= need * 1.6 && need <= _frameWorkH * 1.6) return;
        DecodeFrames(_framePaths.ToList(), ++_framesGeneration, need);
    }

    /// <summary>动画一帧该停多久：下限 15ms，跟走动那档一样，不给主线程把自己刷爆的机会。</summary>
    static int FrameIntervalMs() => Math.Clamp((int)Math.Round(1000.0 / Math.Max(0.5, _frameFps)), 15, 1000);

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
        _cacheDirty = true;
        _walkPos = _x;
    }

    /// <summary>挪动/改尺寸只需要 SetWindowPos；位图仍由上一次 UpdateLayeredWindow 持有。</summary>
    static void ApplyPosition()
    {
        _ = SetWindowPos(_hwnd, IntPtr.Zero, _x, _y, _w, _h, SWP_NOZORDER | SWP_NOACTIVATE);
        Render();
    }

    // ============ 绘制：缓存帧 + 每帧上传 ============

    /// <summary>缓存帧没脏就直接上传；dx/dy 只把成品帧平移（呼噜抖、散步点地），不重画内容。</summary>
    static void Render() => PushFrame(0, 0);

    static void PushFrame(int dx, int dy)
    {
        if (_hwnd == IntPtr.Zero || _src is null || _w <= 0 || _h <= 0) return;
        EnsureCache();
        var source = _cache;
        if (source is null) return;
        if (dx != 0 || dy != 0)
        {
            if (_frame is null || _frame.Width != _w || _frame.Height != _h)
            {
                _frame?.Dispose();
                _frame = new SKBitmap(new SKImageInfo(_w, _h, SKColorType.Bgra8888, SKAlphaType.Premul));
            }
            using (var fc = new SKCanvas(_frame))
            {
                fc.Clear(SKColors.Transparent);
                fc.DrawBitmap(source, dx, dy);
                fc.Flush();
            }
            source = _frame;
        }

        IntPtr screen = IntPtr.Zero, mem = IntPtr.Zero, dib = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
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
            var rowBytes = source.RowBytes;
            var ptr = source.GetPixels();
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
            if (old != IntPtr.Zero && mem != IntPtr.Zero) SelectObject(mem, old);
            if (dib != IntPtr.Zero) DeleteObject(dib);
            if (mem != IntPtr.Zero) DeleteDC(mem);
            if (screen != IntPtr.Zero) ReleaseDC(_hwnd, screen);
        }
    }

    /// <summary>重建成品帧：发光（同形模糊垫底）→ 立绘（含散步镜像）→ 悬停按钮或状态行。</summary>
    static void EnsureCache()
    {
        if (!_cacheDirty && _cache is not null && _cache.Width == _w && _cache.Height == _h) return;
        if (_src is null) return;
        if (_cache is null || _cache.Width != _w || _cache.Height != _h)
        {
            _cache?.Dispose();
            _cache = new SKBitmap(new SKImageInfo(_w, _h, SKColorType.Bgra8888, SKAlphaType.Premul));
        }
        using var canvas = new SKCanvas(_cache);
        canvas.Clear(SKColors.Transparent);
        // 本体贴在下沿，头顶留出 Headroom：那段只给悬停按钮与状态行
        var dest = new SKRect(0, Headroom, _w, _h);
        var srcRect = new SKRect(_body.Left, _body.Top, _body.Right, _body.Bottom);

        if (_glow > 0.01)
        {
            var sigma = (float)(2 + 8 * _glow) * _dpiScale;
            using var glowPaint = new SKPaint
            {
                IsAntialias = true,
                Color = SKColors.White.WithAlpha((byte)Math.Clamp(210 * _glow, 0, 255)),
                ImageFilter = SKImageFilter.CreateBlur(sigma, sigma),
            };
            DrawBody(canvas, glowPaint, dest, srcRect);
        }
        using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
            DrawBody(canvas, paint, dest, srcRect);

        if (_buttonsVisible && _buttonIds.Count > 0) DrawHoverButtons(canvas);
        else if (!string.IsNullOrEmpty(_statusText)) DrawStatusText(canvas);
        canvas.Flush();
        _cacheDirty = false;
    }

    /// <summary>镜像只发生在散步回头那一下：翻转画布重画一次缓存，而不是每帧带矩阵。</summary>
    static void DrawBody(SKCanvas canvas, SKPaint paint, SKRect dest, SKRect srcRect)
    {
        if (!_flip) { canvas.DrawBitmap(_src!, srcRect, dest, paint); return; }
        canvas.Save();
        canvas.Translate(_w, 0);
        canvas.Scale(-1, 1);
        var d = new SKRect(_w - dest.Right, dest.Top, _w - dest.Left, dest.Bottom);
        canvas.DrawBitmap(_src!, srcRect, d, paint);
        canvas.Restore();
    }

    // ============ 悬停按钮与状态行（都画在头顶留白带） ============

    static (int cx, int cy, int r, string id)[] ButtonSlots()
    {
        var r = Math.Max(10, (int)((_headroom - 12) * 0.42f));
        var gap = Math.Max(4, r / 3);
        var n = Math.Min(_buttonIds.Count, 6);
        var total = n * r * 2 + (n - 1) * gap;
        var cx0 = (_w - total) / 2 + r;
        var cy = Math.Max(r + 2, _headroom / 2);
        var slots = new (int, int, int, string)[n];
        for (var i = 0; i < n; i++) slots[i] = (cx0 + i * (r * 2 + gap), cy, r, _buttonIds[i]);
        return slots;
    }

    static void DrawHoverButtons(SKCanvas canvas)
    {
        using var bg = new SKPaint { Color = new SKColor(0xF7, 0xEE, 0xDD, 0xE6), IsAntialias = true };
        using var edge = new SKPaint { Color = new SKColor(0xA8, 0x83, 0x6B, 0x88), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(1, _dpiScale) };
        using var ink = new SKPaint
        {
            Color = new SKColor(0x5A, 0x45, 0x32),
            IsAntialias = true,
            TextSize = 0,
            Typeface = GlyphFace(),
        };
        foreach (var (cx, cy, r, id) in ButtonSlots())
        {
            canvas.DrawCircle(cx, cy, r, bg);
            canvas.DrawCircle(cx, cy, r, edge);
            var glyph = GlyphOf(id);
            ink.TextSize = r * 1.15f;
            ink.TextAlign = SKTextAlign.Center;
            canvas.DrawText(glyph, cx, cy + ink.TextSize * 0.36f, ink);
        }
    }

    static void DrawStatusText(SKCanvas canvas)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(0x5A, 0x45, 0x32),
            IsAntialias = true,
            TextSize = _fontDip * _dpiScale,
            Typeface = SKTypeface.FromFamilyName("Microsoft YaHei UI") ?? SKTypeface.Default,
        };
        var w = paint.MeasureText(_statusText);
        var pad = 6 * _dpiScale;
        var rect = new SKRect((_w - w) / 2 - pad, 2, (_w + w) / 2 + pad, paint.TextSize + pad * 2);
        using var bg = new SKPaint { Color = new SKColor(0xF7, 0xEE, 0xDD, 0xD9), IsAntialias = true };
        canvas.DrawRoundRect(rect, 8 * _dpiScale, 8 * _dpiScale, bg);
        paint.TextAlign = SKTextAlign.Center;
        canvas.DrawText(_statusText, _w / 2f, rect.Bottom - pad, paint);
    }

    static SKTypeface? _glyphFace;
    static SKTypeface GlyphFace() =>
        _glyphFace ??= SKTypeface.FromFamilyName("Segoe UI Symbol") ?? SKTypeface.Default;

    static string GlyphOf(string id) => id switch
    {
        "talk" => "❝",          // 说话
        "think" => "?",          // 想一次
        "pet" => "♥",           // 摸摸头
        "purr" => "≈",          // 呼噜
        "walk" => "→",          // 走走
        "settings" => "⚙",      // 设置
        "home" => "⌂",          // 回窗口
        _ => "•",
    };

    // ============ 动作节拍（帧率/锁定/走动/呼噜的出口） ============

    /// <summary>
    /// 走动/呼噜进行中按帧率抽帧；只有悬停按钮亮着时降到 500ms 一档（只为查「鼠标离开了没有」）；
    /// 什么都不在动就不挂表——静止的角色不烧 CPU，帧率设置的意义全在「有动作时抽多快」。
    /// </summary>
    static void EnsureMotionTimer()
    {
        if (_hwnd == IntPtr.Zero) return;
        var now = Environment.TickCount64;
        var fast = _roam || now < _walkBurstUntil || now < _purrUntil;
        var want = fast ? (_fpsLock ? Math.Max(15, (int)(1000.0 / _fps)) : 16)
                        : _buttonsVisible ? 500 : 0;
        // 帧动画并进这张表，不另建第二张：只有多于一帧才需要挂表，间隔取「一帧该有的时长」与本档里更小者，
        // 免得一个 6fps 的循环把表开到 16ms 空转。
        if (_frames.Count > 1)
        {
            var frameMs = FrameIntervalMs();
            want = want == 0 ? frameMs : Math.Min(want, frameMs);
        }
        if (want == _motionIntervalMs) return;
        _motionIntervalMs = want;
        if (want == 0) KillTimer(_hwnd, MOTION_TIMER);
        else StartTimer(MOTION_TIMER, (uint)want);
    }

    static void MotionStep()
    {
        if (_hwnd == IntPtr.Zero || _src is null) return;
        var now = Environment.TickCount64;
        var drawn = false;

        // 悬停按钮的退场：1.2 秒没有任何落在窗口上的鼠标事件就收
        if (_buttonsVisible && now - _lastHoverAt > 1200)
        {
            _buttonsVisible = false;
            _cacheDirty = true;
            EnsureMotionTimer();
            drawn = true;
        }

        // 帧动画的节拍：一次只走一帧，落后太多就把下一次对齐到「现在 + 一帧」而不是连补几帧追进度——
        // 追进度只会让角色抽搐，还白烧几次缓存重建。整段按循环播：一次性状态由宿主只给一帧或撤动画来表达。
        if (_frames.Count > 1)
        {
            var frameMs = FrameIntervalMs();
            if (_fsHidden) _nextFrameAt = now + frameMs;   // 被全屏盖住时不白烧，回来从这一帧接着走
            else if (now >= _nextFrameAt)
            {
                _frameIndex = (_frameIndex + 1) % _frames.Count;
                _src = _frames[_frameIndex];
                _cacheDirty = true;
                _nextFrameAt = now + frameMs;
                drawn = true;
            }
        }

        var roaming = (_roam || now < _walkBurstUntil) && !_fsHidden;
        var dx = 0; var dy = 0;
        if (roaming)
        {
            var stepPx = 42 * _dpiScale * Math.Max(1, _motionIntervalMs) / 1000.0;
            _walkPos += _dir * stepPx;
            if (TryWorkArea(out var wa))
            {
                var nx = (int)Math.Round(_walkPos);
                if (nx <= wa.left || nx + _w >= wa.right)
                {
                    _dir = -_dir; _flip = !_flip; _cacheDirty = true;
                    _walkPos = Math.Clamp(_walkPos, (double)wa.left, (double)wa.right - _w);
                    nx = (int)Math.Round(_walkPos);
                }
                if (nx != _x) { _x = nx; _ = SetWindowPos(_hwnd, IntPtr.Zero, _x, _y, _w, _h, SWP_NOZORDER | SWP_NOACTIVATE); }
            }
            // 走路点地的小颠：±1.5px 的平移，不重画内容
            dy = -(int)(Math.Abs(Math.Sin(now / 130.0)) * 2.5 * _dpiScale);
            if (now - _lastPositionSyncAt > 600) { _lastPositionSyncAt = now; PositionSynced?.Invoke(); }
            drawn = true;
        }
        if (now < _purrUntil)
        {
            dx = (int)Math.Round(Math.Sin(now / 55.0) * 2.2 * _dpiScale);
            drawn = true;
        }
        if (drawn && !_fsHidden) PushFrame(dx, dy);
    }

    // ============ 全屏自动隐藏 ============

    static void FullscreenCheck()
    {
        if (_hwnd == IntPtr.Zero) return;
        var fg = GetForegroundWindow();
        var hidden = fg != IntPtr.Zero
                     && fg != _hwnd
                     && !IsOurProcess(fg)
                     && CoversMonitor(fg);
        if (hidden == _fsHidden) return;
        _fsHidden = hidden;
        if (hidden) _ = ShowWindow(_hwnd, SW_HIDE);
        else RestoreFromFullscreen();
        FullscreenHidden?.Invoke(hidden);
    }

    static void RestoreFromFullscreen()
    {
        _fsHidden = false;
        _ = ShowWindow(_hwnd, SW_SHOWNA);
        _cacheDirty = true;   // 藏起来的这段时间可能有别的改动没画上，回来补一次整帧
        Render();
    }

    static bool CoversMonitor(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var r)) return false;
        var mon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (mon == IntPtr.Zero || !GetMonitorInfo(mon, ref mi)) return false;
        // 允许 2px 误差：DWM 的边框外沿与显示器矩形本来就不是像素级相等
        return Math.Abs(r.left - mi.monitor.left) <= 2 && Math.Abs(r.top - mi.monitor.top) <= 2
            && Math.Abs(r.right - mi.monitor.right) <= 2 && Math.Abs(r.bottom - mi.monitor.bottom) <= 2;
    }

    static bool IsOurProcess(IntPtr hwnd)
    {
        _ = GetWindowThreadProcessId(hwnd, out var pid);
        return pid != 0 && pid == GetCurrentProcessId();
    }

    static bool TryWorkArea(out RECT wa)
    {
        wa = default;
        if (!SystemParametersInfo(SPI_GETWORKAREA, 0, out wa, 0)) return false;
        return wa.right > wa.left && wa.bottom > wa.top;
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
        if (dpi > 0)
        {
            _dpiScale = dpi / 96f;
            _headroom = Math.Max(HeadroomDip, (int)Math.Round(HeadroomDip * dpi / 96f));
        }
        _cacheDirty = true;
    }

    static IntPtr WndProcHandler(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (msg)
            {
                case WM_LBUTTONDOWN:
                {
                    var (mx, my) = CursorInWindow(lParam);
                    if (_buttonsVisible && HitButton(mx, my, out var id))
                    {
                        // 按下按钮：吃掉这一击（不算拖、不算单击、也不留双击前半）
                        _swallowNextUp = true;
                        _lastUpTick = 0;
                        HoverButtonPressed?.Invoke(id);
                        return IntPtr.Zero;
                    }
                    _dragging = false;
                    _ = SetCapture(hWnd);
                    if (GetCursorPos(out var p)) { _grabX = p.X; _grabY = p.Y; _originX = _x; _originY = _y; }
                    if (IsSecondOfDouble(p.X, p.Y))
                    {
                        _lastUpTick = 0;
                        _swallowNextUp = true; // 这一击的松手是双击的收尾，不再算一次单击
                        RouteDoubleClick();
                    }
                    return IntPtr.Zero;
                }
                case WM_MOUSEMOVE:
                    if (GetCapture() == hWnd)
                    {
                        if (!GetCursorPos(out var c)) break;
                        var dx = c.X - _grabX; var dy = c.Y - _grabY;
                        // 6px 之内不算拖：不然每一次「其实没打算移动」的单击都会把角色挪走半格。
                        // 一旦确认在拖，后面每一帧都要跟着走——早先这里写的是 if (_dragging) break，
                        // 结果角色只跟着第一步动，剩下 240px 的手势全被 DefWindowProc 吃掉了。
                        if (!_dragging && Math.Abs(dx) + Math.Abs(dy) < 6) break;
                        _dragging = true;
                        _lastUpTick = 0; // 拖过就不可能是双击的后半
                        _x = _originX + dx; _y = _originY + dy;
                        _walkPos = _x;
                        _ = SetWindowPos(hWnd, IntPtr.Zero, _x, _y, _w, _h, SWP_NOZORDER | SWP_NOACTIVATE);
                        return IntPtr.Zero;
                    }
                    // 没在拖：这条消息只在指针压在「有像素」的地方时才来——正好当悬停信号用。
                    _lastHoverAt = Environment.TickCount64;
                    if (_buttonIds.Count > 0 && !_buttonsVisible)
                    {
                        _buttonsVisible = true;
                        _cacheDirty = true;
                        Render();
                        EnsureMotionTimer();
                    }
                    break;
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
                    RouteDoubleClick();
                    return IntPtr.Zero;
                case WM_MOUSEWHEEL:
                    var delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
                    if (delta != 0) WheelTurned?.Invoke(delta);
                    return IntPtr.Zero;
                case WM_TIMER:
                    if (wParam.ToInt64() == MOTION_TIMER) MotionStep();
                    else if (wParam.ToInt64() == FS_TIMER) FullscreenCheck();
                    return IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("PetLayeredWindow.WndProc -> " + ex.Message);
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    static void RouteDoubleClick()
    {
        var act = DoubleClickRouter?.Invoke() ?? "home";
        switch (act)
        {
            case "home": DoublePressed?.Invoke(); break;
            case "talk": case "think": Pressed?.Invoke(); break;
            case "none": break;
            default: HoverButtonPressed?.Invoke(act); break;
        }
    }

    static (int x, int y) CursorInWindow(IntPtr lParam)
    {
        var v = lParam.ToInt64();
        return ((short)(v & 0xFFFF), (short)((v >> 16) & 0xFFFF));
    }

    static bool HitButton(int px, int py, out string id)
    {
        foreach (var (cx, cy, r, s) in ButtonSlots())
        {
            var dx = px - cx; var dy = py - cy;
            if (dx * dx + dy * dy <= (r + 3) * (r + 3)) { id = s; return true; }
        }
        id = "";
        return false;
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
    const uint WM_TIMER = 0x0113;
    const nint MOTION_TIMER = 1;
    const nint FS_TIMER = 2;
    const int SM_CXDOUBLECLK = 36;
    const int SM_CYDOUBLECLK = 37;
    const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
    const int SPI_GETWORKAREA = 0x0030;
    const uint MONITOR_DEFAULTTONEAREST = 2;
    const int WS_POPUP = unchecked((int)0x80000000);
    const uint WS_EX_LAYERED = 0x00080000;
    const uint WS_EX_TOPMOST = 0x00000008;
    const uint WS_EX_TOOLWINDOW = 0x00000080;
    const uint WS_EX_NOACTIVATE = 0x08000000;
    const uint SWP_NOZORDER = 0x0004;
    const uint SWP_NOACTIVATE = 0x0010;
    const int SW_SHOWNA = 8;
    const int SW_HIDE = 0;
    const int IDC_ARROW = 32512;
    const uint BI_RGB = 0;
    const uint DIB_RGB_COLORS = 0;
    const uint ULW_ALPHA = 0x00000002;
    const byte AC_SRC_OVER = 0x00;
    const byte AC_SRC_ALPHA = 0x01;
    const string ClassName = "WarmAsBeforePetSprite";

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int CX, CY; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO { public int cbSize; public RECT monitor; public RECT work; public uint flags; }

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
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("kernel32.dll")] static extern uint GetCurrentProcessId();
    [DllImport("user32.dll")] static extern bool SystemParametersInfo(int action, uint param, out RECT pv, uint win);
    [DllImport("user32.dll")] static extern IntPtr SetTimer(IntPtr hWnd, nint id, uint elapsed, IntPtr cb);
    [DllImport("user32.dll")] static extern bool KillTimer(IntPtr hWnd, nint id);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage,
        out IntPtr ppvBits, IntPtr section, uint offset);
    [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")] static extern void CopyMemory(IntPtr dest, IntPtr src, UIntPtr count);

    static void StartTimer(nint id, uint ms) => _ = SetTimer(_hwnd, id, ms, IntPtr.Zero);
    static void StopTimerIfAlive(nint id) { if (_hwnd != IntPtr.Zero) _ = KillTimer(_hwnd, id); }
}
