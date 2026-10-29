using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 「还没自我介绍的角色不该先有名字」。
/// AI 被允许随意加角色的存档里，新人会直接出现在对话里；在他开口报上名字之前，
/// 界面上先显示 ???，这样「认识一个人」是真的发生过，而不是被系统提前告知。
/// </summary>
public static class CharacterMask
{
    public const string Placeholder = "？？？";

    public static string DisplayName(CharacterData? ch, bool maskUnintroduced)
    {
        if (ch is null) return Placeholder;
        var name = ch.Profile.Name?.Trim() ?? "";
        if (name.Length == 0) return Placeholder;
        if (!maskUnintroduced || ch.Introduced) return name;
        return Placeholder;
    }

    /// <summary>
    /// 从一轮回复里认出「他报了名字」。只认第一人称的说法：
    /// 第三人称提到名字（「她叫林晚」）可能是别人在介绍，但那也算被介绍了——
    /// 由调用方按更宽的规则处理，这里只负责最不会误判的那几种。
    /// </summary>
    public static bool TryMarkIntroduced(CharacterData? ch, string? reply)
    {
        if (ch is null || ch.Introduced || string.IsNullOrWhiteSpace(reply)) return false;
        var name = ch.Profile.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name == Placeholder) return false;

        foreach (var lead in new[] { "我叫", "我是", "我的名字是", "我的名字叫", "叫我" })
        {
            if (reply.Contains(lead + name, StringComparison.Ordinal))
            {
                ch.Introduced = true;
                return true;
            }
        }
        foreach (var lead in new[] { "I'm ", "I am ", "my name is " })
        {
            if (reply.Contains(lead + name, StringComparison.OrdinalIgnoreCase))
            {
                ch.Introduced = true;
                return true;
            }
        }
        return false;
    }
}
