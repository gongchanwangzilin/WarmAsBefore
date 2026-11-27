import { useState, type CSSProperties } from 'react';
import { GlassButton } from '../../components/GlassButton';
import { GlassSwitch } from '../../components/GlassSwitch';
import { PageShell } from '../../components/PageShell';
import { Card, PageHeader, Sep } from '../../components/kit';
import type { ScreenProps } from '../types';

type ArchNode = {
  id: string;
  label: string;
  layer: string;
  module: string;
  detail: string;
  kind: ArchKind;
  group: string;
};

type ArchLink = { from: string; to: string; kind: LinkKind };

type ArchKind = 'Step' | 'Mode' | 'Module' | 'Output';
type LinkKind = 'Normal' | 'Bidirectional' | 'Feedback' | 'Control';

/** 监测图骨架：五层论文节点 + Mermaid 拓扑连线，id / 名字 / 说明逐字取自 SuperMonitorViewModel.EnsureArchitecture。 */
const NODES: ArchNode[] = [
  { id: "in.text", label: "文字输入", layer: "1", module: "ChatEngine.SendCore", detail: "Send 入口记录用户文本，整条执行链的起点", kind: "Step", group: "sense" },
  { id: "in.image", label: "图像输入", layer: "1", module: "ChatEngine.SendCore", detail: "附件图片入口，交给视觉特征提取", kind: "Step", group: "sense" },
  { id: "in.face", label: "摄像头人脸", layer: "1", module: "FacePresenceService", detail: "看一眼的情绪 / 年龄 / 是否在场：只读，不参与任何决策", kind: "Step", group: "parse" },
  { id: "in.voice", label: "语音输入", layer: "1", module: "ChatEngine.SendCore", detail: "语音转写文本入口，交给声学分析", kind: "Step", group: "sense" },
  { id: "in.fusion", label: "多模态融合", layer: "1", module: "ChatEngine.SendCore", detail: "文字/图像/语音特征融合后进入具身锚定", kind: "Step", group: "sense" },
  { id: "in.semantic", label: "语义解析", layer: "1", module: "ChatEngine.SendCore", detail: "文本 → 语义特征", kind: "Step", group: "parse" },
  { id: "in.visual", label: "视觉特征", layer: "1", module: "ChatEngine.SendCore", detail: "图像附件的视觉特征提取", kind: "Step", group: "parse" },
  { id: "in.audio", label: "声学分析", layer: "1", module: "ChatEngine.SendCore", detail: "语音附件的声学特征分析", kind: "Step", group: "parse" },
  { id: "in.imagery", label: "意象唤起", layer: "1", module: "HumanMemoryService", detail: "语义特征触发记忆意象", kind: "Step", group: "parse" },
  { id: "in.intent", label: "意图识别", layer: "1", module: "ChatEngine.SendCore", detail: "识别意图，参与后续路由预判", kind: "Step", group: "parse" },
  { id: "in.edge", label: "边缘检测", layer: "1", module: "ChatEngine.SendCore", detail: "图形线条 / 边缘检测", kind: "Step", group: "parse" },
  { id: "in.entity", label: "实体识别", layer: "1", module: "HumanMemoryService", detail: "识别实体并关联记忆条目", kind: "Step", group: "parse" },
  { id: "in.tone", label: "语调情绪", layer: "1", module: "EmotionMix", detail: "语调 / 情绪分析，写入情绪基调", kind: "Step", group: "parse" },
  { id: "in.embodied", label: "具身锚定", layer: "1", module: "EmotionMix / ChatEngine", detail: "具身认知 / 情感锚定：多模态汇聚到心智入口", kind: "Step", group: "sense" },
  { id: "mem.shallow", label: "浅记忆", layer: "2", module: "HumanMemoryService.RecallAsync", detail: "浅记忆 / 前记忆：近期对话缓冲", kind: "Step", group: "memory" },
  { id: "mem.deepen", label: "记忆深化", layer: "2", module: "HumanMemoryService", detail: "重要性 / 回想次数 / 唤醒度 / 时间衰减共同深化", kind: "Step", group: "memory" },
  { id: "mem.deep", label: "深层记忆", layer: "2", module: "HumanMemoryService", detail: "深层记忆：情景 / 语义 / 程序三类", kind: "Step", group: "memory" },
  { id: "mem.episodic", label: "情景记忆", layer: "2", module: "HumanMemoryService.RecallTextAsync", detail: "带时间地点的经历", kind: "Step", group: "memory" },
  { id: "mem.semanticM", label: "语义记忆", layer: "2", module: "HumanMemoryService", detail: "概念与事实", kind: "Step", group: "memory" },
  { id: "mem.procedural", label: "程序记忆", layer: "2", module: "HumanMemoryService", detail: "习惯化的应对套路", kind: "Step", group: "memory" },
  { id: "mem.retrieval", label: "记忆检索器", layer: "2", module: "HumanMemoryService.RecallTextAsync", detail: "按窗口 / 线索回想，受情绪偏置 moodBias 影响", kind: "Step", group: "memory" },
  { id: "emo.state", label: "情绪状态", layer: "2", module: "EmotionMix / ChatEngine", detail: "当前情绪：效价 / 唤醒度 / 控制感三维", kind: "Step", group: "emotion" },
  { id: "emo.valence", label: "情绪效价", layer: "2", module: "EmotionMix", detail: "Valence（-1..1）", kind: "Step", group: "emotion" },
  { id: "emo.arousal", label: "唤醒度", layer: "2", module: "EmotionMix", detail: "Arousal（0..1）", kind: "Step", group: "emotion" },
  { id: "emo.dominance", label: "控制感", layer: "2", module: "EmotionMix", detail: "Dominance", kind: "Step", group: "emotion" },
  { id: "emo.coupling", label: "记忆情绪耦合", layer: "2", module: "ChatEngine.SendCore", detail: "记忆 ↔ 情绪双向耦合，moodBias 生效时点亮", kind: "Step", group: "emotion" },
  { id: "mem.rumination", label: "反刍循环", layer: "2", module: "HumanMemoryService.ChainSearchAsync", detail: "负面回忆链延展，加剧情绪内耗", kind: "Step", group: "memory" },
  { id: "cognition.focus", label: "专注模式", layer: "2", module: "CognitionModeService.EnterFocus", detail: "多巴胺驱动、大模型高精度；耗专属精力，用得越多停留越久", kind: "Mode", group: "cognition" },
  { id: "cognition.wander", label: "神游模式", layer: "2", module: "CognitionModeService.WanderInject", detail: "低精度默认态；反刍/整理/纠错/新想法/回忆连线", kind: "Mode", group: "cognition" },
  { id: "cognition.intervene", label: "神游介入", layer: "2", module: "CognitionModeService.ShouldWanderIntervene", detail: "专注快撑不住时，神游往脑海随机塞旧记忆", kind: "Step", group: "cognition" },
  { id: "mind.trauma", label: "创伤节点", layer: "3", module: "ChatEngine", detail: "核心信念与旧伤的锚点", kind: "Step", group: "self" },
  { id: "mind.persona", label: "人格系统", layer: "3", module: "ChatEngine", detail: "人格 / 核心信念：设定情绪基调", kind: "Step", group: "self" },
  { id: "mind.pattern", label: "行为模式", layer: "3", module: "ChatEngine", detail: "由深层记忆塑造的行为模式", kind: "Step", group: "self" },
  { id: "mind.intrusive", label: "侵入性思维", layer: "3", module: "ChatEngine", detail: "不受控的念头", kind: "Step", group: "cognition" },
  { id: "mind.summary", label: "总结评估", layer: "3", module: "ChatEngine.ProcessReplyAsync", detail: "对当前情境的认知评价", kind: "Step", group: "cognition" },
  { id: "path.state", label: "病理状态机", layer: "3", module: "ChatEngine", detail: "PTSD / 抑郁 / 焦虑 / BPD 状态机", kind: "Step", group: "emotion" },
  { id: "path.ptsd", label: "PTSD", layer: "3", module: "ChatEngine", detail: "降低创伤阈值，杏仁核劫持", kind: "Step", group: "emotion" },
  { id: "path.depression", label: "抑郁", layer: "3", module: "ChatEngine", detail: "反刍死锁，情绪基线极低", kind: "Step", group: "emotion" },
  { id: "path.anxiety", label: "焦虑", layer: "3", module: "ChatEngine", detail: "联想过度发散", kind: "Step", group: "emotion" },
  { id: "path.bpd", label: "BPD", layer: "3", module: "ChatEngine", detail: "防御机制极化", kind: "Step", group: "emotion" },
  { id: "core.driveSystem", label: "内驱动系统", layer: "3", module: "InnerDriveService", detail: "可整体停用：关掉后数值冻结、不进提示词，也不再产驱动力", kind: "Mode", group: "drive" },
  { id: "core.drivePower", label: "三种基本动力", layer: "3", module: "InnerDriveService", detail: "攻击性 / 性 / 自恋：力的来源", kind: "Module", group: "drive" },
  { id: "core.driveSeven", label: "七个驱动", layer: "3", module: "InnerDriveService", detail: "贪欲 / 暴食 / 色欲 / 嫉妒 / 傲慢 / 怠惰 / 暴怒", kind: "Module", group: "drive" },
  { id: "core.driveGate", label: "自律闸门", layer: "3", module: "DriveRules.ConstraintOn", detail: "道德观 × 人生观 × 思维主义决定的压制力道", kind: "Module", group: "drive" },
  { id: "core.discharge", label: "八级发泄", layer: "3", module: "DriveRules.PickForm", detail: "叛逆 → 撒泼 → 怄气 → 任性 → 郁闷 → 拖延 → 厌烦 → 退缩", kind: "Module", group: "drive" },
  { id: "soma.coping", label: "应对机制", layer: "4", module: "OrganSimulationService", detail: "应对决策：战 / 逃 / 僵三分支", kind: "Step", group: "bodylow" },
  { id: "soma.fight", label: "反击防御", layer: "4", module: "OrganSimulationService", detail: "高唤醒下的对峙分支", kind: "Step", group: "bodylow" },
  { id: "soma.reply", label: "理智回复", layer: "4", module: "ChatEngine", detail: "常态输出分支", kind: "Step", group: "bodylow" },
  { id: "soma.freeze", label: "僵直宕机", layer: "4", module: "OrganSimulationService", detail: "低控制感时的冻结分支", kind: "Step", group: "bodylow" },
  { id: "soma.bypass", label: "低级中枢越权", layer: "4", module: "ChatEngine", detail: "低级中枢越权 / 熔断，绕过理智", kind: "Step", group: "bodylow" },
  { id: "soma.response", label: "器官响应", layer: "4", module: "OrganSimulationService.Observe", detail: "器官响应 / 躯体化：脏器状态外显", kind: "Step", group: "somatize" },
  { id: "soma.delay", label: "输出延迟", layer: "4", module: "ChatEngine", detail: "输出延迟 / 反应迟钝（Task.Delay）", kind: "Step", group: "somatize" },
  { id: "soma.stutter", label: "字符碎裂", layer: "4", module: "ChatEngine", detail: "字符碎裂 / 结巴 / 颤抖（正则打断）", kind: "Step", group: "somatize" },
  { id: "soma.downgrade", label: "逻辑降级", layer: "4", module: "ApiGateway", detail: "逻辑降级 / 算力熔断（MaxTokens 限制）", kind: "Step", group: "somatize" },
  { id: "soma.panic", label: "惊恐发作", layer: "4", module: "OrganSimulationService", detail: "惊恐发作 / 呼吸急促模拟", kind: "Step", group: "somatize" },
  { id: "soma.recovery", label: "器官恢复期", layer: "4", module: "OrganSimulationService", detail: "恢复期：情绪反复 / 迟滞", kind: "Step", group: "somatize" },
  { id: "eng.statepack", label: "状态打包", layer: "5", module: "ChatEngine.RunTurnAsync", detail: "把心智 / 器官状态打成模型输入", kind: "Step", group: "assembly" },
  { id: "eng.router", label: "意图路由", layer: "5", module: "ApiGateway", detail: "意图路由分发：本地轻量 / 云端重量", kind: "Step", group: "assembly" },
  { id: "eng.redis", label: "Redis 缓存", layer: "5", module: "ApiGateway", detail: "Redis 状态缓存（MindState）", kind: "Step", group: "assembly" },
  { id: "eng.qdrant", label: "Qdrant 向量", layer: "5", module: "HumanMemoryService", detail: "Qdrant 向量记忆库（Vectors）", kind: "Step", group: "assembly" },
  { id: "eng.sqlite", label: "SQLite 结构化", layer: "5", module: "DailyPlanService", detail: "SQLite / PostgreSQL 结构化数据", kind: "Step", group: "assembly" },
  { id: "eng.model.light", label: "轻量模型", layer: "5", module: "ApiGateway", detail: "轻量本地模型（LLamaSharp / Qwen）", kind: "Step", group: "output" },
  { id: "eng.model.heavy", label: "云端模型", layer: "5", module: "ApiGateway.Chat", detail: "云端大模型（Azure OpenAI / GPT-4o）", kind: "Step", group: "output" },
  { id: "eng.prompt", label: "Prompt 组装", layer: "5", module: "ApiGateway", detail: "System Prompt 动态组装（Semantic Kernel）", kind: "Step", group: "output" },
  { id: "eng.postprocess", label: "后处理", layer: "5", module: "ChatEngine.ProcessReplyAsync", detail: "文本修饰器 / 流式拦截中间件", kind: "Step", group: "output" },
  { id: "eng.output", label: "流式输出", layer: "5", module: "ChatEngine", detail: "SSE 流式输出给用户", kind: "Output", group: "output" },
  { id: "eng.loop", label: "循环反馈", layer: "5", module: "ChatEngine.SendCore", detail: "输出回灌为下一轮输入", kind: "Step", group: "output" },
  { id: "horm.pool", label: "内分泌池", layer: "H", module: "InnerLifeService", detail: "六项化学值按半衰期向气质基线回落", kind: "Step", group: "hormone" },
  { id: "horm.dopa", label: "多巴胺", layer: "H", module: "InnerLifeService", detail: "收益预期与干劲：想做、想要、有盼头", kind: "Step", group: "hormone" },
  { id: "horm.sero", label: "血清素", layer: "H", module: "InnerLifeService", detail: "安宁与稳定：不急、不钻牛角尖", kind: "Step", group: "hormone" },
  { id: "horm.cort", label: "皮质醇", layer: "H", module: "InnerLifeService", detail: "压力与警觉：往坏处想、绷着", kind: "Step", group: "hormone" },
  { id: "horm.adr", label: "肾上腺素", layer: "H", module: "HormoneRules", detail: "危急：精力↑警觉↑，过量有器官代价", kind: "Step", group: "hormone" },
  { id: "horm.endo", label: "内啡肽", layer: "H", module: "InnerDriveService", detail: "镇痛与松快：发泄过、忍下来之后的那口气", kind: "Step", group: "hormone" },
  { id: "horm.chol", label: "胆固醇", layer: "H", module: "HormoneRules", detail: "颓废遭打击时的中和：食欲↓干劲↑", kind: "Step", group: "hormone" },
  { id: "horm.ltd", label: "长期激励素", layer: "H", module: "HormoneRules", detail: "长期目标达成的奖励，衰减极慢", kind: "Step", group: "hormone" },
];

const LINKS: ArchLink[] = [
  { from: "in.text", to: "in.semantic", kind: "Normal" },
  { from: "in.image", to: "in.visual", kind: "Normal" },
  { from: "in.face", to: "in.visual", kind: "Normal" },
  { from: "in.voice", to: "in.audio", kind: "Normal" },
  { from: "in.fusion", to: "in.embodied", kind: "Normal" },
  { from: "in.semantic", to: "in.imagery", kind: "Normal" },
  { from: "in.semantic", to: "in.intent", kind: "Normal" },
  { from: "in.visual", to: "in.edge", kind: "Normal" },
  { from: "in.visual", to: "in.entity", kind: "Normal" },
  { from: "in.audio", to: "in.tone", kind: "Normal" },
  { from: "in.imagery", to: "in.embodied", kind: "Normal" },
  { from: "in.intent", to: "in.embodied", kind: "Normal" },
  { from: "in.entity", to: "in.embodied", kind: "Normal" },
  { from: "in.tone", to: "in.embodied", kind: "Normal" },
  { from: "in.embodied", to: "mem.shallow", kind: "Normal" },
  { from: "mem.shallow", to: "mem.deepen", kind: "Normal" },
  { from: "mem.deepen", to: "mem.deep", kind: "Normal" },
  { from: "mem.deep", to: "mem.episodic", kind: "Normal" },
  { from: "mem.deep", to: "mem.semanticM", kind: "Normal" },
  { from: "mem.deep", to: "mem.procedural", kind: "Normal" },
  { from: "emo.state", to: "emo.valence", kind: "Normal" },
  { from: "emo.state", to: "emo.arousal", kind: "Normal" },
  { from: "emo.state", to: "emo.dominance", kind: "Normal" },
  { from: "mem.deep", to: "emo.state", kind: "Bidirectional" },
  { from: "emo.state", to: "mem.retrieval", kind: "Bidirectional" },
  { from: "mem.deep", to: "mem.retrieval", kind: "Normal" },
  { from: "mem.retrieval", to: "emo.coupling", kind: "Normal" },
  { from: "emo.coupling", to: "emo.state", kind: "Bidirectional" },
  { from: "mem.rumination", to: "mem.retrieval", kind: "Feedback" },
  { from: "mem.rumination", to: "emo.state", kind: "Feedback" },
  { from: "cognition.focus", to: "cognition.wander", kind: "Bidirectional" },
  { from: "cognition.focus", to: "cognition.intervene", kind: "Feedback" },
  { from: "cognition.intervene", to: "cognition.wander", kind: "Normal" },
  { from: "cognition.wander", to: "mem.rumination", kind: "Feedback" },
  { from: "mem.retrieval", to: "mind.trauma", kind: "Normal" },
  { from: "mem.deep", to: "mind.pattern", kind: "Normal" },
  { from: "emo.state", to: "mind.trauma", kind: "Bidirectional" },
  { from: "path.ptsd", to: "mind.trauma", kind: "Feedback" },
  { from: "mind.trauma", to: "mind.persona", kind: "Normal" },
  { from: "mind.pattern", to: "mind.intrusive", kind: "Normal" },
  { from: "mind.pattern", to: "mind.summary", kind: "Normal" },
  { from: "mind.intrusive", to: "mind.summary", kind: "Normal" },
  { from: "mind.summary", to: "mind.persona", kind: "Bidirectional" },
  { from: "path.state", to: "path.ptsd", kind: "Normal" },
  { from: "path.state", to: "path.depression", kind: "Normal" },
  { from: "path.state", to: "path.anxiety", kind: "Normal" },
  { from: "path.state", to: "path.bpd", kind: "Normal" },
  { from: "path.depression", to: "mem.rumination", kind: "Feedback" },
  { from: "path.anxiety", to: "mem.deep", kind: "Feedback" },
  { from: "path.bpd", to: "mind.persona", kind: "Feedback" },
  { from: "mind.persona", to: "emo.state", kind: "Bidirectional" },
  { from: "mind.summary", to: "emo.state", kind: "Bidirectional" },
  { from: "mind.persona", to: "soma.coping", kind: "Normal" },
  { from: "emo.state", to: "soma.coping", kind: "Normal" },
  { from: "mind.summary", to: "soma.coping", kind: "Normal" },
  { from: "mind.trauma", to: "soma.bypass", kind: "Bidirectional" },
  { from: "path.ptsd", to: "soma.bypass", kind: "Feedback" },
  { from: "soma.coping", to: "soma.fight", kind: "Normal" },
  { from: "soma.coping", to: "soma.reply", kind: "Normal" },
  { from: "soma.coping", to: "soma.freeze", kind: "Normal" },
  { from: "soma.bypass", to: "soma.response", kind: "Normal" },
  { from: "soma.response", to: "soma.delay", kind: "Normal" },
  { from: "soma.response", to: "soma.stutter", kind: "Normal" },
  { from: "soma.response", to: "soma.downgrade", kind: "Normal" },
  { from: "soma.response", to: "soma.panic", kind: "Normal" },
  { from: "soma.response", to: "soma.recovery", kind: "Normal" },
  { from: "soma.recovery", to: "emo.state", kind: "Feedback" },
  { from: "soma.fight", to: "eng.statepack", kind: "Normal" },
  { from: "soma.reply", to: "eng.statepack", kind: "Normal" },
  { from: "soma.freeze", to: "eng.statepack", kind: "Normal" },
  { from: "soma.delay", to: "eng.statepack", kind: "Normal" },
  { from: "soma.stutter", to: "eng.statepack", kind: "Normal" },
  { from: "mem.rumination", to: "eng.statepack", kind: "Normal" },
  { from: "eng.statepack", to: "eng.router", kind: "Normal" },
  { from: "eng.router", to: "eng.redis", kind: "Bidirectional" },
  { from: "eng.router", to: "eng.qdrant", kind: "Bidirectional" },
  { from: "eng.router", to: "eng.sqlite", kind: "Bidirectional" },
  { from: "eng.statepack", to: "eng.prompt", kind: "Normal" },
  { from: "eng.prompt", to: "eng.model.light", kind: "Normal" },
  { from: "eng.prompt", to: "eng.model.heavy", kind: "Normal" },
  { from: "eng.router", to: "eng.model.light", kind: "Normal" },
  { from: "eng.router", to: "eng.model.heavy", kind: "Normal" },
  { from: "eng.model.light", to: "eng.postprocess", kind: "Normal" },
  { from: "eng.model.heavy", to: "eng.postprocess", kind: "Normal" },
  { from: "eng.postprocess", to: "eng.output", kind: "Normal" },
  { from: "eng.output", to: "eng.loop", kind: "Normal" },
  { from: "eng.loop", to: "in.text", kind: "Feedback" },
  { from: "core.driveSystem", to: "core.drivePower", kind: "Control" },
  { from: "core.driveSystem", to: "core.driveSeven", kind: "Control" },
  { from: "core.driveSystem", to: "core.driveGate", kind: "Control" },
  { from: "core.driveSystem", to: "core.discharge", kind: "Control" },
  { from: "core.driveSystem", to: "emo.state", kind: "Control" },
  { from: "cognition.focus", to: "mem.retrieval", kind: "Control" },
  { from: "cognition.focus", to: "eng.router", kind: "Control" },
  { from: "cognition.wander", to: "mem.deepen", kind: "Control" },
  { from: "cognition.wander", to: "mem.episodic", kind: "Control" },
  { from: "horm.pool", to: "horm.dopa", kind: "Bidirectional" },
  { from: "horm.dopa", to: "core.drivePower", kind: "Control" },
  { from: "horm.dopa", to: "cognition.focus", kind: "Control" },
  { from: "horm.sero", to: "emo.state", kind: "Control" },
  { from: "horm.cort", to: "emo.state", kind: "Control" },
  { from: "horm.cort", to: "soma.coping", kind: "Control" },
  { from: "horm.adr", to: "soma.fight", kind: "Control" },
  { from: "horm.endo", to: "core.discharge", kind: "Control" },
];
const LAYER_TITLE: Record<string, string> = {
  '1': '1 感知与输入抽象层',
  '2': '2 记忆与情绪双向耦合',
  '3': '3 核心心智与病理',
  '4': '4 低级中枢熔断与躯体化',
  '5': '5 .NET 工程落地',
};

/** SuperMonitorViewModel.LayerTitle：激素那几条没有层号，源码就落在「层 H」这一档。 */
const layerTitle = (layer: string) => LAYER_TITLE[layer] ?? `层 ${layer}`;

/** ArchitectureFlowDrawable.GroupTitles：图上每块子系统的标题。 */
const GROUP_TITLE: Record<string, string> = {
  sense: '感知输入',
  parse: '特征解析',
  memory: '五层记忆',
  cognition: '认知与注意',
  emotion: '情绪与病理',
  hormone: '激素与神经调制',
  drive: '内驱系统',
  self: '自我与人格',
  bodylow: '低级中枢',
  somatize: '躯体化',
  assembly: '装配与路由',
  output: '生成与输出',
};

/** ArchitectureFlowDrawable.GroupCells：4 × 3 的格子，激素居中当媒介。 */
const GROUP_CELL: Record<string, [number, number]> = {
  sense: [0, 0],
  parse: [1, 0],
  memory: [2, 0],
  cognition: [3, 0],
  emotion: [0, 1],
  hormone: [1, 1],
  drive: [2, 1],
  self: [3, 1],
  bodylow: [0, 2],
  somatize: [1, 2],
  assembly: [2, 2],
  output: [3, 2],
};

const CELL_W = 470;
const CELL_H = 404;
const CELL_GAP = 24;
const CELL_PAD = 14;
const CELL_TITLE_H = 26;
const OUTER_PAD = 30;
const NODE_W = 176;
const NODE_H = 48;
const GAP_X = 16;
const GAP_Y = 14;
const GRID_COLS = 4;
const GRID_ROWS = 3;
const CANVAS_W = OUTER_PAD * 2 + GRID_COLS * CELL_W + (GRID_COLS - 1) * CELL_GAP;
const CANVAS_H = OUTER_PAD * 2 + GRID_ROWS * CELL_H + (GRID_ROWS - 1) * CELL_GAP;

type Rect = { x: number; y: number; w: number; h: number };

/** 与 ArchitectureFlowDrawable.Layout 同一套排布：每块子系统两列紧凑居中。 */
function layoutNodes(): Record<string, Rect> {
  const rects: Record<string, Rect> = {};
  for (const [group, [col, row]] of Object.entries(GROUP_CELL)) {
    const members = NODES.filter((node) => node.group === group);
    if (members.length === 0) continue;
    const frameX = OUTER_PAD + col * (CELL_W + CELL_GAP);
    const frameY = OUTER_PAD + row * (CELL_H + CELL_GAP);
    const innerRows = Math.ceil(members.length / 2);
    const stackH = innerRows * NODE_H + (innerRows - 1) * GAP_Y;
    const top = frameY + CELL_TITLE_H + Math.max(CELL_PAD, (CELL_H - CELL_TITLE_H - stackH) / 2);
    members.forEach((node, index) => {
      const rowIndex = Math.floor(index / 2);
      const colIndex = index % 2;
      const inRow = Math.min(2, members.length - rowIndex * 2);
      const rowW = inRow * NODE_W + (inRow - 1) * GAP_X;
      const left = frameX + (CELL_W - rowW) / 2;
      rects[node.id] = {
        x: left + colIndex * (NODE_W + GAP_X),
        y: top + rowIndex * (NODE_H + GAP_Y),
        w: NODE_W,
        h: NODE_H,
      };
    });
  }
  return rects;
}

const LAYOUT = layoutNodes();

/** 连线从一格的边出发、落到另一格的边上，箭头才不会被节点盖住。 */
function borderPoint(rect: Rect, towardX: number, towardY: number) {
  const cx = rect.x + rect.w / 2;
  const cy = rect.y + rect.h / 2;
  const dx = towardX - cx;
  const dy = towardY - cy;
  if (dx === 0 && dy === 0) return { x: cx, y: cy };
  const scaleX = dx === 0 ? Number.POSITIVE_INFINITY : rect.w / 2 / Math.abs(dx);
  const scaleY = dy === 0 ? Number.POSITIVE_INFINITY : rect.h / 2 / Math.abs(dy);
  const scale = Math.min(scaleX, scaleY);
  return { x: cx + dx * scale, y: cy + dy * scale };
}

type EdgeDraw = { key: string; x1: number; y1: number; x2: number; y2: number; kind: LinkKind };

const EDGES: EdgeDraw[] = LINKS.flatMap((link, index) => {
  const from = LAYOUT[link.from];
  const to = LAYOUT[link.to];
  if (!from || !to) return [];
  const start = borderPoint(from, to.x + to.w / 2, to.y + to.h / 2);
  const end = borderPoint(to, from.x + from.w / 2, from.y + from.h / 2);
  return [{ key: `${link.from}->${link.to}#${index}`, x1: start.x, y1: start.y, x2: end.x, y2: end.y, kind: link.kind }];
});

const CAPTION: CSSProperties = { fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)', lineHeight: 1.6 };
const BODY_SM: CSSProperties = { fontSize: 'var(--w-size-small)', color: 'var(--w-ink-body)', lineHeight: 1.6 };
const SECTION_TITLE: CSSProperties = {
  fontSize: 'var(--w-size-small)',
  fontWeight: 700,
  color: 'var(--w-ink-body)',
};
const SUBHEAD: CSSProperties = {
  margin: 0,
  fontSize: 'var(--w-size-subhead)',
  fontWeight: 700,
  color: 'var(--w-ink-display)',
  lineHeight: 1.4,
};

/**
 * 页头在源码里绑的是当前存档那个人（SuperMonitorViewModel.CharacterName），没有存档时留空；
 * 后端没有监测接口，这里用这一页自己的 Title（SuperMonitorPage.xaml:11）当页头。
 */
const PAGE_TITLE = '超级监测';

/** 一条存档都没读到时源码写的那句话（SuperMonitorViewModel.cs:668）。 */
const NO_SAVE = '还没进入存档 —— 监测读的是存档里那个人的当下，先去开一份存档。';

/** FacePanelViewModel.StatusRows 在这一套设备上的读数（开关关着、模型未装）。 */
const FACE_ROWS: { label: string; value: string }[] = [
  { label: '情绪模型', value: '未安装' },
  { label: '本地推理', value: '未编译（WAB_ONNX 未定义）——当前走降级路径' },
  { label: '年龄模型', value: '未安装（可选，缺了只少年龄）' },
  { label: '摄像头开关', value: '关' },
  { label: '本存档授权', value: '未授权' },
  { label: '相机权限', value: '没查过' },
  { label: '上次识别', value: '还没识别过' },
];

const FACE_BLOCKED = '摄像头人脸识别总开关关着：设置 → 情感与身体 里打开它。';

type DetailLine = { label: string; value: string };

/** SuperMonitorViewModel.RealTimeLines：节点 id 能对上哪一路领域数据，就附上哪一路。 */
function realTimeLines(id: string): DetailLine[] {
  if (id === 'in.face') return FACE_ROWS.map((row) => ({ label: row.label, value: row.value }));
  if (id.startsWith('soma.') || id.startsWith('mem.') || id.startsWith('emo.')) return [];
  if (id.startsWith('eng.')) return [];
  if (id.startsWith('in.')) return [{ label: '今日日程', value: '0 项' }];
  return [];
}

function detailLines(node: ArchNode): DetailLine[] {
  const lines: DetailLine[] = [{ label: '所在层', value: layerTitle(node.layer) }];
  if (node.module.trim().length > 0) lines.push({ label: '实现模块', value: node.module });
  if (node.detail.trim().length > 0) lines.push({ label: '机制说明', value: node.detail });
  lines.push({ label: '执行状态', value: '○ 静默' });
  const extra = realTimeLines(node.id);
  if (extra.length > 0) {
    lines.push({ label: '· 实时关联', value: '同刻的领域数据' });
    lines.push(...extra);
  }
  return lines;
}

function pad2(value: number) {
  return String(value).padStart(2, '0');
}

function Lamp({ color, text }: { color: string; text: string }) {
  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: '5px' }}>
      <i
        aria-hidden="true"
        style={{ width: 9, height: 9, borderRadius: '99px', background: color, display: 'inline-block' }}
      />
      <span style={CAPTION}>{text}</span>
    </span>
  );
}

/** 人脸识别面板（FaceMonitorPane）：组件状态 + 图像流，两页共用同一套措辞。 */
export function FacePane() {
  const [preview, setPreview] = useState(false);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
        <span style={SECTION_TITLE}>人脸识别 · 只读</span>
        <GlassButton size="sm">刷新状态</GlassButton>
      </div>
      <p style={{ ...CAPTION, margin: 0 }}>
        只产出情绪 / 年龄 / 是否在场这类信息：不改数值、不推剧情、不触发动作。图像只在本机内存与一张本机临时图里，不上传，也不写进日志。
      </p>

      <div style={{ display: 'flex', flexDirection: 'column' }}>
        {FACE_ROWS.map((row) => (
          <div
            key={row.label}
            style={{ display: 'grid', gridTemplateColumns: '88px minmax(0, 1fr)', gap: 'var(--w-space-2)', padding: '3px 0' }}
          >
            <span style={CAPTION}>{row.label}</span>
            <span style={BODY_SM}>{row.value}</span>
          </div>
        ))}
      </div>

      <div style={{ display: 'flex', flexDirection: 'column', gap: '1px' }}>
        <span style={CAPTION}>本次降级原因</span>
        <span style={BODY_SM}>{FACE_BLOCKED}</span>
      </div>

      <div style={{ margin: '4px 0' }}>
        <Sep />
      </div>

      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: '1px', minWidth: 0 }}>
          <span style={SECTION_TITLE}>实时预览</span>
          <span style={CAPTION}>打开后每两秒在这幅画面上抓一帧识别；画面只在内存里，离开这一页相机立刻关掉。</span>
        </div>
        <GlassSwitch label="实时预览" checked={preview} onChange={setPreview} />
      </div>

      {preview ? <p style={{ ...CAPTION, margin: 0 }}>{`${FACE_BLOCKED}，已回落到最近一帧（画面在下方）。`}</p> : null}

      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
        <span style={SECTION_TITLE}>最近一帧</span>
        <GlassButton size="sm">抓一帧</GlassButton>
      </div>

      <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
        <span style={{ fontSize: 'var(--w-size-body)', color: 'var(--w-ink-display)' }}>还没有画面</span>
        <span style={CAPTION}>{FACE_BLOCKED}</span>
        <span style={{ fontSize: 'var(--w-size-caption)', color: 'var(--w-signal)', lineHeight: 1.6 }}>
          画面本身不受模型影响：点「抓一帧」就能看到最近一帧，只是情绪与年龄判断不了。
        </span>
      </div>
    </div>
  );
}

/**
 * 超级监测（Views/Monitor/SuperMonitorPage.xaml，311 行）+ ViewModels/Monitor/SuperMonitorViewModel.cs。
 * 左画布 / 右读数栏：左看「系统怎么跑」（架构活流程图 + 3D 向量），右读「系统现在怎么样」
 * （选中节点详情 / 日程 / 器官 / 记忆层 / 人脸）。后端没有监测接口，读数停在源码的空态：
 * 关系数值读不到就整栏不出现，页头退回这一页自己的标题。
 */
export function SuperMonitor({ navigate, back }: ScreenProps) {
  const [selected, setSelected] = useState<string | null>(null);
  const node = selected ? NODES.find((item) => item.id === selected) ?? null : null;

  const now = new Date();
  const dateText = `${now.getFullYear()}-${pad2(now.getMonth() + 1)}-${pad2(now.getDate())}`;
  const statusText = NO_SAVE;

  return (
    <PageShell>
      <PageHeader
        eyebrow="// VITALS"
        title={PAGE_TITLE}
        subtitle={dateText}
        backText="←"
        onBack={back}
        right={
          <GlassButton size="sm" onClick={() => setSelected(null)}>
            刷新
          </GlassButton>
        }
      />

      <div className="screen screen--full" style={{ maxWidth: 1720 }}>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--w-space-3)', alignItems: 'flex-start' }}>
          {/* 左：架构活流程图（+ 向量空间覆盖层） */}
          <div
            style={{
              flex: '1 1 620px',
              minWidth: 0,
              display: 'flex',
              flexDirection: 'column',
              gap: 'var(--w-space-3)',
            }}
          >
            <div
              style={{
                display: 'flex',
                flexWrap: 'wrap',
                alignItems: 'center',
                justifyContent: 'space-between',
                gap: 'var(--w-space-3)',
              }}
            >
              <div style={{ display: 'flex', flexDirection: 'column', gap: '2px', minWidth: 0 }}>
                <span style={SECTION_TITLE}>架构活动流程图</span>
                <span style={CAPTION}>对话执行时，流经的节点会亮起；点任意节点看它的读数与影响</span>
              </div>
              <div style={{ display: 'flex', flexDirection: 'column', gap: '5px', alignItems: 'flex-end' }}>
                <span style={SECTION_TITLE}>神游</span>
                <div style={{ display: 'flex', gap: 'var(--w-space-3)' }}>
                  <Lamp color="var(--w-hairline)" text="专注 0%" />
                  <Lamp color="var(--w-signal)" text="神游" />
                  <Lamp color="var(--w-hairline)" text="—" />
                </div>
                <span style={{ ...CAPTION, maxWidth: 330, textAlign: 'right' }}>
                  低精度默认态 · 反刍、整理、纠错、回忆连线都在这里发生
                </span>
              </div>
            </div>

            <Card>
              <div style={{ overflow: 'auto', maxHeight: 620, borderRadius: 'var(--w-radius-card)' }}>
                <svg
                  width={CANVAS_W}
                  height={CANVAS_H}
                  role="img"
                  aria-label="架构活动流程图"
                  style={{ display: 'block' }}
                >
                  <defs>
                    <marker id="arch-arrow" viewBox="0 0 8 8" refX="7" refY="4" markerWidth="6" markerHeight="6" orient="auto">
                      <path d="M0 0 L8 4 L0 8 z" style={{ fill: 'var(--w-ink-faint)' }} />
                    </marker>
                    <marker
                      id="arch-arrow-signal"
                      viewBox="0 0 8 8"
                      refX="7"
                      refY="4"
                      markerWidth="6"
                      markerHeight="6"
                      orient="auto"
                    >
                      <path d="M0 0 L8 4 L0 8 z" style={{ fill: 'var(--w-signal)' }} />
                    </marker>
                  </defs>

                  {Object.entries(GROUP_CELL).map(([group, [col, row]]) => {
                    const x = OUTER_PAD + col * (CELL_W + CELL_GAP);
                    const y = OUTER_PAD + row * (CELL_H + CELL_GAP);
                    return (
                      <g key={group}>
                        <rect
                          x={x}
                          y={y}
                          width={CELL_W}
                          height={CELL_H}
                          rx={14}
                          style={{ fill: 'var(--w-surface-tint)', stroke: 'var(--w-hairline)', strokeDasharray: '6 5' }}
                        />
                        <text
                          x={x + CELL_PAD}
                          y={y + 18}
                          style={{ fill: 'var(--w-ink-muted)', fontSize: 'var(--w-size-caption)', fontWeight: 700 }}
                        >
                          {GROUP_TITLE[group]}
                        </text>
                      </g>
                    );
                  })}

                  {EDGES.map((edge) => {
                    const dashed = edge.kind === 'Bidirectional' || edge.kind === 'Feedback';
                    return (
                      <line
                        key={edge.key}
                        x1={edge.x1}
                        y1={edge.y1}
                        x2={edge.x2}
                        y2={edge.y2}
                        markerEnd={`url(#${edge.kind === 'Feedback' ? 'arch-arrow-signal' : 'arch-arrow'})`}
                        style={{
                          stroke: edge.kind === 'Feedback' ? 'var(--w-signal)' : 'var(--w-ink-faint)',
                          strokeOpacity: edge.kind === 'Control' ? 0.45 : 0.7,
                          strokeWidth: 1,
                          strokeDasharray: edge.kind === 'Control' ? '2 4' : dashed ? '5 4' : undefined,
                        }}
                      />
                    );
                  })}

                  {NODES.map((item) => {
                    const rect = LAYOUT[item.id];
                    if (!rect) return null;
                    const active = item.id === selected;
                    return (
                      <g
                        key={item.id}
                        onClick={() => setSelected(item.id)}
                        style={{ cursor: 'pointer' }}
                      >
                        <title>{item.detail}</title>
                        <rect
                          x={rect.x}
                          y={rect.y}
                          width={rect.w}
                          height={rect.h}
                          rx={10}
                          style={{
                            fill: 'var(--w-surface-tint)',
                            stroke: active ? 'var(--w-signal)' : 'var(--w-hairline)',
                            strokeWidth: active ? 2 : 1,
                          }}
                        />
                        <text
                          x={rect.x + rect.w / 2}
                          y={rect.y + rect.h / 2}
                          textAnchor="middle"
                          dominantBaseline="central"
                          style={{
                            fill: active ? 'var(--w-ink-display)' : 'var(--w-ink-body)',
                            fontSize: 'var(--w-size-small)',
                          }}
                        >
                          {item.label}
                        </text>
                      </g>
                    );
                  })}
                </svg>
              </div>
            </Card>

            <Card>
              <div style={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) 236px', gap: 'var(--w-space-3)' }}>
                <div style={{ display: 'flex', flexDirection: 'column', gap: '2px', minWidth: 0 }}>
                  <span style={SECTION_TITLE}>向量空间</span>
                  <span style={CAPTION}>向量空间还没读出来。</span>
                </div>
                <span style={{ ...CAPTION, textAlign: 'right', alignSelf: 'end' }}>坐标 · 词语</span>
                <svg width="100%" height={300} role="img" aria-label="向量空间三轴投影">
                  <line x1={26} y1={268} x2={250} y2={268} style={{ stroke: 'var(--w-hairline)' }} />
                  <line x1={26} y1={268} x2={26} y2={30} style={{ stroke: 'var(--w-hairline)' }} />
                  <line x1={26} y1={268} x2={130} y2={176} style={{ stroke: 'var(--w-hairline)' }} />
                  <text x={256} y={272} style={{ fill: 'var(--w-ink-muted)', fontSize: 'var(--w-size-caption)' }}>
                    分块 Ⅰ
                  </text>
                  <text x={20} y={22} style={{ fill: 'var(--w-ink-muted)', fontSize: 'var(--w-size-caption)' }}>
                    分块 Ⅱ
                  </text>
                  <text x={132} y={170} style={{ fill: 'var(--w-ink-muted)', fontSize: 'var(--w-size-caption)' }}>
                    分块 Ⅲ
                  </text>
                  <text x={26} y={310} style={{ fill: 'var(--w-ink-faint)', fontSize: 'var(--w-size-caption)' }}>
                    还没有可投影的向量。
                  </text>
                </svg>
                <div />
              </div>
            </Card>

            <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
              <span style={CAPTION}>{statusText}</span>
            </div>
          </div>

          {/* 右：读数栏 */}
          <div
            style={{
              flex: '0 1 360px',
              minWidth: 300,
              display: 'flex',
              flexDirection: 'column',
              gap: 'var(--w-space-3)',
            }}
          >
            {node ? (
              <Card>
                <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
                  <h2 style={SUBHEAD}>{node.label}</h2>
                  <div style={{ display: 'flex', gap: 'var(--w-space-3)' }}>
                    <span style={CAPTION}>{layerTitle(node.layer)}</span>
                    <span style={CAPTION}>静默</span>
                  </div>
                  <div>
                    <GlassButton
                      size="md"
                      onClick={() => navigate(`/moduleInspector?id=${encodeURIComponent(node.id)}`)}
                    >
                      打开这个模块的页面
                    </GlassButton>
                  </div>
                  <Sep />
                  <div
                    style={{
                      display: 'flex',
                      flexDirection: 'column',
                      gap: 'var(--w-space-3)',
                      maxHeight: 260,
                      overflowY: 'auto',
                    }}
                  >
                    {detailLines(node).map((line, index) => (
                      <div key={`${line.label}-${index}`} style={{ display: 'flex', flexDirection: 'column', gap: '1px' }}>
                        <span style={CAPTION}>{line.label}</span>
                        <span style={{ ...BODY_SM, overflowWrap: 'anywhere' }}>{line.value}</span>
                      </div>
                    ))}
                  </div>
                </div>
              </Card>
            ) : null}

            <Card>
              <div style={{ display: 'flex', flexDirection: 'column', gap: '0px' }}>
                <span style={SECTION_TITLE}>今日日程</span>
                <span style={{ ...CAPTION, margin: '2px 0 4px' }}>今天还没有日程：她还没排，或者这一档刚开局</span>

                <div style={{ margin: '10px 0' }}>
                  <Sep />
                </div>

                <span style={SECTION_TITLE}>器官状态</span>
                <span style={{ ...CAPTION, margin: '2px 0 4px' }}>还没有器官读数：躯体化服务还没开始记</span>

                <div style={{ margin: '10px 0' }}>
                  <Sep />
                </div>

                <span style={SECTION_TITLE}>记忆分层</span>
                <span style={{ ...CAPTION, margin: '2px 0 4px' }}>五层都还是 0：和她聊几句，这里会开始长</span>

                <div style={{ margin: '10px 0' }}>
                  <Sep />
                </div>

                <FacePane />
              </div>
            </Card>
          </div>
        </div>
      </div>
    </PageShell>
  );
}
