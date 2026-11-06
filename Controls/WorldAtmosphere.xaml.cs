using Microsoft.Maui.Controls.Xaml;

namespace WarmAsBefore.Controls;

/// <summary>世界大气层 —— 见同目录 WorldAtmosphere.xaml 头注释（UI 标准书 2.7）。</summary>
[XamlCompilation(XamlCompilationOptions.Compile)]
public partial class WorldAtmosphere : Grid
{
    public WorldAtmosphere() => InitializeComponent();
}
