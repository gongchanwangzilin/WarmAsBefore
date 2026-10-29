namespace WarmAsBefore.Views;

/// <summary>
/// 工具窗：关系网、搜索这些「另开一扇」的窗口都用它装。
///
/// 为什么要专门一个类型，而不是直接 <c>new Window(page)</c>：全 App 有六处靠
/// <c>Application.Current.Windows.FirstOrDefault(…)</c> 找主窗口（毛玻璃、置顶、桌宠收纳…），
/// 第二扇窗一出现，它们就可能拿到错的窗口，于是主窗口的置顶跑到工具窗上。
/// 有类型标记，那些地方就能绕开工具窗。
/// </summary>
public sealed class ToolWindow : Window
{
    public ToolWindow(Page page) : base(page)
    {
    }
}
