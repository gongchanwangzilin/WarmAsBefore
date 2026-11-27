using System.Text.Json;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.Tools;

/// <summary>
/// 人脸取图工具：AI 可以主动请求「看一眼现在」——拍一帧、本地识别、拿回一句人话结论与这一帧的本机路径。
///
/// <para>
/// <b>只读声明：</b>本工具只返回信息（情绪、年龄、这一帧有没有判断出来）。
/// 它不改任何数值、不推剧情、不触发动作，也不参与任何决策；<c>face_look</c> 的结果只许转述给用户或写进
/// 这一轮的对话，不许拿去改存档状态。图和识别都不出本机。
/// </para>
/// <para>
/// 取帧走系统相机（用户按快门），因此需要：本存档已授权 + 摄像头总开关打开 + 系统给了相机权限。
/// 任一不满足就快速失败并说清该去哪儿开，绝不把这一轮对话吊在那里。
/// </para>
/// </summary>
public sealed partial class ToolManager
{
    /// <summary>
    /// 注册人脸取图工具。由 <see cref="ToolManager"/> 构造函数调用（注册点见 ToolManager.cs）。
    /// </summary>
    private void RegisterFaceImageTools()
    {
        RegisterBuiltin("face_look",
            "看一眼现在的摄像头画面：拍一帧、在本机识别，返回一句文字结论和这一帧的图片文件路径。"
            + "结论里只有情绪、年龄估计、以及这次判断成功与否——这些只是信息，"
            + "不代表任何决策：不要据此改数值、推剧情、触发动作，也不要把结论当事实反复引用。"
            + "只有在用户真的想让角色「看看我」的时候才调用；用户没提就别主动看。"
            + "需要该存档已授权摄像头人脸识别、总开关打开、系统给了相机权限，"
            + "不满足会直接返回一句原因。图像只在本机处理，不上传。",
            async args =>
            {
                var services = ServicesHelper.GetServices();
                var engine = services?.GetService(typeof(GameEngine)) as GameEngine;
                if (engine is null)
                    return Fail("现在没有进入存档，看不了：人脸识别按存档授权，得先在一局里。");

                var saveId = engine.State?.CharacterId ?? "";
                if (string.IsNullOrWhiteSpace(saveId))
                    return Fail("这一局还没有角色 id，人脸识别按存档授权，暂时看不了。");

                FaceLookResult look;
                try { look = await FacePresenceService.Instance.LookNowAsync(saveId); }
                catch (Exception ex)
                {
                    App.WriteLog("ToolManager.face_look -> " + ex.GetType().Name);
                    return Fail("取图时出了点岔子，这次没看成。");
                }

                if (!look.Captured)
                    return JsonSerializer.Serialize(new
                    {
                        ok = false,
                        message = look.Conclusion,
                        hint = "这是信息类工具的失败，别重试，也别自己瞎猜画面里有什么——跟用户说一声「现在看不了」就行。",
                    });

                var analysis = look.Analysis;
                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    conclusion = look.Conclusion,
                    image_path = look.ImagePath ?? "",
                    emotion = analysis?.DominantEmotion ?? "",
                    emotion_cn = FaceAnalysisService.EmotionName(analysis?.DominantEmotion),
                    confidence = analysis is not null && analysis.Emotions.TryGetValue(analysis.DominantEmotion, out var p) ? Math.Round(p, 3) : 0d,
                    age = analysis?.AgeEstimate,
                    recognized = analysis?.Success == true,
                    taken_at = look.At.ToString("yyyy-MM-dd HH:mm:ss"),
                    note = analysis?.Success == true
                        ? "只读信息：可以提一句你看到了什么，但不要据此改数值、推剧情或触发动作。"
                        : "只读信息：这一帧没判断出情绪/年龄，原因见 conclusion；不要编造画面内容。",
                });
            });
    }
}
