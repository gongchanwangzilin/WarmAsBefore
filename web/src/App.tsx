import { useCallback, useEffect, useMemo, useRef, type CSSProperties } from 'react';
import type { Bridge } from './bridge/client';
import type { TokenMap } from './bridge/protocol';
import { PageDeck } from './components/PageDeck';
import { averageGround, groundCss, groundLayers } from './lib/ground';
import { computeInk } from './lib/ink';
import { registerGround, startLighting } from './lib/light';
import { useRouter } from './lib/router';
import { useAppearance } from './lib/useAppearance';
import { AppearancePage } from './pages/AppearancePage';
import { SettingsPage } from './pages/SettingsPage';
import { TitlePage } from './pages/TitlePage';

type Props = {
  bridge: Bridge;
  tokens: TokenMap;
};

export function App({ bridge, tokens }: Props) {
  const { route, direction, navigate, back } = useRouter();
  const appearance = useAppearance(bridge);
  const groundRef = useRef<HTMLDivElement | null>(null);

  const layers = useMemo(() => groundLayers(tokens), [tokens]);
  const groundAverage = useMemo(() => averageGround(layers), [layers]);
  const ink = useMemo(
    () => computeInk({ alpha: appearance.value.alpha, ground: groundAverage, tokens }),
    [appearance.value.alpha, groundAverage, tokens],
  );

  useEffect(() => {
    registerGround(groundRef.current);
    startLighting();
  }, []);

  // 导航变化告诉宿主：它自己的返回栈、任务栏、硬件返回键才有东西可对。
  useEffect(() => {
    void bridge.request('nav.open', { route: route.path }).catch(() => undefined);
  }, [bridge, route.path]);

  useEffect(
    () => bridge.onEvent('nav.request', ({ route: target }) => navigate(target)),
    [bridge, navigate],
  );

  const replaceAppearance = appearance.replace;
  useEffect(
    () => bridge.onEvent('appearance.changed', ({ appearance: next }) => replaceAppearance(next)),
    [bridge, replaceAppearance],
  );

  const goBack = useCallback(() => {
    void bridge.request('nav.back', { from: route.path }).catch(() => undefined);
    back();
  }, [bridge, route.path, back]);

  const worldVars = {
    '--w-alpha': appearance.value.alpha,
    '--w-frost': appearance.value.frost,
    '--w-liquid': appearance.value.liquid,
    '--w-ink-display': ink.colors.display,
    '--w-ink-body': ink.colors.body,
    '--w-ink-label': ink.colors.label,
    '--w-ink-muted': ink.colors.muted,
    '--w-ink-faint': ink.colors.faint,
    '--w-ink-on-signal': ink.onSignal,
  } as CSSProperties;

  return (
    <div
      className="world"
      data-lightweight={appearance.value.lightweight ? '1' : undefined}
      data-reduced={appearance.value.reducedTransparency ? '1' : undefined}
      style={worldVars}
    >
      <div className="ground" ref={groundRef} style={{ backgroundImage: groundCss(layers) }}>
        <span className="ground__light" aria-hidden="true" />
      </div>

      <PageDeck
        route={route}
        direction={direction}
        render={(frameRoute) => {
          if (frameRoute.id === 'title') {
            return <TitlePage bridge={bridge} onNavigate={navigate} />;
          }
          if (frameRoute.id === 'settings') {
            return <SettingsPage bridge={bridge} onBack={goBack} onNavigate={navigate} />;
          }
          return <AppearancePage store={appearance} onBack={goBack} />;
        }}
      />

      <output
        hidden
        id="ink-audit"
        data-direction={ink.direction}
        data-face={ink.face}
        data-ref={ink.ref}
        data-body={ink.worst.body}
        data-muted={ink.worst.muted}
        data-min={ink.minWorst}
        data-target-body={ink.targets.body}
      >
        {JSON.stringify(ink)}
      </output>
    </div>
  );
}
