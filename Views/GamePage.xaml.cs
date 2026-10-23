using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Graphics;
using WarmAsBefore.DesignSystem.Theme;
using WarmAsBefore.Helpers;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.GameModule;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class GamePage : ContentPage
{
    private readonly GameViewModel _vm;
    private readonly GameBoardDrawable _drawable = new();
    private bool _wasGameOver;
    private int _lastCellCount = -1;

    public GamePage(GameViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
        BoardView.Drawable = _drawable;
        _vm.PropertyChanged += OnVmPropChanged;
        // 新消息自动滚到底部（聊天区）
        _vm.Messages.CollectionChanged += (_, _) => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_vm.Messages.Count > 0)
                ChatList.ScrollTo(_vm.Messages[^1], position: ScrollToPosition.End, animate: false);
        });
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // 进场：全站唯一入口，只做位移。
        // 原来这里是 Content.Opacity = 0 + FadeTo(1)——淡入的起点是「看不见」，
        // 动画回调一丢就把整页留在透明上（和「再续情缘」按钮消失同一个成因），所以整段删掉。
        Motion.ArrivePage(this);
    }

    private void OnVmPropChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameViewModel.BoardCells) && _vm.BoardCells.Count > 0)
            BuildBoard();
    }

    private void BuildBoard()
    {
        // 终局转变 → 胜利/落败庆祝动画
        if (_vm.InGame && _vm.CurrentGameKind is not (MiniGameEngine.GameKind.Snake) && _vm.GameOverFlag)
        {
            if (!_wasGameOver)
            {
                _wasGameOver = true;
                // 终局庆祝：只用缩放，不再 FadeTo(0.82)→FadeTo(1)。
                // BoardView 的透明度不属于动效，棋盘上还有「该不该看得见」的其它输出方。
                var push = (uint)MotionTokens.Ms(MotionTokens.SettleMs);
                var back = (uint)MotionTokens.Ms(MotionTokens.EnterMs);
                _ = BoardView.ScaleTo(1.04, push, Easing.CubicOut);
                _ = Task.Delay(MotionTokens.Ms(MotionTokens.SettleMs) * 2).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() =>
                {
                    _ = BoardView.ScaleTo(1.0, back, Easing.CubicOut);
                }));
            }
        }
        else if (_wasGameOver) _wasGameOver = false;

        // 落子脉冲：棋盘更新时轻微回弹（每手一次，首次构建不触发）
        if (_vm.InGame && !_vm.GameOverFlag && _lastCellCount != -1 && _vm.BoardCells.Count != _lastCellCount)
        {
            BoardView.CancelAnimations();
            _ = BoardView.ScaleTo(1.012, 90, Easing.CubicOut);
            _ = Task.Delay(100).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() =>
                _ = BoardView.ScaleTo(1.0, (uint)MotionTokens.Ms(MotionTokens.SettleMs), Easing.CubicOut)));
        }
        if (_lastCellCount == -1 || _vm.BoardCells.Count != _lastCellCount)
            _lastCellCount = _vm.BoardCells.Count;

        _drawable.Update(_vm.CurrentGameKind, _vm.BoardRows, _vm.BoardCols, _vm.CellSize, _vm.BoardCells);
        BoardView.WidthRequest = _drawable.BoardWidth;
        BoardView.HeightRequest = _drawable.BoardHeight;
        BoardView.Invalidate();
    }

    /// <summary>GraphicsView 触摸：像素坐标换算为棋盘 (Row,Col)，线制取最近交点、格制取所在格。</summary>
    private async void OnBoardTouched(object? sender, TouchEventArgs e)
    {
        if (!_vm.InGame) return;
        var touches = e.Touches.ToArray();
        if (touches.Length == 0) return;
        var p = touches[0];
        int r, c;
        if (_drawable.IsPointBoard)
        {
            r = (int)Math.Round((p.Y - GameBoardDrawable.PAD) / _vm.CellSize);
            c = (int)Math.Round((p.X - GameBoardDrawable.PAD) / _vm.CellSize);
        }
        else
        {
            r = (int)Math.Floor((p.Y - GameBoardDrawable.PAD) / _vm.CellSize);
            c = (int)Math.Floor((p.X - GameBoardDrawable.PAD) / _vm.CellSize);
        }
        if (r < 0 || r >= _vm.BoardRows || c < 0 || c >= _vm.BoardCols) return;

        var cell = _vm.BoardCells.FirstOrDefault(x => x.Row == r && x.Col == c);
        if (cell is null) return;
        if (_vm.CellTappedCommand.CanExecute(cell))
            await _vm.CellTappedCommand.ExecuteAsync(cell);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.PropertyChanged -= OnVmPropChanged;
        _vm.Detach();
    }
}