using System.IO;
using WarmAsBefore.Modules.AiChat;
using WarmAsBefore.Services;

namespace WarmAsBefore.Views;

/// <summary>
/// 桌宠窗口：无边框、置顶、只显示角色立绘。
/// 拖动窗口：Win32 模拟标题栏拖拽（按下移动时调用 PetService.BeginDrag）。
/// 缩放：右上角 +/- 按钮，或鼠标滚轮（WinUI 平台 PointerWheelChanged）。
/// 双击：回到主窗口。底部输入行：直接在桌宠上对话（ChatEngine 单例）。
/// </summary>
public partial class PetPage : ContentPage
{
    private readonly GameEngine _engine;
    private readonly CharacterLibrary _chars;
    private readonly StorageProvider _store;
    private readonly ChatEngine _chat;

    private double _scale = 1.0;
    private const double MinScale = 0.5;
    private const double MaxScale = 3.0;

    public PetPage(GameEngine engine, CharacterLibrary chars, StorageProvider store, ChatEngine chat)
    {
        InitializeComponent();
        _engine = engine;
        _chars = chars;
        _store = store;
        _chat = chat;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        try
        {
            var charId = _engine.State.CharacterId;
            if (string.IsNullOrEmpty(charId)) return;
            var roster = await _chars.ListAsync();
            var ch = roster.FirstOrDefault(c => c.Profile.Id == charId);
            if (ch is null || ch.SpriteMap.Count == 0) return;
            var first = ch.SpriteMap.Keys.First();
            if (ch.SpriteMap.TryGetValue(first, out var rel))
            {
                var full = Path.Combine(_store.Root, rel);
                if (File.Exists(full))
                {
                    SpriteImage.Source = ImageSource.FromFile(full);
                    NameLabel.Text = ch.Profile.Name;
                    AttachWheelZoom();
                }
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("PetPage.OnLoaded -> " + ex);
        }
    }

    private void OnPointerPressed(object sender, PointerEventArgs e) => PetService.BeginDrag(this);

    private void OnDoubleTapped(object sender, TappedEventArgs e) => PetService.ShowMainWindowStatic();

    /// <summary>底部输入行发送：走 ChatEngine（与主界面同一 AI），回复显示在顶部气泡。</summary>
    private async void OnSend(object sender, EventArgs e)
    {
        try
        {
            var text = InputEntry.Text;
            if (string.IsNullOrWhiteSpace(text)) return;
            var charId = _engine.State.CharacterId;
            if (string.IsNullOrEmpty(charId)) return;
            InputEntry.Text = "";
            ReplyBubble.IsVisible = true;
            ReplyLabel.Text = "思考中…";
            string reply;
            try
            {
                reply = await _chat.Send(charId, text);
            }
            catch (Exception ex)
            {
                App.WriteLog("PetPage.OnSend -> " + ex);
                reply = "……";
            }
            ReplyLabel.Text = reply;
        }
        catch (Exception ex)
        {
            App.WriteLog("PetPage.OnSend -> " + ex);
        }
    }

    private void OnZoomIn(object sender, EventArgs e) => ZoomBy(+0.1);

    private void OnZoomOut(object sender, EventArgs e) => ZoomBy(-0.1);

    private void ZoomBy(double delta)
    {
        _scale = Math.Clamp(_scale + delta, MinScale, MaxScale);
        SpriteImage.Scale = _scale;
        ZoomLabel.Text = $"{_scale:P0}";
    }

#if WINDOWS
    /// <summary>在 WinUI 平台层挂接滚轮事件（MAUI 手势不提供滚轮回调）。</summary>
    private void AttachWheelZoom()
    {
        try
        {
            var handler = SpriteImage.Handler;
            var platformView = handler?.PlatformView;
            if (platformView is not Microsoft.UI.Xaml.Controls.Image img) return;
            if (_wheelHooked) return;
            _wheelHooked = true;
            img.PointerWheelChanged += (_, ev) =>
            {
                var delta = ev.GetCurrentPoint(null).Properties.MouseWheelDelta;
                if (delta != 0) ZoomBy(delta > 0 ? 0.1 : -0.1);
            };
        }
        catch (Exception ex)
        {
            App.WriteLog("PetPage.AttachWheelZoom -> " + ex);
        }
    }

    private bool _wheelHooked;
#endif
}