using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Graphics;
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
        // 页面入场：轻微上浮 + 渐入
        Content.Opacity = 0;
        Content.TranslationY = 10;
        _ = Content.FadeTo(1, 180, Easing.CubicOut);
        _ = Content.TranslateTo(0, 0, 220, Easing.CubicOut);
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
                _ = BoardView.ScaleTo(1.04, 160, Easing.CubicOut);
                _ = BoardView.FadeTo(0.82, 160, Easing.CubicOut);
                _ = Task.Delay(320).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() =>
                {
                    _ = BoardView.ScaleTo(1.0, 260, Easing.CubicOut);
                    _ = BoardView.FadeTo(1.0, 260, Easing.CubicOut);
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
                _ = BoardView.ScaleTo(1.0, 160, Easing.CubicOut)));
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