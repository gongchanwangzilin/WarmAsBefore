using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WarmAsBefore.Models;

public sealed class BoardCell : INotifyPropertyChanged
{
    private string? _piece;
    private bool _isHighlighted;

    public int Row { get; init; }
    public int Col { get; init; }

    public string? Piece
    {
        get => _piece;
        set { _piece = value; OnProp(); OnProp(nameof(IsEmpty)); OnProp(nameof(PieceColor)); }
    }

    public bool IsHighlighted
    {
        get => _isHighlighted;
        set { _isHighlighted = value; OnProp(); OnProp(nameof(PieceOpacity)); }
    }

    public bool IsEmpty => string.IsNullOrEmpty(Piece);
    public double PieceOpacity => IsHighlighted ? 1.0 : 0.75;

    public Color BgColor { get; set; } = Color.FromArgb("#F5EFE0");

    /// <summary>中国象棋：是否红方棋子（决定棋子颜色）。</summary>
    public bool IsRed { get; set; }

    /// <summary>覆盖默认棋子配色（贪吃蛇头/尾、飞行棋红蓝子等）。为空则走默认逻辑。</summary>
    public Color? PieceTint { get; set; }

    /// <summary>中国象棋：九宫角格的对角线字符（╱ / ╲），空表示无。</summary>
    public string DiagChar { get; set; } = "";

    public Color PieceColor => PieceTint ?? (IsRed
        ? Color.FromArgb("#C0392B")   // 红方
        : Piece switch
        {
            "●" => Colors.Black,
            "○" => Color.FromArgb("#FAEBD7"),
            null or "" => Color.FromArgb("#5D4A3A"),
            _ => Color.FromArgb("#37474F")   // 黑方/其他
        });

    public string PieceFontSize => Piece switch
    {
        "●" or "○" => "20",
        _ => "14"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnProp([CallerMemberName] string n = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}