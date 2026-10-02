using Microsoft.Maui.Graphics;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.GameModule;

namespace WarmAsBefore.Views;

/// <summary>
/// 专业棋盘自绘渲染器。
/// 线制棋（五子棋、中国象棋）：横竖交叉线 + 棋子落交点；
/// 格制棋（国际象棋、斗兽棋、飞行棋、贪吃蛇）：格子 + 格内棋元素。
/// </summary>
public sealed class GameBoardDrawable : IDrawable
{
    /// <summary>棋盘四周边距（像素）。</summary>
    public const float PAD = 16f;

    private static readonly Color WoodBase = Color.FromArgb("#F3E9D2");
    private static readonly Color WoodLine = Color.FromArgb("#C9A96B");
    private static readonly Color WoodShadow = Color.FromArgb("#A98A55");
    private static readonly Color BoardLine = Color.FromArgb("#7B5B3A");

    private MiniGameEngine.GameKind _kind;
    private int _rows, _cols;
    private double _cellSize = 34;
    private IReadOnlyList<BoardCell> _cells = Array.Empty<BoardCell>();
    private readonly Dictionary<(int r, int c), BoardCell> _byPos = new();

    public void Update(MiniGameEngine.GameKind kind, int rows, int cols, double cellSize, IReadOnlyList<BoardCell> cells)
    {
        _kind = kind;
        _rows = rows;
        _cols = cols;
        _cellSize = cellSize;
        _cells = cells ?? Array.Empty<BoardCell>();
        _byPos.Clear();
        foreach (var c in _cells)
            if (!_byPos.ContainsKey((c.Row, c.Col)))
                _byPos[(c.Row, c.Col)] = c;
    }

    private BoardCell? CellAt(int r, int c) => _byPos.TryGetValue((r, c), out var v) ? v : null;

    /// <summary>是否线制棋（棋子落在交叉点）。</summary>
    public bool IsPointBoard => _kind is MiniGameEngine.GameKind.Gobang or MiniGameEngine.GameKind.ChineseChess or MiniGameEngine.GameKind.Go;

    /// <summary>画布尺寸（含边距）。线制棋：交叉点数-1 段；格制棋：格子数。</summary>
    public float BoardWidth => IsPointBoard ? PAD * 2 + (float)((_cols - 1) * _cellSize) : PAD * 2 + (float)(_cols * _cellSize);
    public float BoardHeight => IsPointBoard ? PAD * 2 + (float)((_rows - 1) * _cellSize) : PAD * 2 + (float)(_rows * _cellSize);

    private float CS => (float)_cellSize;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        // 底板
        canvas.SaveState();
        canvas.FillColor = WoodBase;
        canvas.FillRoundedRectangle(0, 0, BoardWidth, BoardHeight, 10);
        canvas.StrokeColor = WoodLine;
        canvas.StrokeSize = 1.5f;
        canvas.DrawRoundedRectangle(0, 0, BoardWidth, BoardHeight, 10);
        // 底板下沿阴影线
        canvas.StrokeColor = WoodShadow;
        canvas.StrokeSize = 1f;
        canvas.DrawLine(0, BoardHeight - 1, BoardWidth, BoardHeight - 1);
        canvas.RestoreState();

        switch (_kind)
        {
            case MiniGameEngine.GameKind.Gobang: DrawGobang(canvas); break;
            case MiniGameEngine.GameKind.ChineseChess: DrawChineseChess(canvas); break;
            case MiniGameEngine.GameKind.Chess: DrawChess(canvas); break;
            case MiniGameEngine.GameKind.AnimalChess: DrawAnimalChess(canvas); break;
            case MiniGameEngine.GameKind.Ludo: DrawLudo(canvas); break;
            case MiniGameEngine.GameKind.Snake: DrawSnake(canvas); break;
            case MiniGameEngine.GameKind.Go: DrawGo(canvas); break;
        }
    }

    // ================================================================
    // 五子棋：15×15 交叉线，星位，黑白圆子
    // ================================================================
    private void DrawGobang(ICanvas canvas)
    {
        canvas.StrokeColor = BoardLine;
        canvas.StrokeSize = 1.2f;
        for (int i = 0; i < _rows; i++)
        {
            canvas.DrawLine(X(0), Y(i), X(_cols - 1), Y(i));
            canvas.DrawLine(X(i), Y(0), X(i), Y(_rows - 1));
        }
        // 星位
        canvas.FillColor = BoardLine;
        foreach (var (sr, sc) in new[] { (3, 3), (3, 11), (11, 3), (11, 11), (7, 7) })
            canvas.FillCircle(X(sc), Y(sr), 3f);

        foreach (var cell in _cells)
        {
            if (string.IsNullOrEmpty(cell.Piece)) continue;
            var (cx, cy) = (X(cell.Col), Y(cell.Row));
            bool black = cell.Piece == "●";
            DrawDisc(canvas, cx, cy, CS * 0.82f,
                black ? Color.FromArgb("#2B2B2B") : Color.FromArgb("#FDFBF5"),
                black ? null : Color.FromArgb("#C9BFA8"));
        }
    }

    // ================================================================
    // 中国象棋：9×10 交点，楚河汉界、九宫、炮位/兵位标记、字圆子
    // ================================================================
    private void DrawChineseChess(ICanvas canvas)
    {
        var lineColor = Color.FromArgb("#6B4A2B");
        // 外框加粗
        canvas.StrokeColor = lineColor;
        canvas.StrokeSize = 2f;
        canvas.DrawRectangle(X(0), Y(0), X(_cols - 1) - X(0), Y(_rows - 1) - Y(0));
        // 内部线（横线全画，竖线跳过楚河 4/5 行之间）
        canvas.StrokeSize = 1.2f;
        for (int r = 0; r < _rows; r++)
            canvas.DrawLine(X(0), Y(r), X(_cols - 1), Y(r));
        for (int c = 0; c < _cols; c++)
        {
            canvas.DrawLine(X(c), Y(0), X(c), Y(4));
            canvas.DrawLine(X(c), Y(5), X(c), Y(_rows - 1));
        }
        // 九宫斜线
        void Palace(int r1, int c1, int r2, int c2) => canvas.DrawLine(X(c1), Y(r1), X(c2), Y(r2));
        Palace(0, 3, 2, 5); Palace(0, 5, 2, 3);
        Palace(7, 3, 9, 5); Palace(7, 5, 9, 3);

        // 楚河汉界（位于 4/5 行之间的河中线）
        float riverY = (Y(4) + Y(5)) / 2f;
        canvas.FontColor = Color.FromArgb("#93A881");
        canvas.FontSize = CS * 0.62f;
        DrawCentered(canvas, "楚", X(1.5f), riverY);
        DrawCentered(canvas, "河", X(2.5f), riverY);
        DrawCentered(canvas, "汉", X(5.5f), riverY);
        DrawCentered(canvas, "界", X(6.5f), riverY);

        // 炮位十字
        canvas.StrokeColor = lineColor;
        canvas.StrokeSize = 1f;
        foreach (var (pr, pc) in new[] { (2, 1), (2, 7), (7, 1), (7, 7) })
            DrawCross(canvas, X(pc), Y(pr), CS * 0.35f);
        // 兵/卒位小十字
        foreach (var pr in new[] { 3, 6 })
            for (int pc = 0; pc < 9; pc += 2)
                DrawCross(canvas, X(pc), Y(pr), CS * 0.2f);

        // 棋子圆盘
        foreach (var cell in _cells)
        {
            if (string.IsNullOrEmpty(cell.Piece)) continue;
            var (cx, cy) = (X(cell.Col), Y(cell.Row));
            float d = CS * 0.86f;
            // 阴影
            canvas.FillColor = Color.FromArgb("#40000000");
            canvas.FillCircle(cx + 2, cy + 2, d / 2);
            // 盘体
            canvas.FillColor = Color.FromArgb("#FFFDF5");
            canvas.FillCircle(cx, cy, d / 2);
            canvas.StrokeColor = Color.FromArgb("#8B5A2B");
            canvas.StrokeSize = 2f;
            canvas.DrawCircle(cx, cy, d / 2);
            // 字
            bool red = cell.IsRed;
            canvas.FontColor = red ? Color.FromArgb("#C0392B") : Color.FromArgb("#2B2B2B");
            canvas.FontSize = CS * 0.55f;
            DrawCentered(canvas, cell.Piece, cx, cy);
            if (cell.IsHighlighted) DrawSelectionRing(canvas, cx, cy, r: d / 2 + 3);
        }
    }

    // ================================================================
    // 国际象棋：双色格 + Unicode 棋子
    // ================================================================
    private void DrawChess(ICanvas canvas)
    {
        for (int r = 0; r < _rows; r++)
        {
            for (int c = 0; c < _cols; c++)
            {
                var rect = new RectF(X(c), Y(r), CS, CS);
                var (ccx, ccy) = (X(c) + CS / 2, Y(r) + CS / 2);   // 格子中心
                canvas.FillColor = (r + c) % 2 == 0 ? Color.FromArgb("#EFE0C0") : Color.FromArgb("#B9A57F");
                canvas.FillRectangle(rect);
                var cell = CellAt(r, c);
                if (cell is not null && cell.IsHighlighted)
                {
                    canvas.FillColor = new Color(0.90f, 0.66f, 0.10f, 0.35f);
                    canvas.FillRectangle(rect);
                    canvas.StrokeColor = Color.FromArgb("#E6A817");
                    canvas.StrokeSize = 2.5f;
                    canvas.DrawRoundedRectangle(rect.X + 2, rect.Y + 2, rect.Width - 4, rect.Height - 4, 4f);
                }
                if (cell is null || string.IsNullOrEmpty(cell.Piece)) continue;
                bool white = cell.Piece[0] < 0x265A;   // ♔..♙ 为白子
                canvas.FontSize = CS * 0.72f;
                // 描边字：先画偏移大字再画本体
                canvas.FontColor = white ? Color.FromArgb("#5D4A3A") : Color.FromArgb("#F5EBD8");
                DrawCentered(canvas, cell.Piece, ccx + 1.2f, ccy + 1.2f);
                canvas.FontColor = white ? Color.FromArgb("#FFFFFF") : Color.FromArgb("#202020");
                DrawCentered(canvas, cell.Piece, ccx, ccy);
            }
        }
    }

    // ================================================================
    // 斗兽棋：7×9 格 + 水域 + 兽穴 + 陷阱 + 圆形字子
    // ================================================================
    private static bool IsWaterCell(int r, int c) => (r is 2 or 4) && c is >= 3 and <= 5;
    private static bool IsTrapCell(int r, int c) =>
        (r == 0 && c is 3 or 5) || (r == 1 && c == 4) ||
        (r == 6 && c is 3 or 5) || (r == 5 && c == 4);

    private void DrawAnimalChess(ICanvas canvas)
    {
        for (int r = 0; r < _rows; r++)
        {
            for (int c = 0; c < _cols; c++)
            {
                var rect = new RectF(X(c), Y(r), CS, CS);
                var (ccx, ccy) = (X(c) + CS / 2, Y(r) + CS / 2);   // 格子中心
                // 水域优先（覆盖格色）
                if (IsWaterCell(r, c))
                {
                    canvas.FillColor = Color.FromArgb("#AEDFF7");
                    canvas.FillRoundedRectangle(rect, 5);
                    canvas.StrokeColor = Color.FromArgb("#7EC0E5");
                    canvas.StrokeSize = 1f;
                    canvas.DrawRoundedRectangle(rect, 5);
                    canvas.FontColor = Color.FromArgb("#5A9DBF");
                    canvas.FontSize = CS * 0.5f;
                    DrawCentered(canvas, "河", ccx, ccy);
                }
                else
                {
                    canvas.FillColor = (r + c) % 2 == 0 ? Color.FromArgb("#F5EFE0") : Color.FromArgb("#E0C99A");
                    canvas.FillRectangle(rect);
                }
                if (IsTrapCell(r, c))
                {
                    canvas.FillColor = Color.FromArgb("#D8B465");
                    float half = CS * 0.22f;
                    var path = new PathF();
                    path.MoveTo(ccx, ccy - half);
                    path.LineTo(ccx - half, ccy + half);
                    path.LineTo(ccx + half, ccy + half);
                    path.Close();
                    canvas.FillPath(path);
                }
                // 兽穴
                if (r == 0 && c == 4)
                {
                    canvas.FillColor = Color.FromArgb("#E57373");
                    canvas.FillCircle(ccx, ccy, CS * 0.4f);
                    canvas.FontColor = Colors.White;
                    canvas.FontSize = CS * 0.45f;
                    DrawCentered(canvas, "穴", ccx, ccy);
                }
                if (r == 6 && c == 4)
                {
                    canvas.FillColor = Color.FromArgb("#64B5F6");
                    canvas.FillCircle(ccx, ccy, CS * 0.4f);
                    canvas.FontColor = Colors.White;
                    canvas.FontSize = CS * 0.45f;
                    DrawCentered(canvas, "穴", ccx, ccy);
                }

                var cell = CellAt(r, c);
                if (cell is null || string.IsNullOrEmpty(cell.Piece)) continue;
                bool red = cell.IsRed;
                float d = CS * 0.85f;
                canvas.FillColor = Color.FromArgb("#FFFDF5");
                canvas.FillCircle(ccx + 1.5f, ccy + 1.5f, d / 2);
                canvas.FillCircle(ccx, ccy, d / 2);
                canvas.StrokeColor = Color.FromArgb("#8B5A2B");
                canvas.StrokeSize = 1.5f;
                canvas.DrawCircle(ccx, ccy, d / 2);
                canvas.FontColor = red ? Color.FromArgb("#C0392B") : Color.FromArgb("#1565C0");
                canvas.FontSize = CS * 0.45f;
                DrawCentered(canvas, cell.Piece, ccx, ccy);
                if (cell.IsHighlighted) DrawSelectionRing(canvas, ccx, ccy);
            }
        }
    }

    // ================================================================
    // 飞行棋：主环格 + 中央终点区 + 圆棋子
    // ================================================================
    private void DrawLudo(ICanvas canvas)
    {
        for (int r = 0; r < _rows; r++)
        {
            for (int c = 0; c < _cols; c++)
            {
                var cell = CellAt(r, c);
                var rect = new RectF(X(c), Y(r), CS - 2, CS - 2);
                var (ccx, ccy) = (X(c) + CS / 2, Y(r) + CS / 2);
                canvas.FillColor = cell?.BgColor ?? Color.FromArgb("#E8DFCA");
                canvas.FillRoundedRectangle(rect, 4);
                canvas.StrokeColor = Color.FromArgb("#C9A96B");
                canvas.StrokeSize = 1f;
                canvas.DrawRoundedRectangle(rect, 4);
                if (cell is null || string.IsNullOrEmpty(cell.Piece)) continue;
                var tint = cell.PieceTint ?? Color.FromArgb("#D32F2F");
                canvas.FillColor = Color.FromArgb("#33000000");
                canvas.FillCircle(ccx + 1.5f, ccy + 1.5f, CS * 0.32f);
                canvas.FillColor = tint;
                canvas.FillCircle(ccx, ccy, CS * 0.32f);
            }
        }
    }

    // ================================================================
    // 贪吃蛇：格 + 蛇身/食物
    // ================================================================
    private void DrawSnake(ICanvas canvas)
    {
        for (int r = 0; r < _rows; r++)
        {
            for (int c = 0; c < _cols; c++)
            {
                var (ccx, ccy) = (X(c) + CS / 2, Y(r) + CS / 2);
                canvas.FillColor = (r + c) % 2 == 0 ? Color.FromArgb("#F5EFE0") : Color.FromArgb("#EDDFBE");
                canvas.FillRoundedRectangle(X(c) + 0.5f, Y(r) + 0.5f, CS - 1, CS - 1, 3);
                var cell = CellAt(r, c);
                if (cell is null || string.IsNullOrEmpty(cell.Piece)) continue;
                if (cell.Piece == "🍎")
                {
                    canvas.FillColor = Color.FromArgb("#33000000");
                    canvas.FillCircle(ccx + 1.5f, ccy + 1.5f, CS * 0.34f);
                    canvas.FillColor = Color.FromArgb("#E53935");
                    canvas.FillCircle(ccx, ccy, CS * 0.34f);
                }
                else
                {
                    bool head = cell.Piece == "●";
                    canvas.FillColor = head ? Color.FromArgb("#2E7D32") : Color.FromArgb("#66BB6A");
                    canvas.FillRoundedRectangle(X(c) + 2, Y(r) + 2, CS - 4, CS - 4, 6);
                }
            }
        }
    }

    // ================================================================
    // 围棋：19×19 交叉线，星位，黑白圆子（复用五子棋风格）
    // ================================================================
    private void DrawGo(ICanvas canvas)
    {
        canvas.StrokeColor = BoardLine;
        canvas.StrokeSize = 1.2f;
        for (int i = 0; i < _rows; i++)
        {
            canvas.DrawLine(X(0), Y(i), X(_cols - 1), Y(i));
            canvas.DrawLine(X(i), Y(0), X(i), Y(_rows - 1));
        }
        // 星位（19 路：9 星）
        canvas.FillColor = BoardLine;
        if (_rows >= 13)
        {
            foreach (var (sr, sc) in new[] { (3, 3), (3, 9), (3, 15), (9, 3), (9, 9), (9, 15), (15, 3), (15, 9), (15, 15) })
                canvas.FillCircle(X(sc), Y(sr), 3.2f);
        }
        else
        {
            var mid = (_rows - 1) / 2;
            canvas.FillCircle(X(mid), Y(mid), 3.2f);
        }

        foreach (var cell in _cells)
        {
            if (string.IsNullOrEmpty(cell.Piece)) continue;
            var (cx, cy) = (X(cell.Col), Y(cell.Row));
            bool black = cell.Piece == "●";
            DrawDisc(canvas, cx, cy, CS * 0.9f,
                black ? Color.FromArgb("#1B1B1B") : Color.FromArgb("#FDFBF5"),
                black ? null : Color.FromArgb("#C9BFA8"));
            if (cell.IsHighlighted) DrawSelectionRing(canvas, cx, cy, r: CS * 0.28f);
        }
    }

    // ================================================================
    // 工具
    // ================================================================

    /// <summary>画一个带可选描边的圆盘棋子（含右下阴影）。</summary>
    private static void DrawDisc(ICanvas canvas, float cx, float cy, float diameter, Color fill, Color? edge)
    {
        canvas.FillColor = Color.FromArgb("#33000000");
        canvas.FillCircle(cx + 1.5f, cy + 1.5f, diameter / 2);
        canvas.FillColor = fill;
        canvas.FillCircle(cx, cy, diameter / 2);
        if (edge is not null)
        {
            canvas.StrokeColor = edge;
            canvas.StrokeSize = 1.2f;
            canvas.DrawCircle(cx, cy, diameter / 2);
        }
    }

    /// <summary>选中/合法落点：金色外圈 + 实心小点。</summary>
    private static void DrawSelectionRing(ICanvas canvas, float cx, float cy, float? r = null)
    {
        var radius = r ?? 6f;
        canvas.FillColor = Color.FromArgb("#E6A817");
        canvas.FillCircle(cx, cy, radius);
        canvas.StrokeColor = Color.FromArgb("#E6A817");
        canvas.StrokeSize = 2f;
        canvas.DrawCircle(cx, cy, radius + 4);
    }

    private static void DrawCross(ICanvas canvas, float cx, float cy, float half)
    {
        canvas.DrawLine(cx - half, cy, cx + half, cy);
        canvas.DrawLine(cx, cy - half, cx, cy + half);
    }

    private static void DrawCentered(ICanvas canvas, string text, float cx, float cy)
    {
        canvas.DrawString(text, cx - 100, cy - 100, 200, 200,
            HorizontalAlignment.Center, VerticalAlignment.Center, TextFlow.OverflowBounds);
    }

    /// <summary>交点坐标（线制）与格子中心（格制）换算。</summary>
    private float X(float c) => PAD + c * CS;
    private float Y(float r) => PAD + r * CS;
}