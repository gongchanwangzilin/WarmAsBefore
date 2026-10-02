namespace WarmAsBefore.Models;

/// <summary>音乐库条目。</summary>
public sealed class MusicItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Name { get; set; } = "";
    /// <summary>相对于 StorageProvider.Root 的路径（如 assets/audio/xxx.mp3）。</summary>
    public string RelPath { get; set; } = "";
    public string Format { get; set; } = "";
    public int DurationMs { get; set; }
    /// <summary>内容 SHA1，用于导入时去重。</summary>
    public string Hash { get; set; } = "";
}

/// <summary>背景库条目（JPG/PNG/MP4）。</summary>
public sealed class BackgroundItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Name { get; set; } = "";
    public string RelPath { get; set; } = "";
    public string Format { get; set; } = "";
    /// <summary>缩略图相对路径；图片类型可为空（自身即为缩略图），MP4 为首帧 PNG 缓存。</summary>
    public string ThumbRelPath { get; set; } = "";
    /// <summary>内容 SHA1（去重 / 视频首帧缓存命名）。</summary>
    public string HashPath { get; set; } = "";
}

/// <summary>声音分配方案（持久化）。</summary>
public sealed class SoundAssign
{
    /// <summary>左键空处播放的音乐 Id（空则静音）。</summary>
    public string LeftClickId { get; set; } = "";
    /// <summary>右键播放的音乐 Id。</summary>
    public string RightClickId { get; set; } = "";
    /// <summary>按键音（输入等）播放的音乐 Id。</summary>
    public string KeyPressId { get; set; } = "";
    /// <summary>背景音乐轮播列表（音乐 Id 列表，按顺序循环）。</summary>
    public List<string> BgmList { get; set; } = new();
}
