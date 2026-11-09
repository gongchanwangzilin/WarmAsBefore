using System;

namespace WarmAsBefore.Services.Core;

/// <summary>第十七项：女仆下棋输了有可能哭，主人可以去安慰。默认关闭、概率 0.1。</summary>
public sealed record ChessReaction(bool Cry, string Message);

public static class ChessEmotionService
{
    /// <summary>winner 文本由 MiniGameEngine 给出；「你赢了」即女仆输棋。</summary>
    public static ChessReaction OnGameEnded(string? winner, bool enabled, double probability,
                                            double roll, bool comfortEnabled)
    {
        if (!enabled || string.IsNullOrWhiteSpace(winner)) return new ChessReaction(false, "");
        bool maidLost = winner.Contains("你赢", StringComparison.Ordinal)
                        || winner.Contains("你胜", StringComparison.Ordinal);
        if (!maidLost || roll >= Math.Clamp(probability, 0, 1)) return new ChessReaction(false, "");
        string message = comfortEnabled
            ? "她输了棋，眼圈红了，凑过来要主人安慰。"
            : "她输了棋，眼圈红了。";
        return new ChessReaction(true, message);
    }
}
