using Microsoft.Maui.Controls.Shapes;
using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>
/// 设置首页：分类以**两列图块**摊开（窄屏单列），点进 <see cref="SettingsSectionPage"/> 看具体项。
///
/// 结构史：24 张卡挤一个长页（太慢）→ 一张卡装一组行（快了，但读起来像工具库的清单）
/// → 现在的图块网格。材质仍是同一套（CardSolid + WPadRow + 发丝部件），只重排空间。
/// 图块由代码建：BindableLayout 只能线性铺行，双列分摊要按窗口宽度自己算。
/// </summary>
public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _vm;
    private double _lastSide = -1;
    private bool _tilesTwoCol = true;
    private SettingsViewModel.SettingsSectionRow? _usageRow;
    private SettingsViewModel.SettingsSectionRow? _petRow;

    public SettingsPage(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
        SizeChanged += (_, _) =>
        {
            ApplyResponsiveWidth();
            RebuildTiles();
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ApplyResponsiveWidth();
        // 首页只建 13+1 枚图块，构建量极小；各分类的数据装载在子页里按需做
        _vm.PrepareHome();
        RebuildTiles(force: true);
        Motion.ArrivePage(this);
    }

    /// <summary>宽度匹配屏幕：窄屏铺满，宽屏最多 1200（一行文字再长就难读）。</summary>
    private void ApplyResponsiveWidth()
    {
        try
        {
            var w = Width;
            if (w <= 0) return;
            // 内容最多 1200：宽屏时两侧留白把它居中；Center 会让容器按内容收缩（卡片就会变窄），
            // 所以这里用「填满 + 两侧 Margin」的写法。
            var available = Math.Max(0, w - 48);
            var side = Math.Max(24, (available - 1200) / 2);
            if (Math.Abs(side - _lastSide) > 1)
            {
                _lastSide = side;
                Services.PerfLog.Note("设置页宽度", $"Width={w:0} → 内容 {available - 2 * side:0}（两侧各留白 {side:0}）");
            }
            SettingsBody.Margin = new Thickness(side, 0, side, 0);
        }
        catch (Exception ex) { App.WriteLog("SettingsPage.Width -> " + ex.Message); }
    }

    /// <summary>
    /// 建分类图块。口径（2026-10-30 拍板）：**桌宠、用量、外观置顶**，其余按目录原序；
    /// 用量统计不走 SectionRows（它是独立路由页，不是目录里的分类），手工造一枚假行接到 usagestats。
    /// </summary>
    private void RebuildTiles(bool force = false)
    {
        try
        {
            var twoCol = Width > 720;
            if (!force && twoCol == _tilesTwoCol) return;
            _tilesTwoCol = twoCol;

            TilesHost.Clear();

            if (_usageRow == null)
            {
                _usageRow = new SettingsViewModel.SettingsSectionRow
                {
                    Key = "__usage__",
                    Title = "用量统计",
                    Desc = "Token 用量与花费的本地账本",
                    Summary = "Token 用量与花费的本地账本：今日 · 近 7 天 · 成本构成 · 热力图"
                };
            }
            if (_petRow == null)
            {
                _petRow = new SettingsViewModel.SettingsSectionRow
                {
                    Key = "__petsettings__",
                    Title = "桌宠设置",
                    Desc = "桌面上陪你的那一位",
                    Summary = "体型 · 动作 · 气泡 · 声音 · 语音 · 位置 · 自主；AI 一键制作 Q 版桌宠"
                };
            }

            // 置顶：桌宠设置 → 用量统计 → 外观与主题 → 其余按目录原序（platform 不再置顶）。
            var ordered = new List<(SettingsViewModel.SettingsSectionRow Row, bool Usage)>();
            foreach (var row in _vm.SectionRows)
            {
                ordered.Add((row, false));
            }
            MoveToFront(ordered, p => !p.Usage && p.Row.Key == "appearance");
            ordered.Insert(0, (_usageRow, true));
            ordered.Insert(0, (_petRow, false));

            // 一行一 Grid、左右成对：两列各自堆叠会让行高错位（文字像从边框里漏出来）。
            for (var i = 0; i < ordered.Count; i += 2)
            {
                var line = new Grid
                {
                    ColumnSpacing = 12,
                    ColumnDefinitions = twoCol
                        ? new ColumnDefinitionCollection { new(GridLength.Star), new(GridLength.Star) }
                        : new ColumnDefinitionCollection { new(GridLength.Star) },
                };
                line.Add(BuildTile(ordered[i].Row, ordered[i].Usage, i), 0);
                if (twoCol && i + 1 < ordered.Count)
                {
                    line.Add(BuildTile(ordered[i + 1].Row, ordered[i + 1].Usage, i + 1), 1);
                }
                TilesHost.Add(line);
            }
        }
        catch (Exception ex) { App.WriteLog("SettingsPage.Tiles -> " + ex.Message); }
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

    /// <summary>
    /// 一枚图块：编号码（01、02…）+ 标题 + 一行状态。**不用 emoji**：彩色系统 emoji 和纸白材质是两种语言，
    /// 有的还会掉成豆腐块；编号是角色库那套眉标码的自然延伸，也让整张网格的节奏一致。
    /// 卡高固定 92：两列按行对齐后，文字才能和上下边框保持同样的呼吸。
    /// </summary>
    private Border BuildTile(SettingsViewModel.SettingsSectionRow row, bool usage, int index)
    {
        var tile = new Border
        {
            Padding = 0,
            HeightRequest = 92,
            HorizontalOptions = LayoutOptions.Fill,
        };
        TryApplyStyle(tile, "CardSolid");
        HoverFeel.SetEnabled(tile, true);

        var grid = new Grid
        {
            ColumnSpacing = 8,
            RowSpacing = 3,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
            },
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        grid.SetDynamicResource(Microsoft.Maui.Controls.Layout.PaddingProperty, "WPadRow");

        var code = new Label { Text = (index + 1).ToString("00") };
        TryApplyStyle(code, "Eyebrow");

        var chev = new Label { Text = "›", FontSize = 20, VerticalOptions = LayoutOptions.Center };
        chev.SetDynamicResource(Label.TextColorProperty, "WarmFaint");
        Grid.SetRowSpan(chev, 3);

        var title = new Label { Text = row.Title };
        TryApplyStyle(title, "TxtSubhead");
        Grid.SetRow(title, 1);
        Grid.SetColumnSpan(title, 2);

        var body = new Label
        {
            Text = row.Summary,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            VerticalOptions = LayoutOptions.Start,
        };
        TryApplyStyle(body, "TxtRowDesc");
        Grid.SetRow(body, 2);
        Grid.SetColumnSpan(body, 2);

        grid.Add(code, 0, 0);
        grid.Add(chev, 1, 0);
        grid.Children.Add(title);
        grid.Children.Add(body);

        tile.BindingContext = row;
        tile.SetBinding(VisualElement.IsVisibleProperty, "Visible");
        tile.Content = grid;

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
        tile.GestureRecognizers.Add(tap);
        return tile;
    }

    /// <summary>从应用级顶层字典取样式；取不到就什么都不做（别让首页建块失败）。</summary>
    private static void TryApplyStyle(BindableObject target, string key)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Style style)
        {
            target.SetValue(VisualElement.StyleProperty, style);
        }
    }

    private static void TryApplyShape(Border border, string key)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Shape shape)
        {
            border.SetDynamicResource(Border.StrokeShapeProperty, key);
        }
    }
}
