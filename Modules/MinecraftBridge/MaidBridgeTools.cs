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

        tools.RegisterBuiltin("maid_see",
            "以她的眼睛看一次：她正面向的方向上有什么——生物、玩家、别的女仆、矿石与容器、床、作物，"
            + "会被方块挡住就不算看到，结果是一句人话加一份清单。photo=true 时再从主人的客户端取一帧游戏画面，"
            + "交给识图模型讲给她听（要她在 WAB 设置里配好识图模型，插件设置里也要放开「允许她看一眼画面」）。"
            + "想知道「你眼前现在是什么样子」「那个方向有什么」就调用它，别凭印象答。",
            args => bridge.RunToolAsync("see", args),
            SeeSchema);

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

        tools.RegisterBuiltin("maid_feed_rules",
            "查看或设置她的投喂口味表：挑食表、喜爱表、吃几口会腻、吃腻计数。"
            + "表对当前存档里所有女仆生效，改完立即下发到游戏；她在游戏里被喂到表里的东西时，"
            + "最爱的会开心（好感更多），挑食的直接拒绝，同一种吃满上限会嫌腻。"
            + "物品写注册名（minecraft:rotten_flesh）或标签（#forge:candy），标签一次管住一整类。",
            args => bridge.RunToolAsync("feed_rules", args),
            FeedRulesSchema);

        tools.RegisterBuiltin("maid_waypoint",
            "让女仆记住或前往一个地点。action=set 把当前位置（或给的 pos）存成名字，"
            + "action=list 列出她知道的所有地点（带距离），action=go 走到那个地点，action=remove 忘掉。"
            + "常用点建议一次记住：家、仓库、矿洞、农田。地点跟着存档走。",
            args => bridge.RunToolAsync("waypoint", args),
            WaypointSchema);

        tools.RegisterBuiltin("maid_container",
            "操作箱子一类容器（箱子、木桶、漏斗、熔炉都算）。action=list 看里面有什么，"
            + "store 把背包里的东西放进去，take 从里面取出来，dump 把背包里除工具/食物外的全部收进去。"
            + "不给 pos 就找她附近最近的一个；filter 可写物品 id、#标签、food 或 block。"
            + "够不着时她会先走过去，再让你重试。",
            args => bridge.RunToolAsync("container", args),
            ContainerSchema);

        tools.RegisterBuiltin("maid_craft",
            "让她合成或冶炼。action=craft 按产物 id 在工作台/背包合成（3×3 配方要手边有工作台），"
            + "action=smelt 用熔炉配方烧炼（自动从背包找燃料），action=list 看现在能做什么。"
            + "材料从她自己的背包扣；做不出来会告诉你缺什么。",
            args => bridge.RunToolAsync("craft", args),
            CraftSchema);

        tools.RegisterBuiltin("maid_build",
            "让她铺方块。action=line 铺一条、wall 铺一面（可给 height）、box 中空盒子、fill 实心填充、pillar 往上叠。"
            + "from/to 是两端坐标，block 用物品 id（留空就用手上的方块）。默认不覆盖已有方块，一次最多 512 格，材料不够就停。",
            args => bridge.RunToolAsync("build", args),
            BuildSchema);

        tools.RegisterBuiltin("maid_ride",
            "骑乘。action=mount 骑上附近的船/矿车/马（可给 target 指定名字），boat 从背包放一条船坐上去，"
            + "cart 放一辆矿车，dismount 下来。只能骑本来就是坐骑的东西，不抢玩家和其它实体的座位。",
            args => bridge.RunToolAsync("ride", args),
            RideSchema);

        tools.RegisterBuiltin("maid_fight",
            "战斗。action=guard 守着、附近有敌对生物就打（没有就报告安全），defend 打刚才伤害主人的人，"
            + "attack 打指定目标，retreat 撤回主人身边。会自己从背包挑攻击力最高的武器换上。",
            args => bridge.RunToolAsync("fight", args),
            FightSchema);

        tools.RegisterBuiltin("maid_crew",
            "给附近你名下的所有女仆派分工。action=assign + role（miner 挖矿 / farmer 收作物 / hauler 捡东西并送进箱子 / "
            + "guard 守卫），status 看每个人在忙什么，stop 让全部停手。每只女仆各干各的，不会互相抢。",
            args => bridge.RunToolAsync("crew", args),
            CrewSchema);

        // ===== B2 玩法入口：与模组 WabDispatcher 的九个新 method 一一对应 =====
        tools.RegisterBuiltin("maid_item",
            "管理女仆自己的物品与附近容器。action=list 看背包（含耐久/附魔/NBT 有无）、container 读附近容器属性、"
            + "drop 丢弃指定槽位、equip 主动装备、pickup 按名称/属性/耐久筛选捡起。默认全关，要先用对应开关打开。",
            args => bridge.RunToolAsync("item", args), FeatureSchema);
        tools.RegisterBuiltin("maid_farm",
            "农耕。必须显式给 tool（工具）、crop（种植物）、area（区域）和 pos 起点；缺一样就拒绝且不动世界。"
            + "可开垦田地（farming_allow_tilling）。默认关闭，未指定不自动执行。",
            args => bridge.RunToolAsync("farm", args), FeatureSchema);
        tools.RegisterBuiltin("maid_escape",
            "高级逃生。action=mlg 落地水（须持水桶/铁桶，允许时自己去打水）、suffocation 窒息找空气或挖逃生、"
            + "lava_boat 岩浆船（实验性，默认关、仅濒死）。落地水成功不触发求安慰。",
            args => bridge.RunToolAsync("escape", args), FeatureSchema);
        tools.RegisterBuiltin("maid_comfort",
            "濒死时按概率找主人求安慰。落地水成功会抑制；概率可来自配置或 AI，取更保守的那个。默认关闭、概率 0.05。",
            args => bridge.RunToolAsync("comfort", args), FeatureSchema);
        tools.RegisterBuiltin("maid_sneak_eat",
            "偷吃。AI 侧与玩家侧各有一个概率，取较小值；检测到原版偷吃动画时让路，避免同一动作播两遍。默认关闭。",
            args => bridge.RunToolAsync("sneak_eat", args), FeatureSchema);
        tools.RegisterBuiltin("maid_mode_switch",
            "按情境切换守家(home)/跟随(follow)，带冷却，玩家手动指令始终优先。默认走代码规则，可开 AI 情境判断。",
            args => bridge.RunToolAsync("mode_switch", args), FeatureSchema);
        tools.RegisterBuiltin("maid_chat_turn",
            "聊天轮控制。action=wake 叫名字或多个称呼语即可唤醒（不必面对面）、dedup 把上下文中每句只留一次并返回"
            + "每回合只发一遍与隐形对话标志。",
            args => bridge.RunToolAsync("chat_turn", args), FeatureSchema);
        tools.RegisterBuiltin("maid_plan_flow",
            "多步流程：先探测或拍照，再规划路线与工具使用，最多 8 步，最后按 summary/stepwise/none 返回报告。默认关闭。",
            args => bridge.RunToolAsync("plan_flow", args), FeatureSchema);
        tools.RegisterBuiltin("maid_headpat",
            "摸头互动。action=request 由 AI 主动选择要摸头（走讨摸概率与冷却，总开关可关避免与附属冲突）、"
            + "refuse 玩家拒绝后她会伤心并露出伤心表情；三档反应（俯身+飞机耳+眯眼 / 加抱尾巴 / 抱尾巴缩成团）"
            + "按档位播放，小酒壶不俯身。",
            args => bridge.RunToolAsync("headpat", args), FeatureSchema);
        tools.RegisterBuiltin("maid_backup_copy",
            "双处备份：把女仆完整快照写进游戏存档（模组侧一份），WAB 侧另存一份；恢复前做完整性校验，"
            + "冲突按 wab_wins / mod_wins / newest 决定从哪端恢复，校验失败拒绝恢复且不覆盖两端原数据。",
            args => bridge.RunToolAsync("backup_copy", args), FeatureSchema);
    }

    /** 九个玩法入口共用的参数集合；每个入口只挑自己需要的字段，多余字段模组侧忽略。 */
    private static readonly string FeatureSchema =
        """{"type":"object","properties":{""" + MaidProperty + """
        ,"action":{"type":"string","description":"动作名，见各工具说明"},
        "slot":{"type":"integer","description":"背包槽位（drop/equip）"},
        "radius":{"type":"number","description":"搜索半径（格）"},
        "pos":{"type":"array","items":{"type":"number"},"minItems":3,"maxItems":3,"description":"[x,y,z] 起点"},
        "tool":{"type":"string","description":"农耕工具"},
        "crop":{"type":"string","description":"种植物 id"},
        "area":{"type":"string","description":"种植区域描述"},
        "width":{"type":"integer","description":"区域宽"},
        "depth":{"type":"integer","description":"区域长"},
        "airTicks":{"type":"integer","description":"剩余空气 tick"},
        "nearDeath":{"type":"boolean","description":"是否濒死"},
        "mlgSuccess":{"type":"boolean","description":"落地水是否成功"},
        "roll":{"type":"number","description":"外部注入的 0-1 随机值，便于确定性判定"},
        "aiProbability":{"type":"number","description":"AI 给出的概率"},
        "spoken":{"type":"string","description":"玩家说的话"},
        "hashes":{"type":"array","items":{"type":"string"},"description":"上下文句子哈希"},
        "steps":{"type":"array","items":{"type":"object"},"description":"多步流程的步骤"},
        "scanned":{"type":"boolean","description":"是否已探测"},
        "photo":{"type":"boolean","description":"是否已拍照"},
        "mode":{"type":"string","enum":["home","follow"],"description":"切换目标模式"},
        "elapsedSeconds":{"type":"integer","description":"距上次切换过去了多少秒"},
        "wabSavedAt":{"type":"integer","description":"WAB 侧快照时间戳"},
        "modSavedAt":{"type":"integer","description":"模组侧快照时间戳"}
        },"additionalProperties":false}
        """;

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

    private static readonly string SeeSchema =
        """{"type":"object","properties":{""" + MaidProperty + """
        ,"radius":{"type":"number","description":"看得最远多少格，默认 20，最大 48"},
        "cone":{"type":"number","description":"视角张角（度），默认 70，最大 170"},
        "photo":{"type":"boolean","description":"是否再取一帧真实画面给她看，默认 false（要识图模型已配置）"}
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
        ,"action":{"type":"string","enum":["mine","collect","chop","harvest","scoop","fill","tunnel"],"description":"采集方式"},
        "pos":{"type":"array","items":{"type":"number"},"minItems":3,"maxItems":3,"description":"指定作业点；留空则由女仆自己找"},
        "filter":{"type":"string","description":"mine 的目标：ores / 方块 id；scoop 可给 water 或 lava"},
        "radius":{"type":"number","description":"搜索半径（格），默认 16"},
        "count":{"type":"integer","description":"最多处理多少个方块/物品"},
        "length":{"type":"integer","description":"tunnel 往前挖多少格，默认 16"},
        "width":{"type":"integer","enum":[1,3],"description":"tunnel 巷道宽度，1 或 3，默认 1"},
        "torchEvery":{"type":"integer","description":"tunnel 每几格插一支火把，0 表示不插"}
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
        "arg":{"type":"string","maxLength":64,"description":"gesture 的动作名（如 hug、wave）；interact_addon 的行为 id"},
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
          "arg":{"type":"string","maxLength":64,"description":"gesture 动作名或 interact_addon 行为 id"},
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

    private static readonly string WaypointSchema =
        """{"type":"object","properties":{""" + MaidProperty + """
        ,"action":{"type":"string","enum":["set","list","go","remove"],"description":"记住 / 列出 / 前往 / 忘掉"},
        "name":{"type":"string","maxLength":32,"description":"地点名字，如 仓库、矿洞、家"},
        "pos":{"type":"array","items":{"type":"number"},"minItems":3,"maxItems":3,"description":"set 时的坐标；留空用她当前位置"}
        },"required":["action"],"additionalProperties":false}
        """;

    private static readonly string ContainerSchema =
        """{"type":"object","properties":{""" + MaidProperty + """
        ,"action":{"type":"string","enum":["list","store","take","dump"],"description":"查看 / 存入 / 取出 / 收工收纳"},
        "pos":{"type":"array","items":{"type":"number"},"minItems":3,"maxItems":3,"description":"容器坐标；留空找附近最近的"},
        "item":{"type":"string","description":"物品 id 或名称"},
        "filter":{"type":"string","description":"筛选：物品 id、#标签、food、block"},
        "count":{"type":"integer","description":"最多搬多少件，默认 64"},
        "radius":{"type":"number","description":"找容器的半径（格），默认 8"}
        },"required":["action"],"additionalProperties":false}
        """;

    private static readonly string CraftSchema =
        """{"type":"object","properties":{""" + MaidProperty + """
        ,"action":{"type":"string","enum":["craft","smelt","list"],"description":"合成 / 冶炼 / 看能做什么"},
        "item":{"type":"string","description":"产物物品 id，如 minecraft:crafting_table"},
        "count":{"type":"integer","description":"做几个，默认 1，最大 64"},
        "pos":{"type":"array","items":{"type":"number"},"minItems":3,"maxItems":3,"description":"工作台坐标；留空找附近的工作台"}
        },"required":["action"],"additionalProperties":false}
        """;

    private static readonly string BuildSchema =
        """{"type":"object","properties":{""" + MaidProperty + """
        ,"action":{"type":"string","enum":["line","wall","box","fill","pillar"],"description":"建造形状"},
        "from":{"type":"array","items":{"type":"number"},"minItems":3,"maxItems":3,"description":"起点坐标"},
        "to":{"type":"array","items":{"type":"number"},"minItems":3,"maxItems":3,"description":"终点坐标"},
        "block":{"type":"string","description":"方块物品 id；留空用手上的方块"},
        "height":{"type":"integer","description":"墙/柱的高度或箱子的层数，默认 1"},
        "replace":{"type":"boolean","description":"是否覆盖已有方块，默认 false"}
        },"required":["action","from"],"additionalProperties":false}
        """;

    private static readonly string RideSchema =
        """{"type":"object","properties":{""" + MaidProperty + """
        ,"action":{"type":"string","enum":["mount","boat","cart","dismount"],"description":"骑上 / 放船 / 放矿车 / 下来"},
        "target":{"type":"string","description":"要骑的东西的名字，留空找最近的"},
        "radius":{"type":"number","description":"找载具的半径（格），默认 10"}
        },"required":["action"],"additionalProperties":false}
        """;

    private static readonly string FightSchema =
        """{"type":"object","properties":{""" + MaidProperty + """
        ,"action":{"type":"string","enum":["guard","defend","attack","retreat"],"description":"守卫 / 护主 / 攻击 / 撤退"},
        "target":{"type":"string","description":"攻击目标的名字；guard 留空则自动找敌对生物"},
        "radius":{"type":"number","description":"索敌半径（格），默认 12"}
        },"required":["action"],"additionalProperties":false}
        """;

    private static readonly string CrewSchema =
        """{"type":"object","properties":{""" + MaidProperty + """
        ,"action":{"type":"string","enum":["assign","status","stop"],"description":"派活 / 查看 / 全部停手"},
        "role":{"type":"string","enum":["miner","farmer","hauler","guard"],"description":"分工角色"},
        "radius":{"type":"number","description":"队伍半径（格），默认 24"},
        "count":{"type":"integer","description":"每人处理多少个目标，默认 32"}
        },"required":["action"],"additionalProperties":false}
        """;

    private static readonly string FeedRulesSchema =
        """
        {"type":"object","properties":{
        "picky":{"type":"array","items":{"type":"string"},"maxItems":32,"description":"挑食表：物品注册名或 #标签；给数组即整表替换，空数组 = 清空"},
        "favorite":{"type":"array","items":{"type":"string"},"maxItems":32,"description":"喜爱表：同上格式；给数组即整表替换，空数组 = 清空"},
        "boredomLimit":{"type":"integer","minimum":1,"maximum":1000,"description":"同一种食物吃满多少口会腻，默认 5"},
        "clearCounts":{"type":"boolean","description":"true 时清空吃腻计数（比如换季了、想让她重新接受常吃的东西）"}
        },"additionalProperties":false}
        """;
}
