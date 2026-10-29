using System.Text.Json;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;


public sealed class GameEngine
{
    public GameState State { get; private set; } = new();
    public CharacterData? ActiveCharacter { get; private set; }
    public Dictionary<string, CharacterData> Roster { get; } = new();

    /// <summary>当前会话的存档槽位：新开一局时创建，读取存档时恢复。快速存档/自动存档写入该槽位。</summary>
    public string CurrentSaveId { get; set; } = "";

    /// <summary>地图是否可用：只有绑定了世界树的存档才有「发生在哪儿」，其余按普通酒馆走。</summary>
    public bool MapUnlocked => !string.IsNullOrWhiteSpace(State.WorldId);

    public event Action<string>? ScreenChange;
    public event Action<ChatMessage>? MessageEmitted;

    public void Boot()
    {
        State = new GameState();
    }

    /// <summary>读取存档后恢复游戏状态。</summary>
    public void RestoreState(GameState state)
    {
        if (state is null) return;
        State = state;
        if (!string.IsNullOrEmpty(state.CharacterId) && Roster.TryGetValue(state.CharacterId, out var ch))
            ActiveCharacter = ch;
    }

    public void SetCharacter(string id)
    {
        if (!Roster.TryGetValue(id, out var ch)) return;
        ActiveCharacter = ch;
        State.CharacterId = id;
        ScreenChange?.Invoke("character");
    }

    public void MoveTo(string location)
    {
        State.Location = location;
        State.Background = $"{location}_{State.GameTime:HHmm}_xxx";
        ScreenChange?.Invoke("location");
    }

    public void Emit(ChatMessage msg) => MessageEmitted?.Invoke(msg);

    public void ToggleAuto() => State.AutoPlay = !State.AutoPlay;

    public void Register(CharacterData ch) => Roster[ch.Profile.Id] = ch;
}
