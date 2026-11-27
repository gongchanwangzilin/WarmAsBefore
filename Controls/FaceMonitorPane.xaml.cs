namespace WarmAsBefore.Controls;

/// <summary>
/// 人脸识别面板（结构见 FaceMonitorPane.xaml 顶部注释）：组件状态 + 图像流。
/// 绑定对象由宿主页给（BindingContext = FacePanelViewModel），这里不持有页面状态，
/// 也不自己去取帧——取帧只有用户按「抓一帧」或 AI 主动请一次才会发生。
/// </summary>
public partial class FaceMonitorPane : ContentView
{
    public FaceMonitorPane() => InitializeComponent();
}
