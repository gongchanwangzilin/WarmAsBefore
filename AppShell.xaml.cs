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
            // 阶段标记给 UI 卡顿看门狗用：卡在「导航途中」还是「目标页装载」日志里能分开。
            Services.PerfLog.SetPhase("导航 → " + _navTarget);
            // 盖忙碌层：只在「前进」导航（新建/插入/换页）上盖。返回（Pop/PopToRoot）通常
            // 命中已缓存的旧页、瞬时完成，盖层只会闪一下；前进里第一次访问的重页
            // （设置/主界面）才是 300–500ms「建卡」的来源，也是最需要进度反馈的地方。
            if (e.Source is not ShellNavigationSource.Pop and not ShellNavigationSource.PopToRoot)
                ShowNavBusy();
        };
        Navigated += (_, e) =>
        {
            if (_navWatch is not null)
            {
                var to = e.Current?.Location?.OriginalString ?? "";
                Services.PerfLog.Note($"导航 {_navTarget} → {to}",
                    $"{_navWatch.ElapsedMilliseconds} ms{(_navWatch.ElapsedMilliseconds >= 300 ? "  <<< SLOW" : "")}");
                _navWatch = null;
                Services.PerfLog.SetPhase("页面 " + to);
            }
            PlayPageTransition(e);
            // 成就横幅的落点跟着页面顶栏走：标题页没有顶栏（贴顶 16），其余页面默认 64（顶栏下方）。
            Controls.Toast.TopInset = CurrentPage is Views.TitlePage ? 16 : 64;
            // 目标页已上屏（构造+首帧完成），撤掉忙碌层。
            HideNavBusy();
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

    // ============ 导航忙碌层（2026-11-15，回应「切页慢且没有反馈进度」）============
    // 切页那 300–500ms 的「建卡」期用户完全看不到在动，读起来就是卡死。
    // 做法：Navigating（此刻目标页尚未构造、当前仍是旧页）把一层轻量忙碌卡盖到「旧页」最上层。
    // 旧页已经画好上屏，盖上后即便目标页还在构造，画面上也一直有东西在说「正在切换」。
    // Navigated 撤层；另加 2.5s 保险丝（导航异常没触发 Navigated 时也不能永远盖着）。
    // 硬规矩与 LoadingVeil 一致：
    //  · 靠增删子节点控制显隐，绝不写 Opacity；
    //  · 不铺全屏压暗 scrim（scrim 是布局死循环的帮凶），只放一张居中小卡；
    //  · 面/边/圆角全取既有令牌（SurfaceBg / WEdgeBrush / WRadiusCard / AccentWarm / MutedFg），零新增色；
    //  · InputTransparent=true：是提示不是模态锁，不吞点击、不锁窗（锁窗曾直接崩进程）；
    //  · 跨线程回 UI 一律走 Helpers.Ui.Post，绝不用 MainThread.*（线程池上取不到 DispatcherQueue 即崩进程）。
    private Grid? _navBusyOverlay;
    private ContentPage? _navBusyPage;
    private System.Threading.CancellationTokenSource? _navBusyTimeout;

    private void ShowNavBusy()
    {
        try
        {
            if (_navBusyOverlay is not null) return;              // 重入闸：一次导航只盖一层
            var page = CurrentPage as ContentPage;
            if (page?.Content is not Grid root) return;           // 只盖根为 Grid 的页（绝大多数）；非 Grid 不强改其布局

            var overlay = new Grid
            {
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                BackgroundColor = Microsoft.Maui.Graphics.Colors.Transparent,
                InputTransparent = true,
            };
            // 「华丽」口径：复用系统现成的玻璃卡材质 CardFrost（玻璃底、**无影子**）——
            // 红线「零新增 Shadow」：新造影子会重建合成层，症状是「元素位置还在、不再绘制」（整页空白）。
            var card = new Border
            {
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
            };
            card.SetDynamicResource(Border.StyleProperty, "CardFrost");

            var stack = new VerticalStackLayout
            {
                Spacing = 10,
                HorizontalOptions = LayoutOptions.Center,
            };
            var spin = new ActivityIndicator
            {
                IsRunning = true,
                HorizontalOptions = LayoutOptions.Center,
                WidthRequest = 22,
                HeightRequest = 22,
            };
            spin.SetDynamicResource(ActivityIndicator.ColorProperty, "AccentWarm");
            var label = new Label
            {
                Text = Controls.LoadingVeil.LoadingText,
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                HorizontalOptions = LayoutOptions.Center,
            };
            label.SetDynamicResource(Label.TextColorProperty, "SurfaceFg");
            _busyLabel = label;
            // 详细信息展开栏（Windows 式）：默认收起，点「详细信息」看每一步在干什么，
            // 用来回答「到底卡住了没有」。显隐只切 IsVisible。
            _busyDetail = new Label
            {
                IsVisible = false,
                FontSize = 11,
                HorizontalOptions = LayoutOptions.Center,
                HorizontalTextAlignment = Microsoft.Maui.TextAlignment.Start,
            };
            _busyDetail.SetDynamicResource(Label.TextColorProperty, "MutedFg");
            var detailHost = new ScrollView { IsVisible = false, HeightRequest = 110, Content = _busyDetail };
            _busyDetailHost = detailHost;
            var detailToggle = new Button
            {
                Text = "详细信息 ▾",
                FontSize = 11,
                Padding = 0,
                HeightRequest = 24,
                MinimumWidthRequest = 0,
                BackgroundColor = Colors.Transparent,
                TextColor = Microsoft.Maui.Graphics.Colors.Gray,
            };
            detailToggle.Clicked += (_, _) =>
            {
                detailHost.IsVisible = !detailHost.IsVisible;
                detailToggle.Text = detailHost.IsVisible ? "详细信息 ▴" : "详细信息 ▾";
            };
            stack.Children.Add(spin);
            stack.Children.Add(label);
            stack.Children.Add(detailToggle);
            stack.Children.Add(detailHost);
            card.Content = stack;
            overlay.Children.Add(card);

            Grid.SetRow(overlay, 0);
            Grid.SetRowSpan(overlay, Math.Max(1, root.RowDefinitions.Count));
            Grid.SetColumn(overlay, 0);
            Grid.SetColumnSpan(overlay, Math.Max(1, root.ColumnDefinitions.Count));
            root.Children.Add(overlay);
            _busyDetailLines.Clear();
            _navBusyOverlay = overlay;
            _navBusyPage = page;

            // 保险丝：导航异常没触发 Navigated 也不能永远盖着（LoadingVeil 同款兜底思路）
            _navBusyTimeout?.Cancel();
            _navBusyTimeout = new System.Threading.CancellationTokenSource();
            var token = _navBusyTimeout.Token;
            System.Threading.Tasks.Task.Delay(2500, token).ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully) Helpers.Ui.Post(HideNavBusy);
            }, System.Threading.Tasks.TaskScheduler.Default);
        }
        catch (Exception ex) { App.WriteLog("AppShell.ShowNavBusy -> " + ex.Message); }
    }

    private void HideNavBusy()
    {
        try
        {
            _navBusyTimeout?.Cancel(); _navBusyTimeout = null;
            var overlay = _navBusyOverlay; var page = _navBusyPage;
            _navBusyOverlay = null; _navBusyPage = null;
            if (overlay is null) return;
            if (page?.Content is Grid root && root.Children.Contains(overlay))
                root.Children.Remove(overlay);
        }
        catch (Exception ex) { App.WriteLog("AppShell.HideNavBusy -> " + ex.Message); }
    }

    /// <summary>
    /// 供「再续情缘」这类先读档、后导航的入口在等待期间提前亮出忙碌层：
    /// 正常导航链路由 Navigating 自动盖/撤；这里只是把同一层手动开一次，
    /// 与 Navigated 的自动撤层幂等兼容（层已被撤时是 no-op）。
    /// </summary>
    public void ShowBusyNow() => ShowNavBusy();

    /// <summary>与 <see cref="ShowBusyNow"/> 配对的手动撤层；层不在时 no-op。</summary>
    public void HideBusyNow() => HideNavBusy();

    /// <summary>忙碌卡上的阶段文字（读档 2/5 · 校验数据 之类）。层不在时静默。</summary>
    public void SetBusyText(string text)
    {
        try
        {
            if (_navBusyOverlay is not null && _busyLabel is not null)
                _busyLabel.Text = text;
        }
        catch (Exception ex) { App.WriteLog("AppShell.SetBusyText -> " + ex.Message); }
    }

    private Label? _busyLabel;
    private Label? _busyDetail;
    private ScrollView? _busyDetailHost;
    private readonly List<string> _busyDetailLines = new();

    /// <summary>忙碌卡详细信息：追加一行细步记录，只留最近 6 行，收起态照常记录。</summary>
    public void SetBusyDetail(string detail)
    {
        try
        {
            if (_busyDetail is null) return;
            _busyDetailLines.Add(detail);
            while (_busyDetailLines.Count > 6) _busyDetailLines.RemoveAt(0);
            _busyDetail.Text = string.Join("\n", _busyDetailLines);
        }
        catch (Exception ex) { App.WriteLog("AppShell.SetBusyDetail -> " + ex.Message); }
    }

    private void RegisterRoutes()
    {
        Routing.RegisterRoute("main", typeof(Views.MainGamePage));
        // 模块详情页：从架构图点进某个模块自己的页面（多档等级、实时读数、时间线）
        Routing.RegisterRoute("moduleInspector", typeof(Views.ModuleInspectorPage));
        Routing.RegisterRoute("select", typeof(Views.CharacterSelectPage));
        Routing.RegisterRoute("setup", typeof(Views.SetupPage));
        Routing.RegisterRoute("settings", typeof(Views.SettingsPage));
        Routing.RegisterRoute("phone", typeof(Views.PhonePage));
        Routing.RegisterRoute("chat", typeof(Views.WeChatPage));
        // QQ 壳：与微信壳同构的青蓝换皮会话页（无支付/礼物面板，多「论坛」）+ QQ 论坛页
        Routing.RegisterRoute("qqchat", typeof(Views.QqPage));
        Routing.RegisterRoute("qqforum", typeof(Views.QqForumPage));
        Routing.RegisterRoute("map", typeof(Views.MapPage));
        // 取景会话：手机端「为地图取景」（照片绑定到地图坐标；电脑端只能导入照片）
        Routing.RegisterRoute("mapcapture", typeof(Views.MapCapturePage));
        // 3D 灰度视图：同一张图的「站在街上看」读法（编辑器仍默认 2D）
        Routing.RegisterRoute("minimal3d", typeof(Views.Minimal3DPage));
        // 流程图：规则（时机 + 条件 + 动作）的列表编辑器；复杂条件树交给 AI 写
        Routing.RegisterRoute("flows", typeof(Views.FlowEditorPage));
        Routing.RegisterRoute("gallery", typeof(Views.GalleryPage));
        Routing.RegisterRoute("outfit", typeof(Views.OutfitPage));
        Routing.RegisterRoute("figurePack", typeof(Views.FigurePackPage));
        Routing.RegisterRoute("save", typeof(Views.SavePage));
        // 分支树：VS Code 式 git graph，按 ParentId 还原存档血缘；?focus=saveId 从节点回存档页高亮
        Routing.RegisterRoute("saveTree", typeof(Views.SaveTreePage));
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
        Routing.RegisterRoute("chatimport", typeof(Views.ChatImportPage));
        Routing.RegisterRoute("roster", typeof(Views.CharacterLibraryPage));
        // 超级监测：独立整页，charId 经查询参数传入（入口在存档页 / 角色库右上角）
        Routing.RegisterRoute("superMonitor", typeof(Views.SuperMonitorPage));
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
        // 调色页：纯色档「选个颜色…」进，确认后经 ColorPickerHandoff 回传，不走查询串
        Routing.RegisterRoute("colorpicker", typeof(Views.ColorPickerPage));
        Routing.RegisterRoute("scenelibrary", typeof(Views.SceneLibraryPage));
        Routing.RegisterRoute(nameof(Views.SceneCalibrationPage), typeof(Views.SceneCalibrationPage));
        Routing.RegisterRoute("forcecast", typeof(Views.ForceCastPage));
        // 心理状况：按当前角色设置轻重与稳定度（设置 → 身份与心理）
        Routing.RegisterRoute("mentalHealth", typeof(Views.MentalHealthPage));
        // AI 厂商 / 模型管理：设置 → AI 里进，两页互相跳
        Routing.RegisterRoute("aiProviders", typeof(Views.AiProvidersPage));
        Routing.RegisterRoute("aiModels", typeof(Views.AiModelsPage));
        // F04 模型部署向导：用户决定模型名 / 来源 / 安装位置，工具自动下载安装
        Routing.RegisterRoute("modelDeployment", typeof(Views.ModelDeploymentPage));
        // 插件中心：列表 / 启停 / 依赖 / 设置 / 崩溃日志 / 工坊
        Routing.RegisterRoute("pluginCenter", typeof(Views.PluginCenterPage));
        // 原生设置：按 plugin.json 的 fields 现场生成表单
        Routing.RegisterRoute("pluginSettings", typeof(Views.PluginSettingsPage));
        // 应用内插件页面：WebView2 显示，页面由本机回环服务器提供
        Routing.RegisterRoute("pluginWeb", typeof(Views.PluginWebPage));
        // 插件权限台：声明 ∩ 授予，一插件一屏
        Routing.RegisterRoute("pluginPerms", typeof(Views.PluginPermissionsPage));
        // 存档高级设置：这份存档能用哪些插件 / 工具 / 角色
        Routing.RegisterRoute("saveRules", typeof(Views.SaveRulesPage));
        // 桌宠设置：左实时预览 + 右分组设置（设置 → 桌面与桌宠，也挂在托盘菜单里）
        Routing.RegisterRoute("petsettings", typeof(Views.PetSettingsPage));
        // AI 制作桌宠：把角色 Q 版化，逐状态出帧并算好走动步频
        Routing.RegisterRoute("petstudio", typeof(Views.PetStudioPage));
        // 用量统计：总览 / 成本构成 / Token 活跃度 / 时间序列 / 分组明细 / 单价
        Routing.RegisterRoute("usagestats", typeof(Views.UsageStatsPage));
        // 创建地图：检索真实城市（高德 / OSM）/ 导入 / 让 AI 补背景
        Routing.RegisterRoute("mapcreate", typeof(Views.MapCreatePage));
    }
}