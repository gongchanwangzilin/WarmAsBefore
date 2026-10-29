using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>用量统计页：总览、成本构成、Token 活跃度、时间序列、分组明细与单价设定。</summary>
public partial class UsageStatsPage : ContentPage
{
    public UsageStatsPage(UsageStatsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        // 宽窄切换跟着实际宽度走（项目里没有断点基建，这里用 SizeChanged 手动摆）
        SizeChanged += (_, _) => ApplyLayout();
        Loaded += (_, _) => ApplyLayout();
        ApplyLayout();
    }

    private bool _wide = true;
    private bool _applied;

    private void ApplyLayout()
    {
        var wide = Width >= 820;
        if (_applied && wide == _wide) return;
        _applied = true;
        _wide = wide;

        Grid.SetColumnSpan(HeaderGrid, wide ? 2 : 1);
        if (wide)
        {
            BodyGrid.ColumnDefinitions = new ColumnDefinitionCollection {
                new ColumnDefinition(new GridLength(1.7, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(1, GridUnitType.Star))
            };
            BodyGrid.RowDefinitions = new RowDefinitionCollection {
                new RowDefinition(new GridLength(1, GridUnitType.Auto)),
                new RowDefinition(new GridLength(1, GridUnitType.Auto))
            };
            Grid.SetRow(LeftColumn, 1); Grid.SetColumn(LeftColumn, 0);
            Grid.SetRow(RightColumn, 1); Grid.SetColumn(RightColumn, 1);
        }
        else
        {
            BodyGrid.ColumnDefinitions = new ColumnDefinitionCollection {
                new ColumnDefinition(new GridLength(1, GridUnitType.Star))
            };
            BodyGrid.RowDefinitions = new RowDefinitionCollection {
                new RowDefinition(new GridLength(1, GridUnitType.Auto)),
                new RowDefinition(new GridLength(1, GridUnitType.Auto)),
                new RowDefinition(new GridLength(1, GridUnitType.Auto))
            };
            Grid.SetRow(LeftColumn, 1); Grid.SetColumn(LeftColumn, 0);
            Grid.SetRow(RightColumn, 2); Grid.SetColumn(RightColumn, 0);
        }
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        try { await Shell.Current.GoToAsync(".."); }
        catch (Exception ex) { App.WriteLog("UsageStatsPage.Back -> " + ex.Message); }
    }
}
