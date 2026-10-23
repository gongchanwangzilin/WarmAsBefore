using WarmAsBefore.Modules.Tools;

namespace WarmAsBefore.Modules.MinecraftBridge;

/// <summary>
/// 把桥的能力暴露成聊天工具。工具名与模组指令一一对应，参数结构就是模组侧的线上契约。
/// 工具只是入口：是否连接、女仆是谁、动作是否被插件设置放行，都由 <see cref="MaidBridgeService.RunToolAsync"/> 判定。
/// </summary>
internal static class MaidBridgeTools
{
    public static void Register(MaidBridgeService bridge, ToolManager tools)
    {
        tools.RegisterBuiltin("maid_status",
            "查看 Minecraft 里女仆此刻的真实状况：所在维度、时间与天气、生命与饥饿、坐标、背包、手上的任务、"
            + "附近玩家，以及爱恋模组里的信任/恐惧/饱食。要回答「你现在怎么样」「你在哪」，或者行动前确认状态时先调用它。"
            + "返回的是游戏里的真实值，不要凭想象作答。",
            args => bridge.RunToolAsync("status", args),
            StatusSchema);

        tools.RegisterBuiltin("maid_scan",
            "让女仆用感官环顾四周：附近的生物与玩家、矿石与可交互方块、水与岩浆、亮度。"
            + "想挖矿、找水、判断危险或看看身边有什么时，先扫描再决定去哪里、做什么。半径默认 16 格。",
            args => bridge.RunToolAsync("scan", args),
            ScanSchema);

        tools.RegisterBuiltin("maid_go",
            "让女仆移动身体。mode=to 去坐标或目标，near 靠近目标，follow 跟随玩家，wait 原地等，"
            + "home 回家，wander 在附近随意走走。远距离由模组寻路，她会真的走过去，不会瞬移。",
            args => bridge.RunToolAsync("go", args),
            GoSchema);

        tools.RegisterBuiltin("maid_gather",
            "让女仆真的动手采集：mine 挖矿（filter=ores 只挖矿，也可给方块 id），collect 捡起附近掉落物，"
            + "chop 砍树，harvest 收作物，scoop 舀水或岩浆，fill 填坑或放置。"
            + "长动作在游戏里分 tick 执行；返回实际挖到/拿到的东西，没做到会说明原因。",
            args => bridge.RunToolAsync("gather", args),
            GatherSchema);

        tools.RegisterBuiltin("maid_hold",
            "让女仆处理物品与投喂：give 给玩家物品，drop 丢下，take 从容器或地上拿，"
            + "feed_player 直接右键把食物喂给玩家（不需要把食物放在地上），place 放置，equip 装备。",
            args => bridge.RunToolAsync("hold", args),
            HoldSchema);

        tools.RegisterBuiltin("maid_act",
            "让女仆做一个具体动作：attack 攻击目标，use 使用或右键方块与实体，"
            + "gesture 主动做预设动作（如 hug 抱抱、wave 招手），say 说一句她自己的台词，"
            + "sit/stand/sleep 坐下/站起/睡觉，look 看向目标，interact_addon 触发附属模组里本由玩家发起的行为。",
            args => bridge.RunToolAsync("act", args),
            ActSchema);

        tools.RegisterBuiltin("maid_plan",
            "读写女仆的长期日程与自发行为。action=set 用 steps 设置一天的计划（at 为游戏内 HH:mm，do 为动作，"
            + "可带 random 执行概率与 hours 时长），action=get 查看，action=clear 清空；"
            + "autonomy 控制空闲时是否自己找事做。日程由模组在游戏里本地执行，不需要你每分钟盯着。",
            args => bridge.RunToolAsync("plan", args),
            PlanSchema);

        tools.RegisterBuiltin("maid_bind",
            "把 Minecraft 女仆绑定到 WAB 角色。character 给角色名或 Id；create=true 找不到就按女仆名新建角色；"
            + "newSave=true 新建或分支一份 WAB 存档再绑定。女仆太多、性格差异大时，可以为每个女仆建独立角色。",
            args => bridge.RunToolAsync("bind", args),
            BindSchema);

        tools.RegisterBuiltin("maid_backup",
            "把女仆的完整 NBT（背包、状态、AI、日程等）快照备份进当前 WAB 存档。"
            + "重要行动前后可以主动备份；平时由桥按设置定时备份。",
            args => bridge.RunToolAsync("backup", args),
            BackupSchema);
    }

    private const string MaidProperty =
        @"""maid"":{""type"":""string"",""description"":""女仆名或 UUID；留空则用当前绑定的女仆""}";

    private static readonly string StatusSchema =
        """{"type":"object","properties":{""" + MaidProperty + """},"additionalProperties":false}""";

    private static readonly string ScanSchema =
        """{"type":"object","properties":{""" + MaidProperty + """
        ,"radius":{"type":"number","description":"扫描半径（格），默认 16，最大 64"},
        "entities":{"type":"boolean","description":"是否列出附近生物与玩家，默认 true"},
        "blocks":{"type":"boolean","description":"是否列出矿石与可交互方块，默认 true"},
        "fluids":{"type":"boolean","description":"是否找水与岩浆，默认 true"},
        "light":{"type":"boolean","description":"是否读取亮度，默认 true"}
        },"additionalProperties":false}
        """;

    private static readonly string GoSchema =
        """{"type":"object","properties":{""" + MaidProperty + """
        ,"mode":{"type":"string","enum":["to","near","follow","wait","home","wander"],"description":"移动方式"},
        "target":{"type":"string","description":"玩家名、实体名或角色名（mode=follow/near/to 时用）"},
        "pos":{"type":"array","items":{"type":"number"},"minItems":3,"maxItems":3,"description":"目标坐标 [x,y,z]"},
        "distance":{"type":"number","description":"near 时保持的距离（格），默认 2"}
        },"required":["mode"],"additionalProperties":false}
        """;

    private static readonly string GatherSchema =
        """{"type":"object","properties":{""" + MaidProperty + """
        ,"action":{"type":"string","enum":["mine","collect","chop","harvest","scoop","fill"],"description":"采集方式"},
        "pos":{"type":"array","items":{"type":"number"},"minItems":3,"maxItems":3,"description":"指定作业点；留空则由女仆自己找"},
        "filter":{"type":"string","description":"mine 的目标：ores / 方块 id；scoop 可给 water 或 lava"},
        "radius":{"type":"number","description":"搜索半径（格），默认 16"},
        "count":{"type":"integer","description":"最多处理多少个方块/物品"}
        },"required":["action"],"additionalProperties":false}
        """;

    private static readonly string HoldSchema =
        """{"type":"object","properties":{""" + MaidProperty + """
        ,"action":{"type":"string","enum":["give","drop","take","feed_player","place","equip"],"description":"物品动作"},
        "item":{"type":"string","description":"物品 id 或名称；feed_player 可给 food"},
        "count":{"type":"integer","description":"数量，默认 1"},
        "target":{"type":"string","description":"目标玩家/实体/容器名"},
        "pos":{"type":"array","items":{"type":"number"},"minItems":3,"maxItems":3,"description":"place 时的目标坐标"}
        },"required":["action"],"additionalProperties":false}
        """;

    private static readonly string ActSchema =
        """{"type":"object","properties":{""" + MaidProperty + """
        ,"action":{"type":"string","enum":["attack","use","gesture","say","sit","stand","sleep","look","interact_addon"],"description":"动作类型"},
        "target":{"type":"string","description":"目标玩家/生物/方块/角色"},
        "text":{"type":"string","description":"say 时的台词"},
        "arg":{"type":"string","description":"gesture 的动作名（如 hug、wave）；interact_addon 的行为 id"},
        "duration":{"type":"number","description":"持续时间（秒）"}
        },"required":["action"],"additionalProperties":false}
        """;

    private static readonly string PlanSchema =
        """{"type":"object","properties":{""" + MaidProperty + """
        ,"action":{"type":"string","enum":["set","clear","get"],"description":"设置 / 清空 / 查看日程"},
        "steps":{"type":"array","description":"一天的计划步骤","items":{"type":"object","properties":{
          "at":{"type":"string","description":"游戏内时刻 HH:mm"},
          "do":{"type":"string","description":"动作：wander/mine/collect/scoop/feed_player/gesture/say/sit/sleep 等"},
          "random":{"type":"number","description":"执行概率 0~1"},
          "hours":{"type":"number","description":"持续游戏内小时"},
          "filter":{"type":"string","description":"mine/scoop 的目标"},
          "arg":{"type":"string","description":"gesture 动作名或 interact_addon 行为 id"},
          "text":{"type":"string","description":"say 的台词"},
          "target":{"type":"string","description":"动作目标"},
          "item":{"type":"string","description":"涉及的物品"},
          "count":{"type":"integer","description":"数量上限"}
        },"required":["at","do"],"additionalProperties":true}},
        "autonomy":{"type":"object","description":"空闲时的自发行为","properties":{
          "enabled":{"type":"boolean"},
          "idleMinutes":{"type":"integer","description":"空闲多久后自己找事做"},
          "maxActionsPerHour":{"type":"integer","description":"每小时最多自发行动次数"},
          "wanderRadius":{"type":"integer","description":"活动半径（格）"}
        },"additionalProperties":false}
        },"required":["action"],"additionalProperties":false}
        """;

    private static readonly string BindSchema =
        """{"type":"object","properties":{""" + MaidProperty + """
        ,"character":{"type":"string","description":"要绑定的 WAB 角色名或 Id"},
        "create":{"type":"boolean","description":"角色不存在时按女仆名新建，默认 false"},
        "gender":{"type":"string","description":"新建角色时的性别，默认 女"},
        "newSave":{"type":"boolean","description":"绑定前新建或分支一份 WAB 存档"},
        "label":{"type":"string","description":"新存档的名字"}
        },"required":["maid"],"additionalProperties":false}
        """;

    private static readonly string BackupSchema =
        """{"type":"object","properties":{""" + MaidProperty + """
        ,"label":{"type":"string","description":"备份标签，默认 manual"}
        },"additionalProperties":false}
        """;
}
