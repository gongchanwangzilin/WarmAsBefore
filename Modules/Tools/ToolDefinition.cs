namespace WarmAsBefore.Modules.Tools;

/// <summary>工具宿主语言。Builtin=进程内 C# 实现（系统/内置工具）；Python/Java=外部脚本运行时。</summary>
public enum ToolLanguage
{
    Builtin,
    Python,
    Java
}

/// <summary>工具参数描述（供 AI / 外部调用方按 schema 传参）。</summary>
public sealed record ToolParameter
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public bool Required { get; init; }
}

/// <summary>
/// 统一工具定义：内置工具与外部 Java/Python 工具共用同一份契约，不分区。
/// 外部工具约定：
///   Python —— 脚本 {WorkDir}/{Entry}（默认 main.py），进程内 python 解释器运行；
///   Java   —— 可执行 JAR {WorkDir}/{Entry}（默认 tool.jar），java -jar 运行；
/// 二者都通过 stdin/stdout 按 JSON-RPC 2.0 通信（newline 分隔，JsonRpc 协议见 ToolManager）。
/// </summary>
public sealed record ToolDefinition
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public ToolLanguage Language { get; init; } = ToolLanguage.Builtin;

    /// <summary>外部工具的入口文件名（Python 脚本 或 Java JAR）。</summary>
    public string Entry { get; init; } = "";

    /// <summary>工具目录（相对 {root}/tools/{工具名}）。</summary>
    public string WorkDir { get; init; } = "";

    public bool IsExternal => Language != ToolLanguage.Builtin;

    public List<ToolParameter> Parameters { get; init; } = new();

    /// <summary>预计所需运行时空缺时的提示。</summary>
    public string RuntimeNeeded => Language switch
    {
        ToolLanguage.Python => "需要 Python 运行时",
        ToolLanguage.Java => "需要 Java 运行时",
        _ => ""
    };
}