import { useEffect, useMemo, useRef, useState, type CSSProperties } from 'react';
import { Glass } from './components/Glass';
import { GlassSlider } from './components/GlassSlider';
import { GlassSwitch } from './components/GlassSwitch';
import { SignalButton } from './components/SignalButton';
import { TopBar } from './components/TopBar';
import { groundCss } from './lib/ground';
import { computeInk } from './lib/ink';
import { registerGround, startLighting } from './lib/light';

export default function App() {
  const [lightweight, setLightweight] = useState(false);
  const [alpha, setAlpha] = useState(0.85);
  const [frost, setFrost] = useState(0.5);
  const [liquid, setLiquid] = useState(0.4);
  const [applied, setApplied] = useState(false);

  const groundRef = useRef<HTMLDivElement | null>(null);
  const ink = useMemo(() => computeInk({ alpha, liquid }), [alpha, liquid]);

  useEffect(() => {
    registerGround(groundRef.current);
    startLighting();
  }, []);

  useEffect(() => {
    if (!applied) return;
    const timer = window.setTimeout(() => setApplied(false), 1400);
    return () => window.clearTimeout(timer);
  }, [applied]);

  const vars = {
    '--alpha': alpha,
    '--frost': frost,
    '--liquid': liquid,
    '--ink-display': ink.colors.display,
    '--ink-body': ink.colors.body,
    '--ink-label': ink.colors.label,
    '--ink-muted': ink.colors.muted,
    '--ink-faint': ink.colors.faint,
    '--ink-on-signal': ink.onSignal,
  } as CSSProperties;

  return (
    <div className="world" data-lightweight={lightweight ? '1' : undefined} style={vars}>
      <div className="ground" ref={groundRef} style={{ backgroundImage: groundCss() }}>
        <span className="ground__light" aria-hidden="true" />
      </div>

      <div className="page">
        <TopBar title="外观" onBack={() => undefined} />

        <main className="content">
          <Glass as="section" tone="card" radius={12} className="card">
            <h2 className="card__title">玻璃效果</h2>

            <div className="rows">
              <div className="row">
                <span className="row__label">轻量模式</span>
                <GlassSwitch label="轻量模式" checked={lightweight} onChange={setLightweight} />
              </div>

              <GlassSlider label="玻璃透明度" value={alpha} onChange={setAlpha} />
              <GlassSlider label="磨砂程度" value={frost} onChange={setFrost} />
              <GlassSlider label="液态高度" value={liquid} onChange={setLiquid} />
            </div>

            <p className="card__note">
              指针在窗口任意位置移动，卡片与按钮上的光都跟着走，离得越远越暗；墨色由合成面算出，正文不低于
              4.5:1。
            </p>
          </Glass>

          <div className="actions">
            <SignalButton onClick={() => setApplied(true)}>{applied ? '已应用' : '应用'}</SignalButton>
          </div>
        </main>
      </div>

      <output
        hidden
        id="ink-audit"
        data-direction={ink.direction}
        data-face={ink.face}
        data-ref={ink.ref}
        data-body={ink.worst.body}
        data-body-on-face={ink.onFace.body}
        data-muted={ink.worst.muted}
        data-min={ink.minWorst}
        data-target-body={ink.targets.body}
      >
        {JSON.stringify(ink)}
      </output>
    </div>
  );
}
