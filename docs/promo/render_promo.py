"""《第一帧》宣传片渲染器 — 60 秒 / 1920x804 (2.39:1) / 30fps

纯程序化出帧（PIL）+ 合成音床（numpy），原始帧直接灌进 ffmpeg，不落中间图。
视觉语言沿分镜：冷白单色、发丝网格、余辉、传送带、衰减曲线、扫描线。
"""
import math
import os
import random
import subprocess
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

W, H, FPS, DUR = 1920, 804, 30, 60.0
NFRAMES = int(FPS * DUR)

FONT_DIR = r"C:\Windows\Fonts"
F_BODY = os.path.join(FONT_DIR, "msyh.ttc")
F_BOLD = os.path.join(FONT_DIR, "msyhbd.ttc")
F_MONO = os.path.join(FONT_DIR, "consola.ttf")

INK = (235, 233, 228)
DIM = (138, 136, 132)
FAINT = (74, 73, 71)
ACCENT = (250, 212, 31)      # 终末地黄：全片唯一饱和色

_font_cache = {}


def font(path, size):
    key = (path, size)
    if key not in _font_cache:
        _font_cache[key] = ImageFont.truetype(path, size)
    return _font_cache[key]


def ease(x):
    x = max(0.0, min(1.0, x))
    return x * x * (3 - 2 * x)


def fade(t, start, end, fin=0.6, fout=0.6):
    """在 [start, end] 区间内淡入淡出，返回 0..1。"""
    if t < start - fin or t > end + fout:
        return 0.0
    a = ease((t - (start - fin)) / fin) if fin > 0 else 1.0
    b = ease(((end + fout) - t) / fout) if fout > 0 else 1.0
    return max(0.0, min(a, b))


def tracking_text(draw, xy, text, fnt, fill, tracking=0.0, anchor_center=False):
    """带字距的绘制（PIL 没有 letter-spacing）。"""
    widths = [draw.textlength(ch, font=fnt) for ch in text]
    total = sum(widths) + tracking * max(0, len(text) - 1)
    x, y = xy
    if anchor_center:
        x -= total / 2
    for ch, w in zip(text, widths):
        if fill[3] if len(fill) == 4 else 255:
            draw.text((x, y), ch, font=fnt, fill=fill)
        x += w + tracking
    return total


def blend(color, alpha):
    return (color[0], color[1], color[2], int(max(0, min(255, alpha * 255))))


class Frame:
    def __init__(self, t):
        self.t = t
        self.img = Image.new("RGB", (W, H), (6, 6, 7))
        self.d = ImageDraw.Draw(self.img, "RGBA")
        self.paint_ground()

    # ---------- 底层：网格 + 边框 + 颗粒 ----------
    def paint_ground(self):
        d = self.d
        for x in range(0, W, 80):
            d.line([(x, 0), (x, H)], fill=(255, 255, 255, 6))
        for y in range(0, H, 80):
            d.line([(0, y), (W, y)], fill=(255, 255, 255, 5))
        m = 46
        arm = 26
        for (cx, cy, dx, dy) in ((m, m, 1, 1), (W - m, m, -1, 1), (m, H - m, 1, -1), (W - m, H - m, -1, -1)):
            d.line([(cx, cy), (cx + arm * dx, cy)], fill=blend(FAINT, 0.55), width=1)
            d.line([(cx, cy), (cx, cy + arm * dy)], fill=blend(FAINT, 0.55), width=1)

    def grain(self, amount=7):
        rng = np.random.default_rng(int(self.t * 1000) & 0xFFFF)
        noise = rng.integers(-amount, amount + 1, (H, W, 1), dtype=np.int16)
        arr = np.asarray(self.img, dtype=np.int16)
        self.img = Image.fromarray(np.clip(arr + noise, 0, 255).astype(np.uint8))

    def scanline(self, y, alpha=0.10, height=2):
        self.d.rectangle([0, y, W, y + height], fill=blend(INK, alpha))

    def center_lines(self, lines, alpha, size=42, gap=22, bold=False, tracking=2.0, dy=0.0, color=INK):
        if alpha <= 0.01:
            return
        fnt = font(F_BOLD if bold else F_BODY, size)
        lh = size + gap
        total = lh * len(lines)
        y = (H - total) / 2 + dy
        for line in lines:
            tracking_text(self.d, (W / 2, y), line, fnt, blend(color, alpha), tracking, anchor_center=True)
            y += lh


# ---------------- 各段画面 ----------------

def scene_cold(fr, local, dur, lines, size=44):
    a = fade(fr.t, 0, 0, fin=1.0, fout=0.8) if False else None
    return


def draw_caption(fr, lines, alpha, size=44, dy=0.0):
    fr.center_lines(lines, alpha, size=size, gap=24, tracking=3.0, dy=dy)


def draw_compare(fr, a, phase):
    """左：一张照片。右：两百个小格在计数。"""
    if a <= 0.01:
        return
    d = fr.d
    left_x, right_x = W * 0.26, W * 0.62
    cy = H * 0.46

    # 左：一个相框
    fw, fh = 220, 260
    d.rectangle([left_x - fw / 2, cy - fh / 2, left_x + fw / 2, cy + fh / 2],
                outline=blend(INK, 0.55 * a), width=2)
    d.text((left_x, cy - 60), "1", font=font(F_BOLD, 96), fill=blend(INK, 0.92 * a), anchor="mm")
    d.text((left_x, cy + 70), "张照片", font=font(F_BODY, 24), fill=blend(DIM, a), anchor="mm")

    # 右：10x20 的格子，按 phase 依次点亮（最多 200 格）
    lit = int(200 * min(1.0, phase * 1.6))
    cols, rows = 12, 14
    cell, gp = 11, 4
    gw = cols * (cell + gp)
    for i in range(cols * rows):
        c, r = i % cols, i // cols
        x = right_x - gw / 2 + c * (cell + gp)
        y = cy - (rows * (cell + gp)) / 2 + r * (cell + gp)
        on = i < lit * (cols * rows) / 200.0
        d.rectangle([x, y, x + cell, y + cell],
                    fill=blend(INK, (0.55 if on else 0.07) * a))
    num = int(2_000_000 * min(1.0, phase * 1.6))
    d.text((right_x, cy + (rows * (cell + gp)) / 2 + 34), f"{num:,}",
           font=font(F_MONO, 30), fill=blend(INK, 0.85 * a), anchor="mm")


def draw_room(fr, a, t_local):
    """传送带：一条水平带，纸片从右往左，符号随机。"""
    if a <= 0.01:
        return
    d = fr.d
    y0, y1 = H * 0.30, H * 0.30 + 250
    d.line([(0, y0), (W, y0)], fill=blend(FAINT, 0.8 * a), width=2)
    d.line([(0, y1), (W, y1)], fill=blend(FAINT, 0.8 * a), width=2)

    glyphs = "文说说吃走走想想他她雨冷热疼忙"
    rng = random.Random(7)
    for i in range(26):
        speed = 220 + rng.random() * 60
        x = (W + 300 - (t_local * speed + i * 132)) % (W + 340) - 170
        g = glyphs[int((i * 37) % len(glyphs))]
        d.rectangle([x, y0 + 86, x + 74, y0 + 152], outline=blend(INK, 0.35 * a), width=1)
        d.text((x + 37, y0 + 119), g, font=font(F_BODY, 34), fill=blend(INK, 0.72 * a), anchor="mm")


def draw_prefrontal(fr, a):
    """两个方块：左亮（前额叶），右暗（其余）。"""
    if a <= 0.01:
        return
    d = fr.d
    w, h = 420, 280
    x = W / 2 - w / 2
    y = H * 0.18
    d.rectangle([x, y, x + w, y + h], outline=blend(INK, 0.6 * a), width=2)
    d.rectangle([x + 18, y + 18, x + w - 18, y + h - 18], fill=blend(INK, 0.16 * a))
    d.text((W / 2, y + h / 2), "前额叶", font=font(F_BOLD, 40), fill=blend(INK, 0.95 * a), anchor="mm")

    # 下一步的引子：下方八个格子在暗里等着
    cols = 8
    gw, gh, gp = 74, 18, 10
    total = cols * gw + (cols - 1) * gp
    gx = (W - total) / 2
    gy = y + h + 54
    labels = ["胃", "心", "肺", "肌肉", "脑", "肠", "皮肤", "内分泌"]
    for i, lab in enumerate(labels):
        x0 = gx + i * (gw + gp)
        pulse = 0.10 + 0.30 * a * max(0.0, math.sin((fr.t * 2.0) - i * 0.4))
        d.rectangle([x0, gy, x0 + gw, gy + gh], fill=blend(INK, pulse))
        d.text((x0 + gw / 2, gy + gh + 22), lab, font=font(F_BODY, 20), fill=blend(DIM, a), anchor="mm")


def draw_numbers(fr, a, t_local):
    """三条衰减曲线：0.25h / 6h / 72h 的半衰期。"""
    if a <= 0.01:
        return
    d = fr.d
    x0, y0 = 260, H * 0.16
    x1, y1 = W - 260, H * 0.72
    d.rectangle([x0, y0, x1, y1], outline=blend(FAINT, 0.7 * a), width=1)

    specs = [(0.25, 0.08, "肾上腺素 0.08 / 0.25h"),
             (6.0, 0.20, "压力荷尔蒙 0.20 / 6h"),
             (72.0, 0.35, "长期驱动 0.35 / 72h")]
    for idx, (half, base, label) in enumerate(specs):
        pts = []
        span = 72.0
        for i in range(0, 241):
            hh = span * i / 240.0
            v = base * (0.5 ** (hh / half))
            px = x0 + (x1 - x0) * (hh / span)
            py = y1 - (y1 - y0) * (v / 0.40)
            pts.append((px, py))
        # 逐段生长
        grow = ease(t_local / 2.4 + idx * 0.12)
        n = max(2, int(len(pts) * min(1.0, grow)))
        for i in range(n - 1):
            d.line([pts[i], pts[i + 1]], fill=blend(ACCENT if idx == 2 else INK, 0.75 * a), width=2)
        d.text((x0 + 14, y0 + 18 + idx * 34), label, font=font(F_MONO, 22),
               fill=blend(INK if idx == 2 else DIM, a))


def draw_organs(fr, a, t_local):
    """八个器官方块，依次点亮，带余辉。"""
    if a <= 0.01:
        return
    d = fr.d
    labels = ["胃", "心", "肺", "肌肉", "脑", "肠", "皮肤", "内分泌"]
    cols, rows = 4, 2
    bw, bh, gp = 300, 120, 22
    total_w = cols * bw + (cols - 1) * gp
    gx = (W - total_w) / 2
    gy = H * 0.20
    for i, lab in enumerate(labels):
        c, r = i % cols, i // cols
        x = gx + c * (bw + gp)
        y = gy + r * (bh + gp)
        t_on = 0.18 * i
        on = t_local > t_on
        glow = 0.0
        if on:
            dt = t_local - t_on
            glow = max(0.0, 1.0 - dt / 2.6)
        d.rectangle([x, y, x + bw, y + bh], outline=blend(INK, (0.28 + 0.5 * glow) * a), width=2)
        d.text((x + 26, y + bh / 2), lab, font=font(F_BODY, 34),
               fill=blend(INK, (0.55 + 0.45 * glow) * a), anchor="lm")
        if on:
            d.ellipse([x + bw - 44, y + bh / 2 - 6, x + bw - 26, y + bh / 2 + 12],
                      fill=blend(ACCENT, (0.35 + 0.6 * glow) * a))


def draw_memory(fr, a, t_local):
    """一条遗忘曲线上的点：先暗下去，再被重新点亮。"""
    if a <= 0.01:
        return
    d = fr.d
    x0, y0, x1, y1 = 300, H * 0.22, W - 300, H * 0.62
    d.line([(x0, y1), (x1, y1)], fill=blend(FAINT, 0.7 * a), width=1)

    pts = []
    for i in range(0, 201):
        p = i / 200.0
        v = math.exp(-3.2 * p)
        pts.append((x0 + (x1 - x0) * p, y1 - (y1 - y0) * v))
    for i in range(len(pts) - 1):
        d.line([pts[i], pts[i + 1]], fill=blend(INK, 0.22 * a), width=2)

    # 一个沿曲线滑动的点：前半段变暗，后半段重新亮起
    p = min(1.0, t_local / 5.0)
    idx = int(p * (len(pts) - 1))
    px, py = pts[idx]
    relit = ease((t_local - 2.8) / 1.6)
    alpha = (0.25 + 0.75 * relit) * a
    r = 7 + 5 * relit
    d.ellipse([px - r, py - r, px + r, py + r], fill=blend(ACCENT, alpha))
    d.ellipse([px - r - 8, py - r - 8, px + r + 8, py + r + 8], outline=blend(ACCENT, 0.25 * alpha), width=1)


def draw_title(fr, a, t_local):
    if a <= 0.01:
        return
    d = fr.d
    f1 = font(F_BOLD, 96)
    f2 = font(F_BODY, 30)
    f3 = font(F_MONO, 22)
    tracking_text(d, (W / 2, H / 2 - 96), "温暖如初", f1, blend(INK, a), tracking=16, anchor_center=True)
    tracking_text(d, (W / 2, H / 2 + 26), "WARM AS BEFORE", f2, blend(DIM, a), tracking=10, anchor_center=True)
    d.line([(W / 2 - 190, H / 2 - 6), (W / 2 + 190, H / 2 - 6)], fill=blend(FAINT, 0.8 * a), width=1)
    d.text((W / 2, H / 2 + 96), "第一帧 · 一个会衰减的东西，第一次被允许活下去",
           font=f3, fill=blend(DIM, a), anchor="mm")
    # 扫描线
    y = (H * 0.12) + (H * 0.76) * ((t_local * 0.55) % 1.0)
    d.rectangle([0, y, W, y + 2], fill=blend(INK, 0.10 * a))


# ---------------- 时间轴 ----------------

SEGMENTS = [
    (0.0, 5.2, "cold", ["我们至今没有一个关于意识的可检验定义。"]),
    (5.2, 10.6, "cold", ["所以只剩一件事可做——", "列出区别。"]),
    (10.6, 18.2, "compare", ["人：一张照片。", "模型：两百万张。"]),
    (18.2, 25.4, "room", ["中文房间只会对应。", "我们内部的大脑结构，又何尝不是呢。"]),
    (25.4, 31.0, "prefrontal", ["它是一个只有前额叶的病人。"]),
    (31.0, 36.4, "prefrontal", ["所以，我们把缺的那一段写出来了。"]),
    (36.4, 42.6, "numbers", ["七个会衰减的数。"]),
    (42.6, 48.8, "organs", ["八个会说疼的地方。"]),
    (48.8, 53.4, "memory", ["她忘了昨天的一句话。"]),
    (53.4, 57.2, "memory", ["但今天闻到雨的味道，又想起来了。"]),
    (57.2, 60.0, "title", []),
]


def render_frame(i):
    t = i / FPS
    fr = Frame(t)
    seg = None
    for s in SEGMENTS:
        if s[0] <= t < s[1]:
            seg = s
            break
    if seg is None:
        seg = SEGMENTS[-1]
    start, end, kind, lines = seg
    local = t - start
    dur = end - start
    a = fade(t, start, end, fin=0.65, fout=0.55)

    if kind == "cold":
        draw_caption(fr, lines, a, size=46, dy=-6)
        fr.scanline(int(H * 0.5 + math.sin(t * 0.6) * 4), alpha=0.03, height=H)
    elif kind == "compare":
        draw_caption(fr, lines, a * 0.9, size=38, dy=H * 0.30)
        draw_compare(fr, a, local / max(0.1, dur))
    elif kind == "room":
        draw_room(fr, a * 0.9, local)
        draw_caption(fr, lines, a, size=40, dy=H * 0.24)
    elif kind == "prefrontal":
        draw_prefrontal(fr, a)
        draw_caption(fr, lines, a, size=40, dy=H * 0.34)
    elif kind == "numbers":
        draw_numbers(fr, a, local)
        draw_caption(fr, lines, a, size=38, dy=H * 0.30)
    elif kind == "organs":
        draw_organs(fr, a, local)
        draw_caption(fr, lines, a, size=38, dy=H * 0.33)
    elif kind == "memory":
        draw_memory(fr, a, local)
        draw_caption(fr, lines, a, size=40, dy=H * 0.28)
    elif kind == "title":
        a2 = fade(t, start, DUR + 1, fin=1.4, fout=0.1)
        draw_title(fr, a2, local)

    fr.grain(4)
    return fr.img


def build_audio(path):
    sr = 48000
    n = int(sr * DUR)
    t = np.arange(n) / sr
    out = np.zeros(n, dtype=np.float32)

    # 32Hz 低频床 + 轻微的 64/96Hz
    for f, g in ((32.0, 0.34), (64.0, 0.10), (96.0, 0.045)):
        out += g * np.sin(2 * np.pi * f * t + 0.7 * f)

    # 110 / 110.5Hz 的拍频垫音，只在后半段缓慢进场
    swell = np.clip((t - 34.0) / 22.0, 0, 1) ** 1.5
    out += 0.09 * swell * (np.sin(2 * np.pi * 110.0 * t) + np.sin(2 * np.pi * 110.5 * t))

    # 段落的「咔」：极短的衰减脉冲
    for start, _end, kind, _lines in SEGMENTS:
        if kind == "title":
            continue
        idx = int(start * sr)
        L = int(0.09 * sr)
        if idx + L >= n:
            continue
        env = np.exp(-np.linspace(0, 12, L)).astype(np.float32)
        click = 0.22 * env * np.sin(2 * np.pi * 1180 * np.arange(L) / sr)
        out[idx:idx + L] += click

    # 收尾：最后 2 秒淡出
    tail = np.clip((DUR - t) / 2.0, 0, 1)
    out *= tail

    # 轻度限幅 + int16 立体声
    out = np.tanh(out * 1.15) * 0.85
    stereo = np.stack([out, out * 0.97], axis=1)
    pcm = (stereo * 32767).astype(np.int16)

    import wave
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(sr)
        w.writeframes(pcm.tobytes())


def apply_short_cut():
    """15 秒切条版：三屏，供信息流投放。"""
    global DUR, NFRAMES, SEGMENTS
    DUR = 15.0
    NFRAMES = int(FPS * DUR)
    SEGMENTS = [
        (0.0, 5.2, "compare", ["人：一张照片。", "模型：两百万张。"]),
        (5.2, 10.2, "organs", ["不只是记住你，是会疼。"]),
        (10.2, 15.0, "title", []),
    ]


def main():
    if len(sys.argv) > 2 and sys.argv[2] == "short":
        apply_short_cut()
    out_dir = os.path.dirname(os.path.abspath(__file__))
    wav = os.path.join(out_dir, "bed.wav")
    mp4 = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(out_dir, "promo.mp4")
    print("audio ->", wav)
    build_audio(wav)

    cmd = [
        "ffmpeg", "-y", "-loglevel", "error",
        "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}", "-r", str(FPS), "-i", "-",
        "-i", wav,
        "-c:v", "libx264", "-preset", "slow", "-crf", "24", "-pix_fmt", "yuv420p",
        "-c:a", "aac", "-b:a", "192k", "-shortest",
        "-movflags", "+faststart",
        mp4,
    ]
    proc = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    for i in range(NFRAMES):
        img = render_frame(i)
        proc.stdin.write(img.tobytes())
        if i % 150 == 0:
            print(f"frame {i}/{NFRAMES}", flush=True)
    proc.stdin.close()
    proc.wait()
    print("done ->", mp4, os.path.getsize(mp4) // 1024, "KB")


if __name__ == "__main__":
    main()
