namespace WarmAsBefore;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        RegisterRoutes();
        App.WriteLog("AppShell: initialized, routes registered");
        // 导航/弹窗时保持窗口置顶，避免通知被其他窗口遮挡。
        // 通知类应用：默认 BringToFront（弹通知不被挡）；AlwaysOnTop 开启时持续强制常驻。
        Navigated += (_, _) =>
        {
            try
            {
                var sp = Application.Current?.Handler?.MauiContext?.Services;
                var sm = sp?.GetService(typeof(WarmAsBefore.Services.SettingsManager)) as WarmAsBefore.Services.SettingsManager;
                if (sm?.Current.AlwaysOnTop == true)
                    WarmAsBefore.Services.WindowTopmost.Force();
                else
                    WarmAsBefore.Services.WindowTopmost.BringToFront();
            }
            catch { /* 导航早期服务可能未就绪，忽略 */ }
        };
    }

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
        Routing.RegisterRoute("scenelibrary", typeof(Views.SceneLibraryPage));
    }
}