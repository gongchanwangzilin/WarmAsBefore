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
 * 用户在「背景」里选了纯色底时，地面改由那一颗颜色现算：
 * 主光取自它的提亮档、背光取自它的压暗档，三层结构与令牌版一致，
 * 所以玻璃、指针光、墨阶全都跟着这颗颜色走，而不是只换一张纸。
 */
export function groundLayers(tokens: TokenMap, choice?: GroundChoice): GroundLayer[] {
  if (choice && choice.mode === 'solid' && choice.solidHex.trim()) {
    try {
      return groundFrom(parse(choice.solidHex));
    } catch {
      /* 颜色解析失败就退回令牌地面 */
    }
  }
  return [
    {
      kind: 'radial',
      x: 0.8,
      y: -0.12,
      radius: 790,
      stops: [
        [0, tokens['--w-ground-warm']],
        [0.42, tokens['--w-ground-warm-mid']],
        [1, tokens['--w-ground-warm-end']],
      ],
    },
    {
      kind: 'radial',
      x: 0.02,
      y: 1.05,
      radius: 720,
      stops: [
        [0, tokens['--w-ground-cool']],
        [0.5, tokens['--w-ground-cool-mid']],
        [1, tokens['--w-ground-cool-end']],
      ],
    },
    {
      kind: 'linear',
      angle: 165,
      stops: [
        [0, tokens['--w-ground-a']],
        [0.44, tokens['--w-ground-b']],
        [1, tokens['--w-ground-c']],
      ],
    },
  ];
}

const WHITE: RGB = { r: 255, g: 255, b: 255 };
const BLACK: RGB = { r: 0, g: 0, b: 0 };

const alpha = (c: RGB, a: number) =>
  `rgba(${Math.round(c.r)}, ${Math.round(c.g)}, ${Math.round(c.b)}, ${a})`;

/** 一颗颜色 → 同一套三层地面：主光在上、背光在下、主色铺中间那道竖直明暗。 */
function groundFrom(base: RGB): GroundLayer[] {
  const key = mix(base, WHITE, 0.34);
  const rim = mix(base, WHITE, 0.1);
  const shade = mix(base, BLACK, 0.34);
  const deep = mix(base, BLACK, 0.56);

  return [
    {
      kind: 'radial',
      x: 0.82,
      y: -0.1,
      radius: 820,
      stops: [
        [0, alpha(key, 0.42)],
        [0.44, alpha(rim, 0.18)],
        [1, alpha(rim, 0)],
      ],
    },
    {
      kind: 'radial',
      x: 0.04,
      y: 1.04,
      radius: 760,
      stops: [
        [0, alpha(shade, 0.38)],
        [0.52, alpha(shade, 0.14)],
        [1, alpha(shade, 0)],
      ],
    },
    {
      kind: 'linear',
      angle: 165,
      stops: [
        [0, toHex(rim)],
        [0.5, toHex(base)],
        [1, toHex(deep)],
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
