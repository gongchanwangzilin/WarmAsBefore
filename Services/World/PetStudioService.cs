using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 桌宠工作室的编排服务：「Q 版化 → 逐状态生图 → 走动/跳跃的多帧循环 → 写包」。
///
/// 创作确实是「代码 + 生图」共同完成的：
/// - 代码这边定死每个状态的帧数与走动的帧率（<see cref="PlanWalkCadence"/>），并把角色特征写成一段
///   每个状态都复用、永不漂移的「特征锁」，保证同一套动画里始终是同一个人；
/// - 生图这边只走现成的 <see cref="ImageGenerationService"/>（和装扮/背景/立绘同一个服务），一帧一次调用，
///   绝不自己写 HTTP 打模型。
///
/// 纪律：
/// - 串行、可取消、逐帧回报真实进度（<see cref="IProgress{T}"/>），没有任何假进度条；
/// - 单帧失败重试一次，仍失败就跳过这一帧、继续下一个状态，不让一颗帧拖垮整套；
/// - 落盘目录与命名沿用素材库那套约定：包本体在 <c>assets/pet/{角色}/</c>，每状态一个子目录，
///   清单 animation.json 容忍缺字段地读回来；
/// - 只用角色自己的文字设定去生图，绝不出任何官方/受版权保护的游戏素材。
/// </summary>
public sealed class PetStudioService
{
    private readonly StorageProvider _store;
    private readonly ImageGenerationService _images;
    private readonly CharacterLibrary _characters;
    private readonly SettingsManager _settings;

    /// <summary>camelCase + 宽容读（注释/尾逗号/大小写都认），与形象包/素材库同一风格。</summary>
    private static readonly JsonSerializerOptions ReadJson = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteJson = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public PetStudioService(
        StorageProvider store,
        ImageGenerationService images,
        CharacterLibrary characters,
        SettingsManager settings)
    {
        _store = store;
        _images = images;
        _characters = characters;
        _settings = settings;
    }

    // ==================== 状态目录 ====================

    /// <summary>
    /// 一个桌宠状态的定义（不含具体角色）：id、中文名、画几帧、循环与否、这一状态要摆的姿势。
    /// 走动/跳跃的帧数不在这里写死——它们由 <see cref="PlanWalkCadence"/> 现场按移动速度算。
    /// </summary>
    public sealed record PetStateDef(
        string Id,
        string Name,
        string PosePrompt,
        bool Loop,
        int FixedFrames)
    {
        /// <summary>是否走「按步频算帧」的循环（走动/原地跑这类）；否则用 FixedFrames。</summary>
        public bool IsLocomotion => FixedFrames == 0;
    }

    /// <summary>
    /// 默认一整套状态。至少覆盖用户点名的 11 个：普通、开心、坐下、站起、走动、跳跃、喜爱、
    /// 生气、流泪、惊讶、睡着。单帧状态 FixedFrames=1；走动 FixedFrames=0 表示「帧数交给步频公式」。
    /// </summary>
    public static IReadOnlyList<PetStateDef> DefaultStates { get; } = new List<PetStateDef>
    {
        new("normal",    "普通",  "自然站立，双手放松垂在身侧，表情平静温和",                 false, 1),
        new("happy",     "开心",  "咧嘴笑，眼睛弯成月牙，双手轻快地抬起一点点",               false, 1),
        new("sit",       "坐下",  "乖巧地盘腿坐在地上，双手放在膝上，身体重心放低",           false, 1),
        new("stand",     "站起",  "从坐姿刚刚站起来的瞬间，微微前倾、双手撑膝正要起身",       false, 1),
        new("walk",      "走动",  "",                                                        true,  0),
        new("jump",      "跳跃",  "",                                                        true,  4),
        new("love",      "喜爱",  "双手在胸前比一个爱心，脸颊微红，眼神温柔地看向镜头",       false, 1),
        new("angry",     "生气",  "鼓起腮帮、双手叉腰，眉毛倒竖，头顶冒出一小团怒气",         false, 1),
        new("cry",       "流泪",  "低着头，眼眶泛红挂着泪珠，双手抹眼泪，一副委屈的样子",     false, 1),
        new("surprised", "惊讶",  "睁大眼睛、张大小嘴，双手捂到脸颊旁，整个人往后一缩",       false, 1),
        new("sleep",     "睡着",  "闭着眼蜷成一团安安静静睡着，头顶飘着几个小睡泡",           false, 1),
    };

    /// <summary>走动循环默认采样帧数（步频公式给出帧率，帧数固定这么高保证跨状态可比）。</summary>
    private const int WalkFrameCount = 6;

    /// <summary>
    /// 一次生图请求的产出尺寸。「更小」主要落在显示端（<see cref="PetAnimationPack.CanvasHeightBaseline"/>），
    /// 这里再给一个偏小的方图，省流量也省得在桌面上铺成大画布。用户可在设置里改 ImageGenSize，这里默认跟它。
    /// </summary>
    private string StudioSize
    {
        get
        {
            var s = _settings.Current.PetStudioImageSize;
            return string.IsNullOrWhiteSpace(s) ? "1024x1024" : s.Trim();
        }
    }

    // ==================== 步频公式 ====================

    /// <summary>走动节拍的计算结果：帧数、帧率、每帧时长（毫秒）。</summary>
    public readonly record struct WalkCadence(int Frames, double Fps, double FrameMs)
    {
        public override string ToString() => $"{Frames} 帧 · {Fps:0.#} fps · {FrameMs:0} ms/帧";
    }

    /// <summary>桌宠在桌面上走动的水平速度（DIP/秒）。取设置，夹在 [40, 240]，非法回落到 120。</summary>
    private double WalkSpeedDipPerSecond
    {
        get
        {
            var v = _settings.Current.PetWalkSpeedDip;
            return !double.IsFinite(v) || v <= 0 ? 120 : Math.Clamp(v, 40, 240);
        }
    }

    /// <summary>
    /// 走动循环的帧数与帧率——「创作靠代码算节拍」的核心，全程可解释、无写死的魔法数。
    ///
    /// 思路（不滑步的标准做法）：一步的落脚位移 ≈ 半个身位，一个完整循环 = 左右各一步。
    /// 于是循环周期（走一步幅所需的时间）与角色实际移动速度绑定：
    ///   stridePx   = stepFactor × 屏幕上的身体高度      （一个循环走过的距离，DIP）
    ///   cycleSeconds = stridePx ÷ speed                 （走完这一循环要几秒）
    ///   fps        = Frames ÷ cycleSeconds              （要让它看起来不滑步，就该这么快）
    /// 再把 fps 夹进 6–10 这一档（低于 6 卡顿、高于 10 对 Q 版没必要且费素材），
    /// 帧数固定为 <see cref="WalkFrameCount"/>（4–8 之间的 6）。身体高度用基准画布高乘当前 PetScale 估。
    /// 宿主真正播放时仍可在此基础上微调，但包里的 fps 就是这套算法给出的值。
    /// </summary>
    public WalkCadence PlanWalkCadence()
    {
        var baseline = Math.Clamp(_settings.Current.PetStudioCanvasHeight, 96, 720);
        var scale = Math.Clamp(_settings.Current.PetScale, 0.4, 2.6);
        var bodyHeightDip = baseline * scale;
        if (!double.IsFinite(bodyHeightDip) || bodyHeightDip <= 0) bodyHeightDip = baseline;

        const double stepFactor = 1.0;   // 一个完整循环（左右各一步）走过的距离 ≈ 1 个身位高
        var stridePx = Math.Max(1, stepFactor * bodyHeightDip);
        var cycleSeconds = stridePx / WalkSpeedDipPerSecond;
        var fps = cycleSeconds > 0 ? WalkFrameCount / cycleSeconds : WalkFrameCount;
        fps = Math.Clamp(fps, 6, 10);

        var frameMs = fps > 0 ? 1000.0 / fps : 1000.0 / WalkFrameCount;
        return new WalkCadence(WalkFrameCount, Math.Round(fps, 2), Math.Round(frameMs, 1));
    }

    // ==================== 进度契约 ====================

    /// <summary>
    /// 逐帧回报的真实进度。宿主把它直接接到进度条/文案上，绝不用定时器伪造。
    /// Overall 是「已完成生图调用数 ÷ 计划生图调用总数」（0~1），单帧重试不重复计入分母。
    /// </summary>
    public sealed record PetStudioProgress(
        string Phase,
        string StateId,
        string StateName,
        int FrameIndex,
        int FrameTotal,
        int DoneStates,
        int TotalStates,
        int DoneCalls,
        int TotalCalls,
        double Overall,
        string Message,
        bool Failed,
        string? LastFrameRel);

    /// <summary>一次「生成整套」的最终结果。失败也返回对象，UI 直接看 Message。</summary>
    public sealed record PetStudioResult(
        bool Success,
        string Message,
        PetAnimationPack? Pack,
        int GeneratedStates,
        int GeneratedFrames,
        int FailedFrames,
        bool Cancelled)
    {
        public static PetStudioResult Fail(string message)
            => new(false, message, null, 0, 0, 0, false);
    }

    // ==================== 落盘目录 ====================

    /// <summary>某个角色的桌宠包目录（绝对路径）。id 非法回落到 _store.Root 下的 pet 根。</summary>
    public string PackDirectory(string characterId)
        => Path.Combine(_store.Root, "assets", "pet", SafeSegment(characterId));

    /// <summary>包清单绝对路径。</summary>
    public string ManifestPath(string characterId) => Path.Combine(PackDirectory(characterId), "animation.json");

    // ==================== 生成编排 ====================

    /// <summary>
    /// 给一个角色生成整套桌宠动画。串行出图、可取消、逐帧回报进度；单帧失败重试一次。
    /// <paramref name="states"/> 不传就用 <see cref="DefaultStates"/>。返回带素材包与统计的结果。
    /// </summary>
    public async Task<PetStudioResult> GenerateAsync(
        string characterId,
        IProgress<PetStudioProgress>? progress,
        CancellationToken ct,
        IReadOnlyList<PetStateDef>? states = null)
    {
        var defs = states ?? DefaultStates;
        if (defs.Count == 0) return PetStudioResult.Fail("没有要生成的状态。");

        if (!_images.Enabled)
            return PetStudioResult.Fail("画面生成没有开启（设置 → AI → 画面生成 里填接口与密钥）。");

        var character = await FindCharacterAsync(characterId).ConfigureAwait(false);
        if (character is null) return PetStudioResult.Fail("找不到这个角色，没法为它做桌宠。");

        // 先数一遍计划内的生图调用，作为真实进度的分母：走动按步频定帧数，其余按 FixedFrames。
        var cadence = PlanWalkCadence();
        var plan = new List<(PetStateDef Def, int Frames, double Fps)>();
        foreach (var d in defs)
        {
            var frames = d.IsLocomotion ? cadence.Frames : Math.Max(1, d.FixedFrames);
            // 走动用步频算出的 fps；其余（含循环的单/多帧）先按默认循环帧率登记，宿主播放时可再微调。
            var fps = d.IsLocomotion ? cadence.Fps : PetAnimationPack.DefaultLoopFps;
            plan.Add((d, frames, fps));
        }
        var totalCalls = plan.Sum(p => p.Frames);
        var doneCalls = 0;

        var packDir = PackDirectory(character.Profile.Id);
        Directory.CreateDirectory(packDir);

        var featureLock = BuildFeatureLock(character);
        var statesOut = new List<PetAnimationState>();
        var generatedStates = 0;
        var generatedFrames = 0;
        var failedFrames = 0;
        var doneStates = 0;

        foreach (var (def, frames, fps) in plan)
        {
            ct.ThrowIfCancellationRequested();
            doneStates = statesOut.Count;

            var stateFrames = new List<string>();
            var stateFailed = 0;

            for (var i = 0; i < frames; i++)
            {
                ct.ThrowIfCancellationRequested();

                var pose = DescribePose(def, i, frames);
                var prompt = BuildFramePrompt(featureLock, def, pose, i, frames);
                var tag = "pet_" + SafeSegment(character.Profile.Id) + "_" + SafeSegment(def.Id) + "_" + i.ToString("00");

                progress?.Report(new PetStudioProgress(
                    "生成中", def.Id, def.Name, i + 1, frames, doneStates, plan.Count,
                    doneCalls, totalCalls, Ratio(doneCalls, totalCalls),
                    $"正在画「{def.Name}」第 {i + 1}/{frames} 帧…", false, null));

                var (path, message) = await GenerateOneWithRetryAsync(prompt, tag, ct).ConfigureAwait(false);
                doneCalls++;

                if (path is null)
                {
                    failedFrames++;
                    stateFailed++;
                    progress?.Report(new PetStudioProgress(
                        "生成中", def.Id, def.Name, i + 1, frames, doneStates, plan.Count,
                        doneCalls, totalCalls, Ratio(doneCalls, totalCalls),
                        $"「{def.Name}」第 {i + 1} 帧没出成：{message}（先跳过，继续画后面的）", true, null));
                    continue;
                }

                // 把生图服务落在 generated/ 的临时图，收编进包目录里，包才自洽可搬走。
                var destRel = SaveFrameIntoPack(packDir, def.Id, i, path);
                if (destRel is null)
                {
                    failedFrames++;
                    stateFailed++;
                    continue;
                }
                stateFrames.Add(destRel);
                generatedFrames++;
                progress?.Report(new PetStudioProgress(
                    "已出帧", def.Id, def.Name, i + 1, frames, doneStates, plan.Count,
                    doneCalls, totalCalls, Ratio(doneCalls, totalCalls),
                    $"「{def.Name}」第 {i + 1}/{frames} 帧已出。", false, destRel));
            }

            var complete = stateFailed == 0 && stateFrames.Count == frames;
            statesOut.Add(new PetAnimationState
            {
                Id = def.Id,
                Name = def.Name,
                Frames = stateFrames,
                Fps = Math.Round(fps, 2),
                Loop = def.Loop,
                IsComplete = complete,
            });
            if (stateFrames.Count > 0) generatedStates++;
        }

        var pack = new PetAnimationPack
        {
            CharacterId = character.Profile.Id,
            CharacterName = character.Profile.Name,
            CanvasHeightBaseline = Math.Clamp(_settings.Current.PetStudioCanvasHeight, 96, 720),
            GeneratedAt = DateTime.UtcNow,
            States = statesOut,
        };

        var writeError = await WritePackAsync(pack).ConfigureAwait(false);
        if (writeError is not null)
            return new PetStudioResult(false, writeError, pack, generatedStates, generatedFrames, failedFrames, false);

        progress?.Report(new PetStudioProgress(
            "完成", "", "", 0, 0, plan.Count, plan.Count, totalCalls, totalCalls, 1.0,
            $"桌宠素材包已就绪：{generatedStates} 个状态 · {generatedFrames} 帧。", false, null));

        var tail = failedFrames > 0 ? $"（有 {failedFrames} 帧没出成，可重跑补齐）" : "";
        var ok = generatedStates > 0;
        return new PetStudioResult(
            ok,
            ok ? $"已生成 {generatedStates}/{plan.Count} 个状态、共 {generatedFrames} 帧{tail}。"
               : $"一个状态都没能出图，先检查画面生成是否配置好。{tail}",
            pack, generatedStates, generatedFrames, failedFrames, false);
    }

    /// <summary>取消感知的单帧生成：失败重试一次；两次都不行才认失败。取消则原样抛出。</summary>
    private async Task<(string? Path, string Message)> GenerateOneWithRetryAsync(
        string prompt, string tag, CancellationToken ct)
    {
        var (path, message) = await _images.GenerateAsync(prompt, tag, StudioSize).ConfigureAwait(false);
        if (path is not null) return (path, message);
        if (ct.IsCancellationRequested) { ct.ThrowIfCancellationRequested(); return (null, message); }

        // 只重试一次，不无限重试、不整体失败：网络抖一下就重画这张。
        var retry = await _images.GenerateAsync(prompt, tag, StudioSize).ConfigureAwait(false);
        return retry.Path is not null ? (retry.Path, retry.Message) : (null, message);
    }

    // ==================== 提示词 ====================

    /// <summary>
    /// 跨状态永不变的「特征锁」：把角色的名字、性别、性格、设定描述压成一段固定措辞，
    /// 每个状态都原样带上前缀——这是保证「同一套动画始终是同一个人」的关键。
    /// 只喂角色自己的文字设定，不含任何官方/受版权保护的形象。
    /// </summary>
    private static string BuildFeatureLock(CharacterData ch)
    {
        var p = ch.Profile;
        var bits = new StringBuilder();
        bits.Append($"角色「{Trim(p.Name, 40)}」");
        if (!string.IsNullOrWhiteSpace(p.Gender)) bits.Append($"，性别{Trim(p.Gender, 8)}");
        if (!string.IsNullOrWhiteSpace(p.Personality)) bits.Append($"，性格：{Trim(p.Personality, 60)}");
        if (!string.IsNullOrWhiteSpace(p.Description)) bits.Append($"，外貌与设定：{Trim(p.Description, 120)}");
        return bits.ToString();
    }

    /// <summary>某一帧的姿势描述。走动按相位分腿臂，其余状态沿用定义里那句。</summary>
    private static string DescribePose(PetStateDef def, int index, int total)
    {
        if (!string.IsNullOrWhiteSpace(def.PosePrompt)) return def.PosePrompt;

        if (def.Id == "walk")
        {
            // 6 帧循环：左右接触位、下落、经过、抬起——经典步态的相位，代码定死保证一致。
            var phases = new[]
            {
                "左脚在前、右脚在后，正迈步的接触位",
                "重心下沉，前脚踩实、后脚脚跟抬起",
                "双腿交叠经过中线，身体略微升起",
                "右脚在前、左脚在后，反方向的接触位",
                "重心再次下沉，换另一只脚踩实",
                "双腿再度经过中线，回到接近起势的姿态",
            };
            return "侧面行走的循环动画里的一帧：" + phases[index % phases.Length];
        }

        if (def.Id == "jump")
        {
            var phases = new[]
            {
                "屈膝下蹲、双臂后摆，正要起跳",
                "蹬地腾空、双臂上举，身体拉直",
                "升到最高点、四肢微收，头发飘起",
                "落地屈膝缓冲、双臂前伸找平衡",
            };
            return "竖直跳跃的循环动画里的一帧：" + phases[index % phases.Length];
        }

        return "自然站立的一帧。";
    }

    /// <summary>拼一帧完整提示词：特征锁 + Q 版风格 + 姿势 + 可抠背景 + 一致性约束。</summary>
    private static string BuildFramePrompt(string featureLock, PetStateDef def, string pose, int index, int total)
    {
        var multi = total > 1
            ? $"这是「{def.Name}」循环的第 {index + 1}/{total} 帧，请保持与同组其它帧完全相同的人物比例与画风，只改变这个动作的相位。"
            : "";
        return
            $"把「{featureLock}」Q 版化成二头身/chibi 桌宠形象：圆脸大眼、头身比约 2 头身、线条干净、平涂上色。" +
            $"动作/表情：{pose}。{multi}" +
            "全身完整入画，居中，脚下留少量空隙。" +
            "背景为纯透明或单一纯色可抠背景（绿幕或纯洋红皆可），绝对不要场景、道具、文字、水印、边框。" +
            "不要参考或模仿任何现有游戏/动漫的官方角色与素材，只依据上面这段人物描述来画。";
    }

    private static double Ratio(int done, int total) => total <= 0 ? 0 : Math.Clamp((double)done / total, 0, 1);

    // ==================== 收编与读写 ====================

    /// <summary>把 generated/ 里的临时图复制进包目录 <c>{stateId}/frame_{NN}.png</c>，返回相对存储根路径。</summary>
    private string? SaveFrameIntoPack(string packDir, string stateId, int index, string absSource)
    {
        try
        {
            var ext = Path.GetExtension(absSource);
            if (string.IsNullOrEmpty(ext)) ext = ".png";
            var stateDir = Path.Combine(packDir, SafeSegment(stateId));
            Directory.CreateDirectory(stateDir);
            var fileName = $"frame_{index:00}{ext.ToLowerInvariant()}";
            var dest = Path.Combine(stateDir, fileName);
            File.Copy(absSource, dest, overwrite: true);
            return Path.GetRelativePath(_store.Root, dest).Replace('\\', '/');
        }
        catch (Exception ex)
        {
            App.WriteLog("PetStudio.SaveFrame -> " + ex.Message);
            return null;
        }
    }

    /// <summary>读回一个角色的桌宠包；缺文件/坏 JSON 都返回 null（容忍缺字段则靠 Normalize）。</summary>
    public async Task<PetAnimationPack?> LoadPackAsync(string characterId)
    {
        try
        {
            var file = ManifestPath(characterId);
            if (!File.Exists(file)) return null;
            var json = await File.ReadAllTextAsync(file).ConfigureAwait(false);
            var pack = JsonSerializer.Deserialize<PetAnimationPack>(json, ReadJson);
            return pack is null ? null : Normalize(pack, characterId);
        }
        catch (Exception ex)
        {
            App.WriteLog("PetStudio.LoadPack -> " + ex.Message);
            return null;
        }
    }

    /// <summary>把包写成 animation.json。返回 null 表示成功，否则一句人话错误。</summary>
    public async Task<string?> WritePackAsync(PetAnimationPack pack)
    {
        string? tmp = null;
        try
        {
            var dir = PackDirectory(pack.CharacterId);
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "animation.json");
            var json = JsonSerializer.Serialize(pack, WriteJson);
            tmp = file + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            await File.WriteAllTextAsync(tmp, json, new UTF8Encoding(false)).ConfigureAwait(false);
            File.Move(tmp, file, overwrite: true);
            return null;
        }
        catch (Exception ex)
        {
            App.WriteLog("PetStudio.WritePack -> " + ex);
            return "桌宠素材包写盘失败：" + ex.Message;
        }
        finally
        {
            if (tmp is not null) { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
        }
    }

    /// <summary>删除某角色的桌宠包（连帧目录一起），用于「重做」。返回是否删过东西。</summary>
    public bool DeletePack(string characterId)
    {
        try
        {
            var dir = PackDirectory(characterId);
            if (!Directory.Exists(dir)) return false;
            Directory.Delete(dir, recursive: true);
            return true;
        }
        catch (Exception ex)
        {
            App.WriteLog("PetStudio.DeletePack -> " + ex.Message);
            return false;
        }
    }

    /// <summary>把相对路径绝对化（宿主贴帧时用它把 <see cref="PetAnimationPack.FramesOf"/> 变成真文件）。</summary>
    public string ResolveAbs(string rel)
    {
        if (string.IsNullOrWhiteSpace(rel)) return "";
        if (Path.IsPathRooted(rel)) return rel;
        return Path.Combine(_store.Root, rel.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>外部（设置/宿主）改体型时，按当前步频刷新一套循环的 fps（不重画，只改数值）。</summary>
    public PetAnimationPack RefreshCadence(PetAnimationPack pack)
    {
        var cadence = PlanWalkCadence();
        var states = pack.States.Select(s => s with
        {
            Fps = string.Equals(s.Id, "walk", StringComparison.OrdinalIgnoreCase) ? cadence.Fps : s.Fps,
        }).ToList();
        return pack with { States = states };
    }

    // ==================== 小工具 ====================

    private async Task<CharacterData?> FindCharacterAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var list = await _characters.ListAsync().ConfigureAwait(false);
        return list.FirstOrDefault(c => string.Equals(c.Profile.Id, id, StringComparison.Ordinal));
    }

    private static PetAnimationPack Normalize(PetAnimationPack pack, string fallbackId)
    {
        var states = (pack.States ?? new List<PetAnimationState>())
            .Where(s => s is not null)
            .Select(s => s with
            {
                Id = s.Id ?? "",
                Name = string.IsNullOrWhiteSpace(s.Name) ? (s.Id ?? "") : s.Name,
                Frames = (s.Frames ?? new List<string>()).Where(f => !string.IsNullOrWhiteSpace(f)).ToList(),
                Fps = s.Fps > 0 ? s.Fps : PetAnimationPack.DefaultLoopFps,
            })
            .ToList();

        return pack with
        {
            CharacterId = string.IsNullOrWhiteSpace(pack.CharacterId) ? fallbackId : pack.CharacterId,
            CanvasHeightBaseline = pack.CanvasHeightBaseline > 0 ? pack.CanvasHeightBaseline : PetAnimationPack.DefaultCanvasHeight,
            States = states,
        };
    }

    /// <summary>把任意 id/状态名削成能当目录/文件名用的一截 ASCII（中文等非字母数字压成下划线）。</summary>
    private static string SafeSegment(string raw)
    {
        var sb = new StringBuilder(raw?.Length ?? 0);
        foreach (var ch in raw ?? "")
            sb.Append(char.IsLetterOrDigit(ch) && ch < 128 ? ch : '_');
        var s = sb.ToString().Trim('_');
        return s.Length == 0 ? "x" : (s.Length > 40 ? s[..40] : s);
    }

    private static string Trim(string s, int max)
    {
        s = (s ?? "").Trim();
        return s.Length <= max ? s : s[..max] + "…";
    }
}
