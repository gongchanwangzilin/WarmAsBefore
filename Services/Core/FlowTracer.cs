using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace WarmAsBefore.Services.Core;

/// <summary>
/// 架构流程执行追踪器：把正在执行的节点"点亮"，并随时间推移渐隐（余辉）。
/// 纯静态、线程安全、零异常外泄、不打日志（避免刷屏）。
/// 时钟用 <see cref="Environment.TickCount64"/> 单调时钟，避免 DateTime 受系统时钟回拨影响。
/// UI 订阅 <see cref="Changed"/> 后按需重绘；节点是否还亮由 <see cref="IsLit"/>/<see cref="Glow"/> 判定。
/// </summary>
public static class FlowTracer
{
    /// <summary>余辉时长（毫秒）：节点点亮后在该时长内逐渐从 1 渐隐到 0。</summary>
    public const int GlowMs = 2600;

    // nodeId -> 点亮时刻的 TickCount64（毫秒，单调时钟）
    private static readonly ConcurrentDictionary<string, long> _lit = new(StringComparer.Ordinal);

    // 最近一次点亮的角色 id，供 UI 做角色级过滤（单实例场景足够）。
    private static string? _lastCharId;

    /// <summary>最近一次点亮的角色 id；UI 可据此过滤只显示当前角色的亮灯。</summary>
    public static string? LastCharId => _lastCharId;

    /// <summary>任意节点点亮/清空时触发，UI 订阅后按需 Invalidate。handler 抛错被吞掉，绝不影响主流程。</summary>
    public static event Action? Changed;

    /// <summary>
    /// 点亮一个节点：记录单调时钟时间戳，更新最近角色 id，并通知 UI。
    /// 永不抛异常（含事件回调异常），调用方可无脑 inline 一行而不影响主流程。
    /// </summary>
    public static void Trace(string nodeId, string? charId = null)
    {
        if (string.IsNullOrEmpty(nodeId)) return;
        try
        {
            _lastCharId = charId;
            _lit[nodeId] = Environment.TickCount64;
            RaiseChanged();
        }
        catch
        {
            // 追踪器是旁路信号，任何内部异常都必须沉默。
        }
    }

    /// <summary>节点当前是否仍在余辉窗口内（距上次 Trace 不足 <see cref="GlowMs"/> 毫秒）。</summary>
    public static bool IsLit(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return false;
        try
        {
            return _lit.TryGetValue(nodeId, out var t) && (Environment.TickCount64 - t) < GlowMs;
        }
        catch { return false; }
    }

    /// <summary>
    /// 余辉强度 0..1：刚点亮为 1，线性渐隐到 0；未点亮或超过 <see cref="GlowMs"/> 返回 0。
    /// 供 Drawable 画亮灯余辉亮度。
    /// </summary>
    public static float Glow(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return 0f;
        try
        {
            if (!_lit.TryGetValue(nodeId, out var t)) return 0f;
            var elapsed = Environment.TickCount64 - t;
            if (elapsed >= GlowMs) return 0f;
            if (elapsed <= 0) return 1f;
            return 1f - elapsed / (float)GlowMs;
        }
        catch { return 0f; }
    }

    /// <summary>
    /// 当前仍亮着的 nodeId 快照（顺手清掉已过期的条目，避免字典无限增长）。
    /// 返回独立 List，调用方可安全遍历/修改。
    /// </summary>
    public static IReadOnlyCollection<string> LitNodes()
    {
        var snapshot = new List<string>();
        try
        {
            var now = Environment.TickCount64;
            foreach (var kv in _lit)
            {
                if (now - kv.Value < GlowMs)
                    snapshot.Add(kv.Key);
                else
                    _lit.TryRemove(kv.Key, out _);
            }
        }
        catch
        {
            // 静默失败：最多返回不完整的快照，绝不外泄异常。
        }
        return snapshot;
    }

    /// <summary>清空所有点亮状态（进新存档时由 VM 调用），并通知 UI 刷新。</summary>
    public static void Clear()
    {
        try
        {
            _lit.Clear();
            _lastCharId = null;
            RaiseChanged();
        }
        catch
        {
            // 静默失败。
        }
    }

    private static void RaiseChanged()
    {
        var handler = Changed;
        if (handler is null) return;
        try { handler(); }
        catch { /* 订阅方异常绝不能冒回追踪器调用者 */ }
    }
}
