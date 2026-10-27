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
            if (_navWatch is not null)
            {
                var to = e.Current?.Location?.OriginalString ?? "";
                Services.PerfLog.Note($"导航 {_navTarget} → {to}",
                    $"{_navWatch.ElapsedMilliseconds} ms{(_navWatch.ElapsedMilliseconds >= 300 ? "  <<< SLOW" : "")}");
                _navWatch = null;
            }
            PlayPageTransition(e);
        };
        // 这里原先在每次导航时重新置顶/BringToFront。
        // 但那会让「设置 → 窗口置顶」关闭后仍被强制置顶（关不掉），
        // 且每次切页都产生一次 Win32 z-order / Presenter 变更 → 切页卡顿。
        // 置顶策略统一由 RuntimeConfigurator.Apply() 跟随设置下发；
        // 弹通知/对话框时再按需一次性抬前台（见 NotificationService 订阅方）。
    }

    private System.Diagnostics.Stopwatch? _navWatch;
    private string _navTarget = "";
    private bool _firstNav = true;

    /// <summary>
    /// Shell 在 WinUI 上没有页面过场——导航完成那一帧新页直接顶上来，读起来就是「卡了一下」。
    /// 这里只补一个方向位移：前进从右、返回从左，260ms 收回去；幅度写小，别做成整页飞入。
    /// 退出方向不做：Shell 在 Navigated 之前已经换页，旧页拿不到，硬补只会拖慢导航本身。
    /// </summary>
    private void PlayPageTransition(ShellNavigatedEventArgs e)
    {
        var back = e.Source is ShellNavigationSource.Pop or ShellNavigationSource.PopToRoot;
        // 启动那次不算「切换」，但 WinUI 上初始路由很少触发 Navigated；
        // 只跳过落到根页（标题页）的那一次，其余第一跳照常播。
        if (_firstNav)
        {
            _firstNav = false;
            if (!back && CurrentPage is Views.TitlePage) return;
        }
        _ = Helpers.Motion.PageIn(CurrentPage, back);
    }

    private void RegisterRoutes()
    {
        Routing.RegisterRoute("main", typeof(Views.MainGamePage));
        Routing.RegisterRoute("select", typeof(Views.CharacterSelectPage));
        Routing.RegisterRoute("setup", typeof(Views.SetupPage));
        Routing.RegisterRoute("settings", typeof(Views.SettingsPage));
        Routing.RegisterRoute("phone", typeof(Views.PhonePage));
        Routing.RegisterRoute("chat", typeof(Views.WeChatPage));
        // QQ 壳：与微信壳同构的青蓝换皮会话页（无支付/礼物面板，多「论坛」）+ QQ 论坛页
        Routing.RegisterRoute("qqchat", typeof(Views.QqPage));
        Routing.RegisterRoute("qqforum", typeof(Views.QqForumPage));
        Routing.RegisterRoute("map", typeof(Views.MapPage));
        Routing.RegisterRoute("gallery", typeof(Views.GalleryPage));
        Routing.RegisterRoute("outfit", typeof(Views.OutfitPage));
        Routing.RegisterRoute("figurePack", typeof(Views.FigurePackPage));
        Routing.RegisterRoute("save", typeof(Views.SavePage));
        // 云存档：看远端有哪些、哪台机器什么时候存过，一键拉回本地
        Routing.RegisterRoute("cloudsaves", typeof(Views.CloudSavesPage));
        // 头像裁剪：相册/拍照之后就地裁剪，回传绝对路径
        Routing.RegisterRoute("avatarcrop", typeof(Views.AvatarCropPage));
        Routing.RegisterRoute("game", typeof(Views.GamePage));
        Routing.RegisterRoute("novelselect", typeof(Views.NovelSelectPage));
        Routing.RegisterRoute("novelworld", typeof(Views.NovelWorldPage));
        Routing.RegisterRoute("worldbook", typeof(Views.WorldbookPage));
        // 新开一局 / 本局设定：世界树绑定 + 用户人设 + 人格记忆初始化
        Routing.RegisterRoute("newrun", typeof(Views.NewRunPage));
        Routing.RegisterRoute("roster", typeof(Views.CharacterLibraryPage));
        // 新建角色：角色库顶上那颗按钮进来的四步向导
        Routing.RegisterRoute("newcharacter", typeof(Views.NewCharacterPage));
        // 角色工作室：列表那一行点进来的一页（改 TA 的一切都在这儿，不在列表里）
        Routing.RegisterRoute("studio", typeof(Views.CharacterStudioPage));
        // 新建角色：角色库顶上那颗按钮进来的四步向导（基本 → 人设 → 形象 → 世界观）
        Routing.RegisterRoute("newcharacter", typeof(Views.NewCharacterPage));
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
        // 关系网：看得见长出来的那张网（点节点就能改）
        Routing.RegisterRoute(Services.PageWindowKeys.Graph, typeof(Views.MemoryGraphPage));
        // 搜事件：桌面上通常另开一扇窗，这里的路由是手机上那条退路
        Routing.RegisterRoute(Services.PageWindowKeys.Search, typeof(Views.MemorySearchPage));
        // 设置分类子页（首页 → 分类）
        Routing.RegisterRoute("settingssection", typeof(Views.SettingsSectionPage));
        Routing.RegisterRoute("scenelibrary", typeof(Views.SceneLibraryPage));
        Routing.RegisterRoute(nameof(Views.SceneCalibrationPage), typeof(Views.SceneCalibrationPage));
        Routing.RegisterRoute("forcecast", typeof(Views.ForceCastPage));
        // 心理状况：按当前角色设置轻重与稳定度（设置 → 身份与心理）
        Routing.RegisterRoute("mentalHealth", typeof(Views.MentalHealthPage));
        // 插件中心：列表 / 启停 / 依赖 / 设置 / 崩溃日志 / 工坊
        Routing.RegisterRoute("pluginCenter", typeof(Views.PluginCenterPage));
        // 原生设置：按 plugin.json 的 fields 现场生成表单
        Routing.RegisterRoute("pluginSettings", typeof(Views.PluginSettingsPage));
        // 插件权限台：声明 ∩ 授予，一插件一屏
        Routing.RegisterRoute("pluginPerms", typeof(Views.PluginPermissionsPage));
        // 存档高级设置：这份存档能用哪些插件 / 工具 / 角色
        Routing.RegisterRoute("saveRules", typeof(Views.SaveRulesPage));
    }
}