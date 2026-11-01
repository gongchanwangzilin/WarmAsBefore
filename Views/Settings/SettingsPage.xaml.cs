using Microsoft.Maui.Controls.Shapes;
using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>
/// 设置首页：iPhone 标准的分组 inset 列表（大标题 → 搜索 → 「常驻」「全部设置」两组）。
///
/// 结构史：24 张卡挤一个长页（太慢）→ 一张卡装一组行（快了，但像工具库清单）
/// → 两列图块网格（好看，但行高错位、emoji 破风格）→ 现在这版。
/// 行由代码建：编号（01…）、右侧当前值摘要、行首发丝线，全部走 DynamicResource。
/// 搜索过滤照旧：行 IsVisible 绑行 Visible，分组会自然塌掉。
/// </summary>
public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _vm;
    private double _lastSide = -1;
    private bool _narrowLast;
    private bool _wideLast = true;
    private bool _listBuilt;
    private SettingsViewModel.SettingsSectionRow? _usageRow;
    private SettingsViewModel.SettingsSectionRow? _petRow;

    /// <summary>电脑档分界：宽过这一线走双栏组卡 + 说明字上桌；以下退回手机的单列长卡。</summary>
    private bool IsWide => Width > 900;

    public SettingsPage(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
        SizeChanged += (_, _) =>
        {
            ApplyResponsiveWidth();
            RebuildList();
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ApplyResponsiveWidth();
        // 首页只建 11+2 行，构建量极小；各分类的数据装载在子页里按需做
        _vm.PrepareHome();
        RebuildList(force: true);
        Motion.ArrivePage(this);
    }

    /// <summary>宽度匹配屏幕：内容列由 XAML 的 WCardWidth 收口，这里只记日志（留作性能观测）。</summary>
    private void ApplyResponsiveWidth()
    {
        try
        {
            var w = Width;
            if (w <= 0) return;
            var side = Math.Max(0, (w - Math.Clamp(w * 0.9, 320, 1600)) / 2);
            if (Math.Abs(side - _lastSide) > 1)
            {
                _lastSide = side;
                Services.PerfLog.Note("设置页宽度", $"Width={w:0} → 内容列 0.9×窗宽（1600 封顶），两侧留白 {side:0}");
            }
        }
        catch (Exception ex) { App.WriteLog("SettingsPage.Width -> " + ex.Message); }
    }

    /// <summary>
    /// 建分组列表。口径（2026-10-30 拍板 + 10-30 晚 iPhone 化）：
    /// **常驻组 = 桌宠设置 → 用量统计 → 外观与主题**，其余按目录原序进「全部设置」。
    /// 用量统计与桌宠设置不是目录里的分类，手工造两枚假行接独立路由。
    /// </summary>
    private void RebuildList(bool force = false)
    {
        try
        {
            var narrow = Width > 0 && Width <= 720;
            var wide = IsWide;
            if (!force && _listBuilt && narrow == _narrowLast && wide == _wideLast) return;
            _narrowLast = narrow;
            _wideLast = wide;
            _listBuilt = true;
            // 手机味的大标题只在窄屏保留：顶栏已经有页面名，宽屏再顶一个大「设置」
            // 就露馅了——那是 iPhone 的 large title，桌面应用不这么排版。
            BigTitle.IsVisible = !wide;
            BigSubtitle.IsVisible = !wide;

            if (_usageRow == null)
            {
                _usageRow = new SettingsViewModel.SettingsSectionRow
                {
                    Key = "__usage__",
                    Title = "用量统计",
                    Desc = "Token 用量与花费的本地账本",
                    Summary = "今日 · 近 7 天 · 成本构成 · 热力图"
                };
            }
            if (_petRow == null)
            {
                _petRow = new SettingsViewModel.SettingsSectionRow
                {
                    Key = "__petsettings__",
                    Title = "桌宠设置",
                    Desc = "桌面上陪你的那一位",
                    Summary = "体型 · 动作 · 气泡 · 语音 · 位置"
                };
            }

            // 常驻：桌宠设置 → 用量统计 → 外观与主题；其余按目录原序（platform 已含窗口）。
            var ordered = new List<(SettingsViewModel.SettingsSectionRow Row, bool Usage)>();
            foreach (var row in _vm.SectionRows)
            {
                ordered.Add((row, false));
            }
            MoveToFront(ordered, p => !p.Usage && p.Row.Key == "appearance");
            ordered.Insert(0, (_usageRow, true));
            ordered.Insert(0, (_petRow, false));

            var primary = ordered.Take(3).ToList();
            var rest = ordered.Skip(3).ToList();
            FillGroup(GroupPrimaryHost, primary, narrow);
            FillGroup(GroupAllHost, rest, narrow);
        }
        catch (Exception ex) { App.WriteLog("SettingsPage.List -> " + ex.Message); }
    }

    /// <summary>把命中的第一枚挪到最前（置顶次序靠调用顺序收口）。</summary>
    private static void MoveToFront(
        List<(SettingsViewModel.SettingsSectionRow Row, bool Usage)> list,
        Func<(SettingsViewModel.SettingsSectionRow Row, bool Usage), bool> match)
    {
        var pair = list.FirstOrDefault(match);
        if (pair.Row == null) return;
        list.Remove(pair);
        list.Insert(0, pair);
    }

    private void FillGroup(
        VerticalStackLayout host,
        List<(SettingsViewModel.SettingsSectionRow Row, bool Usage)> rows,
        bool narrow)
    {
        host.Clear();
        if (IsWide)
        {
            host.Add(BuildGroupGrid(rows));   // 电脑：一张组卡里两栏，宽屏该有的铺法
            return;
        }
        for (var i = 0; i < rows.Count; i++)
        {
            host.Add(BuildRow(rows[i].Row, rows[i].Usage, i > 0, narrow));
        }
    }

    /// <summary>
    /// 电脑上的组：内容不再一柱到底——左右两栏摊开，中间一道竖发丝线。
    /// 行首不留任何图标（编号、单字、emoji 都不算图标，用户拍板「没有图标不要硬整」）。
    /// 两栏各用一条 VerticalStackLayout 装行——行结构与窄屏单列同源，只有分栏是新的；
    /// 实测把行跨列摆进带 1px 中柱的 Grid，Windows 的测量会把 Star 列测歪（标题折行、说明横穿行界），绕开。
    /// </summary>
    private View BuildGroupGrid(List<(SettingsViewModel.SettingsSectionRow Row, bool Usage)> rows)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(1)),
                new ColumnDefinition(GridLength.Star),
            },
            Padding = new Thickness(4, 0),
        };

        // 列平衡：对半切，前半进左栏、后半进右栏；两栏各自从上往下铺，带上各自的发丝线。
        var split = (rows.Count + 1) / 2;
        var left = new VerticalStackLayout { Spacing = 0 };
        var right = new VerticalStackLayout { Spacing = 0 };
        for (var i = 0; i < rows.Count; i++)
        {
            var inLeft = i < split;
            (inLeft ? left : right).Add(BuildRow(
                rows[i].Row, rows[i].Usage,
                withTopRule: inLeft ? i > 0 : i - split > 0, narrow: false));
        }

        var rail = new BoxView { WidthRequest = 1 };
        TryApplyStyle(rail, "VSep");
        grid.Add(left, 0, 0);
        grid.Add(rail, 1, 0);
        grid.Add(right, 2, 0);
        return grid;
    }

    /// <summary>
    /// 一行（固定 54 高，与窄屏同一结构）：标题 + 右侧当前值摘要 + ›。
    /// 标题/摘要都直接做 Grid 的孩子——再套一层布局，Windows 的测量会把字放出格横穿行界。
    /// 摘要右对齐是 iOS 的做法（当前值永远靠右），窄屏让位给标题。
    /// 每行顶部带自己的发丝线（首行不带）：行被搜索隐藏时线跟着走，不会留下孤儿线。
    /// </summary>
    private View BuildRow(SettingsViewModel.SettingsSectionRow row, bool usage, bool withTopRule, bool narrow)
    {
        var wrap = new VerticalStackLayout { Spacing = 0 };
        if (withTopRule)
        {
            var rule = new BoxView { HeightRequest = 1 };
            TryApplyStyle(rule, "HSepBleed");
            wrap.Add(rule);
        }

        var line = new Grid
        {
            HeightRequest = 54,
            Padding = new Thickness(16, 0),
            ColumnSpacing = 12,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        line.BindingContext = row;
        line.SetBinding(VisualElement.IsVisibleProperty, "Visible");

        var title = new Label
        {
            Text = row.Title,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
        };
        TryApplyStyle(title, "TxtRowTitle");

        var summary = new Label
        {
            Text = row.Summary,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            IsVisible = !narrow,
        };
        TryApplyStyle(summary, "TxtCaption");

        var chev = new Label { Text = "›", FontSize = 18, VerticalOptions = LayoutOptions.Center };
        chev.SetDynamicResource(Label.TextColorProperty, "WarmFaint");

        line.Add(title, 0, 0);
        line.Add(summary, 1, 0);
        line.Add(chev, 2, 0);

        AttachRoute(line, row, usage);
        wrap.Add(line);
        return wrap;
    }

    /// <summary>行的点按路由：手工行（用量统计/桌宠设置）走独立路由，其余交给目录命令。</summary>
    private void AttachRoute(Grid line, SettingsViewModel.SettingsSectionRow row, bool usage)
    {
        var tap = new TapGestureRecognizer();
        if (usage || row.Key == "__usage__")
        {
            tap.Command = new Command(async () =>
            {
                try { await Shell.Current.GoToAsync("usagestats"); }
                catch (Exception ex) { App.WriteLog("SettingsPage.Usage -> " + ex.Message); }
            });
        }
        else if (row.Key == "__petsettings__")
        {
            tap.Command = new Command(async () =>
            {
                try { await Shell.Current.GoToAsync("petsettings"); }
                catch (Exception ex) { App.WriteLog("SettingsPage.Pet -> " + ex.Message); }
            });
        }
        else
        {
            tap.Command = _vm.OpenSectionCommand;
            tap.CommandParameter = row.Key;
        }
        line.GestureRecognizers.Add(tap);
    }

    /// <summary>从应用级顶层字典取样式；取不到就什么都不做（别让首页建行失败）。</summary>
    private static void TryApplyStyle(BindableObject target, string key)
    {
        var s = TryStyle(key);
        if (s is not null) target.SetValue(VisualElement.StyleProperty, s);
    }

    private static Style? TryStyle(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Style style
            ? style : null;
}
