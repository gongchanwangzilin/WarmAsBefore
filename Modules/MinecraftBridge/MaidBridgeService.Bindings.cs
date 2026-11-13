using System.Text.Json.Nodes;

namespace WarmAsBefore.Modules.MinecraftBridge;

/// <summary>
/// 插件「存档管理页」的动作口。这里只改「世界 ↔ 存档 ↔ 女仆」的绑定表：
/// 不替用户打开、新建、分支或切换任何存档 —— 桥是客人，不是主人。
/// </summary>
public sealed partial class MaidBridgeService
{
    public async Task<string> BindingsActionAsync(string action, string argsJson)
    {
        try
        {
            var args = ParseObject(argsJson);
            var worldKey = BridgeJson.Str(args, "worldKey");
            if (string.IsNullOrWhiteSpace(worldKey)) return PageFail("没有指定世界。");

            if (!await _bindingOpsLock.WaitAsync(TimeSpan.FromSeconds(10)))
                return PageFail("绑定操作正忙（上一轮未结束），请稍后再试。");
            try
            {
                await EnsureBindingsLoadedAsync();
                MaidBridgeWorldBinding? world;
                lock (_bindingsGate) _bindings.Worlds.TryGetValue(worldKey, out world);
                if (world is null) return PageFail("没有找到这个世界；它可能已经被清理掉了。");

                string message;
                switch (action)
                {
                    case "unbind":
                    {
                        var maidId = BridgeJson.Str(args, "maid");
                        if (string.IsNullOrWhiteSpace(maidId)) return PageFail("没有指定女仆。");
                        lock (_bindingsGate)
                        {
                            if (!world.Maids.Remove(maidId))
                                return PageFail("这个世界上没有这只女仆的绑定。");
                        }
                        SyncSessionBinding(worldKey, maidId, null);
                        message = "已解绑：她在游戏里回到未绑定状态。";
                        break;
                    }
                    case "rebind":
                    {
                        var maidId = BridgeJson.Str(args, "maid");
                        if (string.IsNullOrWhiteSpace(maidId)) return PageFail("没有指定女仆。");
                        var charId = ResolveCharacterOrName(BridgeJson.Str(args, "character"));
                        if (charId.Length == 0) return PageFail("没有找到这个角色，请从列表里选一个。");
                        lock (_bindingsGate)
                        {
                            if (!world.Maids.TryGetValue(maidId, out var binding))
                                return PageFail("这个世界上没有这只女仆的绑定。");
                            binding.CharacterId = charId;
                            binding.LastSeen = DateTime.UtcNow;
                        }
                        SyncSessionBinding(worldKey, maidId, charId);
                        var name = _engine.Roster.TryGetValue(charId, out var ch) ? ch.Profile.Name : charId;
                        message = $"已改绑到角色「{name}」。";
                        break;
                    }
                    case "retarget":
                    {
                        var saveId = BridgeJson.Str(args, "saveId");
                        if (saveId.Length > 0 && !_store.Exists($"saves/{saveId}"))
                            return PageFail("这份存档不存在。");
                        lock (_bindingsGate) world.SaveId = saveId;
                        message = saveId.Length == 0
                            ? "这个世界已不再指向任何存档。"
                            : "已把这个世界改指向那份存档；没有打开、新建或切换任何存档。";
                        break;
                    }
                    default:
                        return PageFail("不认识的动作：" + action);
                }

                await SaveBindingsLockedAsync();
                // 在线的正是这个世界时，立刻把新的绑定关系下发，模组那边不会留着旧的一份。
                var session = CurrentSession;
                if (session is not null && string.Equals(session.WorldKey, worldKey, StringComparison.Ordinal))
                    await SendBindAsync(session, world, new[] { message });
                return PageOk(message);
            }
            finally
            {
                _bindingOpsLock.Release();
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("MaidBridge.PageAction -> " + ex);
            return PageFail("操作失败：" + ex.Message);
        }
    }

    /// <summary>本次会话正是这个世界时，让它内存里的绑定也跟着页面一起改。</summary>
    private void SyncSessionBinding(string worldKey, string maidId, string? charId)
    {
        var session = CurrentSession;
        if (session is null || !string.Equals(session.WorldKey, worldKey, StringComparison.Ordinal)) return;
        if (charId is null) session.MaidCharacters.TryRemove(maidId, out _);
        else session.MaidCharacters[maidId] = charId;
    }

    private static string PageOk(string message) =>
        new JsonObject { ["ok"] = true, ["message"] = message }.ToJsonString(WireJson);

    private static string PageFail(string error) =>
        new JsonObject { ["ok"] = false, ["error"] = error }.ToJsonString(WireJson);
}
