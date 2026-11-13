namespace WarmAsBefore.Models;

/// <summary>
/// 部署到本机的模型种类。只用于归类，以及部署向导里「该填哪些配置项」的提示，不影响下载/安装逻辑。
/// 名字、来源、安装位置全部由用户决定，工具不内置任何默认。
/// </summary>
public enum ModelKind
{
    /// <summary>向量模型（近义检索）。</summary>
    Embedding = 0,
    /// <summary>小模型（情绪表演 / 工具解析 / 状态包压缩）。</summary>
    SmallModel = 1,
    /// <summary>大语言模型（主对话）。</summary>
    LLM = 2,
    /// <summary>识图模型。</summary>
    Vision = 3
}

/// <summary>一条已部署 / 正在部署的模型登记。所有字段都由用户填写，工具只负责把它落到盘上并登记。</summary>
public sealed record DeployedModel
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];
    /// <summary>用户起的名字，随便一个也行。</summary>
    public string Name { get; init; } = "";
    public ModelKind Kind { get; init; } = ModelKind.Embedding;
    /// <summary>来源：http(s) 链接，或本机上的文件路径。完全由用户填写。</summary>
    public string Source { get; init; } = "";
    /// <summary>安装位置（目录）。完全由用户选择；工具只把文件落到这里。</summary>
    public string InstallPath { get; init; } = "";
    /// <summary>可选 SHA256 校验和：填了才校验，不填直接装。</summary>
    public string? Checksum { get; init; }
    public DeployStatus Status { get; init; } = DeployStatus.Pending;
    public string? LastError { get; init; }
    public long Bytes { get; init; }
    public DateTime AddedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>部署状态。</summary>
public enum DeployStatus
{
    Pending = 0,
    Downloading = 1,
    Verifying = 2,
    Installed = 3,
    Failed = 4
}

/// <summary>部署进度（UI 绑定）。</summary>
public sealed record ModelDeployProgress
{
    public string Id { get; init; } = "";
    public DeployStatus Phase { get; init; }
    public long Received { get; init; }
    public long Total { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// 「按电脑性能推荐」给出的只是**示例建议**——名字、来源占位、安装位置都还是你自己定；
/// 工具不会偷偷替你选好任何模型，也不会内置默认路径。
/// </summary>
public sealed record ModelSuggestion
{
    public string Name { get; init; } = "";
    public ModelKind Kind { get; init; }
    public string ExampleSource { get; init; } = "";
    public string Note { get; init; } = "";
    public long ApproxBytes { get; init; }
}
