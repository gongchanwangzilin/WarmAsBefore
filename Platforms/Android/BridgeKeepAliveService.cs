using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace WarmAsBefore.Droid;

/// <summary>
/// 安卓前台服务：只做一件事——让「桥接监听」在切到《我的世界》（FCL / 同机 Java 版）之后不被系统冻结。
///
/// 为什么需要它：安卓 12+ 会把退到后台的应用放进 cached 冷冻区，进程里的 UDP/TCP 监听虽然还在，
/// 但 socket 收不到包、心跳超时，模组那边就会判定掉线。前台服务带一条常驻通知，进程不进冷冻区，
/// 回环（127.0.0.1）上的发现与会话就能一直通。
///
/// 只在桥接真正开始监听后启动，桥停了就停——没开插件的人不会看到这条通知。
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class BridgeKeepAliveService : Service
{
    private const string ChannelId = "wab_bridge";
    private const int NotificationId = 4101;

    public static void Start(Context context)
    {
        try
        {
            var intent = new Intent(context, typeof(BridgeKeepAliveService));
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                context.StartForegroundService(intent);
            else
                context.StartService(intent);
        }
        catch (Exception ex)
        {
            global::WarmAsBefore.App.WriteLog("BridgeKeepAlive.Start -> " + ex.Message);
        }
    }

    public static void Stop(Context context)
    {
        try { context.StopService(new Intent(context, typeof(BridgeKeepAliveService))); }
        catch (Exception ex)
        {
            global::WarmAsBefore.App.WriteLog("BridgeKeepAlive.Stop -> " + ex.Message);
        }
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var manager = (NotificationManager?)GetSystemService(NotificationService);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O && manager is not null)
        {
            var channel = new NotificationChannel(ChannelId, "桥接保活", NotificationImportance.Low)
            {
                Description = "保持与《我的世界》的连接"
            };
            channel.SetShowBadge(false);
            manager.CreateNotificationChannel(channel);
        }

        var builder = Build.VERSION.SdkInt >= BuildVersionCodes.O
            ? new Notification.Builder(this, ChannelId)
            : new Notification.Builder(this);
        var icon = ApplicationInfo?.Icon ?? global::Android.Resource.Drawable.IcDialogInfo;
        var notification = builder
            .SetContentTitle("温暖如初")
            .SetContentText("正在保持与《我的世界》的连接")
            .SetSmallIcon(icon)
            .SetOngoing(true)
            .Build();

        StartForeground(NotificationId, notification);
        // STICKY：系统内存紧张杀掉后自动拉起来，桥接继续监听
        return StartCommandResult.Sticky;
    }

    public override void OnTaskRemoved(Intent? rootIntent)
    {
        // 用户从最近任务里划掉应用时，服务跟着停，不留孤儿通知
        StopSelf();
        base.OnTaskRemoved(rootIntent);
    }
}
