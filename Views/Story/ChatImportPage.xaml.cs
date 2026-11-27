using WarmAsBefore.Helpers;
using WarmAsBefore.Services;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>聊天记录导入页：读文件 / 粘贴 → 结果 → 带回本局。写入存档不在这里发生。</summary>
[QueryProperty(nameof(Character), "character")]
public partial class ChatImportPage : ContentPage
{
    private readonly ChatImportViewModel _vm;
    private bool _loaded;

    public ChatImportPage(ChatImportViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    public string Character { get; set; } = "";

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        _loaded = true;
        UiSounds.Notice();
        _ = _vm.LoadAsync(Uri.UnescapeDataString(Character ?? ""));
        Motion.ArrivePage(this);
    }
}
