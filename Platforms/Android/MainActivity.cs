using Android.App;
using Android.Content.PM;
using Android.OS;

namespace WarmAsBefore.Platforms.Android;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode)]
public class MainActivity : MauiAppCompatActivity
{
    /// <summary>把当前 Activity 暴露给 ScreenOrientationService（锁屏需拿到 Activity 实例）。</summary>
    public static global::Android.App.Activity? CurrentActivity { get; private set; }

    protected override void OnCreate(Bundle savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // 默认不锁方向（auto 模式）：手机自然竖屏，平板保持系统方向（通常横屏）。
        // 设置值由 App 启动时 ScreenOrientationService.ApplyOnStart() 统一锁定（含横屏/竖屏手动选项）。
        RequestedOrientation = ScreenOrientation.Unspecified;
        CurrentActivity = this;
    }

    /// <summary>把语音识别的结果转发给 SpeechService。
    /// （旧的 Activity.StartActivityForResultAsync 扩展已被移除，改回原生回调。）</summary>
    protected override void OnActivityResult(int requestCode, Result resultCode, global::Android.Content.Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        global::WarmAsBefore.Services.SpeechService.HandleActivityResult(requestCode, resultCode, data);
    }
}