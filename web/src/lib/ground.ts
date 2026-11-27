import type { TokenMap } from '../bridge/protocol';
import { linearToSrgb, srgbToLinear, type RGB } from './color';

export type GroundStop = [offset: number, color: string];

export type GroundLayer =
  | { kind: 'radial'; x: number; y: number; radius: number; stops: GroundStop[] }
  | { kind: 'linear'; angle: number; stops: GroundStop[] };

/**
 * 第 1 条：纯色底上玻璃等于塑料，页面底必须是一层有明暗的渐变。
 * 形状写在这里，颜色全部从令牌拿 —— 同一份定义同时喂 CSS（页面底）与 canvas（求平均色），
 * 所以换配色时墨阶不会和背景漂移。
 */
export function groundLayers(tokens: TokenMap): GroundLayer[] {
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
