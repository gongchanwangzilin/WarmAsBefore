namespace WarmAsBefore.Services;

/// <summary>
/// 调色页与设置页之间的握手：调色页确认时把颜色放进来，设置页在 <c>OnAppearing</c> 取走。
///
/// 为什么不用 Shell 查询串：<c>GoToAsync("..?color=%23FDF8F0")</c> 要自己转义 <c>#</c>，
/// 回来时再解一次；六位色的井号在 URI 里是片段起点，少转一次就只剩空串，
/// 用户看到的是「选完色回来什么都没变」。一次放进一取，没有字符串编码这一层。
///
/// 取走即清空：<see cref="Take"/> 之后再看就是空的，设置页重进一次不会把上次的颜色再套一遍。
/// 只存一个字符串，没有线程问题（写入与读取都在 UI 线程：确认按钮与页面出现事件）。
/// </summary>
public static class ColorPickerHandoff
{
    /// <summary>确认的颜色（#RRGGBB）；空串 = 用户选了「跟主题走」。</summary>
    public static string? Result { get; private set; }

    /// <summary>有没有待取的结果。设置页只在有结果时才动当前设置。</summary>
    public static bool HasResult => Result is not null;

    /// <summary>调色页确认时调用。</summary>
    public static void Post(string hex) => Result = (hex ?? "").Trim();

    /// <summary>取走结果并清空；没有结果时返回 null（与「取到空串」区分得开）。</summary>
    public static string? Take()
    {
        var r = Result;
        Result = null;
        return r;
    }
}
