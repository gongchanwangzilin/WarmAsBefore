using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.ApiManager;

namespace WarmAsBefore.Services.Core;

/// <summary>
/// F08：设置调节 AI。用一段临时、与角色隔离的 AI 调用，分析当前设置并给出优化建议。
/// 关键隔离点：不走任何角色记忆、不写记忆、不套角色人格；只把设置的「事实摘要」喂给模型，
/// 让模型以「设置顾问」身份给建议。上下文与原角色完全隔离，仅临时用。
/// 运行频率由 <see cref="UserSettings.SettingsAdvisorFrequency"/> 控制
/// （never / daily / 3days / weekly），默认 never —— 默认不打扰，用户主动开启后才按频率跑。
/// </summary>
public sealed class SettingsAdvisorService
{
    private readonly SettingsManager _settings;
    private readonly ApiGateway _api;

    public SettingsAdvisorService(SettingsManager settings, ApiGateway api)
    {
        _settings = settings;
        _api = api;
    }

    /// <summary>是否正在运行（UI 可绑定以禁用按钮 / 显示进度）。</summary>
    public bool IsRunning { get; private set; }
    /// <summary>最近一次建议文本。</summary>
    public string LastSuggestion { get; private set; } = "";
    /// <summary>最近一次运行时间（本地时间），无则 null。</summary>
    public DateTimeOffset? LastSuggestionAt { get; private set; }

    private static readonly Dictionary<string, TimeSpan> S_frequencies = new(StringComparer.Ordinal)
    {
        ["daily"] = TimeSpan.FromDays(1),
        ["3days"] = TimeSpan.FromDays(3),
        ["weekly"] = TimeSpan.FromDays(7),
    };

    /// <summary>按频率判断是否该运行；该运行则执行并返回 true。</summary>
    public async Task<bool> MaybeRunAsync()
    {
        var s = _settings.Current;
        if (!s.SettingsAdvisorEnabled) return false;
        var freq = (s.SettingsAdvisorFrequency ?? "never").ToLowerInvariant();
        if (freq == "never" || !S_frequencies.TryGetValue(freq, out var gap)) return false;
        if (s.SettingsAdvisorLastRun > 0)
        {
            var last = new DateTimeOffset(s.SettingsAdvisorLastRun, TimeSpan.Zero);
            if (DateTimeOffset.UtcNow - last < gap) return false;
        }
        await RunNowAsync();
        return true;
    }

    /// <summary>立即跑一次设置建议（忽略频率），并把运行时间写回设置持久化。</summary>
    public async Task<string?> RunNowAsync()
    {
        if (IsRunning) return LastSuggestion;
        IsRunning = true;
        try
        {
            var s = _settings.Current;
            var ep = new AiEndpoint
            {
                Url = s.AiUrl,
                Key = s.AiKey,
                Model = s.AiModel,
                Temperature = 0.4,
                MaxTokens = 800,
                TimeoutSeconds = s.AiTimeoutSeconds > 0 ? s.AiTimeoutSeconds : 25,
                SupportsTools = false
            };
            var history = BuildIsolatedHistory(s);
            var result = await _api.Chat(history, ep);
            var text = string.IsNullOrWhiteSpace(result) ? "（模型没有返回建议）" : result.Trim();
            LastSuggestion = text;
            LastSuggestionAt = DateTimeOffset.Now;
            // 写回上次运行时间（持久化），下次按频率判断
            try
            {
                var updated = s with { SettingsAdvisorLastRun = DateTimeOffset.UtcNow.Ticks };
                _settings.Apply(updated);
                await _settings.Persist();
            }
            catch (Exception ex) { App.WriteLog("SettingsAdvisor.Persist -> " + ex.Message); }
            return text;
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsAdvisor.RunNow -> " + ex.Message);
            LastSuggestion = "分析失败：" + ex.Message;
            return LastSuggestion;
        }
        finally { IsRunning = false; }
    }

    /// <summary>构建一段与角色完全隔离的请求：system 说明顾问身份，user 只给设置事实摘要。</summary>
    private static List<ChatMessage> BuildIsolatedHistory(UserSettings s)
    {
        const string system =
            "你是「设置优化顾问」，与任何角色人格无关，也不读取任何角色记忆。" +
            "下面是一份 AI 陪伴软件的用户设置摘要。请只基于这些设置，用中文、分条给出 3-6 条具体、可操作的优化建议，" +
            "并指出每条对应的设置项名称。不要虚构设置项，不要涉及角色剧情，不要写代码。" +
            "若设置已经合理，直接说「当前设置总体合理」。";
        var summary = SummarizeSettings(s);
        return new List<ChatMessage>
        {
            new() { Role = "system", Content = system },
            new() { Role = "user", Content = "当前设置摘要：\n" + summary + "\n\n请给出优化建议。" }
        };
    }

    /// <summary>只暴露设置事实，绝不混入角色人格 / 记忆。</summary>
    private static string SummarizeSettings(UserSettings s)
    {
        var parts = new List<string>
        {
            $"AI 模型：{s.AiModel}（端点 {s.AiUrl}）",
            $"温度：{s.AiTemperature}，最大 Token：{s.AiMaxTokens}",
            $"深度思考：{(s.DeepThink ? "开" : "关")}（深度模型：{s.DeepModel}）",
            $"思考档位：{s.ThinkTierCount} 档，当前 {s.ActiveThinkTier}，参数键 {s.ThinkParamKey}=[{s.ThinkParamValues}]",
            $"小模型优先：{(s.SmallModelFirst ? "开" : "关")}（{s.SmallAiModel}）",
            $"记忆窗口：{s.MemoryWindowDays} 天，回忆随机：{s.MemoryRecallRandomness}",
            $"量化模型：{(s.EmbeddingEnabled ? "开" : "关")}（{s.EmbeddingModel}）",
            $"大脑预判：{(s.BrainEnabled ? "开" : "关")}，AI 辅助预判：{(s.BrainAiAssistEnabled ? "开" : "关")}",
            $"AI 自主深度思考：{(s.AiAutonomousThink ? "开" : "关")}，AI 自主多步计划：{(s.AiPlanFlowEnabled ? "开" : "关")}，多步上限：{s.PlanMaxSteps}",
            $"设置建议 AI：{(s.SettingsAdvisorEnabled ? "开" : "关")}（频率 {s.SettingsAdvisorFrequency}）",
            $"智能省钱：{(s.SmartSavingEnabled ? "开" : "关")}，上下文上限：{s.ContextTokenLimit}",
            $"语音 TTS：{(s.TtsEnabled ? "开" : "关")}，STT：{(s.SttEnabled ? "开" : "关")}",
        };
        return string.Join("\n", parts);
    }
}
