import { useEffect, useState, type CSSProperties } from 'react';
import { GlassSwitch } from '../../components/GlassSwitch';
import { PageShell } from '../../components/PageShell';
import { SignalButton } from '../../components/SignalButton';
import { Card, Field, PageHeader, Select } from '../../components/kit';
import type { ScreenProps } from '../types';
import { Inert } from './parts';

/**
 * 心理状况（Views/Settings/MentalHealthPage.xaml）：一句「这不是诊断」，安全感读数与小模型整理开关，
 * 当前生效的几条（可调轻重、可移除），最后从 116 条目录里添加。没有存档时只留入口，不写任何角色的状况。
 */

/** MentalHealthService.Catalog 的 116 条，顺序与源码一致（按类别排）。 */
const CATALOG = [
  '抑郁症',
  '重度抑郁障碍',
  '持续性抑郁障碍',
  '双相I型障碍',
  '双相II型障碍',
  '环性心境障碍',
  '破坏性心境失调障碍',
  '经前烦躁障碍',
  '产后抑郁',
  '季节性情绪失调',
  '广泛性焦虑障碍',
  '惊恐障碍',
  '广场恐惧症',
  '社交焦虑障碍',
  '特定恐惧症',
  '分离焦虑障碍',
  '选择性缄默症',
  '疾病焦虑障碍',
  '惊恐发作',
  '考试焦虑',
  '创伤后应激障碍',
  '复杂性创伤后应激障碍',
  '急性应激障碍',
  '适应障碍',
  '解离性遗忘',
  '人格解体/现实解体障碍',
  '解离性身份障碍',
  '替代性创伤',
  '创伤再体验发作',
  '背叛创伤',
  '强迫症',
  '躯体变形障碍',
  '囤积障碍',
  '拔毛癖',
  '皮肤抠抓障碍',
  '强迫性计数',
  '强迫性检查',
  '侵入性思维',
  '侵入性暴力画面',
  '自我恐惧',
  '神经性厌食症',
  '神经性贪食症',
  '暴食障碍',
  '回避性摄食障碍',
  '异食癖',
  '反刍障碍',
  '失眠障碍',
  '嗜睡障碍',
  '发作性睡病',
  '阻塞性睡眠呼吸暂停',
  '昼夜节律睡眠觉醒障碍',
  '梦魇障碍',
  '睡惊症',
  '睡行症',
  '不宁腿综合征',
  '精神分裂症',
  '分裂情感性障碍',
  '短暂精神病性障碍',
  '妄想障碍',
  '偏执型人格障碍',
  '幻听',
  '被害妄想',
  '科塔尔综合征',
  '卡普格拉斯综合征',
  '边缘型人格障碍',
  '自恋型人格障碍',
  '表演型人格障碍',
  '依赖型人格障碍',
  '回避型人格障碍',
  '强迫型人格障碍',
  '分裂样人格障碍',
  '反社会型人格障碍',
  '被动攻击型人格',
  '抑郁型人格',
  '注意缺陷多动障碍',
  '自闭谱系障碍',
  '抽动秽语综合征',
  '阅读障碍',
  '计算障碍',
  '发育性协调障碍',
  '轻度认知障碍',
  '阿尔茨海默病',
  '血管性痴呆',
  '额颞叶痴呆',
  '颞叶癫痫',
  '偏头痛',
  '脑震荡后综合征',
  '慢性疲劳综合征',
  '纤维肌痛',
  '功能性神经症状障碍',
  '躯体症状障碍',
  '转换障碍',
  '慢性疼痛障碍',
  '心因性非癫痫发作',
  '间歇性暴怒障碍',
  '病理性赌博',
  '游戏障碍',
  '网络成瘾',
  '酒精使用障碍',
  '物质使用障碍',
  '强迫性购物',
  '性冲动控制障碍',
  '高敏感体质',
  '安全感缺失',
  '共情过载',
  '情绪海绵',
  '声音敏感',
  '光敏感',
  '触觉敏感',
  '述情障碍',
  '联觉',
  '冒名顶替感',
  '过度共情',
  '被动自杀意念',
  '自伤冲动',
  '暴力冲动',
];

const NOTE =
  '这不是诊断，也不会把病名写进对话：只决定 TA 怎么感受、怎么应对。安慰与陪伴会提高稳定度；时间几乎不起作用。涉及自伤、自杀、伤人的条目有硬边界——可以写痛苦与冲动，不写方法、不美化，始终引向现实求助。';
const SECURITY_NOTE =
  '被安慰、被倾听会往上走；被忽视、被拒绝触碰、看到你对别人亲密、被辱骂会往下掉；独处过久缓慢下降，一直有人陪缓慢回升。每类事件各有冷却与每日上限，连刷不会连涨。';
const AI_NOTE =
  '只在「一串事件归纳成一段经历」和「连续同向漂移的复核」时调用，它给的数值一律经系统夹取。关掉就只剩系统规则。模型沿用大脑预判里配的小模型。';
const CATALOG_NOTE =
  '目录按类别分：情绪、焦虑、创伤、强迫、进食、睡眠、精神病性、人格、神经发育、神经、躯体、冲动、特质与安全类。轻度不等于没事，重度也不等于随时失控——严重度决定的是波动幅度与恢复速度。';

const subhead: CSSProperties = {
  margin: 0,
  fontSize: 'var(--w-size-subhead)',
  lineHeight: 'var(--w-lh-subhead)',
  fontWeight: 600,
  color: 'var(--w-ramp-1)',
};
const caption: CSSProperties = { margin: 0, fontSize: 'var(--w-size-caption)', color: 'var(--w-ramp-4)', lineHeight: 'var(--w-lh-caption)' };
const bodyText: CSSProperties = { margin: 0, fontSize: 'var(--w-size-body)', color: 'var(--w-ramp-2)', lineHeight: 'var(--w-lh-body)' };
const accent: CSSProperties = { color: 'var(--w-ramp-3)' };

const SEVERITIES = [
  { value: '轻度', label: '轻度' },
  { value: '中度', label: '中度' },
  { value: '重度', label: '重度' },
];

export function MentalHealth({ bridge, back }: ScreenProps) {
  const [securityAi, setSecurityAi] = useState(true);
  // 安全感是角色身上的一个数值（CharacterState.Security，0–1000，与好感同刻度）。
  // 桥已经把这一格送到 characters.list 上，所以这里读真数；还没进存档时按源码空态写。
  const [security, setSecurity] = useState<{ who: string; value: number } | null>(null);

  useEffect(() => {
    let alive = true;
    void bridge
      .request('characters.list', {})
      .then(({ characters }) => {
        if (!alive) return;
        const who = characters.find((c) => typeof c.security === 'number');
        if (!who) return;
        setSecurity({ who: who.nickname || who.name, value: Math.round(((who.security ?? 0) / 1000) * 100) });
      })
      .catch(() => undefined);
    return () => {
      alive = false;
    };
  }, [bridge]);
  const [condition, setCondition] = useState('');
  const [severity, setSeverity] = useState('轻度');
  const [note, setNote] = useState('');

  return (
    <PageShell>
      {/* 页名只出一处：谁的状况放副标题，版面上只剩一个重心。 */}
      <PageHeader
        title="心理状况"
        subtitle={security ? `${security.who} · 心理状况` : '还没有进入存档 · 心理状况'}
        backText="< 返回"
        onBack={back}
      />

      <main className="screen">
        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-step-3)', width: '100%' }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-step-1)' }}>
            <p style={caption}>{NOTE}</p>
            <p style={{ ...caption, ...accent, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
              进入一局游戏后，才能给当前角色设置心理状况。
            </p>
          </div>

          <Card>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
              <span style={subhead}>安全感</span>
              <span style={{ ...caption, ...accent }}>
                {security ? `${security.who} · 安全感 ${security.value} / 100` : '还没有进入存档 · 安全感'}
              </span>
            </div>
            <p style={caption}>{SECURITY_NOTE}</p>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr auto', gap: 'var(--w-space-2)', alignItems: 'center' }}>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                <span style={bodyText}>小模型整理</span>
                <span style={caption}>{AI_NOTE}</span>
              </div>
              <GlassSwitch checked={securityAi} onChange={setSecurityAi} label="小模型整理" />
            </div>
          </Card>

          <Card>
            <p style={subhead}>添加一条</p>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr auto', gap: 'var(--w-space-2)' }}>
              <Select
                value={condition}
                label="选择心理状况（116 条）"
                onChange={setCondition}
                options={[{ value: '', label: '选择心理状况（116 条）' }, ...CATALOG.map((name) => ({ value: name, label: name }))]}
              />
              <Select value={severity} label="严重度" onChange={setSeverity} options={SEVERITIES} />
            </div>
            <Field
              value={note}
              onChange={setNote}
              placeholder="备注：触发点 / 来源 / 想被怎么对待（可留空）"
              label="备注"
            />
            <div>
              <Inert>
                <SignalButton>加到当前角色</SignalButton>
              </Inert>
            </div>
            <p style={caption}>{CATALOG_NOTE}</p>
          </Card>
        </div>
      </main>
    </PageShell>
  );
}
