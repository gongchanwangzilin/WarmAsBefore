using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.Plugin;

/// <summary>
/// 通过Application.Current获取服务（与PetService.cs一致）。
/// </summary>
internal static class ServicesHelper
{
    public static IServiceProvider? GetServices() =>
        Application.Current?.Handler?.MauiContext?.Services;
}

/// <summary>
/// 插件系统：允许外部脚本/AI通过标准输入输出与主线程交互。
/// 插件可以：读取游戏状态、发送消息、控制立绘、执行动作等。
/// </summary>
public sealed class PluginManager : IDisposable
{
    private readonly ConcurrentDictionary<string, Func<string, Task<string>>> _handlers = new();
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public event Action<string>? PluginMessageReceived;

    public PluginManager()
    {
        RegisterDefaultHandlers();
        _ = StartStdioLoopAsync();
    }

    private void RegisterDefaultHandlers()
    {
        // 获取游戏状态
        Register("get_state", async args =>
        {
            var services = ServicesHelper.GetServices();
            if (services is null) return JsonSerializer.Serialize(new { error = "无法获取服务" });
            var engine = services.GetService(typeof(GameEngine)) as GameEngine;
            if (engine is null) return JsonSerializer.Serialize(new { error = "GameEngine 不可用" });
            var vm = services.GetService(typeof(ViewModels.MainGameViewModel)) as ViewModels.MainGameViewModel;
            // 从角色状态获取好感度、信任度、精力
            var charData = engine.Roster.TryGetValue(engine.State.CharacterId, out var ch) ? ch : null;
            return JsonSerializer.Serialize(new
            {
                character = engine.State.CharacterId,
                characterName = vm?.CharacterName ?? "未知",
                affection = charData?.State.Affection ?? 0,
                trust = charData?.State.Trust ?? 0,
                energy = charData?.State.Energy ?? 100,
                location = engine.State.Location,
                isSpeaking = vm?.IsSpeaking,
                messageCount = vm?.Messages.Count
            });
        });

        // 发送消息
        Register("send_message", async args =>
        {
            var services = ServicesHelper.GetServices();
            if (services is null) return "错误：服务不可用";
            var vm = services.GetService(typeof(ViewModels.MainGameViewModel)) as ViewModels.MainGameViewModel;
            if (vm is null) return "错误：MainGameViewModel 不可用";
            if (string.IsNullOrWhiteSpace(args)) return "错误：消息为空";
            vm.InputText = args;
            await vm.SendMessageCommand.ExecuteAsync(null);
            return "消息已发送";
        });

        // 设置立绘位置
        Register("set_sprite_position", async args =>
        {
            var services = ServicesHelper.GetServices();
            if (services is null) return "错误：服务不可用";
            var vm = services.GetService(typeof(ViewModels.MainGameViewModel)) as ViewModels.MainGameViewModel;
            if (vm is null) return "错误：MainGameViewModel 不可用";
            try
            {
                var parts = args.Split(',');
                if (parts.Length >= 1)
                {
                    var pos = parts[0].Trim().ToLower();
                    vm.SpritePosition = pos switch
                    {
                        "left" => "left",
                        "right" => "right",
                        _ => "center"
                    };
                }
                if (parts.Length >= 2 && double.TryParse(parts[1].Trim(), out var x))
                    vm.SpriteX = x;
                if (parts.Length >= 3 && double.TryParse(parts[2].Trim(), out var y))
                    vm.SpriteY = y;
                return "立绘位置已设置";
            }
            catch (Exception ex)
            {
                return $"错误：{ex.Message}";
            }
        });

        // 设置立绘透明度
        Register("set_sprite_opacity", async args =>
        {
            var services = ServicesHelper.GetServices();
            if (services is null) return "错误：服务不可用";
            var vm = services.GetService(typeof(ViewModels.MainGameViewModel)) as ViewModels.MainGameViewModel;
            if (vm is null) return "错误：MainGameViewModel 不可用";
            if (double.TryParse(args.Trim(), out var opacity))
            {
                opacity = Math.Clamp(opacity, 0, 1);
                vm.SpriteOpacity = opacity;
                return $"立绘透明度已设置为 {opacity}";
            }
            return "错误：无效的透明度值";
        });

        // 执行动画
        Register("animate", async args =>
        {
            var services = ServicesHelper.GetServices();
            if (services is null) return "错误：服务不可用";
            var vm = services.GetService(typeof(ViewModels.MainGameViewModel)) as ViewModels.MainGameViewModel;
            if (vm is null) return "错误：MainGameViewModel 不可用";
            var action = args.Trim().ToLower();
            if (action == "sink") { vm.SinkAnimationAsync(); return "下沉动画已开始"; }
            if (action == "jump") { vm.JumpAnimationAsync(); return "跳跃动画已开始"; }
            if (action == "shake") { vm.ShakeAnimationAsync(); return "颤抖动画已开始"; }
            return $"未知动画：{action}，可用: sink, jump, shake";
        });

        // 订阅事件
        Register("subscribe", async args =>
        {
            var event_type = args.Trim().ToLower();
            if (event_type == "message")
            {
                PluginMessageReceived += _ => { };
                return "已订阅消息事件";
            }
            return $"未知事件类型：{event_type}";
        });
    }

    public void Register(string name, Func<string, Task<string>> handler)
    {
        _handlers[name] = handler;
    }

    public async Task<string> Execute(string name, string args)
    {
        if (_handlers.TryGetValue(name, out var handler))
            return await handler(args);
        return $"未知命令：{name}，可用命令: {string.Join(", ", _handlers.Keys)}";
    }

    private async Task StartStdioLoopAsync()
    {
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var line = await Console.In.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;
                var response = await ProcessCommandAsync(line);
                Console.Out.WriteLine(response);
                Console.Out.Flush();
            }
        }
        catch (OperationCanceledException)
        {
            // 正常退出
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"插件系统错误：{ex.Message}");
        }
    }

    private async Task<string> ProcessCommandAsync(string input)
    {
        try
        {
            if (input.StartsWith("{"))
            {
                using var doc = JsonDocument.Parse(input);
                var root = doc.RootElement;
                var command = root.TryGetProperty("command", out var c) ? c.GetString() : "";
                var args = root.TryGetProperty("args", out var a) ? a.GetString() : "";
                if (!string.IsNullOrEmpty(command))
                    return await Execute(command, args ?? "");
            }
            else
            {
                var parts = input.Split(' ', 2);
                var command = parts[0];
                var args = parts.Length > 1 ? parts[1] : "";
                return await Execute(command, args);
            }
        }
        catch (Exception ex)
        {
            return $"错误：{ex.Message}";
        }
        return "未知命令";
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _cts.Cancel();
            _handlers.Clear();
            _disposed = true;
        }
    }
}
