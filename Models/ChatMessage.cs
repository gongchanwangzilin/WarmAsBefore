using CommunityToolkit.Mvvm.ComponentModel;

namespace WarmAsBefore.Models;

public sealed partial class ChatItem : ObservableObject
{
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private string _displayText = "";
    /// <summary>图片/动图消息（含用户导入的表情包）：非空时气泡内显示图片。</summary>
    [ObservableProperty] private ImageSource? _imageSource;
    /// <summary>视频消息：非空时气泡内直接内联播放。</summary>
    public string VideoPath { get; init; } = "";
    /// <summary>非图片/视频文件消息（文档等）：显示文件名卡片，点击用系统默认程序打开。</summary>
    [ObservableProperty] private string _fileName = "";
    public bool IsUser { get; init; }
    /// <summary>落盘消息的去重键；空 = 只在内存里的老消息。</summary>
    public string Key { get; init; } = "";
    /// <summary>对方显示名；空则回落到默认名字。</summary>
    public string SenderName { get; init; } = "";
    public bool HasImage => ImageSource is not null;
    public bool IsVideo => !string.IsNullOrEmpty(VideoPath);
    public bool IsFile => !HasImage && !IsVideo && !string.IsNullOrEmpty(FileName);

    /// <summary>F22：这是一条「工具调用」气泡（不是普通文字消息）。</summary>
    [ObservableProperty] private bool _isToolCall;
    /// <summary>工具调用摘要（如「3 次工具调用」），折叠时显示。</summary>
    [ObservableProperty] private string _toolSummary = "";
    /// <summary>展开后的详细内容：每次调用的工具名 / 参数 / 返回结果。</summary>
    [ObservableProperty] private string _toolDetails = "";
    /// <summary>是否已展开详情。</summary>
    [ObservableProperty] private bool _toolExpanded;

    public bool IsToolVisible => IsToolCall;
    public bool IsToolDetailVisible => IsToolCall && ToolExpanded;

    public bool IsText => !HasImage && !IsVideo && !IsFile && !IsToolCall;

    /// <summary>
    /// 气泡里真正显示的字。空字符串的消息（表情包撤回、只带换行的粘贴）以前会画成一只空泡泡，
    /// 看着像界面坏了；现在给它一句「（空消息）」，至少能认出这是一条真的空消息。
    /// </summary>
    public string BubbleText => string.IsNullOrWhiteSpace(DisplayText) ? "（空消息）" : DisplayText;
    public string Sender => IsUser ? "我" : (string.IsNullOrEmpty(SenderName) ? "小雨" : SenderName);

    partial void OnImageSourceChanged(ImageSource? value) => OnPropertyChanged(nameof(HasImage));
    partial void OnFileNameChanged(string value) => OnPropertyChanged(nameof(IsFile));
    partial void OnDisplayTextChanged(string value) => OnPropertyChanged(nameof(IsText));
    partial void OnIsToolCallChanged(bool value)
    {
        OnPropertyChanged(nameof(IsToolVisible));
        OnPropertyChanged(nameof(IsToolDetailVisible));
        OnPropertyChanged(nameof(IsText));
    }
    partial void OnToolExpandedChanged(bool value) => OnPropertyChanged(nameof(IsToolDetailVisible));
}