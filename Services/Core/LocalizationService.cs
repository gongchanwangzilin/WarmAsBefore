using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace WarmAsBefore.Services;

/// <summary>
/// 运行时本地化服务：切换语言时立即刷新所有绑定到它的 XAML 文本。
/// 添加新语言 = 在 Resources/Strings/ 下新增 AppResources.&lt;culture&gt;.resx（键名与默认文件一致）。
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    public static LocalizationService Current { get; } = new();

    private static readonly ResourceManager Res =
        new("WarmAsBefore.Resources.Strings.AppResources", typeof(LocalizationService).Assembly);

    private CultureInfo _culture = CultureInfo.GetCultureInfo("zh-CN");

    public CultureInfo Culture => _culture;

    /// <summary>XAML 用法：Text="{Binding [KeyName], Source={x:Static i18n:LocalizationService.Current}}"</summary>
    public string this[string key] => Res.GetString(key, _culture) ?? key;

    public void SetCulture(string cultureName)
    {
        try
        {
            var c = CultureInfo.GetCultureInfo(cultureName);
            _culture = c;
            // 同步环境 culture，让日期/数字格式化与资源回退保持一致
            CultureInfo.CurrentUICulture = c;
            CultureInfo.CurrentCulture = c;
        }
        catch
        {
            _culture = CultureInfo.GetCultureInfo("zh-CN");
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
