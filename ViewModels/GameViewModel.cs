using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Dispatching;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.GameModule;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

[QueryProperty(nameof(GameKindParam), "kind")]
public sealed partial class GameViewModel : ObservableObject
{
    private readonly MiniGameEngine _game;
    private readonly SpeechService _speech;
    private readonly SettingsManager _settings;
    private readonly Modules.Market.ShopService? _shop;
    private bool _started;
    private bool _recorded;
    private int _selR = -1, _selC = -1;                    // 已选中的己方棋子
    private List<(int r, int c)> _legalTargets = new();     // 选中棋子的合法落点
    private IDispatcherTimer? _snakeTimer;
    private readonly Modules.GameModule.GameSkillTracker? _skill;
    private DateTime _lastDifficultyRefresh = DateTime.MinValue;   // LLM 难度精调节流

    [ObservableProperty] private string _gameKindParam = "";
    [ObservableProperty] private string _gameTitle = "";
    [ObservableProperty] private string _boardDisplay = "";
    [ObservableProperty] private bool _inGame;
    [ObservableProperty] private bool _isListening;
    [ObservableProperty] private int _affection = 50;
    [ObservableProperty] private int _boardRows;
    [ObservableProperty] private int _boardCols;
    [ObservableProperty] private double _cellSize = 36;
    [ObservableProperty] private double _boardPixelWidth;
    [ObservableProperty] private List<BoardCell> _boardCells = new();
    [ObservableProperty] private ObservableCollection<ChatBubble> _messages = new();
    [ObservableProperty] private string _chatInput = "";
    [ObservableProperty] private bool _isChineseChess;
    [ObservableProperty] private bool _isLudo;
    [ObservableProperty] private bool _isSnake;
    [ObservableProperty] private bool _isGo;
    [ObservableProperty] private int _ludoDie;
    [ObservableProperty] private int _snakeScore;

    /// <summary>当前游戏类型（渲染层判断线制/格制棋盘用）。</summary>
    public MiniGameEngine.GameKind CurrentGameKind => _game.CurrentGame;

    /// <summary>是否已终局（页面动画用）。</summary>
    public bool GameOverFlag => _game.GameOver;

    public GameViewModel(MiniGameEngine game, SpeechService speech, SettingsManager settings,
        Modules.GameModule.ChessBrainService? brain = null, Modules.Market.ShopService? shop = null,
        Modules.GameModule.GameSkillTracker? skill = null)
    {
        _game = game;
        _speech = speech;
        _settings = settings;
        _shop = shop;
        _skill = skill;
        _game.Brain = brain;
        // AI 异步问候从引擎事件送达（棋盘渲染不再被网络等待阻塞）
        _game.OnGameEvent += OnEngineEvent;
    }

    /// <summary>难度聪明度：手动档位；AI 自动开启时先按本地熟练度/认真度规则快速定档（不阻塞开局），
    /// 随后 LLM 以「世界书+记忆+表现」后台精调（对局中仍可换难度），全程不阻塞棋盘。</summary>
    private void ApplyDifficulty(MiniGameEngine.GameKind kind)
    {
        try
        {
            var s = _settings.Current;
            // 难度档位只对国象/中象/斗兽棋的 AI 有实际影响
            bool usesDifficulty = kind is MiniGameEngine.GameKind.Chess
                or MiniGameEngine.GameKind.ChineseChess or MiniGameEngine.GameKind.AnimalChess;
            if (!s.AiAutoDifficulty || _skill is null || !usesDifficulty)
            {
                _game.Difficulty = s.GameDifficulty switch
                {
                    "easy" => MiniGameEngine.AiDifficulty.Easy,
                    "hard" => MiniGameEngine.AiDifficulty.Hard,
                    _ => MiniGameEngine.AiDifficulty.Normal
                };
                return;
            }
            // 自动档：先用本地经验规则立即生效，再异步等 LLM 精调（若云端不可用则维持本地结果）
            _ = RefreshDifficultyAsync(force: true);
        }
        catch (Exception ex)
        {
            App.WriteLog($"GameViewModel.ApplyDifficulty -> {ex.Message}");
        }
    }

    /// <summary>后台刷新难度（LLM 决策 + 本地规则兜底）。fire-and-forget，绝不阻塞棋盘交互。</summary>
    private async Task RefreshDifficultyAsync(bool force)
    {
        if (_skill is null || !InGame) return;
        var now = DateTime.UtcNow;
        if (!force && (now - _lastDifficultyRefresh).TotalSeconds < 20) return;  // 节流：20 秒内不重复问 LLM
        if (!force && _game.MoveCount < 4) return;                               // 开局前几步不折腾
        _lastDifficultyRefresh = now;
        try
        {
            var name = GameTitle;
            var before = _game.Difficulty;
            // LLM 精调：中途换难度（对局中改变 Difficulty 属性即对下一步 AI 走子生效）
            var decided = await _skill.DecideDifficultyAsync(
                string.IsNullOrEmpty(name) ? GameNameOf(_game.CurrentGame) : name,
                _game.MoveCount);
            if (decided == before || _game.GameOver) return;
            _game.Difficulty = decided;
            AddBubble($"（小雨悄悄调整了状态：{_game.DifficultyLabel}）", isAi: true);
        }
        catch (Exception ex)
        {
            App.WriteLog($"GameViewModel.RefreshDifficultyAsync -> {ex.Message}");
        }
    }

    private static string GameNameOf(MiniGameEngine.GameKind k) => k switch
    {
        MiniGameEngine.GameKind.Gobang => "五子棋",
        MiniGameEngine.GameKind.AnimalChess => "斗兽棋",
        MiniGameEngine.GameKind.Ludo => "飞行棋",
        MiniGameEngine.GameKind.Chess => "国际象棋",
        MiniGameEngine.GameKind.ChineseChess => "中国象棋",
        MiniGameEngine.GameKind.Snake => "贪吃蛇",
        MiniGameEngine.GameKind.Go => "围棋",
        _ => "游戏"
    };

    partial void OnGameKindParamChanged(string value)
    {
        if (!string.IsNullOrEmpty(value) && !_started)
        {
            _started = true;
            var kind = value.ToLower() switch
            {
                "gobang" => MiniGameEngine.GameKind.Gobang,
                "animalchess" => MiniGameEngine.GameKind.AnimalChess,
                "ludo" => MiniGameEngine.GameKind.Ludo,
                "chess" => MiniGameEngine.GameKind.Chess,
                "chinesechess" => MiniGameEngine.GameKind.ChineseChess,
                "snake" => MiniGameEngine.GameKind.Snake,
                "go" => MiniGameEngine.GameKind.Go,
                _ => MiniGameEngine.GameKind.Gobang
            };
            MainThread.BeginInvokeOnMainThread(async () => await Start(kind));
        }
    }

    private async Task Start(MiniGameEngine.GameKind kind)
    {
        try
        {
            StopSnakeTimer();
            InGame = true;
            Messages.Clear();
            _recorded = false;
            _selR = -1; _selC = -1; _legalTargets.Clear();

            GameTitle = kind switch
            {
                MiniGameEngine.GameKind.Gobang => "五子棋",
                MiniGameEngine.GameKind.AnimalChess => "斗兽棋",
                MiniGameEngine.GameKind.Ludo => "飞行棋",
                MiniGameEngine.GameKind.Chess => "国际象棋",
                MiniGameEngine.GameKind.ChineseChess => "中国象棋",
                MiniGameEngine.GameKind.Snake => "贪吃蛇",
                MiniGameEngine.GameKind.Go => "围棋",
                _ => "游戏"
            };
            IsChineseChess = kind == MiniGameEngine.GameKind.ChineseChess;
            IsLudo = kind == MiniGameEngine.GameKind.Ludo;
            IsSnake = kind == MiniGameEngine.GameKind.Snake;
            IsGo = kind == MiniGameEngine.GameKind.Go;
            LudoDie = 0;
            SnakeScore = 0;

            // 难度聪明度：手动档位 + AI 自动调节（熟练度/认真度 → LLM 难度决策）
            ApplyDifficulty(kind);

            BoardRows = _game.Rows;
            BoardCols = _game.Cols;
            CellSize = kind switch
            {
                MiniGameEngine.GameKind.Gobang => 30.0,
                MiniGameEngine.GameKind.AnimalChess => 34.0,
                MiniGameEngine.GameKind.Ludo => 36.0,
                MiniGameEngine.GameKind.Chess => 38.0,
                MiniGameEngine.GameKind.ChineseChess => 44.0,
                MiniGameEngine.GameKind.Snake => 30.0,
                MiniGameEngine.GameKind.Go => 22.0,
                _ => 34.0
            };
            BoardPixelWidth = CellSize * BoardCols;

            // 同步开局：立即渲染棋盘，不等待任何网络调用
            var startMsg = _game.StartGame(kind, "小雨");
            BoardRows = _game.Rows;
            BoardCols = _game.Cols;
            BoardPixelWidth = CellSize * BoardCols;
            SyncBoardFromGame();
            AddBubble(startMsg, isAi: true);

            if (IsSnake) StartSnakeTimer();
        }
        catch (Exception ex)
        {
            App.WriteLog("GameViewModel.Start -> " + ex);
            InGame = false;
            AddBubble($"游戏初始化失败: {ex.Message}", isAi: true);
        }
    }

    // ==================================================================
    // 棋盘渲染
    // ==================================================================

    private void SyncBoardFromGame()
    {
        var cells = new List<BoardCell>();
        if (IsLudo) BuildLudoCells(cells);
        else if (IsSnake) BuildSnakeCells(cells);
        else BuildNormalCells(cells);
        BoardCells = cells;

        if (_game.GameOver) BoardDisplay = _game.Winner;
        else if (IsSnake) BoardDisplay = $"得分 {_game.SnakeScore}";
        else if (IsLudo) BoardDisplay = LudoDie > 0 ? $"骰子 {LudoDie} · 掷出 6 可起飞" : "点击掷骰子开始";
        else BoardDisplay = _game.PlayerTurn ? "轮到你走" : "小雨思考中…";

        TryRecordGame();
    }

    /// <summary>终局后一次性记录战绩并发放亲密币奖励（防重复记账）。</summary>
    private void TryRecordGame()
    {
        if (!_game.GameOver || _recorded || !InGame) return;
        _recorded = true;
        var w = _game.Winner;
        // 胜：不是"小雨赢…"、不是平局消息
        bool won = !string.IsNullOrEmpty(w)
                   && !w.StartsWith("小雨", StringComparison.Ordinal)
                   && !w.Contains("小雨赢", StringComparison.Ordinal)
                   && !w.StartsWith("逼和", StringComparison.Ordinal)
                   && !w.StartsWith("平局", StringComparison.Ordinal);
        var name = GameTitle;
        if (!string.IsNullOrEmpty(name) && _shop is not null)
        {
            var moves = _game.MoveCount;
            _ = _shop.AddGameRecordAsync(name, won, moves, w);
        }
        // 熟练度：胜 +2 / 负 +1，刷新最近游玩时间（长时间不玩会衰减）
        if (_skill is not null && !string.IsNullOrEmpty(name))
            _ = _skill.RecordGameAsync(name, won);
    }

    private void BuildNormalCells(List<BoardCell> cells)
    {
        var kind = _game.CurrentGame;
        bool isCC = IsChineseChess;
        bool isAnimal = kind == MiniGameEngine.GameKind.AnimalChess;
        for (int r = 0; r < BoardRows; r++)
        {
            for (int c = 0; c < BoardCols; c++)
            {
                var p = _game.Piece(r, c);
                var sel = _selR == r && _selC == c;
                var legal = _legalTargets.Contains((r, c));
                var cell = new BoardCell
                {
                    Row = r,
                    Col = c,
                    BgColor = BgOf(kind, r, c),
                    IsRed = (isCC || isAnimal) && _game.IsRedSide(r, c),
                    DiagChar = isCC ? DiagOf(kind, r, c) : "",
                    IsHighlighted = sel || legal,
                    PieceTint = kind == MiniGameEngine.GameKind.Chess && !string.IsNullOrEmpty(p)
                        ? (char.IsUpper(p[0]) ? Color.FromArgb("#6D4C41") : Color.FromArgb("#2B1D10"))
                        : null
                };
                cell.Piece = kind == MiniGameEngine.GameKind.Chess && !string.IsNullOrEmpty(p)
                    ? ChessGlyph(p[0])
                    : p;
                cells.Add(cell);
            }
        }
    }

    private void BuildSnakeCells(List<BoardCell> cells)
    {
        for (int r = 0; r < BoardRows; r++)
        {
            for (int c = 0; c < BoardCols; c++)
            {
                var p = _game.Piece(r, c);
                var cell = new BoardCell
                {
                    Row = r,
                    Col = c,
                    BgColor = (r + c) % 2 == 0 ? Color.FromArgb("#F5EFE0") : Color.FromArgb("#EDDFBE"),
                    PieceTint = p switch
                    {
                        "🍎" => Color.FromArgb("#E53935"),
                        "●" => Color.FromArgb("#2E7D32"),
                        "○" => Color.FromArgb("#A5D6A7"),
                        _ => null
                    }
                };
                cell.Piece = string.IsNullOrEmpty(p) ? null : p;
                cells.Add(cell);
            }
        }
    }

    private void BuildLudoCells(List<BoardCell> cells)
    {
        var ring = _game.LudoCells();
        var ringPos = new Dictionary<(int, int), int>();
        foreach (var (r, c, idx) in ring) ringPos[(r, c)] = idx;

        // 玩家红 / AI 蓝 子分布
        var occ = new Dictionary<(int, int), (char ch, Color tint)>();
        foreach (var p in _game.LudoPlayerPositions)
        {
            (int r, int c) pos;
            if (p >= 0 && p < 40) pos = (ring[p].r, ring[p].c);
            else if (p >= 40) pos = _game.LudoFinishCell(p);
            else continue;
            occ[pos] = ('●', Color.FromArgb("#D32F2F"));
        }
        foreach (var p in _game.LudoAiPositions)
        {
            (int r, int c) pos;
            if (p >= 0 && p < 40) pos = (ring[p].r, ring[p].c);
            else if (p >= 40) pos = _game.LudoFinishCell(p);
            else continue;
            occ[pos] = ('●', Color.FromArgb("#1976D2"));
        }

        int playerStart = 0, aiStart = 18;
        for (int r = 0; r < BoardRows; r++)
        {
            for (int c = 0; c < BoardCols; c++)
            {
                bool isRing = ringPos.ContainsKey((r, c));
                bool isFinish = (r is 4 or 5) && (c is 4 or 5);
                var cell = new BoardCell
                {
                    Row = r,
                    Col = c,
                    BgColor = isFinish ? Color.FromArgb("#FFE082")
                        : ringPos.ContainsKey((r, c)) && ringPos[(r, c)] == playerStart ? Color.FromArgb("#FFCDD2")
                        : ringPos.ContainsKey((r, c)) && ringPos[(r, c)] == aiStart ? Color.FromArgb("#BBDEFB")
                        : isRing ? Color.FromArgb("#F5EFE0")
                        : Color.FromArgb("#E8DFCA")
                };
                if (occ.TryGetValue((r, c), out var m))
                {
                    cell.Piece = m.ch.ToString();
                    cell.PieceTint = m.tint;
                }
                cells.Add(cell);
            }
        }
    }

    private static Color BgOf(MiniGameEngine.GameKind kind, int r, int c)
    {
        bool dark = (r + c) % 2 == 0;
        return kind switch
        {
            MiniGameEngine.GameKind.ChineseChess => Color.FromArgb("#F5EFE0"),
            MiniGameEngine.GameKind.Chess => dark ? Color.FromArgb("#D4C9B0") : Color.FromArgb("#F5EFE0"),
            MiniGameEngine.GameKind.Gobang => Color.FromArgb("#FAEBD7"),
            MiniGameEngine.GameKind.AnimalChess => dark ? Color.FromArgb("#F5EFE0") : Color.FromArgb("#EDD5A8"),
            _ => dark ? Color.FromArgb("#E8DFCA") : Color.FromArgb("#FDF6EE")
        };
    }

    private static string ChessGlyph(char ch)
    {
        bool white = char.IsUpper(ch);
        return char.ToUpperInvariant(ch) switch
        {
            'K' => white ? "♔" : "♚",
            'Q' => white ? "♕" : "♛",
            'R' => white ? "♖" : "♜",
            'B' => white ? "♗" : "♝",
            'N' => white ? "♘" : "♞",
            'P' => white ? "♙" : "♟",
            _ => ch.ToString()
        };
    }

    /// <summary>中国象棋九宫斜线：九宫四角各画一段对角字符。</summary>
    private static string DiagOf(MiniGameEngine.GameKind kind, int r, int c)
    {
        if (kind != MiniGameEngine.GameKind.ChineseChess) return "";
        bool inPalaceA = r is 0 or 2 && c is 3 or 5;   // 黑方九宫
        bool inPalaceB = r is 7 or 9 && c is 3 or 5;   // 红方九宫
        if (!inPalaceA && !inPalaceB) return "";
        bool topLeft = (r == 0 || r == 7) && c == 3;
        bool bottomRight = (r == 2 || r == 9) && c == 5;
        return topLeft || bottomRight ? "╲" : "╱";
    }

    // ==================================================================
    // 交互
    // ==================================================================

    [RelayCommand]
    private async Task CellTapped(BoardCell? cell)
    {
        if (cell is null || !InGame || IsLudo || IsSnake) return;
        var r = cell.Row; var c = cell.Col;

        if (_game.CurrentGame is MiniGameEngine.GameKind.Gobang or MiniGameEngine.GameKind.Go)
        {
            var (ok, msg) = await _game.PlayerMove(-1, -1, r, c);
            AddBubble(ok ? msg : "这里不能下", isAi: ok);
            SyncBoardFromGame();
            return;
        }

        // 选子已定：点击合法落点 → 移动
        if (_selR >= 0 && _legalTargets.Contains((r, c)))
        {
            var (ok, msg) = await _game.PlayerMove(_selR, _selC, r, c);
            _selR = -1; _selC = -1; _legalTargets.Clear();
            AddBubble(ok ? msg : "这一步走不了", isAi: ok);
            SyncBoardFromGame();
            // 对局中换难度：AI 自动档时按节流让 LLM 以最新记忆+表现重判
            if (ok && _game.CurrentGame is not (MiniGameEngine.GameKind.Gobang or MiniGameEngine.GameKind.Go or MiniGameEngine.GameKind.Ludo or MiniGameEngine.GameKind.Snake))
                _ = RefreshDifficultyAsync(force: false);
            return;
        }

        // 点击己方棋子 → 选中并显示合法落点（不往聊天栏发消息）
        if (_game.IsPlayerPiece(r, c) && _game.PlayerTurn)
        {
            _selR = r; _selC = c;
            _legalTargets = _game.LegalMoves(r, c);
            SyncBoardFromGame();
            return;
        }

        // 点击其他处 → 取消选中
        if (_selR >= 0)
        {
            _selR = -1; _selC = -1; _legalTargets.Clear();
            SyncBoardFromGame();
        }
    }

    [RelayCommand]
    private void RollLudo()
    {
        if (!InGame || !IsLudo || _game.GameOver) return;
        var msg = _game.RollLudo();
        LudoDie = _game.LudoDie;
        AddBubble(msg, isAi: true);
        SyncBoardFromGame();
    }

    /// <summary>围棋：点目结算（提前终局数子）。</summary>
    [RelayCommand]
    private void SettleGo()
    {
        if (!InGame || !IsGo || _game.GameOver) return;
        var msg = _game.SettleGoScore();
        AddBubble(msg, isAi: true);
        SyncBoardFromGame();
    }

    [RelayCommand] private void SnakeUp() => SnakeDir(-1, 0);
    [RelayCommand] private void SnakeDown() => SnakeDir(1, 0);
    [RelayCommand] private void SnakeLeft() => SnakeDir(0, -1);
    [RelayCommand] private void SnakeRight() => SnakeDir(0, 1);

    private void SnakeDir(int dr, int dc)
    {
        if (!InGame || !IsSnake || _game.GameOver) return;
        _game.SetSnakeDirection(dr, dc);
    }

    private void StartSnakeTimer()
    {
        StopSnakeTimer();
        _snakeTimer = Application.Current?.Dispatcher.CreateTimer();
        if (_snakeTimer is null) return;
        _snakeTimer.Interval = TimeSpan.FromMilliseconds(420);
        _snakeTimer.Tick += (_, _) =>
        {
            if (!InGame || !IsSnake || _game.GameOver) { _snakeTimer.Stop(); return; }
            var msg = _game.StepSnake();
            SnakeScore = _game.SnakeScore;
            SyncBoardFromGame();
            if (!string.IsNullOrEmpty(msg)) AddBubble(msg, isAi: true);
        };
        _snakeTimer.Start();
    }

    private void StopSnakeTimer()
    {
        if (_snakeTimer is null) return;
        _snakeTimer.Stop();
        _snakeTimer = null;
    }

    [RelayCommand]
    private async Task Concede()
    {
        if (!InGame) return;
        StopSnakeTimer();
        // AiConcede 触发 OnGameEvent("end")，由 OnEngineEvent 统一加气泡
        await _game.AiConcede();
        SyncBoardFromGame();
        InGame = false;
    }

    [RelayCommand]
    private async Task Taunt()
    {
        if (!InGame) return;
        var reply = await _game.AiTaunt();
        AddBubble(reply, isAi: true);
    }

    [RelayCommand]
    private async Task SendChat()
    {
        var text = ChatInput?.Trim();
        if (string.IsNullOrWhiteSpace(text) || !InGame) return;
        ChatInput = "";
        AddBubble(text, isAi: false);
        var reply = await _game.ChatCasual(text);
        AddBubble(string.IsNullOrWhiteSpace(reply) ? "（小雨没听清）" : reply, isAi: true);
    }

    [RelayCommand]
    private async Task VoiceInput()
    {
        IsListening = true;
        _speech.OnRecognized += OnSpeechResult;
        await _speech.StartListening();
    }

    private void OnSpeechResult(string text)
    {
        _speech.OnRecognized -= OnSpeechResult;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            IsListening = false;
            if (string.IsNullOrWhiteSpace(text)) return;
            AddBubble($"语音: {text}", isAi: false);
            var reply = await _game.ChatCasual(text);
            AddBubble(reply, isAi: true);
        });
    }

    [RelayCommand]
    private async Task SpeakLast()
    {
        if (Messages.Count == 0) return;
        await _speech.Speak(Messages[^1].Text);
    }

    [RelayCommand]
    private async Task GoBack()
    {
        StopSnakeTimer();
        if (InGame)
        {
            InGame = false;
            Messages.Clear();
            BoardCells = new List<BoardCell>();
            _started = false;
        }
        await Shell.Current.GoToAsync("..");
    }

    private void AddBubble(string text, bool isAi)
    {
        Messages.Add(new ChatBubble
        {
            Text = $"{(isAi ? "小雨" : "你")}: {text}".TrimEnd(),
            IsAi = isAi
        });
    }

    /// <summary>页面销毁时调用：解除单例引擎的事件订阅，避免旧 ViewModel 泄漏。</summary>
    public void Detach()
    {
        _game.OnGameEvent -= OnEngineEvent;
    }

    private void OnEngineEvent(string type, string msg)
    {
        if (type is "start" or "end" or "chat")
            MainThread.BeginInvokeOnMainThread(() => AddBubble(msg, isAi: true));
    }
}

public sealed class ChatBubble
{
    public string Text { get; set; } = "";
    public bool IsAi { get; set; }
    public Color BgColor => IsAi ? Color.FromArgb("#F5EFE0") : Color.FromArgb("#FFCA28");
    public Color TextColor => IsAi ? Color.FromArgb("#4A3D2C") : Color.FromArgb("#5D4A3A");
    public LayoutOptions Align => IsAi ? LayoutOptions.Start : LayoutOptions.End;
}