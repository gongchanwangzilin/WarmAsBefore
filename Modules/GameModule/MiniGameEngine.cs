using System.Text;
using WarmAsBefore.Modules.AiChat;

namespace WarmAsBefore.Modules.GameModule;

/// <summary>
/// 六合一棋牌规则引擎：真实棋盘矩阵 + 各游戏完整走法/吃子/胜负判定。
/// 五子棋（先手玩家）、国际象棋、中国象棋、斗兽棋（选子走棋）、飞行棋（掷骰自动）、贪吃蛇（方向定时移动）。
/// </summary>
public sealed class MiniGameEngine
{
    public enum GameKind { Gobang, AnimalChess, Ludo, Chess, ChineseChess, Snake, Go }

    private readonly ChatEngine _ai;
    private string _charId = "";
    private GameKind _currentGame;
    private readonly List<string> _moveHistory = new();
    private string[][] _board = Array.Empty<string[]>();
    private bool _playerTurn = true;

    // 贪吃蛇状态
    private List<(int r, int c)> _snake = new();
    private (int r, int c) _food;
    private (int dr, int dc) _snakeDir = (0, 1);
    private int _snakeScore;

    // 飞行棋状态（双人简化：玩家红 vs AI 蓝，各 4 子）
    private readonly List<int> _ludoPlayer = new() { -1, -1, -1, -1 };   // -1=巢
    private readonly List<int> _ludoAi = new() { -1, -1, -1, -1 };
    private int _ludoDie;

    // 中国象棋/斗兽棋：每格棋子的阵营标记 (true=红方/AI方，false=玩家方)，用于渲染颜色
    private bool[,]? _ownerRed;

    // 国际象棋：易位/吃过路兵状态跟踪
    private bool _wkMoved, _bkMoved, _wQmRookMoved, _wKmRookMoved, _bQmRookMoved, _bKmRookMoved;
    private (int fr, int fc, int tr, int tc)? _lastChessMove;   // 上一步（吃过路兵判定）
    private (int fr, int fc, int tr, int tc)? _lastPawnDouble;  // 上一步兵双格推进（吃过路兵判定）

    // 围棋：气/提子/打劫状态
    private string[][]? _goPrevBoard;          // 上一手后棋盘快照（打劫判定）
    private string[][]? _goBeforeBoard;        // 当前落子前快照（失败时还原）
    private (int r, int c)? _goLastMove;       // 上一手落点（渲染标记用）

    // AI 本地回话话术池（杜绝每步云端延迟与刷屏）
    private static readonly string[] _quips =
    {
        "嘿嘿，吃你一子！", "这子我收下了~", "还你一记，服不服？", "吃掉！看你心疼不心疼",
        "送你一个惊喜！", "这波不亏！", "拿下拿下！", "哼，早就瞄准它了"
    };
    private static readonly string[] _cheers =
    {
        "该你了哦~", "轮到你了！", "好好想一下，我可不会放水", "认真点，这局我要赢哦",
        "慢慢来，不着急", "我在这儿等着你", "下一手你打算怎么办？", "加油鸭~"
    };
    private int _aiMoveCount;
    private readonly Random _rnd = new();

    public bool GameOver { get; private set; }
    public string Winner { get; private set; } = "";
    public int Rows { get; private set; } = 8;
    public int Cols { get; private set; } = 8;
    public bool IsSnake => _currentGame == GameKind.Snake;
    public bool IsLudo => _currentGame == GameKind.Ludo;
    public bool PlayerTurn => _playerTurn;
    public int SnakeScore => _snakeScore;
    public int LudoDie => _ludoDie;
    public string[][] Board => _board;
    public GameKind CurrentGame => _currentGame;
    public string MoveLog => string.Join("\n", _moveHistory.TakeLast(20));
    public int MoveCount => _moveHistory.Count;

    /// <summary>AI 难度：Easy=随机 / Normal=贪吃 / Hard=前瞻防反。</summary>
    public enum AiDifficulty { Easy, Normal, Hard }
    public AiDifficulty Difficulty { get; set; } = AiDifficulty.Normal;
    public string DifficultyLabel => Difficulty switch
    {
        AiDifficulty.Easy => "简单（新手练手）",
        AiDifficulty.Hard => "困难（步步紧逼）",
        _ => "普通（默认）"
    };

    /// <summary>可选云端棋力脑：启用时每回合后台请求候选择优，未配置则纯本地（默认）。</summary>
    public ChessBrainService? Brain { get; set; }
    private (int fr, int fc, int tr, int tc)? _brainAdvice;

    /// <summary>飞行棋主环格数（外圈+内圈）。</summary>
    private const int LudoRing = 40;
    /// <summary>飞行棋终点通道长度。</summary>
    private const int LudoFinish = 4;

    public event Action<string, string>? OnGameEvent; // (eventType, message)

    public MiniGameEngine(ChatEngine ai) => _ai = ai;

    /// <summary>同步开局：立即构建真实棋盘。AI 问候语经 OnGameEvent 事件异步送达（不阻塞渲染）。返回系统级提示。</summary>
    public string StartGame(GameKind kind, string characterId)
    {
        _charId = characterId;
        _currentGame = kind;
        _moveHistory.Clear();
        GameOver = false;
        Winner = "";
        _playerTurn = true;
        // 阵营表必须随局重建：先清空防残留尺寸（单例引擎下若漏建会越界闪退），
        // 需要阵营表的棋类（国象/中象/斗兽）在各自 Build 内按棋盘尺寸重新初始化。
        _ownerRed = null;
        // 国际象棋状态重置
        _wkMoved = _bkMoved = _wQmRookMoved = _wKmRookMoved = _bQmRookMoved = _bKmRookMoved = false;
        _lastChessMove = null;
        _lastPawnDouble = null;
        // 围棋状态重置
        _goPrevBoard = null;
        _goBeforeBoard = null;
        _goLastMove = null;

        switch (kind)
        {
            case GameKind.Gobang: BuildGobang(); break;
            case GameKind.Chess: BuildChess(); break;
            case GameKind.ChineseChess: BuildChineseChess(); break;
            case GameKind.AnimalChess: BuildAnimalChess(); break;
            case GameKind.Ludo: BuildLudo(); break;
            case GameKind.Snake: BuildSnake(); break;
            case GameKind.Go: BuildGo(); break;
        }

        var gameName = kind switch
        {
            GameKind.Gobang => "五子棋",
            GameKind.AnimalChess => "斗兽棋",
            GameKind.Ludo => "飞行棋",
            GameKind.Chess => "国际象棋",
            GameKind.ChineseChess => "中国象棋",
            GameKind.Snake => "贪吃蛇",
            GameKind.Go => "围棋",
            _ => "游戏"
        };
        var tip = kind switch
        {
            GameKind.Gobang => "点击空位落子，连成五子即胜",
            GameKind.Chess or GameKind.ChineseChess or GameKind.AnimalChess => "点击己方棋子再点目标格走子",
            GameKind.Go => "点击交叉点落子，围地多者胜",
            GameKind.Ludo => "点击 🎲 掷骰子",
            GameKind.Snake => "用方向键控制小蛇吃果子",
            _ => "开始"
        };
        _ = GreetAsync(gameName);
        return $"{gameName}开始！{tip}";
    }

    /// <summary>异步 AI 问候，完成后通过事件送达（fire-and-forget，绝不阻塞棋盘渲染与交互）。</summary>
    private async Task GreetAsync(string gameName)
    {
        string reply;
        try { reply = await SendAiWithTimeout($"我们来玩{gameName}吧！你是{_charId}。游戏开始，请简短打个招呼并说明规则要点（两句话以内）。"); }
        catch { reply = $"{gameName}开始！轮到你走（{gameName}：点击己方棋子再点击目标格）。"; }
        _moveHistory.Add($"AI: {reply}");
        OnGameEvent?.Invoke("start", reply);
    }

    /// <summary>引擎内所有 AI 调用统一 8 秒硬超时，防止网络挂起冻结棋盘交互。</summary>
    private async Task<string> SendAiWithTimeout(string prompt)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try { return await _ai.Send(_charId, prompt).WaitAsync(cts.Token); }
        catch { return "…"; }
    }

    /// <summary>节流版云端俏皮话：fire-and-forget，失败静默，绝不阻塞棋局渲染与交互。</summary>
    private async Task QuipAsync(string captured)
    {
        try
        {
            var reply = await SendAiWithTimeout(
                $"（{GameName(_currentGame)} 棋局进行中，小雨刚{(string.IsNullOrEmpty(captured) ? "落子" : "吃子")}）简短说一句加油或挑衅的话（12 字内）。");
            if (string.IsNullOrWhiteSpace(reply) || reply == "…") return;
            _moveHistory.Add($"AI: {reply}");
            OnGameEvent?.Invoke("chat", reply);
        }
        catch { /* 静默：网络失败不影响对局 */ }
    }

    /// <summary>AI 走子后检测玩家是否被将军（国象/中象）。仅用于本地话术，不阻塞。</summary>
    private bool IsCheckAfterMove()
    {
        try
        {
            if (_currentGame == GameKind.Chess)
            {
                // 白方（玩家）国王是否被黑方攻击
                for (int r = 0; r < 8; r++)
                    for (int c = 0; c < 8; c++)
                        if (_board[r][c] == "K") return IsSquareAttacked(r, c, false);
            }
            else if (_currentGame == GameKind.ChineseChess)
            {
                // 将帅照面 = 黑将被红帅"盯住"→ 视为被将军
                return GeneralsFaceEachOther();
            }
        }
        catch { }
        return false;
    }

    // ==================================================================
    // 棋盘构建
    // ==================================================================

    private void BuildGobang()
    {
        Rows = 15; Cols = 15;
        _board = Empty(Rows, Cols);
    }

    private void BuildGo()
    {
        Rows = 19; Cols = 19;
        _board = Empty(Rows, Cols);
    }

    private void BuildChess()
    {
        Rows = 8; Cols = 8;
        _board = Empty(8, 8);
        _board[0] = "rnbqkbnr".Select(c => c.ToString()).ToArray();
        _board[1] = Enumerable.Repeat("p", 8).ToArray();
        _board[6] = Enumerable.Repeat("P", 8).ToArray();
        _board[7] = "RNBQKBNR".Select(c => c.ToString()).ToArray();
        // 阵营表必须与当前棋盘尺寸一致：白方（玩家，大写）为 "红方" 语义，供走子后同步归属。
        // 注意：引擎为单例，若这里不重建，残留的斗兽棋 7×9 / 中象 10×9 数组会在 8×8 遍历时越界闪退。
        _ownerRed = new bool[8, 8];
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
                _ownerRed[r, c] = char.IsUpper(_board[r][c][0]);
    }

    private void BuildChineseChess()
    {
        Rows = 10; Cols = 9;
        _board = Empty(10, 9);
        var back = new[] { "车", "马", "象", "士", "将", "士", "象", "马", "车" };
        var front = new[] { "车", "马", "相", "仕", "帅", "仕", "相", "马", "车" };
        for (int c = 0; c < 9; c++) { _board[0][c] = back[c]; _board[9][c] = front[c]; }
        _board[2][1] = "炮"; _board[2][7] = "炮";
        _board[7][1] = "炮"; _board[7][7] = "炮";
        for (int c = 0; c < 9; c += 2) { _board[3][c] = "卒"; _board[6][c] = "兵"; }
        // 阵营：红方（玩家）= 下方，黑方（AI）= 上方
        _ownerRed = new bool[10, 9];
        for (int r = 0; r < 10; r++)
            for (int c = 0; c < 9; c++)
                _ownerRed[r, c] = r >= 5 && !string.IsNullOrEmpty(_board[r][c]);
    }

    private void BuildAnimalChess()
    {
        // 7 行 9 列：上方红方（AI），下方蓝方（玩家），中间第 2/4 行的 3、4、5 列为河
        Rows = 7; Cols = 9;
        _board = Empty(7, 9);
        var row = new[] { "象", "狮", "虎", "豹", "狼", "狗", "猫", "鼠" };
        _board[0][0] = row[0]; _board[0][1] = row[1]; _board[0][2] = row[2];
        _board[0][3] = row[3]; _board[0][4] = row[4]; _board[0][5] = row[5];
        _board[0][6] = row[6]; _board[0][7] = row[7];
        _board[6][0] = row[0]; _board[6][1] = row[1]; _board[6][2] = row[2];
        _board[6][3] = row[3]; _board[6][4] = row[4]; _board[6][5] = row[5];
        _board[6][6] = row[6]; _board[6][7] = row[7];
        // 阵营：上方 AI 红方，下方玩家蓝方
        _ownerRed = new bool[7, 9];
        for (int c = 0; c < 9; c++) { _ownerRed[0, c] = true; _ownerRed[6, c] = false; }
    }

    private void BuildLudo()
    {
        Rows = 9; Cols = 9;
        _board = Empty(9, 9);
        for (int i = 0; i < 4; i++) { _ludoPlayer[i] = -1; _ludoAi[i] = -1; }
        _ludoDie = 0;
    }

    private void BuildSnake()
    {
        Rows = 10; Cols = 10;
        _board = Empty(10, 10);
        _snake.Clear();
        _snake.Add((4, 5)); _snake.Add((4, 4)); _snake.Add((4, 3));
        _snakeDir = (0, 1);
        _snakeScore = 0;
        SpawnFood();
        PaintSnake();
    }

    private static string[][] Empty(int r, int c)
    {
        var b = new string[r][];
        for (int i = 0; i < r; i++)
        {
            b[i] = new string[c];
            for (int j = 0; j < c; j++) b[i][j] = "";
        }
        return b;
    }

    // ==================================================================
    // 通用：选子与走法
    // ==================================================================

    /// <summary>该格是否是当前回合玩家的棋子（用于选中）。</summary>
    public bool IsPlayerPiece(int r, int c)
    {
        if (GameOver || _currentGame is GameKind.Snake or GameKind.Ludo) return false;
        if (!_playerTurn) return false;
        var p = Piece(r, c);
        if (string.IsNullOrEmpty(p)) return false;
        return _currentGame switch
        {
            GameKind.Gobang => p == "●",
            GameKind.Chess => char.IsUpper(p[0]),
            GameKind.ChineseChess => IsRedSide(r, c),
            GameKind.AnimalChess => !IsRedSide(r, c),   // 玩家=下方蓝方
            _ => false
        };
    }

    public string Piece(int r, int c) => (r >= 0 && r < Rows && c >= 0 && c < Cols) ? _board[r][c] : "";

    /// <summary>该格棋子是否为红方/AI方（中国象棋、斗兽棋渲染用）。非阵营棋盘返回 false。</summary>
    public bool IsRedSide(int r, int c) =>
        _ownerRed is not null && r >= 0 && r < _ownerRed.GetLength(0) && c >= 0 && c < _ownerRed.GetLength(1) && _ownerRed[r, c] && !string.IsNullOrEmpty(_board[r][c]);

    /// <summary>返回 (r,c) 处棋子的全部合法落点（含吃子位）。</summary>
    public List<(int r, int c)> LegalMoves(int r, int c)
    {
        var moves = new List<(int, int)>();
        var p = Piece(r, c);
        if (string.IsNullOrEmpty(p)) return moves;
        switch (_currentGame)
        {
            case GameKind.Gobang:
            case GameKind.Snake:
            case GameKind.Ludo:
                return moves;
            case GameKind.Chess: return LegalChess(r, c, p);
            case GameKind.ChineseChess: return LegalChineseChess(r, c, p);
            case GameKind.AnimalChess: return LegalAnimalChess(r, c, p);
        }
        return moves;
    }

    /// <summary>玩家走子。返回 (是否成功, 消息)。成功且未终局时自动接 AI 回合。</summary>
    public async Task<(bool ok, string msg)> PlayerMove(int fr, int fc, int tr, int tc)
    {
        if (GameOver) return (false, "游戏已结束");
        if (!_playerTurn && _currentGame != GameKind.Gobang) return (false, "轮到对手走");
        if (fr < 0)  // 五子棋/围棋无选择直接落子
        {
            if (_currentGame == GameKind.Gobang) return await PlaceGobang(tr, tc);
            if (_currentGame == GameKind.Go) return await PlaceGo(tr, tc);
            return (false, "");
        }
        var p = Piece(fr, fc);
        var moves = LegalMoves(fr, fc);
        if (!moves.Contains((tr, tc))) return (false, "这一步走不了");
        var captured = Piece(tr, tc);
        var pLabel = ChineseLabel(p, fr, fc);
        // 记谱：中国象棋用传统记谱法（在改棋盘前计算「前/后」位置）
        string note = _currentGame == GameKind.ChineseChess && _ownerRed is not null
            ? ChineseNotation((fr, fc, tr, tc), p, _ownerRed[fr, fc])
            : "";
        _board[tr][tc] = p;
        _board[fr][fc] = "";
        if (_currentGame == GameKind.Chess) ApplyChessSideEffects(fr, fc, tr, tc, p, ref captured);
        if (_ownerRed is not null && tr < _ownerRed.GetLength(0) && tc < _ownerRed.GetLength(1)
            && fr < _ownerRed.GetLength(0) && fc < _ownerRed.GetLength(1))
        {
            _ownerRed[tr, tc] = _ownerRed[fr, fc];
            _ownerRed[fr, fc] = false;
        }
        _moveHistory.Add(string.IsNullOrEmpty(note)
            ? $"玩家 {(fr + 1)},{fc + 1}→{(tr + 1)},{tc + 1} ({pLabel}{(string.IsNullOrEmpty(captured) ? "" : " 吃" + ChineseLabel(captured, tr, tc))})"
            : $"玩家 {note}{(string.IsNullOrEmpty(captured) ? "" : " 吃" + ChineseLabel(captured, tr, tc))}");

        var end = CheckEnd();
        if (end is not null) { GameOver = true; Winner = end; return (true, end); }

        _playerTurn = false;
        var aiMsg = await AiMoveAsync();
        return (true, aiMsg);
    }

    // ==================================================================
    // 五子棋
    // ==================================================================

    private async Task<(bool ok, string msg)> PlaceGobang(int r, int c)
    {
        if (r < 0 || r >= Rows || c < 0 || c >= Cols || !string.IsNullOrEmpty(Piece(r, c)))
            return (false, "这里不能下");
        _board[r][c] = "●";   // 玩家黑
        _moveHistory.Add($"玩家落子 ({r + 1},{c + 1})");
        if (CheckFive(r, c, "●")) { GameOver = true; Winner = "你赢了！五子连珠"; return (true, Winner); }

        var ai = AiGobangMove();
        if (ai.r < 0) { GameOver = true; Winner = "平局，棋盘已满"; return (true, Winner); }
        _board[ai.r][ai.c] = "○";
        _moveHistory.Add($"AI 落子 ({ai.r + 1},{ai.c + 1})");
        if (CheckFive(ai.r, ai.c, "○")) { GameOver = true; Winner = "小雨赢了…五子连珠"; return (true, Winner); }
        if (IsBoardFull()) { GameOver = true; Winner = "平局，棋盘已满"; return (true, Winner); }
        return (true, $"你下 ({r + 1},{c + 1})，小雨回 ({ai.r + 1},{ai.c + 1})");
    }

    // ==================================================================
    // 围棋
    // ==================================================================

    /// <summary>玩家落黑子：提子、禁着点、打劫校验；随后 AI 落白子；双方皆无可落子时自动数子终局。</summary>
    private async Task<(bool ok, string msg)> PlaceGo(int r, int c)
    {
        if (r < 0 || r >= Rows || c < 0 || c >= Cols || !string.IsNullOrEmpty(Piece(r, c)))
            return (false, "这里不能落子");
        if (_goPrevBoard is not null && IsKoRepetition(r, c))
            return (false, "打劫，不能立即提回");

        // 快照并模拟落黑
        _goBeforeBoard = SnapshotBoard();
        _board[r][c] = "●";
        RemoveCaptured("○");
        if (CountLiberties(r, c, "●") == 0)
        {
            RestoreBoard(_goBeforeBoard);
            return (false, "禁着点：落子后无气");   // 禁着点（自杀）
        }
        _goLastMove = (r, c);
        _moveHistory.Add($"你落子 ({r + 1},{c + 1})");
        _goPrevBoard = _goBeforeBoard;   // 打劫基准 = 落子前局面

        // AI 落白
        var ai = AiGoMove();
        if (ai.r < 0)
        {
            if (PlayerHasAnyMove()) { GameOver = true; Winner = SettleGoScore(); return (true, Winner); }
            GameOver = true; Winner = SettleGoScore(); return (true, Winner);
        }
        _goBeforeBoard = SnapshotBoard();
        _board[ai.r][ai.c] = "○";
        RemoveCaptured("●");
        bool koRepeat = _goPrevBoard is not null && BoardsEqual(_board, _goPrevBoard);
        if (CountLiberties(ai.r, ai.c, "○") == 0 || koRepeat)
        {
            RestoreBoard(_goBeforeBoard);   // AI 不该走自杀/打劫重复，防御性回退
        }
        else
        {
            _goLastMove = (ai.r, ai.c);
            _moveHistory.Add($"小雨落子 ({ai.r + 1},{ai.c + 1})");
            _goPrevBoard = _goBeforeBoard;
        }
        return (true, $"你下 ({r + 1},{c + 1})，小雨回 ({ai.r + 1},{ai.c + 1})");
    }

    /// <summary>提掉 color 方所有无气棋子组，返回被提子数。</summary>
    private int RemoveCaptured(string color)
    {
        int removed = 0;
        for (int rr = 0; rr < Rows; rr++)
            for (int cc = 0; cc < Cols; cc++)
                if (_board[rr][cc] == color && CountLiberties(rr, cc, color) == 0)
                {
                    // 整组提掉
                    var group = GroupOf(rr, cc, color);
                    foreach (var (gr, gc) in group)
                    {
                        _board[gr][gc] = "";
                        removed++;
                    }
                }
        return removed;
    }

    /// <summary>(r,c) 处 color 棋子的整组连通坐标。</summary>
    private List<(int r, int c)> GroupOf(int r, int c, string color)
    {
        var group = new List<(int, int)>();
        var seen = new HashSet<(int, int)>();
        var stack = new Stack<(int, int)>();
        stack.Push((r, c));
        seen.Add((r, c));
        while (stack.Count > 0)
        {
            var (cr, cc) = stack.Pop();
            group.Add((cr, cc));
            foreach (var (dr, dc) in new[] { (0, 1), (0, -1), (1, 0), (-1, 0) })
            {
                var nr = cr + dr; var nc = cc + dc;
                if (nr < 0 || nr >= Rows || nc < 0 || nc >= Cols) continue;
                if (_board[nr][nc] != color || !seen.Add((nr, nc))) continue;
                stack.Push((nr, nc));
            }
        }
        return group;
    }

    /// <summary>(r,c) 处 color 棋子的气数（直线相邻空点，整组去重计数）。</summary>
    private int CountLiberties(int r, int c, string color)
    {
        if (string.IsNullOrEmpty(_board[r][c])) return 0;
        var liberties = new HashSet<(int, int)>();
        var group = GroupOf(r, c, color);
        foreach (var (gr, gc) in group)
        {
            foreach (var (dr, dc) in new[] { (0, 1), (0, -1), (1, 0), (-1, 0) })
            {
                var nr = gr + dr; var nc = gc + dc;
                if (nr < 0 || nr >= Rows || nc < 0 || nc >= Cols) continue;
                if (string.IsNullOrEmpty(_board[nr][nc])) liberties.Add((nr, nc));
            }
        }
        return liberties.Count;
    }

    /// <summary>落子会导致全局同形（打劫）：模拟后与上一手快照相同即重复。</summary>
    private bool IsKoRepetition(int r, int c)
    {
        _goBeforeBoard = SnapshotBoard();
        _board[r][c] = "●";
        RemoveCaptured("○");
        bool same = _goPrevBoard is not null && BoardsEqual(_board, _goPrevBoard);
        RestoreBoard(_goBeforeBoard);
        return same;
    }

    /// <summary>玩家（黑）是否仍有合法落点。</summary>
    private bool PlayerHasAnyMove()
    {
        for (int rr = 0; rr < Rows; rr++)
            for (int cc = 0; cc < Cols; cc++)
            {
                if (!string.IsNullOrEmpty(_board[rr][cc])) continue;
                _goBeforeBoard = SnapshotBoard();
                _board[rr][cc] = "●";
                RemoveCaptured("○");
                bool legal = CountLiberties(rr, cc, "●") > 0;
                RestoreBoard(_goBeforeBoard);
                if (legal) return true;
            }
        return false;
    }

    /// <summary>围棋 AI：按难度。Easy=随机合法点；Normal/Hard=评分（提子 + 己方气 + 攻击权重加倍）。</summary>
    private (int r, int c) AiGoMove()
    {
        var cands = new List<(int r, int c, int score)>();
        for (int rr = 0; rr < Rows; rr++)
        {
            for (int cc = 0; cc < Cols; cc++)
            {
                if (!string.IsNullOrEmpty(_board[rr][cc])) continue;
                _goBeforeBoard = SnapshotBoard();
                _board[rr][cc] = "○";
                int captured = RemoveCaptured("●");
                int lib = CountLiberties(rr, cc, "○");
                RestoreBoard(_goBeforeBoard);
                if (lib == 0) continue;   // 自杀点不可走
                var score = captured * 40 + lib * 5;
                // 攻击权重：Hard 加倍（更主动围杀）
                if (Difficulty == AiDifficulty.Hard) score = captured * 80 + lib * 8;
                cands.Add((rr, cc, score));
            }
        }
        if (cands.Count == 0) return (-1, -1);
        if (Difficulty == AiDifficulty.Easy)
        {
            var pick = cands[_rnd.Next(cands.Count)];
            return (pick.r, pick.c);
        }
        var best = cands.OrderByDescending(x => x.score).ThenBy(_ => _rnd.Next()).First();
        return (best.r, best.c);
    }

    /// <summary>数子法结算：子 + 围空。返回胜负文本。</summary>
    public string SettleGoScore()
    {
        int black = 0, white = 0;
        for (int rr = 0; rr < Rows; rr++)
            for (int cc = 0; cc < Cols; cc++)
            {
                if (_board[rr][cc] == "●") black++;
                else if (_board[rr][cc] == "○") white++;
            }
        // 围空：空区域 BFS，接触单一色归该色
        var visited = new HashSet<(int, int)>();
        for (int rr = 0; rr < Rows; rr++)
        {
            for (int cc = 0; cc < Cols; cc++)
            {
                if (!string.IsNullOrEmpty(_board[rr][cc]) || !visited.Add((rr, cc))) continue;
                var region = new List<(int, int)>();
                var stack = new Stack<(int, int)>();
                stack.Push((rr, cc));
                var touchBlack = false; var touchWhite = false;
                while (stack.Count > 0)
                {
                    var (cr, ccr) = stack.Pop();
                    region.Add((cr, ccr));
                    foreach (var (dr, dc) in new[] { (0, 1), (0, -1), (1, 0), (-1, 0) })
                    {
                        var nr = cr + dr; var nc = ccr + dc;
                        if (nr < 0 || nr >= Rows || nc < 0 || nc >= Cols) continue;
                        if (string.IsNullOrEmpty(_board[nr][nc]))
                        {
                            if (visited.Add((nr, nc))) stack.Push((nr, nc));
                        }
                        else if (_board[nr][nc] == "●") touchBlack = true;
                        else touchWhite = true;
                    }
                }
                if (touchBlack && !touchWhite) black += region.Count;
                else if (touchWhite && !touchBlack) white += region.Count;
            }
        }
        GameOver = true;
        Winner = black > white ? $"你赢了！黑 {black} 目 vs 白 {white} 目"
            : white > black ? $"小雨赢了…白 {white} 目 vs 黑 {black} 目"
            : $"平局，黑 {black} 目 = 白 {white} 目";
        return Winner;
    }

    private string[][] SnapshotBoard()
    {
        var copy = new string[Rows][];
        for (int i = 0; i < Rows; i++)
            copy[i] = (string[])_board[i].Clone();
        return copy;
    }

    private void RestoreBoard(string[][] snap)
    {
        for (int i = 0; i < Rows && i < snap.Length; i++)
            for (int j = 0; j < Cols && j < snap[i].Length; j++)
                _board[i][j] = snap[i][j];
    }

    private static bool BoardsEqual(string[][] a, string[][] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            for (int j = 0; j < a[i].Length; j++)
                if (a[i][j] != b[i][j]) return false;
        return true;
    }

    private bool CheckFive(int r, int c, string p)
    {
        foreach (var (dr, dc) in new[] { (0, 1), (1, 0), (1, 1), (1, -1) })
        {
            int cnt = 1;
            for (int s = 1; ; s++)
            {
                var nr = r + dr * s; var nc = c + dc * s;
                if (nr < 0 || nr >= Rows || nc < 0 || nc >= Cols || _board[nr][nc] != p) break;
                cnt++;
            }
            for (int s = 1; ; s++)
            {
                var nr = r - dr * s; var nc = c - dc * s;
                if (nr < 0 || nr >= Rows || nc < 0 || nc >= Cols || _board[nr][nc] != p) break;
                cnt++;
            }
            if (cnt >= 5) return true;
        }
        return false;
    }

    private bool IsBoardFull()
    {
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                if (string.IsNullOrEmpty(_board[r][c])) return false;
        return true;
    }

    /// <summary>五子棋 AI：按难度分级。Easy=随机、Normal=攻防评分、Hard=评分权重加倍（更主动进攻）。</summary>
    private (int r, int c) AiGobangMove()
    {
        if (Difficulty == AiDifficulty.Easy)
        {
            var free = new List<(int r, int c)>();
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                    if (string.IsNullOrEmpty(_board[r][c])) free.Add((r, c));
            return free.Count == 0 ? (-1, -1) : free[_rnd.Next(free.Count)];
        }
        int best = -1; var pick = (-1, -1);
        var attackW = Difficulty == AiDifficulty.Hard ? 4 : 2;
        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Cols; c++)
            {
                if (!string.IsNullOrEmpty(_board[r][c])) continue;
                var score = ScoreGobangCell(r, c, attackW);
                if (score > best) { best = score; pick = (r, c); }
            }
        }
        return pick;
    }

    private int ScoreGobangCell(int r, int c, double attackWeight = 2)
    {
        int score = 0;
        foreach (var (dr, dc) in new[] { (0, 1), (1, 0), (1, 1), (1, -1) })
        {
            score += (int)(LineScore(r, c, dr, dc, "○") * attackWeight);   // 进攻
            score += LineScore(r, c, dr, dc, "●");                          // 防守
        }
        return score;
    }

    private int LineScore(int r, int c, int dr, int dc, string p)
    {
        int cnt = 1;
        for (int s = 1; ; s++)
        {
            var nr = r + dr * s; var nc = c + dc * s;
            if (nr < 0 || nr >= Rows || nc < 0 || nc >= Cols) break;
            if (_board[nr][nc] == p) cnt++; else break;
        }
        for (int s = 1; ; s++)
        {
            var nr = r - dr * s; var nc = c - dc * s;
            if (nr < 0 || nr >= Rows || nc < 0 || nc >= Cols) break;
            if (_board[nr][nc] == p) cnt++; else break;
        }
        return cnt switch
        {
            1 => 1,
            2 => 10,
            3 => 100,
            4 => 1000,
            _ => 10000
        };
    }

    // ==================================================================
    // 国际象棋
    // ==================================================================

    private List<(int r, int c)> LegalChess(int r, int c, string p)
    {
        var list = LegalChessCore(r, c, p, withCastling: true);
        // 安全过滤：走子后己方王不得被将军（含王自身不能走入被攻击格）
        bool white = char.IsUpper(p[0]);
        list.RemoveAll(m => WouldLeaveKingInCheck(r, c, m.r, m.c, white));
        return list;
    }

    /// <summary>国际象棋核心走法生成（不含王安全过滤）。供攻击检测复用。</summary>
    private List<(int r, int c)> LegalChessCore(int r, int c, string p, bool withCastling)
    {
        var list = new List<(int, int)>();
        bool white = char.IsUpper(p[0]);
        switch (char.ToUpperInvariant(p[0]))
        {
            case 'P':
                AddPawn(r, c, white, list);
                break;
            case 'R':
                AddLine(r, c, list, new[] { (0, 1), (0, -1), (1, 0), (-1, 0) }, white);
                break;
            case 'B':
                AddLine(r, c, list, new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) }, white);
                break;
            case 'Q':
                AddLine(r, c, list, new[] { (0, 1), (0, -1), (1, 0), (-1, 0), (1, 1), (1, -1), (-1, 1), (-1, -1) }, white);
                break;
            case 'N':
                AddKnight(r, c, list, white);
                break;
            case 'K':
                AddLine(r, c, list, new[] { (0, 1), (0, -1), (1, 0), (-1, 0), (1, 1), (1, -1), (-1, 1), (-1, -1) }, white, max: 1);
                if (withCastling) AddCastling(r, c, white, list);
                break;
        }
        return list;
    }

    /// <summary>模拟走子后，己方王是否暴露在对方攻击下。</summary>
    private bool WouldLeaveKingInCheck(int fr, int fc, int tr, int tc, bool sideWhite)
    {
        var saveT = _board[tr][tc];
        var moved = _board[fr][fc];
        _board[tr][tc] = moved;
        _board[fr][fc] = "";
        // 移动的是王 → 王到 tr,tc；否则原地找己方王
        int kr, kc;
        if (moved == (sideWhite ? "K" : "k")) { kr = tr; kc = tc; }
        else
        {
            kr = -1; kc = -1;
            var king = sideWhite ? "K" : "k";
            for (int rr = 0; rr < 8; rr++)
                for (int cc = 0; cc < 8; cc++)
                    if (_board[rr][cc] == king) { kr = rr; kc = cc; }
        }
        bool inCheck = kr >= 0 && IsSquareAttacked(kr, kc, !sideWhite);
        _board[fr][fc] = _board[tr][tc];
        _board[tr][tc] = saveT;
        return inCheck;
    }

    /// <summary>(r,c) 是否被 byWhite 方棋子攻击。</summary>
    private bool IsSquareAttacked(int r, int c, bool byWhite)
    {
        for (int rr = 0; rr < 8; rr++)
        {
            for (int cc = 0; cc < 8; cc++)
            {
                var q = _board[rr][cc];
                if (string.IsNullOrEmpty(q)) continue;
                if (char.IsUpper(q[0]) != byWhite) continue;   // 攻击方=byWhite
                foreach (var (tr, tc) in LegalChessCore(rr, cc, q, withCastling: false))
                    if (tr == r && tc == c) return true;
            }
        }
        return false;
    }

    /// <summary>王车易位：王与对应车均未动且车仍在原位、中间空格畅通，且王不在将军中、途经格不被攻击。</summary>
    private void AddCastling(int r, int c, bool white, List<(int, int)> list)
    {
        if (IsSquareAttacked(r, c, !white)) return;            // 王正被将军 → 不可易位
        if (white)
        {
            if (r != 7 || c != 4 || _wkMoved) return;                       // 王必须原位且未动
            if (!_wKmRookMoved && _board[7][7] == "R" && string.IsNullOrEmpty(_board[7][5]) && string.IsNullOrEmpty(_board[7][6])
                && !IsSquareAttacked(7, 5, byWhite: false))                  // 途经 f1 不可被攻击
                list.Add((7, 6));                                           // 王翼易位
            if (!_wQmRookMoved && _board[7][0] == "R" && string.IsNullOrEmpty(_board[7][3]) && string.IsNullOrEmpty(_board[7][2]) && string.IsNullOrEmpty(_board[7][1])
                && !IsSquareAttacked(7, 3, byWhite: false))                  // 途经 d1 不可被攻击
                list.Add((7, 2));                                           // 后翼易位
        }
        else
        {
            if (r != 0 || c != 4 || _bkMoved) return;
            if (!_bKmRookMoved && _board[0][7] == "r" && string.IsNullOrEmpty(_board[0][5]) && string.IsNullOrEmpty(_board[0][6])
                && !IsSquareAttacked(0, 5, byWhite: true))
                list.Add((0, 6));
            if (!_bQmRookMoved && _board[0][0] == "r" && string.IsNullOrEmpty(_board[0][3]) && string.IsNullOrEmpty(_board[0][2]) && string.IsNullOrEmpty(_board[0][1])
                && !IsSquareAttacked(0, 3, byWhite: true))
                list.Add((0, 2));
        }
    }

    private void AddPawn(int r, int c, bool white, List<(int, int)> list)
    {
        int dir = white ? -1 : 1;
        var nr = r + dir;
        if (nr >= 0 && nr < 8 && string.IsNullOrEmpty(_board[nr][c])) list.Add((nr, c));
        if (white && r == 6 && string.IsNullOrEmpty(_board[5][c]) && string.IsNullOrEmpty(_board[4][c])) list.Add((4, c));
        if (!white && r == 1 && string.IsNullOrEmpty(_board[2][c]) && string.IsNullOrEmpty(_board[3][c])) list.Add((3, c));
        foreach (var dc in new[] { -1, 1 })
        {
            var nc = c + dc;
            if (nc < 0 || nc > 7 || nr < 0 || nr > 7) continue;
            var t = _board[nr][nc];
            if (!string.IsNullOrEmpty(t) && char.IsUpper(t[0]) != white) list.Add((nr, nc));
        }
        // 吃过路兵：对方兵刚双格推进到相邻列，且该格为空 → 可斜吃
        if (_lastPawnDouble is { } ep && ep.tr == r && Math.Abs(ep.tc - c) == 1)
        {
            var er = r + dir;
            if (er >= 0 && er < 8 && string.IsNullOrEmpty(_board[er][ep.tc]))
                list.Add((er, ep.tc));
        }
    }

    /// <summary>国际象棋走子副作用：易位移车、吃过路兵移除、兵升变、状态跟踪。</summary>
    private void ApplyChessSideEffects(int fr, int fc, int tr, int tc, string p, ref string captured)
    {
        // 1) 王车易位：王两格横移时同时移动车
        if (p == "K" && fr == 7 && fc == 4 && tr == 7 && tc == 6) { _board[7][5] = _board[7][7]; _board[7][7] = ""; }
        else if (p == "K" && fr == 7 && fc == 4 && tr == 7 && tc == 2) { _board[7][3] = _board[7][0]; _board[7][0] = ""; }
        else if (p == "k" && fr == 0 && fc == 4 && tr == 0 && tc == 6) { _board[0][5] = _board[0][7]; _board[0][7] = ""; }
        else if (p == "k" && fr == 0 && fc == 4 && tr == 0 && tc == 2) { _board[0][3] = _board[0][0]; _board[0][0] = ""; }

        // 2) 吃过路兵：兵斜走且目标原本为空（不在斜向吃子分支内）→ 移除被越过的兵
        if (p is "P" or "p" && fr != tr && tc != fc && string.IsNullOrEmpty(captured))
        {
            var capR = tr + (p == "P" ? 1 : -1);   // 被吃兵位于撤退格
            var capPiece = _board[capR][tc];
            if (!string.IsNullOrEmpty(capPiece) && (capPiece == "p" || capPiece == "P"))
            {
                _board[capR][tc] = "";
                captured = capPiece;               // 计入被吃日志
            }
        }

        // 3) 兵升变：白兵冲到底线变皇后，黑兵同理
        if (p == "P" && tr == 0) _board[0][tc] = "Q";
        if (p == "p" && tr == 7) _board[7][tc] = "q";

        // 4) 状态跟踪：王/车移动标记 + 上一步兵双格记录
        if (p == "K") _wkMoved = true;
        if (p == "k") _bkMoved = true;
        if (p == "R")
        {
            if (fr == 7 && fc == 0) _wQmRookMoved = true;
            if (fr == 7 && fc == 7) _wKmRookMoved = true;
        }
        if (p == "r")
        {
            if (fr == 0 && fc == 0) _bQmRookMoved = true;
            if (fr == 0 && fc == 7) _bKmRookMoved = true;
        }
        _lastPawnDouble = (p is "P" or "p") && Math.Abs(tr - fr) == 2 ? (fr, fc, tr, tc) : null;
    }

    private void AddKnight(int r, int c, List<(int, int)> list, bool white)
    {
        foreach (var (dr, dc) in new[] { (1, 2), (1, -2), (-1, 2), (-1, -2), (2, 1), (2, -1), (-2, 1), (-2, -1) })
        {
            var nr = r + dr; var nc = c + dc;
            if (nr < 0 || nr > 7 || nc < 0 || nc > 7) continue;
            var t = _board[nr][nc];
            if (string.IsNullOrEmpty(t) || char.IsUpper(t[0]) != white) list.Add((nr, nc));
        }
    }

    private void AddLine(int r, int c, List<(int, int)> list, (int, int)[] dirs, bool white, int max = 8)
    {
        foreach (var (dr, dc) in dirs)
        {
            for (int s = 1; s <= max; s++)
            {
                var nr = r + dr * s; var nc = c + dc * s;
                if (nr < 0 || nr > 7 || nc < 0 || nc > 7) break;
                var t = _board[nr][nc];
                if (string.IsNullOrEmpty(t)) list.Add((nr, nc));
                else
                {
                    if (char.IsUpper(t[0]) != white) list.Add((nr, nc));
                    break;
                }
            }
        }
    }

    // ==================================================================
    // 中国象棋
    // ==================================================================

    /// <summary>红方路数名（红方视角：最右为「一」，最左为「九」）。</summary>
    private static readonly string[] RedFileNames = { "一", "二", "三", "四", "五", "六", "七", "八", "九" };

    /// <summary>列 → 记谱路数：红方从右侧(8)起「一」；黑方从黑方视角右侧(0)起「1」。</summary>
    private static string FileName(int c, bool red) => red ? RedFileNames[8 - c] : (c + 1).ToString();

    /// <summary>
    /// 中国象棋传统记谱：炮八进五 / 车一进七 / 马二进三 / 相三进五 / 仕四进五 / 兵三平四。
    /// 直线子(车炮帅兵卒)用步数；斜行子(马相仕)用目标路数；同列同名取「前/后」前缀。
    /// 必须在走子修改棋盘前调用，才能正确判定「前/后」。
    /// </summary>
    private string ChineseNotation((int fr, int fc, int tr, int tc) m, string p, bool red)
    {
        // 同列同名子（含自身）计数 + 本子是否居前（前=更靠近对方底线）
        int same = 0;
        bool ahead = false;
        for (int rr = 0; rr < 10; rr++)
        {
            var q = _board[rr][m.fc];
            if (string.IsNullOrEmpty(q) || q != p || IsRedSide(rr, m.fc) != red) continue;
            same++;
            if (red ? rr < m.fr : rr > m.fr) ahead = true;
        }
        string subject = same > 1 ? (ahead ? $"前{p}" : $"后{p}") : p;
        string from = FileName(m.fc, red);
        if (m.tr == m.fr)
            return $"{subject}{from}平{FileName(m.tc, red)}";   // 横移

        bool forward = red ? m.tr < m.fr : m.tr > m.fr;
        string verb = forward ? "进" : "退";
        string arg = p is "马" or "相" or "象" or "仕" or "士"
            ? FileName(m.tc, red)                     // 斜行子记目标路数
            : Math.Abs(m.tr - m.fr).ToString();       // 直线子记步数
        return $"{subject}{from}{verb}{arg}";
    }

    private List<(int r, int c)> LegalChineseChess(int r, int c, string p)
    {
        var list = new List<(int r, int c)>();
        bool red = IsRedSide(r, c);
        switch (p)
        {
            case "帅": case "将":
                // 1 格直线，不出九宫
                foreach (var (dr, dc) in new[] { (0, 1), (0, -1), (1, 0), (-1, 0) })
                {
                    var nr = r + dr; var nc = c + dc;
                    if (nr < 0 || nr >= 10 || nc < 0 || nc > 8) continue;
                    if (nc is < 3 or > 5) continue;
                    if (red && nr < 7 || !red && nr > 2) continue;   // 九宫：红方 7-9 行，黑方 0-2 行
                    AddIfNotOwn(list, nr, nc, red);
                }
                break;
            case "仕": case "士":
                foreach (var (dr, dc) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
                {
                    var nr = r + dr; var nc = c + dc;
                    if (nr < 0 || nr >= 10 || nc < 0 || nc > 8) continue;
                    if (nc is < 3 or > 5) continue;
                    if (red && nr < 7 || !red && nr > 2) continue;
                    AddIfNotOwn(list, nr, nc, red);
                }
                break;
            case "相": case "象":
                foreach (var (dr, dc) in new[] { (2, 2), (2, -2), (-2, 2), (-2, -2) })
                {
                    var nr = r + dr; var nc = c + dc;
                    if (nr < 0 || nr >= 10 || nc < 0 || nc > 8) continue;
                    if (red && nr < 5 || !red && nr > 4) continue;      // 象不过河：红方 5-9 行，黑方 0-4 行
                    if (!string.IsNullOrEmpty(_board[r + dr / 2][c + dc / 2])) continue;   // 塞象眼
                    AddIfNotOwn(list, nr, nc, red);
                }
                break;
            case "马":
                foreach (var (dr, dc) in new[] { (1, 2), (1, -2), (-1, 2), (-1, -2), (2, 1), (2, -1), (-2, 1), (-2, -1) })
                {
                    var nr = r + dr; var nc = c + dc;
                    if (nr < 0 || nr >= 10 || nc < 0 || nc > 8) continue;
                    int legR = dr != 0 ? r + (dr > 0 ? 1 : -1) : r;
                    int legC = dc != 0 ? c + (dc > 0 ? 1 : -1) : c;
                    if (!string.IsNullOrEmpty(_board[legR][legC])) continue;   // 蹩马腿
                    AddIfNotOwn(list, nr, nc, red);
                }
                break;
            case "车":
                foreach (var (dr, dc) in new[] { (0, 1), (0, -1), (1, 0), (-1, 0) })
                {
                    for (int s = 1; ; s++)
                    {
                        var nr = r + dr * s; var nc = c + dc * s;
                        if (nr < 0 || nr >= 10 || nc < 0 || nc > 8) break;
                        var t = _board[nr][nc];
                        if (string.IsNullOrEmpty(t)) list.Add((nr, nc));
                        else { if (IsRedSide(nr, nc) != red) list.Add((nr, nc)); break; }
                    }
                }
                break;
            case "炮":
                foreach (var (dr, dc) in new[] { (0, 1), (0, -1), (1, 0), (-1, 0) })
                {
                    bool seen = false;
                    for (int s = 1; ; s++)
                    {
                        var nr = r + dr * s; var nc = c + dc * s;
                        if (nr < 0 || nr >= 10 || nc < 0 || nc > 8) break;
                        var t = _board[nr][nc];
                        if (!seen)
                        {
                            if (string.IsNullOrEmpty(t)) list.Add((nr, nc));
                            else seen = true;                    // 炮架
                        }
                        else if (!string.IsNullOrEmpty(t))
                        {
                            if (IsRedSide(nr, nc) != red) list.Add((nr, nc));   // 隔山打
                            break;
                        }
                    }
                }
                break;
            case "兵": case "卒":
                {
                    int dir = red ? -1 : 1;
                    var nr = r + dir;
                    if (nr >= 0 && nr < 10) AddIfNotOwn(list, nr, c, red);
                    if (red && r <= 4 || !red && r >= 5)   // 过河可横
                    {
                        foreach (var dc in new[] { 1, -1 })
                        {
                            var nc2 = c + dc;
                            if (nc2 >= 0 && nc2 <= 8) AddIfNotOwn(list, r, nc2, red);
                        }
                    }
                }
                break;
        }
        // 飞将：任何走子后若将帅同列且中间无遮挡 → 步法非法（谁走成照面谁违规）
        list.RemoveAll(m => WouldExposeGenerals(r, c, m.r, m.c));
        return list;
    }

    /// <summary>模拟走 (fr,fc)→(tr,tc)，返回是否会让红帅与黑将同列照面。</summary>
    private bool WouldExposeGenerals(int fr, int fc, int tr, int tc)
    {
        var save = _board[tr][tc];
        _board[tr][tc] = _board[fr][fc];
        _board[fr][fc] = "";
        bool expose = GeneralsFaceEachOther();
        _board[fr][fc] = _board[tr][tc];
        _board[tr][tc] = save;
        return expose;
    }

    /// <summary>红帅与黑将当前是否同列且中间无遮挡（照面）。</summary>
    private bool GeneralsFaceEachOther()
    {
        int rs = -1, rc = -1, bs = -1, bc = -1;
        for (int r = 0; r < 10; r++)
        {
            for (int c = 0; c < 9; c++)
            {
                if (_board[r][c] == "帅") { rs = r; rc = c; }
                if (_board[r][c] == "将") { bs = r; bc = c; }
            }
        }
        if (rs < 0 || bs < 0 || rc != bc) return false;
        for (int r = Math.Min(rs, bs) + 1; r < Math.Max(rs, bs); r++)
            if (!string.IsNullOrEmpty(_board[r][rc])) return false;
        return true;
    }

    private void AddIfNotOwn(List<(int, int)> list, int r, int c, bool red)
    {
        var t = _board[r][c];
        if (string.IsNullOrEmpty(t)) list.Add((r, c));
        else if (IsRedSide(r, c) != red) list.Add((r, c));
    }

    // ==================================================================
    // 斗兽棋
    // ==================================================================

    private static readonly string[] AnimalRank = { "象", "狮", "虎", "豹", "狼", "狗", "猫", "鼠" };
    private static bool IsWaterCell(int r, int c) => (r is 2 or 4) && c is >= 3 and <= 5;

    /// <summary>陷阱格：兽穴相邻三格。位于陷阱内的棋子，任意敌子均可吃（包括更小的）。</summary>
    private static bool IsTrapCell(int r, int c) =>
        (r == 0 && c is 3 or 5) || (r == 1 && c == 4) ||   // 红方(上方)陷阱
        (r == 6 && c is 3 or 5) || (r == 5 && c == 4);      // 蓝方(下方)陷阱

    private List<(int r, int c)> LegalAnimalChess(int r, int c, string p)
    {
        var list = new List<(int, int)>();
        bool isPlayer = _ownerRed is not null && !_ownerRed[r, c];   // 玩家=下方蓝方
        foreach (var (dr, dc) in new[] { (0, 1), (0, -1), (1, 0), (-1, 0) })
        {
            var nr = r + dr; var nc = c + dc;
            if (nr < 0 || nr >= 7 || nc < 0 || nc >= 9) continue;
            // 自己兽穴不能入；对方兽穴可入（进入即获胜，由 CheckEnd 判定）
            if (isPlayer && nr == 6 && nc == 4) continue;    // 玩家(下方)的兽穴在 (6,4)
            if (!isPlayer && nr == 0 && nc == 4) continue;   // AI(上方)的兽穴在 (0,4)
            var t = _board[nr][nc];
            if (string.IsNullOrEmpty(t)) { list.Add((nr, nc)); continue; }
            if (CanAnimalTake(p, t, nr, nc, moverRed: !isPlayer)) list.Add((nr, nc));
        }
        // 狮虎跳河：河区为 rows 2..4（水域 2/4 + 河心 3）。位于一侧(行≤1 或 ≥5)时，
        // 沿同列直跳对岸(行≥5 或 ≤1)，河区该列必须无阻挡（水中只有鼠，鼠会挡路）。
        if (p is "狮" or "虎")
        {
            var candidates = r <= 1 ? new[] { 5, 6 } : new[] { 0, 1 };
            foreach (var targetR in candidates)
            {
                if (targetR == r) continue;
                if (targetR < 0 || targetR >= 7) continue;
                bool clear = true;
                for (int mr = Math.Min(r, targetR) + 1; mr < Math.Max(r, targetR) && clear; mr++)
                    if (!string.IsNullOrEmpty(_board[mr][c])) clear = false;
                if (!clear) continue;
                if (IsWaterCell(targetR, c)) continue;   // 对岸不能是水域
                var t = _board[targetR][c];
                if (string.IsNullOrEmpty(t) || CanAnimalTake(p, t, targetR, c, moverRed: !isPlayer)) list.Add((targetR, c));
            }
        }
        return list;
    }

    /// <summary>斗兽棋吃子判定（moverRed=攻击方是否红方/AI方）：
    /// 陷阱规则——目标在"对方"陷阱内时任意敌子可吃；在"己方"陷阱内受保护不可吃；
    /// 水中——只有鼠能进，水中鼠只能被鼠吃；鼠吃象，象不能吃鼠；可吃同级或低级。</summary>
    private bool CanAnimalTake(string p, string target, int tr, int tc, bool moverRed)
    {
        bool inWater = IsWaterCell(tr, tc);
        if (inWater) return p == "鼠" && target == "鼠";   // 只有鼠能进水；水中鼠只能被鼠吃

        if (IsTrapCell(tr, tc))
        {
            bool targetRed = _ownerRed is not null && _ownerRed[tr, tc];
            bool targetOnOwnTrap = targetRed == moverRed;   // 陷阱属于目标方 → 己方陷阱受保护
            return !targetOnOwnTrap;                         // 对方陷阱：任意敌子可吃
        }
        if (p == "鼠" && target == "象") return true;        // 鼠吃象
        if (p == "象" && target == "鼠") return false;       // 象不能吃鼠
        int pr = Array.IndexOf(AnimalRank, p);
        int pr2 = Array.IndexOf(AnimalRank, target);
        return pr <= pr2;                                    // 高级可吃同级或低级
    }

    // ==================================================================
    // 飞行棋
    // ==================================================================

    /// <summary>掷骰子并自动执行玩家 + AI 回合。返回回合消息。</summary>
    public string RollLudo()
    {
        if (GameOver || !IsLudo) return "游戏未开始";
        var rnd = System.Random.Shared;
        _ludoDie = rnd.Next(1, 7);
        var msg = $"🎲 骰子 {_ludoDie}。";

        msg += MoveLudoPieces(_ludoPlayer, 0, 18, "你") + "\n";
        if (LudoWin(_ludoPlayer)) { GameOver = true; Winner = "你赢了！四子全部回家"; return msg + Winner; }

        _ludoDie = rnd.Next(1, 7);
        msg += $"🎲 骰子 {_ludoDie}。";
        msg += MoveLudoPieces(_ludoAi, 18, 0, "小雨") + "\n";
        if (LudoWin(_ludoAi)) { GameOver = true; Winner = "小雨赢了…"; return msg + Winner; }
        return msg.Trim();
    }

    /// <summary>自动移动一方棋子（起飞优先，其次最前子前进）。</summary>
    private string MoveLudoPieces(List<int> pieces, int start, int enemyStart, string who)
    {
        // 起飞：掷 6 且巢内有子
        if (_ludoDie == 6)
        {
            var home = pieces.FindIndex(x => x == -1);
            if (home >= 0)
            {
                pieces[home] = start;
                return $"{who} 从巢起飞 🛫";
            }
        }
        // 有子在环上：走最靠前且不会浪费的子
        var moves = pieces.Where(x => x >= 0 && x < LudoRing + LudoFinish)
            .Select(x => x)
            .OrderByDescending(x => x)
            .ToList();
        foreach (var pos in moves)
        {
            var newPos = pos + _ludoDie;
            if (newPos >= LudoRing + LudoFinish) continue;   // 超出终点放弃
            if (newPos >= LudoRing) newPos = newPos;          // 进入终点通道（40..43）
            var idx = pieces.IndexOf(pos);
            pieces[idx] = newPos;
            // 踩子：环上 40 格内踩到对方
            if (newPos < LudoRing)
            {
                for (int i = 0; i < _ludoPlayer.Count; i++)
                {
                    if (_ludoAi[i] == newPos) { _ludoAi[i] = -1; return $"{who} 前进到 {PosLabel(newPos)}，击落了小雨的棋子！"; }
                    if (_ludoPlayer[i] == newPos) { _ludoPlayer[i] = -1; return $"{who} 前进到 {PosLabel(newPos)}，击落了你的棋子！"; }
                }
            }
            return $"{who} 前进到 {PosLabel(newPos)}";
        }
        return $"{who} 没有棋子可走";
    }

    private static string PosLabel(int pos) =>
        pos >= LudoRing ? $"终点通道 {pos - LudoRing + 1}/4" : $"第 {pos + 1} 格";

    private static bool LudoWin(List<int> pieces) => pieces.All(x => x >= LudoRing);

    /// <summary>飞行棋渲染：返回主环 40 格 (r,c,idx) 坐标序列（外圈 32 + 内圈 8）。</summary>
    public List<(int r, int c, int idx)> LudoCells()
    {
        var list = new List<(int, int, int)>();
        // 外圈 32 格顺时针
        for (int c = 0; c < 9; c++) list.Add((0, c, list.Count));
        for (int r = 1; r < 9; r++) list.Add((r, 8, list.Count));
        for (int c = 7; c >= 0; c--) list.Add((8, c, list.Count));
        for (int r = 7; r >= 1; r--) list.Add((r, 0, list.Count));
        // 内圈 8 格（承接外圈，凑满 40）
        for (int c = 1; c <= 7 && list.Count < 40; c++) list.Add((1, c, list.Count));
        if (list.Count < 40) list.Add((2, 7, list.Count));
        return list;
    }

    /// <summary>玩家飞行棋 4 子位置（-1=巢，0..39=主环，40..43=终点通道）。</summary>
    public IReadOnlyList<int> LudoPlayerPositions => _ludoPlayer;
    /// <summary>AI 飞行棋 4 子位置。</summary>
    public IReadOnlyList<int> LudoAiPositions => _ludoAi;

    /// <summary>终点通道格子 → 棋盘中央 (r,c) 渲染位。</summary>
    public (int r, int c) LudoFinishCell(int pos)
    {
        var off = pos - LudoRing;   // 0..3
        return (4 + off / 2, 4 + off % 2);
    }

    // ==================================================================
    // 贪吃蛇
    // ==================================================================

    /// <summary>设置蛇方向（不允许直接掉头）。</summary>
    public void SetSnakeDirection(int dr, int dc)
    {
        if (_snakeDir == (-dr, -dc)) return;   // 不能掉头
        _snakeDir = (dr, dc);
    }

    private void SpawnFood()
    {
        var free = new List<(int, int)>();
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                if (!_snake.Contains((r, c))) free.Add((r, c));
        if (free.Count == 0) { GameOver = true; Winner = "平局：场地已满"; return; }
        _food = free[System.Random.Shared.Next(free.Count)];
    }

    private void PaintSnake()
    {
        _board = Empty(Rows, Cols);
        _board[_food.r][_food.c] = "🍎";
        for (int i = 0; i < _snake.Count; i++)
        {
            var (r, c) = _snake[i];
            _board[r][c] = i == 0 ? "●" : "○";
        }
    }

    /// <summary>蛇前进一步。返回回合消息（撞墙/撞自身/吃食/正常）。</summary>
    public string StepSnake()
    {
        if (GameOver || !IsSnake) return "";
        var head = _snake[0];
        var nr = head.r + _snakeDir.dr;
        var nc = head.c + _snakeDir.dc;
        if (nr < 0 || nr >= Rows || nc < 0 || nc >= Cols)
        {
            GameOver = true; Winner = "撞墙了…游戏结束";
            return Winner;
        }
        if (_snake.Contains((nr, nc)))
        {
            GameOver = true; Winner = $"咬到尾巴了…游戏结束，长度 {_snakeScore + 3}";
            return Winner;
        }
        _snake.Insert(0, (nr, nc));
        if ((nr, nc) == _food)
        {
            _snakeScore++;
            SpawnFood();
            _moveHistory.Add($"吃果子，长度 {_snakeScore + 3}");
        }
        else
        {
            _snake.RemoveAt(_snake.Count - 1);
        }
        PaintSnake();
        return "";
    }

    // ==================================================================
    // AI 与胜负
    // ==================================================================

    /// <summary>AI 走一步（国象/中象/斗兽）。走子后立即返回本地回合消息（绝不等待网络，杜绝掉帧卡顿）。</summary>
    private async Task<string> AiMoveAsync()
    {
        var (fr, fc, tr, tc) = AiPickMove();
        if (fr < 0) { GameOver = true; Winner = "对方没有可走的棋，你赢了！"; return Winner; }
        var p = _board[fr][fc];
        var captured = _board[tr][tc];
        // 记谱：中国象棋 AI(黑方) 用传统记谱法（改棋盘前计算「前/后」）
        string note = _currentGame == GameKind.ChineseChess && _ownerRed is not null
            ? ChineseNotation((fr, fc, tr, tc), p, _ownerRed[fr, fc])
            : "";
        _board[tr][tc] = p;
        _board[fr][fc] = "";
        if (_currentGame == GameKind.Chess) ApplyChessSideEffects(fr, fc, tr, tc, p, ref captured);
        if (_ownerRed is not null && tr < _ownerRed.GetLength(0) && tc < _ownerRed.GetLength(1)
            && fr < _ownerRed.GetLength(0) && fc < _ownerRed.GetLength(1))
        {
            _ownerRed[tr, tc] = _ownerRed[fr, fc];
            _ownerRed[fr, fc] = false;
        }
        _moveHistory.Add(string.IsNullOrEmpty(note)
            ? $"AI {(fr + 1)},{fc + 1}→{(tr + 1)},{tc + 1}"
            : $"小雨 {note}{(string.IsNullOrEmpty(captured) ? "" : " 吃" + ChineseLabel(captured, tr, tc))}");

        var end = CheckEnd();
        if (end is not null) { GameOver = true; Winner = end; return end; }

        _playerTurn = true;
        var local = LocalAiQuip(captured);
        // 节流：每 3 回合异步触发一次云端俏皮话，fire-and-forget 绝不阻塞棋局，失败静默
        if (++_aiMoveCount % 3 == 0)
            _ = QuipAsync(captured);
        return local;
    }

    /// <summary>本地回合话术池：吃子 / 将军 / 普通落子，微秒级返回。</summary>
    private string LocalAiQuip(string captured)
    {
        var name = GameName(_currentGame);
        if (!string.IsNullOrEmpty(captured))
            return _quips[_rnd.Next(_quips.Length)]; // 吃子话术池
        if (name is "国际象棋" or "中国象棋" && IsCheckAfterMove())
            return "将军！看你怎么办~";
        return _cheers[_rnd.Next(_cheers.Length)];
    }

    /// <summary>AI 选一步：优先采用云端棋力建议（若可用），否则按难度本地决策；随后后台预热下一步建议。</summary>
    private (int fr, int fc, int tr, int tc) AiPickMove()
    {
        // 1) 云端建议（上回合后台预热的合法走子）→ 直接采用
        if (_brainAdvice is { } adv && IsBrainMoveLegal(adv))
        {
            _brainAdvice = null;
            WarmBrainAsync();
            return adv;
        }
        _brainAdvice = null;

        // 2) 本地决策（Easy/Normal/Hard）
        var local = LocalPickMove();

        // 3) 后台预热云端下一步建议（不阻塞，失败静默）
        if (Brain is { Enabled: true })
            WarmBrainAsync();
        return local;
    }

    /// <summary>后台预热云端下一步建议（fire-and-forget）。走子采用云端建议后调用，使下一回合也有云端建议可用。</summary>
    private void WarmBrainAsync()
    {
        if (Brain is not { Enabled: true }) return;
        var candidates = CollectLegalMoves();
        if (candidates.Count == 0) return;
        var boardText = BoardText();
        var kind = _currentGame;
        var list = candidates;
        _ = Task.Run(async () =>
        {
            var adv = await Brain.SuggestBestAsync(kind, boardText, list);
            if (adv is not null) _brainAdvice = adv;
        });
    }

    /// <summary>当前可用的全部 AI 走子（含吃子评分，供云端择优）。</summary>
    private List<(int fr, int fc, int tr, int tc)> CollectLegalMoves()
    {
        var list = new List<(int fr, int fc, int tr, int tc)>();
        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Cols; c++)
            {
                var p = _board[r][c];
                if (string.IsNullOrEmpty(p)) continue;
                if (_currentGame == GameKind.Chess && !char.IsUpper(p[0])) continue;
                if (_currentGame == GameKind.ChineseChess && IsRedSide(r, c)) continue;   // AI=黑方
                if (_currentGame == GameKind.AnimalChess && !IsRedSide(r, c)) continue;   // AI=上方红方
                foreach (var (tr, tc) in LegalMoves(r, c))
                    list.Add((r, c, tr, tc));
            }
        }
        return list;
    }

    /// <summary>云端建议合法性校验：起点/终点在界内，走法确实在 AI 合法走子清单中。</summary>
    private bool IsBrainMoveLegal((int fr, int fc, int tr, int tc) m)
    {
        if (m.fr < 0 || m.fc < 0 || m.tr < 0 || m.tc < 0) return false;
        if (m.fr >= Rows || m.fc >= Cols || m.tr >= Rows || m.tc >= Cols) return false;
        return CollectLegalMoves().Contains(m);
    }

    /// <summary>棋盘文本快照（供云端模型推理用）。</summary>
    private string BoardText()
    {
        var sb = new StringBuilder();
        for (int r = 0; r < Rows; r++)
        {
            sb.AppendLine(string.Join(" ", _board[r].Select(x => string.IsNullOrEmpty(x) ? "." : x)));
        }
        return sb.ToString();
    }

    /// <summary>本地难度决策（Easy 随机 / Normal 贪吃 / Hard 前瞻防反）。</summary>
    private (int fr, int fc, int tr, int tc) LocalPickMove()
    {
        if (Difficulty == AiDifficulty.Easy)
        {
            var legal = new List<(int r, int c, int tr, int tc)>();
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                {
                    var p = _board[r][c];
                    if (string.IsNullOrEmpty(p)) continue;
                    if (_currentGame == GameKind.Chess && !char.IsUpper(p[0])) continue;
                    if (_currentGame == GameKind.ChineseChess && IsRedSide(r, c)) continue;
                    if (_currentGame == GameKind.AnimalChess && !IsRedSide(r, c)) continue;
                    foreach (var (tr, tc) in LegalMoves(r, c))
                        legal.Add((r, c, tr, tc));
                }
            if (legal.Count == 0) return (-1, -1, -1, -1);
            var rnd = legal[_rnd.Next(legal.Count)];
            return (rnd.r, rnd.c, rnd.tr, rnd.tc);
        }

        var candidates = new List<(int fr, int fc, int tr, int tc, int score)>();
        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Cols; c++)
            {
                var p = _board[r][c];
                if (string.IsNullOrEmpty(p)) continue;
                if (_currentGame == GameKind.Chess && !char.IsUpper(p[0])) continue;      // AI=黑
                if (_currentGame == GameKind.ChineseChess && IsRedSide(r, c)) continue;
                if (_currentGame == GameKind.AnimalChess && !IsRedSide(r, c)) continue;   // AI=上方红方
                foreach (var (tr, tc) in LegalMoves(r, c))
                {
                    var t = _board[tr][tc];
                    var gain = string.IsNullOrEmpty(t) ? 0 : ValueOf(t);
                    candidates.Add((r, c, tr, tc, gain));
                }
            }
        }
        if (candidates.Count == 0) return (-1, -1, -1, -1);

        // Hard：扣除"落子后会被对手吃掉的己方子价值"（1 步前瞻防反）
        if (Difficulty == AiDifficulty.Hard)
        {
            var best = (-1, -1, -1, -1); var bestScore = int.MinValue;
            foreach (var m in candidates)
            {
                var moveScore = m.score - RecaptureRisk(m);
                if (moveScore > bestScore) { bestScore = moveScore; best = (m.fr, m.fc, m.tr, m.tc); }
            }
            return best;
        }

        var greedy = candidates.OrderByDescending(x => x.score).ThenBy(_ => _rnd.Next()).First();
        return (greedy.fr, greedy.fc, greedy.tr, greedy.tc);
    }

    /// <summary>Hard 前瞻：模拟落子后，该子是否会被玩家下一步吃掉；会则扣掉其价值。</summary>
    private int RecaptureRisk((int fr, int fc, int tr, int tc, int gain) m)
    {
        try
        {
            var save = _board[m.tr][m.tc];
            var mover = _board[m.fr][m.fc];
            _board[m.tr][m.tc] = mover;
            _board[m.fr][m.fc] = "";
            int risk = 0;
            // 玩家(=红/白/下方蓝) 是否能吃到刚落下的子
            for (int r = 0; r < Rows; r++)
            {
                for (int c = 0; c < Cols; c++)
                {
                    var p = _board[r][c];
                    if (string.IsNullOrEmpty(p)) continue;
                    if (_currentGame == GameKind.Chess && !char.IsLower(p[0])) continue;      // 玩家=白
                    if (_currentGame == GameKind.ChineseChess && !IsRedSide(r, c)) continue;    // 玩家=红
                    if (_currentGame == GameKind.AnimalChess && IsRedSide(r, c)) continue;       // 玩家=下方蓝方
                    foreach (var (tr, tc) in LegalMoves(r, c))
                        if (tr == m.tr && tc == m.tc) { risk = ValueOf(mover); r = Rows; break; } // 能被反吃
                }
            }
            _board[m.fr][m.fc] = mover;
            _board[m.tr][m.tc] = save;
            return risk;
        }
        catch { return 0; }
    }

    private static int ValueOf(string p) => p.ToLowerInvariant() switch
    {
        "p" => 1, "n" or "b" => 3, "r" => 5, "q" => 9, "k" => 100,
        "将" or "帅" => 1000, "车" => 90, "炮" => 45, "马" => 40, "象" or "相" => 20, "士" or "仕" => 20, "兵" or "卒" => 10,
        _ => AnimalRank.Contains(p) ? (10 - Array.IndexOf(AnimalRank, p)) : 1
    };

    /// <summary>终局判定。返回胜者文本，未终局返回 null。</summary>
    private string? CheckEnd()
    {
        switch (_currentGame)
        {
            case GameKind.Chess:
                {
                    // 王安全过滤已禁止送王/吃王 → 用将死/逼和判定终局
                    int wr = -1, wc = -1, br = -1, bc = -1;
                    for (int r = 0; r < 8; r++)
                        for (int c = 0; c < 8; c++)
                        {
                            if (_board[r][c] == "K") { wr = r; wc = c; }
                            if (_board[r][c] == "k") { br = r; bc = c; }
                        }
                    if (wr < 0) return "小雨赢了（黑棋吃王）";
                    if (br < 0) return "你赢了！白棋吃王";
                    bool whiteMovedLast = _playerTurn;   // 玩家=白，其走子后 _playerTurn 仍为 true
                    if (whiteMovedLast)
                    {
                        bool checkBlack = IsSquareAttacked(br, bc, byWhite: true);
                        bool blackHasMove = HasAnyLegalMove(sideWhite: false);
                        if (checkBlack && !blackHasMove) return "你赢了！将死（Checkmate）";
                        if (!checkBlack && !blackHasMove) return "逼和（Stalemate），平局";
                    }
                    else
                    {
                        bool checkWhite = IsSquareAttacked(wr, wc, byWhite: false);
                        bool whiteHasMove = HasAnyLegalMove(sideWhite: true);
                        if (checkWhite && !whiteHasMove) return "小雨赢了…将死你";
                        if (!checkWhite && !whiteHasMove) return "逼和（Stalemate），平局";
                    }
                    break;
                }
            case GameKind.ChineseChess:
                {
                    bool rs = false, bs = false;
                    for (int r = 0; r < 10; r++)
                        for (int c = 0; c < 9; c++)
                        {
                            if (_board[r][c] == "帅") rs = true;
                            if (_board[r][c] == "将") bs = true;
                        }
                    if (!rs) return "小雨赢了（黑方擒帅）";
                    if (!bs) return "你赢了！红方捉将";
                    // 困毙：轮到谁走而谁无子可走即判负（与国象逼和不同，中国象棋无子可走=输）
                    bool blackToMove = _playerTurn;   // 玩家(红)刚走完 → 黑方(对方)无子可走则黑负
                    if (blackToMove && !HasAnyLegalChineseMove(redSide: false)) return "你赢了！困毙对方";
                    if (!blackToMove && !HasAnyLegalChineseMove(redSide: true)) return "小雨赢了…困毙你";
                    break;
                }
            case GameKind.AnimalChess:
                {
                    // 入对方兽穴即胜：(6,4)=玩家的兽穴（AI 进入则 AI 胜），(0,4)=AI 的兽穴（玩家进入则玩家胜）
                    if (!string.IsNullOrEmpty(_board[0][4])) return "你赢了！攻入红方兽穴";
                    if (!string.IsNullOrEmpty(_board[6][4])) return "小雨赢了…攻入你的兽穴";
                    bool red = false, blue = false;
                    for (int r = 0; r < 7; r++)
                        for (int c = 0; c < 9; c++)
                        {
                            var t = _board[r][c];
                            if (string.IsNullOrEmpty(t)) continue;
                            if (IsRedSide(r, c)) red = true; else blue = true;
                        }
                    if (!red) return "你赢了！吃光对方棋子";
                    if (!blue) return "小雨赢了…吃光你的棋子";
                    break;
                }
        }
        return null;
    }

    /// <summary>中国象棋：redSide 方是否存在任何合法走子（困毙判定用）。</summary>
    private bool HasAnyLegalChineseMove(bool redSide)
    {
        for (int r = 0; r < 10; r++)
        {
            for (int c = 0; c < 9; c++)
            {
                var q = _board[r][c];
                if (string.IsNullOrEmpty(q)) continue;
                if (IsRedSide(r, c) != redSide) continue;
                if (LegalChineseChess(r, c, q).Count > 0) return true;
            }
        }
        return false;
    }

    /// <summary>国际象棋：sideWhite 方是否存在任何合法走子。</summary>
    private bool HasAnyLegalMove(bool sideWhite)
    {
        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                var q = _board[r][c];
                if (string.IsNullOrEmpty(q) || char.IsUpper(q[0]) != sideWhite) continue;
                if (LegalChess(r, c, q).Count > 0) return true;
            }
        }
        return false;
    }

    public async Task<string> AiConcede()
    {
        var reply = await TryAi($"{ScenarioContext()}我认输！你赢了，说句俏皮话吧。");
        GameOver = true; Winner = "你赢了（对方认输）";
        _moveHistory.Add($"AI(认输): {reply}");
        OnGameEvent?.Invoke("end", reply);
        return reply;
    }

    public async Task<string> AiTaunt()
    {
        var reply = await TryAi($"{ScenarioContext()}（棋局进行中）说一句嘲讽或挑衅的话，结合当前局面。");
        return reply;
    }

    /// <summary>普通闲聊（游戏语音输入回退聊天用）。闲聊话题结合当前棋局状态与局势。</summary>
    public async Task<string> ChatCasual(string text)
    {
        var reply = await TryAi($"{ScenarioContext()}（玩家在{GameName(_currentGame)}对局中说：{text}）结合当前局面自然回应，简短。");
        return reply;
    }

    /// <summary>对局上下文摘要：游戏名 + 轮到谁 + 子力对比 + 最近几步。供闲聊/吐槽/认输 prompt 注入。</summary>
    private string ScenarioContext()
    {
        if (GameOver) return $"（{GameName(_currentGame)}对局已结束，{Winner}）";
        var stage = PlayerTurn ? "轮到你了" : "小雨思考中";
        var moves = string.Join("；", _moveHistory.TakeLast(5));
        var power = "";
        try
        {
            if (_currentGame is GameKind.Chess or GameKind.ChineseChess or GameKind.AnimalChess)
            {
                int ai = 0, me = 0;
                for (int r = 0; r < Rows; r++)
                    for (int c = 0; c < Cols; c++)
                    {
                        var q = _board[r][c];
                        if (string.IsNullOrEmpty(q)) continue;
                        var v = ValueOf(q);
                        bool aiPiece = _currentGame == GameKind.Chess ? char.IsLower(q[0]) && char.IsLetter(q[0])
                            : _currentGame == GameKind.ChineseChess ? !IsRedSide(r, c)
                            : IsRedSide(r, c);
                        if (aiPiece) ai += v; else me += v;
                    }
                power = ai > me ? "小雨子力占优" : me > ai ? "你的子力占优" : "双方子力相当";
            }
        }
        catch { }
        return $"（{GameName(_currentGame)}：{stage}，{power}。最近：{moves}。）";
    }

    private async Task<string> TryAi(string prompt)
    {
        try { return await SendAiWithTimeout(prompt); }
        catch { return "…"; }
    }

    private static string GameName(GameKind k) => k switch
    {
        GameKind.Gobang => "五子棋",
        GameKind.AnimalChess => "斗兽棋",
        GameKind.Ludo => "飞行棋",
        GameKind.Chess => "国际象棋",
        GameKind.ChineseChess => "中国象棋",
        GameKind.Snake => "贪吃蛇",
        GameKind.Go => "围棋",
        _ => "游戏"
    };

    /// <summary>棋子显示名（日志/消息用）。</summary>
    private static string ChineseLabel(string p, int r, int c)
    {
        if (p is "●" or "○" or "🍎") return p;
        if (char.IsLetter(p[0])) return char.ToUpperInvariant(p[0]).ToString();
        return p;
    }
}