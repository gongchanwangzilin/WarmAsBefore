import { useEffect, useState } from 'react';
import type { CharacterRow } from '../../bridge/protocol';
import { Glass } from '../../components/Glass';
import { GlassButton } from '../../components/GlassButton';
import { PageShell } from '../../components/PageShell';
import { PageHeader } from '../../components/kit';
import { useEscape } from '../../lib/keys';
import type { ScreenProps } from '../types';
import './viewer.css';

/**
 * 角色观察页：整屏底色 + 立绘按屏幕高度铺满，左右切服装/表情，底部一条读数。
 *
 * 角色库右边那块预览是「缩小的适应框」——看得到人在那儿，看不清细节。
 * 这一页就是给「想仔细看 TA」的那一眼：图占满屏高，切一帧是一帧，
 * 底下只有四条读数（好感 / 信任 / 精力 / 安全感），其余都收起来。
 *
 * 数据：`characters.get` 给立绘清单（outfit × emotion），`characters.list` 补安全感 ——
 * `characters.get` 里没有这一项，读不到就写「—」，不拿好感去顶。
 */

type Frame = { outfit: string; emotion: string; url: string };

type Detail = {
  id: string;
  name: string;
  nickname: string;
  outfit: string;
  emotion: string;
  affection: number;
  trust: number;
  energy: number;
  emotions: Frame[];
};

/** Helpers.AffectionScale：每 100 点一级，共 10 级，0 点也算第 1 级。 */
const levelOf = (points: number) => (points <= 0 ? 1 : Math.min(10, Math.floor((points - 1) / 100) + 1));

export function CharacterView({ bridge, query, navigate, back }: ScreenProps) {
  const id = (query.get('id') ?? '').trim();
  const [detail, setDetail] = useState<Detail | null>(null);
  const [security, setSecurity] = useState<number | null>(null);
  const [error, setError] = useState('');
  const [at, setAt] = useState(0);

  useEscape(true, back);

  useEffect(() => {
    if (!id) {
      setError('这一页要知道看谁：从角色库点「仔细看」进来。');
      return;
    }
    let dropped = false;
    void Promise.allSettled([bridge.request('characters.get', { id }), bridge.request('characters.list', {})]).then(
      ([one, all]) => {
        if (dropped) return;
        if (one.status === 'rejected') {
          setError(one.reason instanceof Error ? one.reason.message : '这个角色没有读出来。');
          return;
        }
        const found = one.value;
        if (!found) {
          setError('这个角色不在了：可能刚被删掉。');
          return;
        }
        setDetail({
          id: found.id,
          name: found.nickname || found.name,
          nickname: found.nickname,
          outfit: found.outfit,
          emotion: found.emotion,
          affection: found.affection,
          trust: found.trust,
          energy: found.energy,
          emotions: found.emotions,
        });
        // 安全感只有名册那一行有；读不到就留 null，底部写「—」。
        if (all.status === 'fulfilled') {
          const row = all.value.characters.find((c: CharacterRow) => c.id === found.id);
          setSecurity(row?.security ?? null);
        }
        setAt(Math.max(0, found.emotions.findIndex((f) => f.outfit === found.outfit && f.emotion === found.emotion)));
      },
    );
    return () => {
      dropped = true;
    };
  }, [bridge, id]);

  const frames = detail?.emotions ?? [];
  const index = frames.length === 0 ? 0 : Math.min(at, frames.length - 1);
  const frame = frames[index] ?? null;

  /** 左右切一帧；到头绕回另一头，键盘和按钮走同一条。 */
  const step = (dir: -1 | 1) => {
    if (frames.length < 2) return;
    setAt((prev) => (prev + dir + frames.length) % frames.length);
  };

  useEffect(() => {
    if (frames.length < 2) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'ArrowLeft') step(-1);
      if (event.key === 'ArrowRight') step(1);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [frames.length]);

  return (
    <PageShell>
      <PageHeader
        eyebrow="// OBSERVE"
        title={detail ? detail.name : '仔细看'}
        subtitle={frame ? `${frame.outfit || '默认装扮'} · ${frame.emotion || '默认表情'}` : undefined}
        backText="←"
        onBack={back}
        right={
          <>
            {frames.length > 1 ? (
              <span className="cv__count" aria-live="polite">
                {index + 1} / {frames.length}
              </span>
            ) : null}
            <GlassButton size="sm" onClick={() => navigate(`/studio?id=${encodeURIComponent(id)}`)} disabled={!id}>
              进工作室
            </GlassButton>
          </>
        }
      />

      <section className="cv">
        {error ? (
          <div className="cv__note">
            <span className="cv__note-title">没看成</span>
            <span className="cv__note-body">{error}</span>
            <GlassButton size="sm" onClick={() => navigate('/roster')}>
              回角色库
            </GlassButton>
          </div>
        ) : detail === null ? (
          <div className="cv__note">
            <span className="cv__note-title">正在读立绘…</span>
          </div>
        ) : frame ? (
          <>
            <img className="cv__stage" src={frame.url} alt={`${detail.name} · ${frame.outfit} · ${frame.emotion}`} draggable={false} />
            {frames.length > 1 ? (
              <>
                <GlassButton size="md" className="cv__nav cv__nav--prev" onClick={() => step(-1)} ariaLabel="上一张">
                  ‹
                </GlassButton>
                <GlassButton size="md" className="cv__nav cv__nav--next" onClick={() => step(1)} ariaLabel="下一张">
                  ›
                </GlassButton>
              </>
            ) : null}
          </>
        ) : (
          <div className="cv__note">
            <span className="cv__note-title">这个角色还没有立绘</span>
            <span className="cv__note-body">进工作室生成一套，或从 zip 补进来。</span>
            <GlassButton size="sm" onClick={() => navigate(`/studio?id=${encodeURIComponent(id)}`)}>
              去工作室
            </GlassButton>
          </div>
        )}
      </section>

      {detail ? (
        <Glass as="footer" tone="bar" radius={0} className="cv__bar">
          <Reading label="好感" value={`Lv.${levelOf(detail.affection)}`} note={`${detail.affection}/1000`} />
          <Reading label="信任" value={String(detail.trust)} note="/100" />
          <Reading label="精力" value={String(detail.energy)} note="/100" />
          <Reading label="安全感" value={security === null ? '—' : String(security)} note={security === null ? '这一项没有读数' : '/1000'} />
        </Glass>
      ) : null}
    </PageShell>
  );
}

function Reading({ label, value, note }: { label: string; value: string; note: string }) {
  return (
    <div className="cv__read">
      <span className="cv__read-label">{label}</span>
      <span className="cv__read-value">
        {value}
        <small>{note}</small>
      </span>
    </div>
  );
}
