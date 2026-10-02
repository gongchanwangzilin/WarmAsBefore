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
    public bool HasImage => ImageSource is not null;
    public bool IsVideo => !string.IsNullOrEmpty(VideoPath);
    public bool IsFile => !HasImage && !IsVideo && !string.IsNullOrEmpty(FileName);
    public bool IsText => !HasImage && !IsVideo && !IsFile;
    public string Sender => IsUser ? "我" : "小雨";

    partial void OnImageSourceChanged(ImageSource? value) => OnPropertyChanged(nameof(HasImage));
    partial void OnFileNameChanged(string value) => OnPropertyChanged(nameof(IsFile));
    partial void OnDisplayTextChanged(string value) => OnPropertyChanged(nameof(IsText));
}