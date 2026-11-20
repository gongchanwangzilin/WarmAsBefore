using System.Collections.Concurrent;
using Microsoft.Maui.Graphics;

namespace WarmAsBefore.Helpers;

/// <summary>
/// 给 <see cref="IDrawable"/> 加一层兜底：绘制的异常就地吞掉，画布只少画这一帧。
///
/// 为什么必须有这一层（bugs.log 实证）：WinUI 的 GraphicsView 走 Win2D，
/// Draw 里逃出的托管异常会打断 CanvasDrawingSession 的释放，随后原生层连抛
/// 「The last drawing session returned by CreateDrawingSession must be disposed」
/// 与 DCOMPOSITION_ERROR_SURFACE_BEING_RENDERED / 「找不到元素」——
/// 一个绘制 bug 会放大成上百条 COMException，用户看到的是满屏报错加白屏
/// （v1.59.1 那次 GraphPalette 静态构造失败就是这么炸的）。
/// 绘制优化到最后一层也挡不住的错误，最坏结果只能是「这块画布空着」。
///
/// 同一处错误只写一条 bugs.log（按 名称+异常类型+消息 去重），
/// 连续失败 <see cref="GiveUpAfter"/> 次后停止调用内部实现，避免每帧抛一次异常白烧 CPU。
/// </summary>
public sealed class SafeDrawable : IDrawable
{
    private const int GiveUpAfter = 60;

    private static readonly ConcurrentDictionary<string, byte> _logged = new();

    private readonly IDrawable _inner;
    private readonly string _name;
    private int _failures;

    private SafeDrawable(IDrawable inner, string name)
    {
        _inner = inner;
        _name = name;
    }

    /// <summary>包一层兜底；已经包过（或为 null）的原样返回，重复挂不会套两层。</summary>
    public static IDrawable? Wrap(IDrawable? drawable, string name)
    {
        if (drawable is null || drawable is SafeDrawable) return drawable;
        return new SafeDrawable(drawable, name);
    }

    /// <summary>这个画布是否已经因连续失败停画（页面可据此给一句提示）。</summary>
    public bool GaveUp => Volatile.Read(ref _failures) >= GiveUpAfter;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (GaveUp) return;
        try
        {
            _inner.Draw(canvas, dirtyRect);
            Volatile.Write(ref _failures, 0);
        }
        catch (Exception ex)
        {
            var n = Interlocked.Increment(ref _failures);
            var key = _name + "|" + ex.GetType().FullName + "|" + ex.Message;
            if (_logged.TryAdd(key, 0))
            {
                try
                {
                    Services.Core.BugReporter.Report(ex, "Draw", $"画布 {_name} 绘制失败（已就地兜底，不影响其它界面）",
                        recordOnly: true);
                }
                catch { /* 兜底自己绝不能成为新的异常源 */ }
            }
            if (n == GiveUpAfter)
            {
                try { App.WriteLog($"SafeDrawable 停画: {_name} 连续失败 {n} 次，{ex.GetType().Name}"); } catch { }
            }
        }
    }
}
