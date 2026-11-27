import { contrast, luminance, mix, parse, toHex, type RGB } from './color';
import { averageGround } from './ground';

/** 卡面 tint（深档 8%~12% 的取样，暖灰），与 CSS 里卡片底色同源。 */
const SURFACE_TINT = parse('#2b2723');
const SIGNAL = parse('#fad542');
const SIGNAL_SEED = parse('#1a1406');
const WHITE: RGB = { r: 255, g: 255, b: 255 };

export type InkName = 'display' | 'body' | 'label' | 'muted' | 'faint';

/** SPEC §2.3 深档目标：显示 9.0 / 正文 7.6 / 标签 6.4 / 次要 5.0 / 装饰 3.1。 */
const TARGETS: Record<InkName, number> = {
  display: 9.0,
  body: 7.6,
  label: 6.4,
  muted: 5.0,
  faint: 3.1,
};

/** 正文与说明的硬下限（本次要求 ≥ 4.5:1），装饰 3:1。 */
const FLOOR: Record<InkName, number> = {
  display: 4.5,
  body: 4.5,
  label: 4.5,
  muted: 4.5,
  faint: 3.0,
};

export type Ink = {
  direction: 'dark' | 'light';
  face: string;
  ref: string;
  colors: Record<InkName, string>;
  onSignal: string;
  /** 相对 SPEC 合成面（地面平均色 → tint 8% → 受光 7%）的对比度 */
  onFace: Record<InkName, number>;
  /** 相对面上最亮处（含面渐变最亮端与材料柔光）的对比度 */
  worst: Record<InkName, number>;
  minWorst: number;
  targets: Record<InkName, number>;
};

export type InkInput = {
  /** 玻璃透明度滑杆 0..1 */
  alpha: number;
  /** 液态高度滑杆 0..1 */
  liquid: number;
};

/**
 * 墨阶是算出来的，不是选出来的。
 * 1. 合成面（SPEC §2）：地面渐变逐像素平均 → 卡面 tint 8% → 面上受光 7%。
 * 2. 受光上限：再叠上面渐变最亮端与材料柔光，得到整块玻璃最亮的那个角（实测约 24% 白）。
 * 3. 方向：合成面相对亮度 > 0.5 走深墨，否则走浅墨。
 * 4. 目标比先用「白/黑在该面上能拿到的天花板」等比缩放（SPEC §2：拿不到时先保层次），
 *    再逐档二分把对比恰好钉到目标值；正文与说明另有 4.5 的硬下限。
 */
export function computeInk({ alpha }: InkInput): Ink {
  const ground = averageGround();
  const faceK = 1.3 - alpha * 0.55; // 与 CSS 的 --face-k 同式
  const lift = 0.07 * (faceK / 0.8325); // 默认参数下正好 7%
  const tier = 0.95; // 卡片档

  const face = mix(mix(ground, SURFACE_TINT, 0.08), WHITE, lift);
  // 面渐变最亮端（0.26 × faceK × tier）+ 材料柔光（0.10 × tier）在最亮角上的合成，实测 0.24
  const cornerGlow = Math.min(0.5, (0.26 * faceK * tier + 0.1 * tier) * 0.8);
  const ref = mix(face, WHITE, cornerGlow);

  const direction = luminance(face) > 0.5 ? 'dark' : 'light';
  const extreme: RGB = direction === 'dark' ? { r: 0, g: 0, b: 0 } : WHITE;
  const ceiling = contrast(extreme, ref);
  const scale = Math.min(1, ceiling / TARGETS.display);

  // 等比缩放后再自下而上抬到各自的下限，保证 显示 > 正文 > 标签 > 次要 > 装饰 的次序不塌
  const resolved = {} as Record<InkName, number>;
  resolved.faint = Math.max(FLOOR.faint, Math.min(TARGETS.faint * scale, TARGETS.muted * scale - 0.3));
  resolved.muted = Math.max(FLOOR.muted, TARGETS.muted * scale, resolved.faint + 0.3);
  resolved.label = Math.max(FLOOR.label, TARGETS.label * scale, resolved.muted + 0.35);
  resolved.body = Math.max(FLOOR.body, TARGETS.body * scale, resolved.label + 0.4);
  resolved.display = Math.max(resolved.body + 0.6, TARGETS.display * scale);

  const seed = parse('#7a7670');
  const colors = {} as Record<InkName, string>;
  const onFace = {} as Record<InkName, number>;
  const worst = {} as Record<InkName, number>;
  const targets = {} as Record<InkName, number>;

  for (const name of Object.keys(TARGETS) as InkName[]) {
    const target = resolved[name];
    targets[name] = Math.round(target * 100) / 100;

    let lo = 0;
    let hi = 1;
    for (let i = 0; i < 24; i++) {
      const mid = (lo + hi) / 2;
      if (contrast(mix(seed, extreme, mid), ref) >= target) hi = mid;
      else lo = mid;
    }
    const ink = mix(seed, extreme, hi);
    colors[name] = toHex(ink);
    onFace[name] = Math.round(contrast(ink, face) * 100) / 100;
    worst[name] = Math.round(contrast(ink, ref) * 100) / 100;
  }

  // 信号面永远亮，墨的方向是手写的（SPEC §2.4：Lock 只兜底补对比、不判断方向）
  const onSignal =
    contrast(SIGNAL_SEED, SIGNAL) >= 4.6
      ? SIGNAL_SEED
      : contrast({ r: 0, g: 0, b: 0 }, SIGNAL) >= contrast(WHITE, SIGNAL)
        ? { r: 0, g: 0, b: 0 }
        : WHITE;

  return {
    direction,
    face: toHex(face),
    ref: toHex(ref),
    colors,
    onSignal: toHex(onSignal),
    onFace,
    worst,
    minWorst: Math.min(...Object.values(worst)),
    targets,
  };
}
