using Microsoft.UI.Xaml;

namespace WarmAsBefore.WinUI;

/// <summary>
/// WinUI 应用入口（ApplicationDefinition），触发 WinUI XAML 编译与 PRI 生成。
/// </summary>
public partial class App : MauiWinUIApplication
{
	public App()
	{
		this.InitializeComponent();
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	protected override void OnLaunched(LaunchActivatedEventArgs args)
	{
		WarmAsBefore.App.WriteLog("WinUI.App.OnLaunched: before base");
		try
		{
			base.OnLaunched(args);
			WarmAsBefore.App.WriteLog("WinUI.App.OnLaunched: after base OK");
		}
		catch (Exception ex)
		{
			WarmAsBefore.App.WriteLog("WinUI.App.OnLaunched: EXCEPTION -> " + ex);
			throw;
		}
	}
}
