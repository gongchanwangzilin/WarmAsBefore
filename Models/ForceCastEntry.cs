using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WarmAsBefore.Models;

/// <summary>
/// 「点名」条目：人类在手机里点名一个角色，指定她的初始位置（场景）。
///
/// 被点名的角色会被强制变成常驻 NPC（出现在微信联系人里），
/// 并通过「确定」按钮强制进入对话剧情。
/// </summary>
public sealed class ForceCastEntry : INotifyPropertyChanged
{
    /// <summary>角色 Id（对应 CharacterLibrary 里的角色）。</summary>
    public string CharacterId { get; init; } = "";

    /// <summary>角色名（显示用）。</summary>
    public string CharacterName { get; init; } = "";

    /// <summary>头像（相对存储路径，可空）。</summary>
    public string Avatar { get; init; } = "";

    /// <summary>名字首字（无头像时占位显示）。</summary>
    public string Initial => string.IsNullOrEmpty(CharacterName) ? "?" : CharacterName[..1];

    public bool HasAvatar => !string.IsNullOrEmpty(Avatar);

    /// <summary>可选场景列表（与所有条目共享同一份引用，供 Picker 使用）。</summary>
    public IReadOnlyList<MapScene> SceneOptions { get; set; } = Array.Empty<MapScene>();

    private bool _selected;
    /// <summary>是否被勾选（多选）。</summary>
    public bool Selected
    {
        get => _selected;
        set { if (_selected != value) { _selected = value; OnPropertyChanged(); } }
    }

    private string _sceneId = "";
    /// <summary>指定的初始位置（场景 id）。</summary>
    public string SceneId
    {
        get => _sceneId;
        set { if (_sceneId != value) { _sceneId = value; OnPropertyChanged(); } }
    }

    private string _sceneName = "";
    /// <summary>场景名（显示用）。</summary>
    public string SceneName
    {
        get => _sceneName;
        set { if (_sceneName != value) { _sceneName = value; OnPropertyChanged(); } }
    }

    private MapScene? _selectedScene;
    /// <summary>Picker 双向绑定用：选中的场景对象（与 SceneId/SceneName 同步）。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public MapScene? SelectedScene
    {
        get => _selectedScene;
        set
        {
            if (!ReferenceEquals(_selectedScene, value))
            {
                _selectedScene = value;
                if (value is not null)
                {
                    SceneId = value.Id;
                    SceneName = value.Name;
                }
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
