namespace WarmAsBefore;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        RegisterRoutes();
        App.WriteLog("AppShell: initialized, routes registered");
        // 页面切换耗时打点：卡顿到底在「导航本身」还是「目标页的首屏装载」，日志里分得清
        Navigating += (_, e) =>
        {
            _navWatch = System.Diagnostics.Stopwatch.StartNew();
            _navTarget = e.Target?.Location?.OriginalString ?? "";
        };
        Navigated += (_, e) =>
        {
            if (_navWatch is null) return;
            var to = e.Current?.Location?.OriginalString ?? "";
            Services.PerfLog.Note($"导航 {_navTarget} → {to}",
                $"{_navWatch.ElapsedMilliseconds} ms{(_navWatch.ElapsedMilliseconds >= 300 ? "  <<< SLOW" : "")}");
            _navWatch = null;
        };
        // 这里原先在每次导航时重新置顶/BringToFront。
        // 但那会让「设置 → 窗口置顶」关闭后仍被强制置顶（关不掉），
        // 且每次切页都产生一次 Win32 z-order / Presenter 变更 → 切页卡顿。
        // 置顶策略统一由 RuntimeConfigurator.Apply() 跟随设置下发；
        // 弹通知/对话框时再按需一次性抬前台（见 NotificationService 订阅方）。
    }

    private System.Diagnostics.Stopwatch? _navWatch;
    private string _navTarget = "";

    private void RegisterRoutes()
    {
        Routing.RegisterRoute("main", typeof(Views.MainGamePage));
        Routing.RegisterRoute("select", typeof(Views.CharacterSelectPage));
        Routing.RegisterRoute("settings", typeof(Views.SettingsPage));
        Routing.RegisterRoute("phone", typeof(Views.PhonePage));
        Routing.RegisterRoute("chat", typeof(Views.WeChatPage));
        Routing.RegisterRoute("map", typeof(Views.MapPage));
        Routing.RegisterRoute("gallery", typeof(Views.GalleryPage));
        Routing.RegisterRoute("outfit", typeof(Views.OutfitPage));
        Routing.RegisterRoute("save", typeof(Views.SavePage));
        Routing.RegisterRoute("game", typeof(Views.GamePage));
        Routing.RegisterRoute("novelselect", typeof(Views.NovelSelectPage));
        Routing.RegisterRoute("novelworld", typeof(Views.NovelWorldPage));
        Routing.RegisterRoute("worldbook", typeof(Views.WorldbookPage));
        Routing.RegisterRoute("roster", typeof(Views.CharacterLibraryPage));
        Routing.RegisterRoute("shop", typeof(Views.ShopPage));
        Routing.RegisterRoute("showcase-list", typeof(Views.ShowcaseListPage));
        Routing.RegisterRoute("showcase-edit", typeof(Views.ShowcaseEditPage));
        Routing.RegisterRoute("showcase-play", typeof(Views.ShowcasePlayPage));
        Routing.RegisterRoute("affection-level-up", typeof(Views.AffectionLevelUpPage));
        Routing.RegisterRoute("cg-view", typeof(Views.CgViewPage));
        Routing.RegisterRoute("battle", typeof(Views.BattlePage));
        Routing.RegisterRoute("materials", typeof(Views.MaterialsPage));
        // 立绘标定：非 1080×1920 逻辑画布的素材从这里调试后再上场
        Routing.RegisterRoute("portraitCalibration", typeof(Views.PortraitCalibrationPage));
        // 主设定 × 副设定对比
        Routing.RegisterRoute("subProfile", typeof(Views.SubProfilePage));
        // 记忆管理：关系网 / 标签 / 记得的事 / 副设定
        Routing.RegisterRoute("memoryManage", typeof(Views.MemoryManagePage));
        Routing.RegisterRoute("scenelibrary", typeof(Views.SceneLibraryPage));
        Routing.RegisterRoute(nameof(Views.SceneCalibrationPage), typeof(Views.SceneCalibrationPage));
        Routing.RegisterRoute("forcecast", typeof(Views.ForceCastPage));
    }
}