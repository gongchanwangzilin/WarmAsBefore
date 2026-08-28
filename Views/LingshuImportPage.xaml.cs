using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Essentials;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.Views;

public partial class LingshuImportPage : ContentPage
{
    private readonly LingshuImporter _importer;
    private readonly GameEngine _engine;
    private string? _selectedFilePath;
    private LingshuImporter.ValidationResult? _validatedResult;

    public LingshuImportPage(LingshuImporter importer, GameEngine engine)
    {
        InitializeComponent();
        _importer = importer;
        _engine = engine;
        
        // 默认选中创建新角色
        ModePicker.SelectedIndex = 0;
    }

    private async void OnSelectFileClicked(object sender, EventArgs e)
    {
        try
        {
            var result = await PickAndOpenAsync();
            if (result is null) return;

            _selectedFilePath = result.Path;
            FilePathLabel.Text = Path.GetFileName(_selectedFilePath);
            StatusLabel.Text = "文件已选择，点击「验证文件」检查格式";
            
            // 禁用导入按钮，等待验证
            ImportButton.IsEnabled = false;
            PreviewFrame.IsVisible = false;
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"选择文件失败：{ex.Message}";
            StatusLabel.TextColor = Colors.Red;
        }
    }

    private async void OnValidateClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedFilePath))
        {
            StatusLabel.Text = "请先选择文件";
            StatusLabel.TextColor = Colors.Orange;
            return;
        }

        StatusLabel.Text = "正在验证...";
        StatusLabel.TextColor = Colors.Gray;

        var result = await _importer.ValidateAsync(_selectedFilePath);
        _validatedResult = result;

        if (result.Valid)
        {
            StatusLabel.Text = "✓ 文件格式正确";
            StatusLabel.TextColor = Colors.Green;
            ImportButton.IsEnabled = true;

            // 显示预览
            PreviewNameLabel.Text = $"角色名称：{result.CharacterName}";
            PreviewMemoriesLabel.Text = result.HasMemories 
                ? $"记忆片段：{result.MemoryCount} 条" 
                : "记忆片段：无";
            PreviewDialoguesLabel.Text = result.HasDialogues 
                ? $"对话历史：{result.DialogueCount} 条" 
                : "对话历史：无";
            PreviewFrame.IsVisible = true;
        }
        else
        {
            StatusLabel.Text = $"✗ {result.ErrorMessage}";
            StatusLabel.TextColor = Colors.Red;
        }
    }

    private async void OnImportClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedFilePath) || _validatedResult is null || !_validatedResult.Valid)
        {
            StatusLabel.Text = "请先验证文件";
            StatusLabel.TextColor = Colors.Orange;
            return;
        }

        // 获取导入模式
        var mode = ModePicker.SelectedIndex switch
        {
            0 => LingshuImporter.ImportMode.CreateNew,
            1 => LingshuImporter.ImportMode.Overwrite,
            2 => LingshuImporter.ImportMode.Append,
            _ => LingshuImporter.ImportMode.CreateNew
        };

        StatusLabel.Text = "正在导入...";
        StatusLabel.TextColor = Colors.Gray;
        ImportButton.IsEnabled = false;

        var result = await _importer.ImportAsync(_selectedFilePath, mode);

        if (result.Success)
        {
            StatusLabel.Text = $"✓ 导入成功！已导入角色「{result.CharacterName}」";
            StatusLabel.TextColor = Colors.Green;
            
            // 延迟返回
            await Task.Delay(1500);
            await Navigation.PopAsync();
        }
        else
        {
            StatusLabel.Text = $"✗ {result.ErrorMessage}";
            StatusLabel.TextColor = Colors.Red;
            ImportButton.IsEnabled = true;
        }
    }

    private async Task<(string Path)?> PickAndOpenAsync()
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "选择灵枢 JSON 文件",
                FileTypes = FilePickerFilters.Custom(new[] 
                { 
                    ("JSON", new[] { ".json" }),
                    ("所有文件", new[] { "*/*" })
                })
            });

            if (result is null) return null;

            return (result.Path,);
        }
        catch (Exception ex)
        {
            App.WriteLog($"LingshuImportPage.PickAndOpen -> {ex}");
            throw;
        }
    }
}
