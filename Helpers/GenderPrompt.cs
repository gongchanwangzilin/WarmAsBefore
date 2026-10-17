namespace WarmAsBefore.Helpers;

/// <summary>
/// 性别采集：女 / 男 / 其他 三个常用值，外加「自定义…」自由输入。
///
/// 性别是必填项，没有默认值：它决定这个角色去查哪一条周期规则。留空或者替用户猜一个，
/// 结果是把周期算到错的人身上（或者干脆算不出来），所以宁可让用户多点一下。
/// 返回 null 表示用户放弃（取消、或自定义里什么都没写），调用方据此中止创建。
/// </summary>
public static class GenderPrompt
{
    public const string CustomChoice = "自定义…";

    public static async Task<string?> PickAsync(string title)
    {
        var pick = await Shell.Current.DisplayActionSheet(title, "取消", null, "女", "男", "其他", CustomChoice);
        if (string.IsNullOrEmpty(pick) || pick == "取消") return null;
        if (pick != CustomChoice) return pick;

        var input = await Shell.Current.DisplayPromptAsync(title, "TA 的性别（自己写，例如：机器人 / 双性 / 精灵）：",
            "确定", "取消", maxLength: 12);
        var custom = (input ?? "").Trim();
        return custom.Length == 0 ? null : custom;
    }
}
