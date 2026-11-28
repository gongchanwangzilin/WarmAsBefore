import type { WeatherKind } from '../bridge/protocol';
import { luminance, parse } from './color';

/**
 * 天气层：只有雨、雪、尘、雾四种「天上的东西」，没有光。
 *
 * 这一层画在页面底之上、卡片之下，所以它透进玻璃时已经被磨砂糊过一遍：
 * 它要负责的是「窗外真的在下雨」，不是「哪里亮了一下」。
 *
 * 契约：天气类型与强度只从宿主来（Appearance.scene.weatherKind / weatherIntensity，
 * 对应 C# 的 WeatherSceneKind 与 BackgroundCatalog 的场景强度）。这里不新增任何一档。
 *
 * 预算（写死，不给用户调）：
 *   · 粒子数按窗口面积缩放，每一档都有硬上限（见 PLAN）；
 *   · DPR 上限 1.25 —— 再多看不出差别，像素填充翻倍；
 *   · document.hidden 时停帧，指针与尺寸变化才重新要帧；
 *   · 粒子颜色只有一个中性：暗场用白，亮场用墨，不引入任何色相；
 *   · 减少动效时整层不画。
 */

type Drop = { x: number; y: number; z: number; len: number; spd: number; bucket: number };
type Flake = { x: number; y: number; z: number; r: number; spd: number; phase: number; drift: number; tier: number };
type Mote = { x: number; y: number; z: number; vx: number; vy: number; r: number };
type Haze = { x: number; y: number; r: number; depth: number; vx: number; vy: number };

export type Atmosphere = {
  dispose(): void;
  setEnabled(on: boolean): void;
  setWeather(kind: WeatherKind, intensity: number): void;
};

const DPR_CAP = 1.25;

/** 每档的粒子密度（粒 / 万像素）与硬上限、几何、不透明度。 */
type Plan = {
  /** 近景雨：粒 / 万px²，上限，长度 px 区间，透明度 */
  rain?: { density: number; cap: number; len: [number, number]; alpha: number; wind: number; dim: number; far: number };
  /** 雪：粒 / 万px²，上限 */
  snow?: { density: number; cap: number };
  /** 尘：粒 / 万px²，上限 */
  dust?: { density: number; cap: number; alpha: number };
  /** 雾团数 */
  haze?: number;
};

const PLAN: Record<WeatherKind, Plan> = {
  Sunny: { dust: { density: 0.22, cap: 56, alpha: 0.5 } },
  Cloudy: { dust: { density: 0.13, cap: 36, alpha: 0.34 } },
  Drizzle: { rain: { density: 0.55, cap: 88, len: [9, 22], alpha: 0.3, wind: 0.06, dim: 0, far: 0.4 } },
  LightRain: { rain: { density: 1.15, cap: 170, len: [16, 46], alpha: 0.4, wind: 0.09, dim: 0, far: 0.5 } },
  HeavyRain: { rain: { density: 2.3, cap: 300, len: [26, 78], alpha: 0.48, wind: 0.12, dim: 0.05, far: 0.62 } },
  Storm: { rain: { density: 3.1, cap: 320, len: [30, 92], alpha: 0.52, wind: 0.17, dim: 0.075, far: 0.7 } },
  Snow: { snow: { density: 1.3, cap: 240 } },
  Fog: { haze: 6 },
};

// 面积缩放 + 强度调制，最后再夹上限：上限是硬上限，不能被强度乘回去。
const count = (area: number, density: number, cap: number, k: number) =>
  Math.min(cap, Math.round((area / 10000) * density * k));

export function startAtmosphere(
  canvas: HTMLCanvasElement,
  enabled: boolean,
  reduceMotion: boolean,
  kind: WeatherKind = 'Sunny',
  intensity = 0.35,
): Atmosphere {
  const ctx = canvas.getContext('2d', { alpha: true });
  if (!ctx) return { dispose() {}, setEnabled() {}, setWeather() {} };

  let on = enabled && !reduceMotion;
  let raf = 0;
  let w = 0;
  let h = 0;
  let dpr = 1;
  let now = performance.now();
  let last = 0;
  // 只认 PLAN 里有的那几档：宿主如果还没跟上（旧版载荷），退回晴，不让画布空转报错。
  const known = (k: WeatherKind | string | undefined): WeatherKind =>
    k && PLAN[k as WeatherKind] ? (k as WeatherKind) : 'Sunny';

  let weather: WeatherKind = known(kind);
  let power = Math.min(1, Math.max(0, intensity));
  /** 粒子主色：暗场上白、亮场上墨。只有明暗，没有色相。 */
  let tone = '255, 255, 255';

  const drops: Drop[] = [];
  const fars: Drop[] = [];
  const flakes: Flake[] = [];
  const motes: Mote[] = [];
  const hazes: Haze[] = [];
  const pointer = { x: 0, y: 0 };
  let hazeSprite: HTMLCanvasElement | null = null;

  /** 地面亮度决定粒子是白是墨；令牌里那一颗「页」色就是地的本色。 */
  function readTone() {
    try {
      const raw = getComputedStyle(document.documentElement).getPropertyValue('--w-ground-b').trim();
      const lit = luminance(parse(raw));
      tone = lit > 0.42 ? '26, 28, 32' : '244, 246, 250';
    } catch {
      tone = '244, 246, 250';
    }
  }

  const rand = (a: number, b: number) => a + Math.random() * (b - a);
  const nearness = (m: Mote) => Math.max(0, 1 - Math.hypot(m.x - pointer.x, m.y - pointer.y) / 380);

  /** 雨的初值：长短不一，速度与长度成正比（长的落得快，才读得出纵深）。 */
  function seedDrop(far: boolean): Drop {
    const p = PLAN[weather].rain;
    const lens = p ? p.len : [14, 40];
    const z = far ? rand(0.25, 0.5) : rand(0.45, 1);
    const len = rand(lens[0], lens[1]) * (far ? 0.62 : 1);
    return {
      x: Math.random() * (w + 160) - 80,
      y: Math.random() * h,
      z,
      len,
      spd: (len * 16 + 150) * (far ? 0.6 : 1),
      bucket: 3,
    };
  }

  function seedFlake(): Flake {
    // 三档尺度：近大远小，速度与横向漂移都各自不同，相位随机 —— 免得满屏同节奏。
    const tier = Math.random();
    const z = tier < 0.34 ? rand(0.22, 0.48) : tier < 0.72 ? rand(0.5, 0.78) : rand(0.8, 1);
    return {
      x: Math.random() * w,
      y: Math.random() * h,
      z,
      r: 0.8 + z * 2.4,
      spd: 18 + z * 62,
      phase: Math.random() * Math.PI * 2,
      drift: 0.55 + z * 0.9,
      tier: z < 0.48 ? 0 : z < 0.72 ? 1 : 2,
    };
  }

  function seedMote(): Mote {
    return {
      x: Math.random() * w,
      y: Math.random() * h,
      z: rand(0.3, 1),
      vx: rand(-5, 5),
      vy: rand(-7, -1.5),
      r: 0.5 + Math.random() * 1.3,
    };
  }

  function seedHaze(): Haze {
    const depth = rand(0.35, 1);
    return {
      x: Math.random() * w,
      y: Math.random() * h,
      r: (170 + Math.random() * 250) * depth,
      depth,
      vx: rand(-3.4, 3.4) * depth,
      vy: rand(-1.6, 1.6) * depth,
    };
  }

  function build() {
    const p = PLAN[weather] ?? PLAN.Sunny;
    const area = w * h;
    // 强度只做轻微调制：场景强度本来就是「这一档有多重」，不需要再翻一倍。
    const k = 0.7 + power * 0.5;

    drops.length = 0;
    fars.length = 0;
    flakes.length = 0;
    motes.length = 0;
    hazes.length = 0;

    if (p.rain) {
      const n = count(area, p.rain.density, p.rain.cap, k);
      for (let i = 0; i < n; i++) drops.push(seedDrop(false));
      const fn = Math.round(n * p.rain.far);
      for (let i = 0; i < fn; i++) fars.push(seedDrop(true));
    }
    if (p.snow) {
      const n = count(area, p.snow.density, p.snow.cap, k);
      for (let i = 0; i < n; i++) flakes.push(seedFlake());
    }
    if (p.dust) {
      const n = count(area, p.dust.density, p.dust.cap, k);
      for (let i = 0; i < n; i++) motes.push(seedMote());
    }
    if (p.haze) for (let i = 0; i < p.haze; i++) hazes.push(seedHaze());
  }

  function resize() {
    dpr = Math.min(window.devicePixelRatio || 1, DPR_CAP);
    w = canvas.clientWidth;
    h = canvas.clientHeight;
    canvas.width = Math.max(1, Math.round(w * dpr));
    canvas.height = Math.max(1, Math.round(h * dpr));
    ctx!.setTransform(dpr, 0, 0, dpr, 0, 0);
    readTone();
    build();
  }

  /** 雾团烘焙成一张 sprite：径向一次，之后只 drawImage，不再每帧建渐变。 */
  function bakeHaze() {
    const size = 128;
    const off = document.createElement('canvas');
    off.width = size;
    off.height = size;
    const c = off.getContext('2d')!;
    const g = c.createRadialGradient(size / 2, size / 2, 0, size / 2, size / 2, size / 2);
    g.addColorStop(0, `rgba(${tone}, 0.055)`);
    g.addColorStop(0.55, `rgba(${tone}, 0.026)`);
    g.addColorStop(1, `rgba(${tone}, 0)`);
    c.fillStyle = g;
    c.fillRect(0, 0, size, size);
    return off;
  }

  function drawRain(dt: number) {
    const p = PLAN[weather].rain!;
    // 风：一条慢的摆，雨的斜度跟着它左右动（不是整层横移）。
    const sway = Math.sin(now / 3600) * 0.5 + Math.sin(now / 910) * 0.2;
    const slant = p.wind * (1 + sway);
    const windPx = slant * 620;

    // 先走一步（一帧只积一次），顺手把「离地多远」记成桶号。
    const advance = (list: Drop[], far: boolean) => {
      for (const d of list) {
        d.y += d.spd * dt;
        d.x += windPx * d.z * dt;
        if (d.y > h + d.len) {
          const fresh = seedDrop(far);
          d.x = fresh.x;
          d.y = -d.len - Math.random() * 40;
          d.z = fresh.z;
          d.len = fresh.len;
          d.spd = fresh.spd;
        }
        if (d.x < -140) d.x = w + 120;
        if (d.x > w + 140) d.x = -120;
        const fade = Math.min(1, (h - d.y) / (h * 0.22));
        d.bucket = fade <= 0.05 ? -1 : Math.min(3, Math.floor(fade * 4));
      }
    };

    // 按桶批成 path：一桶一次 stroke，落地前那几档自然变淡。
    const stroke = (list: Drop[], alpha: number, width: number) => {
      for (let bucket = 0; bucket < 4; bucket++) {
        ctx!.beginPath();
        let any = false;
        for (const d of list) {
          if (d.bucket !== bucket) continue;
          any = true;
          const dx = slant * d.len * 1.6 * d.z;
          ctx!.moveTo(d.x, d.y);
          ctx!.lineTo(d.x + dx, d.y + d.len);
        }
        if (!any) continue;
        ctx!.strokeStyle = `rgba(${tone}, ${(alpha * (0.2 + bucket * 0.27)).toFixed(3)})`;
        ctx!.lineWidth = width;
        ctx!.stroke();
      }
    };

    advance(fars, true);
    advance(drops, false);

    // 远景雨幕：更细、更淡、更慢 —— 大雨以上才有，给雨一点纵深。
    if (fars.length) stroke(fars, p.alpha * 0.34, 0.8);
    stroke(drops, p.alpha, 1.15);
  }

  function drawSnow(dt: number) {
    // 一帧只积一次位置：三档各自的速度、横向漂移与相位都在这里落地。
    for (const f of flakes) {
      f.y += f.spd * dt;
      // 横向漂移各自一个相位：每片雪绕自己的落点画弧，不是整屏一起摆。
      f.x += Math.sin(now / 1400 + f.phase) * f.drift * f.z * 14 * dt;
      if (f.y > h + 6) {
        const fresh = seedFlake();
        f.x = fresh.x;
        f.y = -6 - Math.random() * 30;
        f.z = fresh.z;
        f.r = fresh.r;
        f.spd = fresh.spd;
        f.phase = fresh.phase;
        f.drift = fresh.drift;
        f.tier = fresh.tier;
      }
      if (f.x < -10) f.x = w + 8;
      if (f.x > w + 10) f.x = -8;
    }

    // 三档各一条 path：同档透明度相同，一块 fill 就够。
    for (let tier = 0; tier < 3; tier++) {
      ctx!.beginPath();
      let any = false;
      for (const f of flakes) {
        if (f.tier !== tier) continue;
        if (Math.min(1, (h - f.y) / (h * 0.18)) <= 0.05) continue;
        any = true;
        ctx!.moveTo(f.x + f.r, f.y);
        ctx!.arc(f.x, f.y, f.r, 0, Math.PI * 2);
      }
      if (!any) continue;
      ctx!.fillStyle = `rgba(${tone}, ${0.16 + tier * 0.16})`;
      ctx!.fill();
    }
  }

  function drawDust(dt: number) {
    const p = PLAN[weather].dust;
    if (!p) return;
    ctx!.fillStyle = `rgba(${tone}, ${(p.alpha * 0.14).toFixed(3)})`;
    ctx!.beginPath();
    for (const m of motes) {
      m.x += m.vx * dt;
      m.y += m.vy * dt;
      if (m.y < -6) {
        m.y = h + 6;
        m.x = Math.random() * w;
      }
      if (m.x < -6) m.x = w + 6;
      if (m.x > w + 6) m.x = -6;
      const near = nearness(m);
      const r = m.r * m.z + near * 0.8;
      ctx!.moveTo(m.x + r, m.y);
      ctx!.arc(m.x, m.y, r, 0, Math.PI * 2);
    }
    ctx!.fill();

    // 靠近指针的那几粒另起一笔：稍亮一点，读起来是空气里的浮尘，不是光斑。
    let lit = false;
    ctx!.beginPath();
    for (const m of motes) {
      if (nearness(m) < 0.35) continue;
      lit = true;
      const r = m.r * m.z + nearness(m) * 0.8;
      ctx!.moveTo(m.x + r, m.y);
      ctx!.arc(m.x, m.y, r, 0, Math.PI * 2);
    }
    if (lit) {
      ctx!.fillStyle = `rgba(${tone}, ${(p.alpha * 0.3).toFixed(3)})`;
      ctx!.fill();
    }
  }

  function drawHaze(dt: number) {
    if (!hazeSprite) hazeSprite = bakeHaze();
    for (const b of hazes) {
      b.x += b.vx * dt;
      b.y += b.vy * dt;
      if (b.x < -b.r) b.x = w + b.r;
      if (b.x > w + b.r) b.x = -b.r;
      if (b.y < -b.r) b.y = h + b.r;
      if (b.y > h + b.r) b.y = -b.r;
      ctx!.globalAlpha = 0.55 + b.depth * 0.45;
      ctx!.drawImage(hazeSprite, b.x - b.r, b.y - b.r, b.r * 2, b.r * 2);
    }
    ctx!.globalAlpha = 1;
  }

  function frame(stamp: number) {
    raf = 0;
    if (!on || document.hidden) return;
    now = stamp;
    // dt 夹住：切回来、掉帧时不至于让粒子瞬移。
    const dt = Math.min(0.05, last ? (stamp - last) / 1000 : 0.016);
    last = stamp;

    ctx!.clearRect(0, 0, w, h);
    ctx!.lineCap = 'butt';

    if (PLAN[weather].haze) drawHaze(dt);
    if (PLAN[weather].rain) drawRain(dt);
    if (PLAN[weather].snow) drawSnow(dt);
    if (PLAN[weather].dust) drawDust(dt);

    // 密雨整体压暗一档：只在 canvas 里叠一层中性暗雾，玻璃后面那一层就跟着沉下去。
    const dim = PLAN[weather].rain?.dim ?? 0;
    if (dim > 0) {
      ctx!.fillStyle = `rgba(0, 0, 0, ${(dim * (0.7 + power * 0.5)).toFixed(3)})`;
      ctx!.fillRect(0, 0, w, h);
    }

    raf = requestAnimationFrame(frame);
  }

  function kick() {
    if (on && !raf && !document.hidden) {
      last = 0;
      raf = requestAnimationFrame(frame);
    }
  }

  resize();

  const onPointer = (event: PointerEvent) => {
    pointer.x = event.clientX;
    pointer.y = event.clientY;
  };
  const onResize = () => {
    resize();
  };
  const onVisibility = () => {
    if (document.hidden) {
      if (raf) cancelAnimationFrame(raf);
      raf = 0;
    } else kick();
  };

  window.addEventListener('pointermove', onPointer, { passive: true });
  window.addEventListener('resize', onResize);
  document.addEventListener('visibilitychange', onVisibility);
  kick();

  return {
    dispose() {
      on = false;
      if (raf) cancelAnimationFrame(raf);
      raf = 0;
      window.removeEventListener('pointermove', onPointer);
      window.removeEventListener('resize', onResize);
      document.removeEventListener('visibilitychange', onVisibility);
    },
    setEnabled(next: boolean) {
      on = next && !reduceMotion;
      if (!on) {
        if (raf) cancelAnimationFrame(raf);
        raf = 0;
        ctx!.clearRect(0, 0, w, h);
      } else {
        readTone();
        kick();
      }
    },
    setWeather(next: WeatherKind, nextIntensity: number) {
      const target = known(next);
      const changed = target !== weather;
      weather = target;
      power = Math.min(1, Math.max(0, nextIntensity));
      hazeSprite = null;
      if (changed) build();
      if (on) {
        // 换了档就重来一帧，粒子立刻以新几何出现，不残留上一档的状态。
        if (raf) cancelAnimationFrame(raf);
        raf = 0;
        ctx!.clearRect(0, 0, w, h);
        kick();
      }
    },
  };
}
