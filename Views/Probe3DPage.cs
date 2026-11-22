using System.Runtime.InteropServices;
using WarmAsBefore.Controls;

namespace WarmAsBefore.Views;

/// <summary>
/// 3D 建链探针页：只在进程启动参数带 --probe3d 时出现。
///
/// 为什么要单独一个进程：3D 初始化失败时可能直接在原生侧把进程带走（WER 记录为
/// combase.dll / 0x80004003），托管 catch 兜不住。把它放进一个独立进程里跑，
/// 崩了只死探针，主程序不受影响；而留下的 breadcrumb 会告诉我们究竟死在哪一步。
/// </summary>
public sealed class Probe3DPage : ContentPage
{
    /// <summary>探针的模式：page=上屏后建链；hide=隐藏时建链再显示；show=显示后再建链。</summary>
    private readonly string _mode = "page";

    private readonly Label _status = new()
    {
        FontSize = 12,
        TextColor = Colors.White,
        LineBreakMode = LineBreakMode.WordWrap,
        Margin = new Thickness(0, 6)
    };

    public Probe3DPage() : this("page") { }

    public Probe3DPage(string mode)
    {
        _mode = mode;
        Title = "3D 建链探针 · " + mode;
        var view = new EvergineWeatherView { HeightRequest = 420 };
        view.SizeChanged += (_, _) => Line($"view 尺寸 {view.Width:0}x{view.Height:0}");

        var button = new Button { Text = "建链（设 Application）" };
        button.Clicked += (_, _) => Attach(view);

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 16,
                Spacing = 8,
                Children =
                {
                    new Label { Text = "3D 建链探针 —— 崩了只死这个进程", FontSize = 15, TextColor = Colors.White },
                    _status,
                    view,
                    button
                }
            }
        };
        BackgroundColor = Color.FromArgb("#1C1A17");

        // 三种时序各跑一次，看哪一种会把进程带走：
        // page —— 上屏后再建链（正常路径）；
        // hide —— 先隐藏、建链、再显示（监测页开关打开前的实际状态）；
        // show —— 先显示、再建链（开关打开那一刻的行为）。
        Loaded += (_, _) =>
        {
            Line("页面 Loaded，mode=" + _mode);
            if (_mode == "hide" || _mode == "show") view.IsVisible = false;
            Dispatcher.Dispatch(async () =>
            {
                await Task.Delay(1200);
                Line("计时到，开始动作");
                if (_mode == "show")
                {
                    Line("先把视图设为可见");
                    view.IsVisible = true;
                    await Task.Delay(600);
                    Line("再建链");
                    Attach(view);
                }
                else if (_mode == "hide")
                {
                    Line("先建链（此时视图仍隐藏）");
                    Attach(view);
                    await Task.Delay(600);
                    Line("再把视图设为可见");
                    view.IsVisible = true;
                }
                else
                {
                    Attach(view);
                }
                await Task.Delay(2500);
                Line("动作后存活 2.5 秒，未崩溃");
            });
        };
    }

    private bool _attached;

    private void Attach(EvergineWeatherView view)
    {
        if (_attached) { Line("已经建过链，跳过（OneShot）"); return; }
        _attached = true;
        Line("Attach: 创建 MonitorVectorApplication");
        try
        {
            var app = new WarmAsBefore.Weather3D.MonitorVectorApplication();
            Line("Attach: Application 已创建，赋值给视图");
            view.Application = app;
            Line("Attach: 赋值完成（建链在 handler 的 Loaded 回调里）");
        }
        catch (Exception ex)
        {
            Line("Attach 抛托管异常: " + ex.GetType().Name + " / " + ex.Message);
        }
    }

    private void Line(string text)
    {
        Trace(text);
        var line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + text;
        _status.Text = string.IsNullOrEmpty(_status.Text) ? line : _status.Text + "\n" + line;
    }

    /// <summary>同步落盘：进程被原生崩溃带走时，这一行必须已经在磁盘上。</summary>
    private static void Trace(string text)
    {
        try
        {
            App.WriteLogNow("[Probe3D] " + text);
        }
        catch (SEHException ex)
        {
            try
            {
                App.WriteLogNow("[Probe3D] SEHException 0x" + ex.HResult.ToString("X8") + " @ " + text);
            }
            catch { }
        }
        catch { }
    }
}
