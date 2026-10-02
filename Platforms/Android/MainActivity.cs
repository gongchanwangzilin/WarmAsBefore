using Android.App;
using Android.Content.PM;
using Android.OS;

namespace WarmAsBefore.Platforms.Android;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // 默认横屏
        RequestedOrientation = ScreenOrientation.Landscape;
    }

    /// <summary>把语音识别的结果转发给 SpeechService。
    /// （旧的 Activity.StartActivityForResultAsync 扩展已被移除，改回原生回调。）</summary>
    protected override void OnActivityResult(int requestCode, Result resultCode, global::Android.Content.Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        global::WarmAsBefore.Services.SpeechService.HandleActivityResult(requestCode, resultCode, data);
    }
}