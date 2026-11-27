import { useState } from 'react';
import { Glass } from '../../components/Glass';
import { GlassButton } from '../../components/GlassButton';
import { PageShell } from '../../components/PageShell';
import { Empty, PageHeader } from '../../components/kit';
import type { ScreenProps } from '../types';
import { Cap, InlineField, Section, ToolRow, Well } from './parts';

/**
 * 商店「美了么」：Views/Games/ShopPage.xaml（304 行）+ 同名 code-behind。
 * 页头（身体状态 + 钱包）· 战绩条 · AI 选品 · GitHub 仓库同步 · 搜索 · 分类 · 商品清单。
 * 商品取 ShopService.BuildSeedCatalog 的种子货架：名字、说明、价格、是否场景券都逐条照抄。
 */

type Item = { cat: string; name: string; desc: string; price: number; ticket?: boolean };

const SEED: Item[] = [
  { cat: '饮品', name: '招牌珍珠奶茶', desc: '经典黑糖珍珠，暖心暖胃', price: 16 },
  { cat: '饮品', name: '杨枝甘露', desc: '芒果西柚，夏日清爽', price: 22 },
  { cat: '饮品', name: '生椰拿铁', desc: '椰香浓郁，咖啡醇厚', price: 24 },
  { cat: '饮品', name: '蜜桃乌龙', desc: '蜜桃香气配乌龙茶底', price: 18 },
  { cat: '甜品', name: '草莓蛋糕', desc: '草莓口味的蛋糕，甜度刚好', price: 28 },
  { cat: '甜品', name: '提拉米苏', desc: '咖啡酒香，入口即化', price: 26 },
  { cat: '甜品', name: '抹茶冰淇淋', desc: '微苦回甘，细腻顺滑', price: 15 },
  { cat: '甜品', name: '马卡龙礼盒', desc: '法式少女心，六色六味', price: 32 },
  { cat: '餐饮', name: '红烧肉套餐', desc: '肥而不腻，下饭神器', price: 38 },
  { cat: '餐饮', name: '麻辣小龙虾', desc: '夏夜大排档标配', price: 68 },
  { cat: '餐饮', name: '番茄炒蛋单人份', desc: '家常味，暖暖的', price: 18 },
  { cat: '方便食品', name: '螺蛳粉', desc: '酸辣鲜香，越吃越上瘾', price: 14 },
  { cat: '方便食品', name: '自热小火锅', desc: '加班深夜救星', price: 22 },
  { cat: '药物', name: '感冒灵冲剂', desc: '换季感冒，一包见效', price: 28 },
  { cat: '药物', name: '创可贴一盒', desc: '小伤口必备', price: 8 },
  { cat: '药物', name: '维生素C泡腾片', desc: '增强抵抗力', price: 25 },
  { cat: '情趣用品', name: '香薰蜡烛', desc: '氛围感拉满', price: 39 },
  { cat: '情趣用品', name: '真丝眼罩', desc: '助眠神器', price: 29 },
  { cat: '礼品', name: '玫瑰花束', desc: '11朵红玫瑰，表白必备', price: 99 },
  { cat: '礼品', name: '毛绒玩具熊', desc: '抱抱更温暖', price: 45 },
  { cat: '礼品', name: '星空投影灯', desc: '满屋星河', price: 76 },
  { cat: '玩具', name: '乐高积木', desc: '拼出想象力', price: 128 },
  { cat: '玩具', name: '盲盒', desc: '拆开有惊喜', price: 29 },
  { cat: '节日特殊商品', name: '生日蛋糕', desc: '专属生日蛋糕，可定制', price: 88 },
  { cat: '节日特殊商品', name: '年货大礼包', desc: '春节年味十足', price: 128 },
  {
    cat: '场景券',
    name: '游乐园门票',
    desc: '解锁【游乐园】场景，旋转木马、过山车，和 TA 尽情玩',
    price: 168,
    ticket: true,
  },
  { cat: '场景券', name: '电影院双人票', desc: '解锁【电影院】场景，抱着爆米花看晚场电影', price: 98, ticket: true },
  { cat: '场景券', name: '温泉度假券', desc: '解锁【温泉汤屋】场景，一起泡温泉看星星', price: 198, ticket: true },
];

/** 分类标签：源码按 OrdinalIgnoreCase 排序后把「全部」插在最前。 */
const CATEGORIES = ['全部', ...Array.from(new Set(SEED.map((item) => item.cat))).sort((a, b) => (a < b ? -1 : a > b ? 1 : 0))];

const priceText = (price: number) => (price < 0 ? '价格异常' : `${price.toLocaleString('en-US')} 币`);

export function Shop({ back }: ScreenProps) {
  const [query, setQuery] = useState('');
  const [category, setCategory] = useState('全部');
  const [aiPrompt, setAiPrompt] = useState('');
  const [repo, setRepo] = useState('');
  const [bodyOpen, setBodyOpen] = useState(false);

  const items = SEED.filter((item) => category === '全部' || item.cat === category).filter((item) =>
    query.trim().length === 0 ? true : item.name.includes(query.trim()) || item.desc.includes(query.trim()),
  );

  return (
    <PageShell>
      <PageHeader
        title="美了么"
        onBack={back}
        right={
          <>
            <button type="button" className="k-textbtn" style={{ fontSize: 'var(--w-size-caption)' }} onClick={() => setBodyOpen(true)}>
              身体状态
            </button>
            <span
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                height: 24,
                padding: '0 10px',
                borderRadius: 99,
                background: 'var(--w-surface-tint)',
                fontSize: 'var(--w-size-body)',
                color: 'var(--w-ink-body)',
                whiteSpace: 'nowrap',
              }}
            >
              亲密币 0
            </span>
          </>
        }
      />

      {/* 战绩条 + buff 标记条 */}
      <div style={{ background: 'var(--w-tint-bar)' }}>
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: 'var(--w-space-3)',
            padding: 'var(--w-space-2) var(--w-pad-page)',
          }}
        >
          <Cap tone="body" style={{ whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
            小游戏战绩：0 胜 0 负
          </Cap>
          <Cap style={{ whiteSpace: 'nowrap' }}>胜局 +30 亲密币 · 败局 +10</Cap>
        </div>
      </div>

      <div className="screen" style={{ gap: 'var(--w-space-3)' }}>
        {/* AI 选品 */}
        <Glass tone="card" radius={12}>
          <div style={{ display: 'flex', flexDirection: 'column', padding: 'var(--w-space-2)' }}>
            <div style={{ padding: '10px 0', borderBottom: '1px solid var(--w-hairline)' }}>
              <span style={{ fontSize: 'var(--w-size-body)', color: 'var(--w-ink-body)' }}>
                AI 选品 · 想要啥说啥，实时上新
              </span>
            </div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--w-space-1)', padding: '10px 0' }}>
              <InlineField
                value={aiPrompt}
                onChange={setAiPrompt}
                placeholder="例：TA 喜欢的草莓味饮品、生日蛋糕…"
                fontSize="var(--w-size-caption)"
                style={{ flex: 1 }}
              />
              <GlassButton size="sm">生成</GlassButton>
            </div>
          </div>
        </Glass>

        {/* GitHub 仓库同步 */}
        <Glass tone="card" radius={12}>
          <div style={{ display: 'flex', flexDirection: 'column', padding: 'var(--w-space-2)' }}>
            <div style={{ padding: '10px 0', borderBottom: '1px solid var(--w-hairline)' }}>
              <span style={{ fontSize: 'var(--w-size-body)', color: 'var(--w-ink-body)' }}>
                GitHub 仓库同步 · 仓库内 shop/catalog.json 会自动上架
              </span>
            </div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--w-space-1)', padding: '10px 0' }}>
              <InlineField
                value={repo}
                onChange={setRepo}
                placeholder="owner/repo，如 my-username/my-shop"
                fontSize="var(--w-size-caption)"
                style={{ flex: 1 }}
              />
              <GlassButton size="sm">同步</GlassButton>
            </div>
          </div>
        </Glass>

        {/* 搜索 */}
        <Glass tone="card" radius={12}>
          <div style={{ padding: '10px var(--w-space-2)' }}>
            <InlineField value={query} onChange={setQuery} placeholder="搜索商品…" />
          </div>
        </Glass>

        {/* 分类横滑 */}
        <ToolRow>
          {CATEGORIES.map((name) => (
            <Well
              key={name}
              active={category === name}
              onClick={() => setCategory(name)}
              style={{ padding: '4px 12px', borderRadius: 99, border: '1px solid var(--w-hairline)' }}
            >
              {name}
            </Well>
          ))}
        </ToolRow>

        {/* 商品清单 */}
        <Glass tone="card" radius={12}>
          <div style={{ padding: '0 var(--w-space-2)' }}>
            {items.length === 0 ? (
              <Empty
                title="货架上是空的"
                reason="搜索词或分类把商品全过滤掉了，也可能是还没同步过仓库"
                hint="清空搜索、点「全部」，或者用上面的 AI 选品现生成一件"
              />
            ) : (
              items.map((item) => (
                <div key={`${item.cat}-${item.name}`} style={{ display: 'flex', flexDirection: 'column' }}>
                  <div
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      gap: 'var(--w-space-3)',
                      minHeight: 'var(--w-row-h)',
                      padding: '10px 0',
                    }}
                  >
                    <div
                      style={{
                        flex: '0 0 auto',
                        width: 50,
                        height: 50,
                        borderRadius: 'var(--w-radius-well)',
                        background: 'var(--w-surface-tint)',
                        border: '1px solid var(--w-hairline)',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        fontSize: 'var(--w-size-title)',
                        color: 'var(--w-ink-muted)',
                      }}
                    >
                      {item.name.slice(0, 1)}
                    </div>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 2, flex: 1, minWidth: 0 }}>
                      <span
                        style={{
                          fontSize: 'var(--w-size-body)',
                          color: 'var(--w-ink-body)',
                          whiteSpace: 'nowrap',
                          overflow: 'hidden',
                          textOverflow: 'ellipsis',
                        }}
                        title={item.name}
                      >
                        {item.name}
                      </span>
                      <Cap style={{ display: '-webkit-box', WebkitLineClamp: 2, WebkitBoxOrient: 'vertical', overflow: 'hidden' }}>
                        {item.desc}
                      </Cap>
                      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--w-space-1)' }}>
                        <Cap style={{ whiteSpace: 'nowrap' }}>{item.cat}</Cap>
                      </div>
                    </div>
                    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 'var(--w-space-1)' }}>
                      <span style={{ fontSize: 'var(--w-size-body)', color: 'var(--w-ink-display)' }}>{priceText(item.price)}</span>
                      <GlassButton size="sm">{item.ticket ? '解锁' : '购买'}</GlassButton>
                      <div style={{ display: 'flex', gap: 'var(--w-space-1)' }}>
                        <GlassButton size="sm">送礼</GlassButton>
                        <GlassButton size="sm">使用</GlassButton>
                      </div>
                    </div>
                  </div>
                  <div className="k-sep" role="presentation" />
                </div>
              ))
            )}
          </div>
        </Glass>
      </div>

      {/* 身体状态覆盖层 */}
      {bodyOpen ? (
        <div
          style={{
            position: 'fixed',
            inset: 0,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            background: 'color-mix(in oklab, var(--w-ink-display) 42%, transparent)',
            zIndex: 20,
          }}
        >
          <Glass tone="card" radius={12} style={{ width: 320, maxWidth: '92vw' }}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)', padding: 'var(--w-space-3)' }}>
              <span style={{ fontSize: 'var(--w-size-title)', fontWeight: 600, color: 'var(--w-ink-display)', textAlign: 'center' }}>
                TA 的身体状态
              </span>
              <div
                style={{
                  padding: 'var(--w-space-2)',
                  borderRadius: 'var(--w-radius-well)',
                  background: 'var(--w-surface-tint)',
                }}
              >
                <Section muted>还没有身体状态</Section>
                <Cap style={{ display: 'block', marginTop: 2 }}>
                  这一份由后端的日程与情绪算出来，眼下手边没有数据。等后台挂上来，这里会显示她此刻的身体状态。
                </Cap>
              </div>
              <GlassButton size="md" onClick={() => setBodyOpen(false)}>
                关闭
              </GlassButton>
            </div>
          </Glass>
        </div>
      ) : null}
    </PageShell>
  );
}
