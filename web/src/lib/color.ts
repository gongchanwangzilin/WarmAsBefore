export type RGB = { r: number; g: number; b: number };

export const clamp01 = (v: number) => Math.min(1, Math.max(0, v));

export function parse(hex: string): RGB {
  const body = hex.trim().replace(/^#/, '');
  const full =
    body.length === 3
      ? body
          .split('')
          .map((c) => c + c)
          .join('')
      : body;
  if (full.length !== 6) throw new Error(`unsupported color: ${hex}`);
  return {
    r: parseInt(full.slice(0, 2), 16),
    g: parseInt(full.slice(2, 4), 16),
    b: parseInt(full.slice(4, 6), 16),
  };
}

export function toHex(c: RGB): string {
  const h = (v: number) => Math.round(clamp01(v / 255) * 255).toString(16).padStart(2, '0');
  return `#${h(c.r)}${h(c.g)}${h(c.b)}`;
}

export function mix(a: RGB, b: RGB, k: number): RGB {
  return {
    r: a.r + (b.r - a.r) * k,
    g: a.g + (b.g - a.g) * k,
    b: a.b + (b.b - a.b) * k,
  };
}

export function srgbToLinear(v: number): number {
  return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
}

export function linearToSrgb(v: number): number {
  return v <= 0.0031308 ? v * 12.92 : 1.055 * Math.pow(v, 1 / 2.4) - 0.055;
}

export function luminance(c: RGB): number {
  return (
    0.2126 * srgbToLinear(c.r / 255) +
    0.7152 * srgbToLinear(c.g / 255) +
    0.0722 * srgbToLinear(c.b / 255)
  );
}

export function contrast(a: RGB, b: RGB): number {
  const la = luminance(a);
  const lb = luminance(b);
  const hi = Math.max(la, lb);
  const lo = Math.min(la, lb);
  return (hi + 0.05) / (lo + 0.05);
}

/**
 * SPEC §2.3: 方向先由背景定，再由二分把对比恰好推到目标值。
 * 起始色取中间调，所以每一档都停在各自的目标上，墨阶不会塌成同一层。
 */
export function lock(fg: RGB, ground: RGB, target: number): RGB {
  if (contrast(fg, ground) >= target) return fg;
  const toward: RGB = luminance(ground) > 0.5 ? { r: 0, g: 0, b: 0 } : { r: 255, g: 255, b: 255 };
  let lo = 0;
  let hi = 1;
  for (let i = 0; i < 24; i++) {
    const mid = (lo + hi) / 2;
    if (contrast(mix(fg, toward, mid), ground) >= target) hi = mid;
    else lo = mid;
  }
  return mix(fg, toward, hi);
}
