import type { BackgroundMode, TokenMap } from '../bridge/protocol';
import { linearToSrgb, mix, parse, srgbToLinear, toHex, type RGB } from './color';

export type GroundStop = [offset: number, color: string];

export type GroundLayer =
  | { kind: 'radial'; x: number; y: number; radius: number; stops: GroundStop[] }
  | { kind: 'linear'; angle: number; stops: GroundStop[] };

export type GroundChoice = {
  mode: BackgroundMode;
  solidHex: string;
  pageTintHex: string;
};

/**
 * 第 1 条：纯色底上玻璃等于塑料，页面底必须是一层有明暗的渐变。
 * 形状写在这里，颜色全部从令牌拿 —— 同一份定义同时喂 CSS（页面底）与 canvas（求平均色），
 * 所以换配色时墨阶不会和背景漂移。
 *
 * 只画「纸」：一条明度台阶（面 → 页 → 井）加一条同色系的斜向薄雾。
 * 不画径向光斑 —— 卡片后面一团来路不明的亮斑，读起来是发光特效，不是纸。
 * 层次交给细渐变的台阶数与 .ground::after 的暗角、.ground::before 的噪点。
 *
 * 用户在「背景」里选了纯色底时，地面改由那一颗颜色现算：
 * 同一条台阶从这颗颜色的提亮档走到压暗档，玻璃、墨阶全都跟着它走，而不是只换一张纸。
 */
export function groundLayers(tokens: TokenMap, choice?: GroundChoice): GroundLayer[] {
  if (choice && choice.mode === 'solid' && choice.solidHex.trim()) {
    try {
      return groundFrom(parse(choice.solidHex));
    } catch {
      /* 颜色解析失败就退回令牌地面 */
    }
  }
  return groundFromTokens(tokens);
}

const WHITE: RGB = { r: 255, g: 255, b: 255 };
const BLACK: RGB = { r: 0, g: 0, b: 0 };

const alpha = (c: RGB, a: number) =>
  `rgba(${Math.round(c.r)}, ${Math.round(c.g)}, ${Math.round(c.b)}, ${a})`;

/**
 * 令牌地面：面 / 页 / 井三档本身就是一个明度台阶（都由同一颗地色推出来），
 * 这里只把台阶铺开，再叠一层极淡的同色斜雾给竖直方向一点错位。
 */
function groundFromTokens(tokens: TokenMap): GroundLayer[] {
  const a = parse(tokens['--w-ground-a'] || '#fdf8f0');
  const b = parse(tokens['--w-ground-b'] || '#f6efe4');
  const c = parse(tokens['--w-ground-c'] || '#efe6d8');

  return [
    {
      kind: 'linear',
      angle: 166,
      stops: [
        [0, toHex(mix(a, b, 0.18))],
        [0.22, toHex(a)],
        [0.48, toHex(b)],
        [0.78, toHex(mix(b, c, 0.62))],
        [1, toHex(c)],
      ],
    },
    {
      kind: 'linear',
      angle: 208,
      stops: [
        [0, alpha(mix(c, b, 0.5), 0.5)],
        [0.46, alpha(b, 0.16)],
        [1, alpha(a, 0)],
      ],
    },
  ];
}

/** 一颗颜色 → 同一条台阶：提亮档在上、本色居中、压暗档在底。 */
function groundFrom(base: RGB): GroundLayer[] {
  const rim = mix(base, WHITE, 0.1);
  const deep = mix(base, BLACK, 0.3);
  const well = mix(base, BLACK, 0.46);

  return [
    {
      kind: 'linear',
      angle: 166,
      stops: [
        [0, toHex(rim)],
        [0.26, toHex(base)],
        [0.72, toHex(mix(base, deep, 0.7))],
        [1, toHex(well)],
      ],
    },
    {
      kind: 'linear',
      angle: 208,
      stops: [
        [0, alpha(mix(deep, base, 0.5), 0.34)],
        [1, alpha(rim, 0)],
      ],
    },
  ];
}

const stops = (list: GroundStop[], scale = 100) =>
  list.map(([at, color]) => `${color} ${(at * scale).toFixed(1)}%`).join(', ');

export function groundCss(layers: GroundLayer[]): string {
  return layers
    .map((layer) =>
      layer.kind === 'radial'
        ? `radial-gradient(${layer.radius}px at ${(layer.x * 100).toFixed(1)}% ${(layer.y * 100).toFixed(1)}%, ${stops(layer.stops)})`
        : `linear-gradient(${layer.angle}deg, ${stops(layer.stops)})`,
    )
    .join(',\n    ');
}

/**
 * 同一份渐变画到 128×80 的 canvas 上逐像素平均（线性光里平均），
 * 得到「地面渐变平均色」，供墨阶推导使用。
 */
export function averageGround(layers: GroundLayer[]): RGB {
  const W = 128;
  const H = 80;
  const K = W / 1280;
  const canvas = document.createElement('canvas');
  canvas.width = W;
  canvas.height = H;
  const ctx = canvas.getContext('2d', { willReadFrequently: true });
  if (!ctx) throw new Error('2d context unavailable');

  for (const layer of layers) {
    let gradient: CanvasGradient;
    if (layer.kind === 'radial') {
      const cx = layer.x * W;
      const cy = layer.y * H;
      gradient = ctx.createRadialGradient(cx, cy, 0, cx, cy, layer.radius * K);
    } else {
      const rad = (layer.angle * Math.PI) / 180;
      const len = Math.abs(W * Math.sin(rad)) + Math.abs(H * Math.cos(rad));
      const dx = (Math.sin(rad) * len) / 2;
      const dy = (-Math.cos(rad) * len) / 2;
      gradient = ctx.createLinearGradient(W / 2 - dx, H / 2 - dy, W / 2 + dx, H / 2 + dy);
    }
    for (const [at, color] of layer.stops) gradient.addColorStop(at, color);
    ctx.fillStyle = gradient;
    ctx.fillRect(0, 0, W, H);
  }

  const data = ctx.getImageData(0, 0, W, H).data;
  let r = 0;
  let g = 0;
  let b = 0;
  const n = W * H;
  for (let i = 0; i < data.length; i += 4) {
    r += srgbToLinear(data[i] / 255);
    g += srgbToLinear(data[i + 1] / 255);
    b += srgbToLinear(data[i + 2] / 255);
  }
  return {
    r: linearToSrgb(r / n) * 255,
    g: linearToSrgb(g / n) * 255,
    b: linearToSrgb(b / n) * 255,
  };
}
