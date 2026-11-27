import { useState, type CSSProperties } from 'react';
import { GlassButton } from '../../components/GlassButton';
import { PageShell } from '../../components/PageShell';
import { Card, PageHeader, Sep } from '../../components/kit';
import type { ScreenProps } from '../types';
import { FacePane } from './superMonitor';

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

/** 取不到数时那一行写的话：说清楚是「没有这一项」，不是「读取失败」。 */
const NoReading = '该模块暂无独立读数';

/** 与 FaceMonitorPane 共用同一句降级原因。 */
const FACE_BLOCKED = '摄像头人脸识别总开关关着：设置 → 情感与身体 里打开它。';

type Row = { label: string; value: string };

/** 与监测页骨架同一套 id、名字与说明（ModuleInspectorViewModel.Catalog）。 */
const CATALOG: Record<string, { name: string; layer: string; summary: string }> = {
  "in.text": { name: "文字输入", layer: "1", summary: "Send 入口记录用户文本，整条执行链的起点" },
  "in.image": { name: "图像输入", layer: "1", summary: "附件图片入口，交给视觉特征提取" },
  "in.face": { name: "摄像头人脸", layer: "1", summary: "看一眼的情绪 / 年龄 / 是否在场：只读，不参与任何决策" },
  "in.voice": { name: "语音输入", layer: "1", summary: "语音转写文本入口，交给声学分析" },
  "in.fusion": { name: "多模态融合", layer: "1", summary: "文字 / 图像 / 语音特征融合后进入具身锚定" },
  "in.semantic": { name: "语义解析", layer: "1", summary: "文本 → 语义特征" },
  "in.visual": { name: "视觉特征", layer: "1", summary: "图像附件的视觉特征提取" },
  "in.audio": { name: "声学分析", layer: "1", summary: "语音附件的声学特征分析" },
  "in.imagery": { name: "意象唤起", layer: "1", summary: "语义特征触发记忆意象" },
  "in.intent": { name: "意图识别", layer: "1", summary: "识别意图，参与后续路由预判" },
  "in.edge": { name: "边缘检测", layer: "1", summary: "图形线条 / 边缘检测" },
  "in.entity": { name: "实体识别", layer: "1", summary: "识别实体并关联记忆条目" },
  "in.tone": { name: "语调情绪", layer: "1", summary: "语调 / 情绪分析，写入情绪基调" },
  "in.embodied": { name: "具身锚定", layer: "1", summary: "具身认知 / 情感锚定：多模态汇聚到心智入口" },
  "mem.shallow": { name: "浅记忆", layer: "2", summary: "浅记忆 / 前记忆：近期对话缓冲" },
  "mem.deepen": { name: "记忆深化", layer: "2", summary: "重要性 / 回想次数 / 唤醒度 / 时间衰减共同深化" },
  "mem.deep": { name: "深层记忆", layer: "2", summary: "深层记忆：情景 / 语义 / 程序三类" },
  "mem.episodic": { name: "情景记忆", layer: "2", summary: "带时间地点的经历" },
  "mem.semanticM": { name: "语义记忆", layer: "2", summary: "概念与事实" },
  "mem.procedural": { name: "程序记忆", layer: "2", summary: "习惯化的应对套路" },
  "mem.retrieval": { name: "记忆检索器", layer: "2", summary: "按窗口 / 线索回想，受情绪偏置 moodBias 影响" },
  "mem.rumination": { name: "反刍循环", layer: "2", summary: "负面回忆链延展，加剧情绪内耗" },
  "emo.state": { name: "情绪状态", layer: "2", summary: "当前情绪：效价 / 唤醒度 / 控制感三维" },
  "emo.valence": { name: "情绪效价", layer: "2", summary: "Valence（-1..1）" },
  "emo.arousal": { name: "唤醒度", layer: "2", summary: "Arousal（0..1）" },
  "emo.dominance": { name: "控制感", layer: "2", summary: "Dominance" },
  "emo.coupling": { name: "记忆情绪耦合", layer: "2", summary: "记忆 ↔ 情绪双向耦合，moodBias 生效时点亮" },
  "cognition.focus": { name: "专注模式", layer: "2", summary: "多巴胺驱动、大模型高精度；耗专属精力，用得越多停留越久" },
  "cognition.wander": { name: "神游模式", layer: "2", summary: "低精度默认态；反刍 / 整理 / 纠错 / 新想法 / 回忆连线" },
  "cognition.intervene": { name: "神游介入", layer: "2", summary: "专注快撑不住时，神游往脑海随机塞旧记忆" },
  "mind.trauma": { name: "创伤节点", layer: "3", summary: "核心信念与旧伤的锚点" },
  "mind.persona": { name: "人格系统", layer: "3", summary: "人格 / 核心信念：设定情绪基调" },
  "mind.pattern": { name: "行为模式", layer: "3", summary: "由深层记忆塑造的行为模式" },
  "mind.intrusive": { name: "侵入性思维", layer: "3", summary: "不受控的念头" },
  "mind.summary": { name: "总结评估", layer: "3", summary: "对当前情境的认知评价" },
  "path.state": { name: "病理状态机", layer: "3", summary: "PTSD / 抑郁 / 焦虑 / BPD 状态机" },
  "path.ptsd": { name: "PTSD", layer: "3", summary: "降低创伤阈值，杏仁核劫持" },
  "path.depression": { name: "抑郁", layer: "3", summary: "反刍死锁，情绪基线极低" },
  "path.anxiety": { name: "焦虑", layer: "3", summary: "联想过度发散" },
  "path.bpd": { name: "BPD", layer: "3", summary: "防御机制极化" },
  "core.driveSystem": { name: "内驱动系统", layer: "3", summary: "可整体停用：关掉后数值冻结、不进提示词，也不再产驱动力" },
  "core.drivePower": { name: "三种基本动力", layer: "3", summary: "攻击性 / 性 / 自恋：力的来源" },
  "core.driveSeven": { name: "七个驱动", layer: "3", summary: "贪欲 / 暴食 / 色欲 / 嫉妒 / 傲慢 / 怠惰 / 暴怒" },
  "core.driveGate": { name: "自律闸门", layer: "3", summary: "道德观 × 人生观 × 思维主义决定的压制力道" },
  "core.discharge": { name: "八级发泄", layer: "3", summary: "叛逆 → 撒泼 → 怄气 → 任性 → 郁闷 → 拖延 → 厌烦 → 退缩" },
  "soma.coping": { name: "应对机制", layer: "4", summary: "应对决策：战 / 逃 / 僵三分支" },
  "soma.fight": { name: "反击防御", layer: "4", summary: "高唤醒下的对峙分支" },
  "soma.reply": { name: "理智回复", layer: "4", summary: "常态输出分支" },
  "soma.freeze": { name: "僵直宕机", layer: "4", summary: "低控制感时的冻结分支" },
  "soma.bypass": { name: "低级中枢越权", layer: "4", summary: "低级中枢越权 / 熔断，绕过理智" },
  "soma.response": { name: "器官响应", layer: "4", summary: "器官响应 / 躯体化：脏器状态外显" },
  "soma.delay": { name: "输出延迟", layer: "4", summary: "输出延迟 / 反应迟钝" },
  "soma.stutter": { name: "字符碎裂", layer: "4", summary: "字符碎裂 / 结巴 / 颤抖" },
  "soma.downgrade": { name: "逻辑降级", layer: "4", summary: "逻辑降级 / 算力熔断" },
  "soma.panic": { name: "惊恐发作", layer: "4", summary: "惊恐发作 / 呼吸急促模拟" },
  "soma.recovery": { name: "器官恢复期", layer: "4", summary: "恢复期：情绪反复 / 迟滞" },
  "eng.statepack": { name: "状态打包", layer: "5", summary: "把心智 / 器官状态打成模型输入" },
  "eng.router": { name: "意图路由", layer: "5", summary: "意图路由分发：本地轻量 / 云端重量" },
  "eng.redis": { name: "Redis 缓存", layer: "5", summary: "Redis 状态缓存" },
  "eng.qdrant": { name: "Qdrant 向量", layer: "5", summary: "Qdrant 向量记忆库" },
  "eng.sqlite": { name: "SQLite 结构化", layer: "5", summary: "结构化数据（日程 / 存档）" },
  "eng.model.light": { name: "轻量模型", layer: "5", summary: "轻量本地模型" },
  "eng.model.heavy": { name: "云端模型", layer: "5", summary: "云端大模型" },
  "eng.prompt": { name: "Prompt 组装", layer: "5", summary: "System Prompt 动态组装" },
  "eng.postprocess": { name: "后处理", layer: "5", summary: "文本修饰器 / 流式拦截中间件" },
  "eng.output": { name: "流式输出", layer: "5", summary: "SSE 流式输出给用户" },
  "eng.loop": { name: "循环反馈", layer: "5", summary: "输出回灌为下一轮输入" },
  "horm.pool": { name: "内分泌池", layer: "H", summary: "六项化学值按半衰期向气质基线回落" },
  "horm.dopa": { name: "多巴胺", layer: "H", summary: "收益预期与干劲：想做、想要、有盼头" },
  "horm.sero": { name: "血清素", layer: "H", summary: "安宁与稳定：不急、不钻牛角尖" },
  "horm.cort": { name: "皮质醇", layer: "H", summary: "压力与警觉：往坏处想、绷着" },
  "horm.adr": { name: "肾上腺素", layer: "H", summary: "危急：精力↑ 警觉↑，过量有器官代价" },
  "horm.endo": { name: "内啡肽", layer: "H", summary: "镇痛与松快：发泄过、忍下来之后的那口气" },
  "horm.chol": { name: "胆固醇", layer: "H", summary: "颓废遭打击时的中和：食欲↓ 干劲↑" },
  "horm.ltd": { name: "长期激励素", layer: "H", summary: "长期目标达成的奖励，衰减极慢" },
};
function systemOf(id: string): string {
  if (id.startsWith('in.')) return '感知输入 · 特征解析';
  if (id.startsWith('mem.')) return '五层记忆';
  if (id.startsWith('emo.')) return '情绪与病理';
  if (id.startsWith('path.')) return '情绪与病理';
  if (id.startsWith('cognition.')) return '认知与注意';
  if (id.startsWith('mind.')) return '自我与人格';
  if (id.startsWith('core.')) return '内驱系统';
  if (id.startsWith('soma.')) return '低级中枢 · 躯体化';
  if (id.startsWith('eng.')) return '装配与路由 · 生成与输出';
  if (id.startsWith('horm.')) return '激素与神经调制';
  return '未登记模块';
}

function layerOf(layer: string): string {
  switch (layer) {
    case '1':
      return '第 1 层 · 感知与输入抽象层';
    case '2':
      return '第 2 层 · 记忆与情绪双向耦合';
    case '3':
      return '第 3 层 · 核心心智与病理';
    case '4':
      return '第 4 层 · 低级中枢熔断与躯体化';
    case '5':
      return '第 5 层 · .NET 工程落地';
    case 'H':
      return '介质层 · 激素与神经调制';
    default:
      return '';
  }
}

/** 每个数都取不到时那一行就是「该模块暂无独立读数」；说明性文字照源码原样留在页上。 */
function hormoneRows(id: string): Row[] {
  switch (id) {
    case 'horm.pool':
      return [
        { label: '数据来源', value: 'InnerLifeService 化学池（取数前已结算一次）' },
        { label: '多巴胺（兴奋 / 期待）', value: NoReading },
        { label: '皮质醇（压力 / 警觉）', value: NoReading },
        { label: '血清素（安宁 / 稳定）', value: NoReading },
        { label: '肾上腺素（危急）', value: NoReading },
        { label: '胆固醇（打击中和）', value: NoReading },
        { label: '长期激励素', value: NoReading },
        { label: '干劲（派生）', value: NoReading },
        { label: '疲劳', value: NoReading },
        { label: '更新于', value: NoReading },
      ];
    case 'horm.dopa':
      return [
        { label: '当前值', value: NoReading },
        { label: '它在做什么', value: '收益预期与干劲：想做、想要、有盼头' },
        { label: '派生干劲', value: NoReading },
        { label: '更新于', value: NoReading },
      ];
    case 'horm.sero':
      return [
        { label: '当前值', value: NoReading },
        { label: '它在做什么', value: '安宁与稳定：不急、不钻牛角尖' },
        { label: '更新于', value: NoReading },
      ];
    case 'horm.cort':
      return [
        { label: '当前值', value: NoReading },
        { label: '它在做什么', value: '压力与警觉：往坏处想、绷着' },
        { label: '越线判据', value: '高过 0.68 时低级中枢越权，理智回复那条分支会被顶掉' },
        { label: '更新于', value: NoReading },
      ];
    case 'horm.adr':
      return [
        { label: '当前值', value: NoReading },
        { label: '它在做什么', value: '危急：精力↑ 警觉↑，过量自带虚脱感，也会伤及各器官' },
        { label: '疲劳', value: NoReading },
        { label: '更新于', value: NoReading },
      ];
    case 'horm.endo':
      return [
        { label: '当前值', value: NoReading },
        { label: '它在做什么', value: '镇痛与松快：发泄过、忍下来之后的那口气' },
        { label: '当前出口', value: NoReading },
        { label: '更新于', value: NoReading },
      ];
    case 'horm.chol':
      return [
        { label: '当前值', value: NoReading },
        { label: '它在做什么', value: '颓废遭打击时的中和：食欲↓ 干劲↑' },
        { label: '更新于', value: NoReading },
      ];
    case 'horm.ltd':
      return [
        { label: '当前值', value: NoReading },
        { label: '它在做什么', value: '长期目标达成的奖励，衰减极慢' },
        { label: '更新于', value: NoReading },
      ];
    default:
      return [{ label: '当前值', value: NoReading }];
  }
}

function driveRows(id: string): Row[] {
  switch (id) {
    case 'core.driveSystem':
      return [
        { label: '内驱系统', value: '未开启（或这个角色还没有驱动记录）' },
        { label: '最强一股', value: NoReading },
        { label: '火气', value: NoReading },
        { label: '更新于', value: NoReading },
      ];
    case 'core.drivePower':
      return [
        { label: '攻击性（推着去争）', value: NoReading },
        { label: '性（连接与亲密）', value: NoReading },
        { label: '自恋（被看见）', value: NoReading },
        { label: '三者的角色', value: '力的来源：决定她多容易被推动' },
      ];
    case 'core.driveSeven':
      return [
        { label: '贪欲', value: NoReading },
        { label: '暴食', value: NoReading },
        { label: '色欲', value: NoReading },
        { label: '嫉妒', value: NoReading },
        { label: '傲慢', value: NoReading },
        { label: '怠惰', value: NoReading },
        { label: '暴怒', value: NoReading },
        { label: '越过 0.42', value: '会化成一句「想做点什么」进提示词' },
      ];
    case 'core.driveGate':
      return [
        { label: '自律', value: NoReading },
        { label: '道德观', value: NoReading },
        { label: '人生观', value: NoReading },
        { label: '思维主义', value: NoReading },
        { label: '关系安全度', value: NoReading },
      ];
    case 'core.discharge':
      return [
        { label: '火气', value: NoReading },
        { label: '当前出口', value: NoReading },
        { label: '具体表现', value: NoReading },
        { label: '内啡肽', value: NoReading },
        { label: '八种出口', value: '叛逆 > 撒泼 > 怄气 > 任性 > 郁闷 > 拖延 > 厌烦 > 退缩' },
      ];
    default:
      return [{ label: '当前值', value: NoReading }];
  }
}

function emotionRows(id: string): Row[] {
  switch (id) {
    case 'emo.state':
      return [
        { label: '效价（-1..1）', value: NoReading },
        { label: '唤醒度（0..1）', value: NoReading },
        { label: '控制感', value: NoReading },
        { label: '数据来源', value: 'InnerLifeService 化学池派生（与监测页角标同一套映射）' },
        { label: '更新于', value: NoReading },
      ];
    case 'emo.valence':
      return [
        { label: '效价（多巴胺 − 皮质醇）', value: NoReading },
        { label: '多巴胺', value: NoReading },
        { label: '皮质醇', value: NoReading },
        { label: '更新于', value: NoReading },
      ];
    case 'emo.arousal':
      return [
        { label: '唤醒度（肾上腺素）', value: NoReading },
        { label: '肾上腺素', value: NoReading },
        { label: '疲劳', value: NoReading },
        { label: '更新于', value: NoReading },
      ];
    case 'emo.dominance':
      return [
        { label: '控制感（血清素 − 皮质醇）', value: NoReading },
        { label: '血清素', value: NoReading },
        { label: '皮质醇', value: NoReading },
        { label: '更新于', value: NoReading },
      ];
    case 'emo.coupling':
      return [
        { label: '多巴胺', value: NoReading },
        { label: '血清素', value: NoReading },
        { label: '带情绪配比的记忆', value: NoReading },
        { label: '耦合方式', value: '回想时的情绪偏置 moodBias 会按配比改写记忆检索的权重' },
      ];
    default:
      return [{ label: '当前值', value: NoReading }];
  }
}

function memoryRows(id: string): Row[] {
  switch (id) {
    case 'mem.shallow':
      return [
        { label: '数据来源', value: 'HumanMemoryService 记忆库' },
        { label: '在场记忆', value: NoReading },
        { label: '核心记忆', value: NoReading },
        { label: '最近写入', value: NoReading },
        { label: '最近回想', value: NoReading },
      ];
    case 'mem.deepen':
      return [
        { label: '核心记忆', value: NoReading },
        { label: '平均重要性', value: NoReading },
        { label: '平均回想次数', value: NoReading },
        { label: '深化依据', value: '重要性 × 回想次数 × 唤醒度 × 时间衰减共同决定下不下去' },
      ];
    case 'mem.deep':
      return [
        { label: '深层记忆', value: NoReading },
        { label: '情景', value: NoReading },
        { label: '语义', value: NoReading },
        { label: '核心 / 创伤', value: NoReading },
      ];
    case 'mem.episodic':
      return [{ label: '情景记忆', value: NoReading }];
    case 'mem.semanticM':
      return [{ label: '语义记忆', value: NoReading }];
    case 'mem.procedural':
      return [
        { label: '程序记忆', value: NoReading },
        { label: '可用的抽象印象', value: NoReading },
        { label: '机制', value: '程序记忆是习惯化的应对套路，由深层记忆塑造；它现在没有独立的数值化服务' },
      ];
    case 'mem.retrieval':
      return [
        { label: '可回想的核心记忆', value: NoReading },
        { label: '情绪偏置 moodBias', value: NoReading },
        { label: '最近回想', value: NoReading },
        { label: '检索方式', value: '按窗口 / 线索打分：重要性 × 时间衰减 × 复述次数 × 随机扰动，再乘情绪偏置' },
      ];
    case 'mem.rumination':
      return [
        { label: '创伤记忆', value: NoReading },
        { label: '反刍链读数', value: NoReading },
        { label: '最近回想', value: NoReading },
        { label: '机制', value: '负面回忆顺着关联词串开，越串越重；这条链只有「被走了几次」，没有独立数值' },
      ];
    default:
      return [{ label: '当前值', value: NoReading }];
  }
}

function mindRows(id: string): Row[] {
  if (id === 'path.state') {
    return [
      { label: '携带状况', value: '未携带任何目录条目' },
      { label: '更新于', value: NoReading },
    ];
  }
  if (id === 'path.ptsd' || id === 'path.depression' || id === 'path.anxiety' || id === 'path.bpd') {
    const rows: Row[] = [
      { label: '是否携带', value: '未携带（这条状态机目前没有条目）' },
      { label: '严重度', value: NoReading },
      { label: '稳定度', value: NoReading },
      { label: '起始于', value: NoReading },
      { label: '目录症状', value: NoReading },
      { label: '行为倾向', value: NoReading },
      { label: '备注', value: NoReading },
    ];
    if (id === 'path.ptsd') rows.push({ label: '创伤记忆', value: NoReading });
    return rows;
  }
  switch (id) {
    case 'mind.trauma':
      return [
        { label: '创伤类状况', value: '无' },
        { label: '核心创伤记忆', value: NoReading },
        { label: '最深的阶段', value: NoReading },
      ];
    case 'mind.intrusive':
      return [{ label: '侵入性思维', value: '当前无侵入性思维类状况' }];
    case 'mind.persona':
      return [
        { label: '人格读数', value: NoReading },
        { label: '人格锚点（核心记忆）', value: NoReading },
        { label: '携带状况', value: '无' },
        { label: '说明', value: '核心记忆与情绪基调共同设这个人格；它自己没有单独的数值' },
      ];
    case 'mind.pattern':
      return [
        { label: '行为模式读数', value: NoReading },
        { label: '抽象印象', value: NoReading },
        { label: '机制', value: '程序记忆：由深层记忆塑造的习惯化应对套路（对应图上 mem.procedural）' },
        { label: '建模状态', value: '没有专门的数值化服务，这一页不显示推测数值' },
      ];
    case 'mind.summary':
      return [
        { label: '总结评估读数', value: NoReading },
        { label: '携带状况', value: '无' },
        { label: '说明', value: '总结评估是对当前情境的认知评价，每次回复都会重算，不落盘成单独数值' },
      ];
    default:
      return [{ label: '当前值', value: NoReading }];
  }
}

function somaRows(id: string): Row[] {
  const base: Row[] = [
    { label: '器官读数覆盖', value: NoReading },
    { label: '点亮的器官', value: '全部平静' },
    { label: '读数最高的一处', value: NoReading },
    { label: '当前症状', value: '无明显症状' },
    { label: '连锁规则', value: NoReading },
    { label: '更新于', value: NoReading },
  ];
  if (id === 'soma.coping') {
    return [
      { label: '应对分支读数', value: NoReading },
      { label: '当前皮质醇', value: NoReading },
      { label: '分支判定', value: '战 / 逃 / 僵由唤醒度与控制感共同决定，没有单独的落盘数值' },
      ...base,
    ];
  }
  if (id === 'soma.fight' || id === 'soma.freeze' || id === 'soma.reply') {
    return [
      { label: '分支读数', value: NoReading },
      { label: '说明', value: '这一分支只在当轮执行里走过一次，图上亮灯即代表走过，没有独立数值' },
      ...base,
    ];
  }
  if (id === 'soma.bypass') {
    return [
      { label: '当前皮质醇', value: NoReading },
      { label: '越线判据', value: '皮质醇高过 0.68 时低级中枢越权，理智回复那条分支被顶掉' },
      ...base,
    ];
  }
  return [{ label: '本节点独立读数', value: NoReading }, ...base];
}

function cognitionRows(id: string): Row[] {
  if (id === 'cognition.intervene') {
    return [
      { label: '神游是否该介入', value: NoReading },
      { label: '当前档位', value: NoReading },
      { label: '专注燃料', value: '多巴胺越高，进入专注越快' },
    ];
  }
  return [
    { label: '当前档位', value: NoReading },
    { label: '专注储备', value: NoReading },
    { label: '介入判据', value: NoReading },
    { label: '专注燃料', value: '多巴胺越高，进入专注越快' },
  ];
}

function faceRows(): Row[] {
  return [
    { label: '这一路做什么', value: '看一眼：情绪 / 年龄 / 是否在场。只返回信息，不改数值、不推剧情、不触发动作' },
    { label: '上次识别', value: '还没识别过' },
    { label: '降级原因', value: FACE_BLOCKED },
  ];
}

function flowRows(): Row[] {
  return [
    { label: '执行状态', value: '○ 静默' },
    { label: '余辉', value: '0.00' },
    { label: '本节点读数', value: NoReading },
    { label: '写入的记忆', value: NoReading },
    { label: '最近写入', value: NoReading },
  ];
}

function readingsFor(id: string): Row[] {
  if (id.startsWith('horm.')) return hormoneRows(id);
  if (id.startsWith('core.')) return driveRows(id);
  if (id.startsWith('emo.')) return emotionRows(id);
  if (id.startsWith('mem.')) return memoryRows(id);
  if (id.startsWith('mind.') || id.startsWith('path.')) return mindRows(id);
  if (id.startsWith('soma.')) return somaRows(id);
  if (id.startsWith('cognition.')) return cognitionRows(id);
  if (id === 'in.face') return faceRows();
  return flowRows();
}

function pad2(value: number) {
  return String(value).padStart(2, '0');
}

/**
 * 模块详情（Views/Monitor/ModuleInspectorPage.xaml，134 行）+ ViewModels/Monitor/ModuleInspectorViewModel.cs。
 * 从超级监测的图上点一枚节点进来，只回答三件事：这是谁、它此刻是多少、它最近怎么变的。
 * 后端没有读数接口，每一行都停在源码那句「该模块暂无独立读数」，不猜数。
 */
export function ModuleInspector({ query, back }: ScreenProps) {
  const id = (query.get('id') ?? '').trim();
  const [readAt, setReadAt] = useState(() => new Date());

  const meta = id.length > 0 ? CATALOG[id] : undefined;
  const missing = id.length === 0;
  const moduleTitle = missing ? '模块详情' : meta?.name ?? id;
  const systemName = missing ? '' : systemOf(id);
  const summary = missing
    ? '没有拿到要看哪个模块：这一页要通过 shell 路由 moduleInspector 带着 id 进来。'
    : meta?.summary ?? '这个节点不在监测图的骨架里（多半是日程 / 器官 / 记忆条目），只能读到它执行时的痕迹。';
  const layerText = meta ? layerOf(meta.layer) : '';
  const locationText = missing ? '' : layerText.length > 0 ? `${layerText} · 节点 id ${id}` : `节点 id ${id}`;
  const readings = missing ? [{ label: '模块 id', value: NoReading }] : readingsFor(id);
  const hasFacePane = id === 'in.face';
  const statusText = missing
    ? '缺少节点 id'
    : `读数 ${readings.length} 行 · 变化 0 条 · 取数于 ${pad2(readAt.getHours())}:${pad2(readAt.getMinutes())}:${pad2(readAt.getSeconds())}`;

  return (
    <PageShell>
      <PageHeader
        eyebrow="// MODULE INSPECTOR"
        title={moduleTitle}
        subtitle={systemName}
        backText="←"
        onBack={back}
        right={
          <GlassButton size="sm" onClick={() => setReadAt(new Date())}>
            刷新
          </GlassButton>
        }
      />

      <div className="screen" style={{ maxWidth: 1120 }}>
        <Card>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-3)' }}>
            <div style={{ display: 'flex', alignItems: 'flex-start', gap: 'var(--w-space-3)' }}>
              <i
                aria-hidden="true"
                style={{
                  width: 14,
                  height: 14,
                  marginTop: 4,
                  borderRadius: '7px',
                  background: 'var(--w-ink-faint)',
                  flex: '0 0 auto',
                }}
              />
              <div style={{ display: 'flex', flexDirection: 'column', gap: '2px', minWidth: 0 }}>
                <h2 style={SUBHEAD}>{moduleTitle}</h2>
                <span style={CAPTION}>{systemName}</span>
              </div>
            </div>
            <span style={CAPTION}>暗 · 静默</span>
            <span style={{ ...BODY_SM, overflowWrap: 'anywhere' }}>{summary}</span>
            <Sep />
            <span style={CAPTION}>{locationText}</span>
          </div>
        </Card>

        <Card>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-3)' }}>
            <span style={SECTION_TITLE}>实时读数</span>
            <span style={CAPTION}>每一行都带着出处；取不到的那一行会写明「该模块暂无独立读数」，不猜数。</span>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-3)' }}>
              {readings.map((row, index) => (
                <div
                  key={`${row.label}-${index}`}
                  style={{ display: 'grid', gridTemplateColumns: 'auto minmax(0, 1fr)', gap: 'var(--w-space-3)', alignItems: 'start' }}
                >
                  <span style={{ ...CAPTION, maxWidth: 132, overflowWrap: 'anywhere' }}>{row.label}</span>
                  <span style={{ ...BODY_SM, overflowWrap: 'anywhere' }}>{row.value}</span>
                </div>
              ))}
            </div>
          </div>
        </Card>

        {hasFacePane ? (
          <Card>
            <FacePane />
          </Card>
        ) : null}

        <Card>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-3)' }}>
            <span style={SECTION_TITLE}>最近变化</span>
            <span style={CAPTION}>最多 12 条，按时间倒序；每条都带着它的出处。</span>
            <span style={CAPTION}>这一路还没有留下变化记录。</span>
          </div>
        </Card>

        <span style={CAPTION}>{statusText}</span>
      </div>
    </PageShell>
  );
}
