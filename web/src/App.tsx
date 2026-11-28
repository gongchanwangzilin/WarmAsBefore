import { useCallback, useEffect, useMemo, useRef, type CSSProperties } from 'react';
import type { Bridge } from './bridge/client';
import type { TokenMap } from './bridge/protocol';
import { BackChip } from './components/BackChip';
import { ErrorBoundary } from './components/ErrorBoundary';
import { PageHeader } from './components/kit';
import { PageDeck } from './components/PageDeck';
import { ToastHost } from './components/ToastHost';
import { PageShell } from './components/PageShell';
import { averageGround, groundCss, groundLayers } from './lib/ground';
import { computeInk } from './lib/ink';
import { startLighting } from './lib/light';
import { startAtmosphere, type Atmosphere } from './lib/atmosphere';
import { prefersReducedMotion } from './lib/motion';
import { useRouter, type Route } from './lib/router';
import { useAppearance } from './lib/useAppearance';
import { AppearancePage } from './pages/AppearancePage';
import { SettingsPage } from './pages/SettingsPage';
import { TitlePage } from './pages/TitlePage';
import { screenFor } from './screens/lazy';
import { Suspense } from 'react';

type Props = {
  bridge: Bridge;
  tokens: TokenMap;
};

/**
 * 首屏画完之后再做视觉层的挂载：requestIdleCallback 有就等空闲，没有就退到 120ms。
 * 返回一个取消函数，卸载或依赖变化时不会留下半挂的监听。
 */
function afterFirstPaint(run: () => void): () => void {
  const scope = window as Window & {
    requestIdleCallback?: (cb: () => void, options?: { timeout: number }) => number;
    cancelIdleCallback?: (handle: number) => void;
  };
  if (typeof scope.requestIdleCallback === 'function') {
    const handle = scope.requestIdleCallback(run, { timeout: 400 });
    return () => scope.cancelIdleCallback?.(handle);
  }
  const timer = window.setTimeout(run, 120);
  return () => window.clearTimeout(timer);
}

export function App({ bridge, tokens }: Props) {
  const { route, direction, navigate, back } = useRouter();
  const appearance = useAppearance(bridge);
  const groundRef = useRef<HTMLDivElement | null>(null);
  const skyRef = useRef<HTMLCanvasElement | null>(null);
  const sky = useRef<Atmosphere | null>(null);

  // 外观里的「页面色」过去只染背景，玻璃一点没变，看起来像没生效 —— 现在它驱动玻璃底色。
  const pageTintHex = appearance.value.background.pageTintHex;
  useEffect(() => {
    const root = document.documentElement;
    if (!pageTintHex) {
      root.style.removeProperty('--w-tint-page');
      return;
    }
    root.style.setProperty('--w-tint-page', `color-mix(in oklab, ${pageTintHex} 26%, transparent)`);
  }, [pageTintHex]);

  const background = appearance.value.background;
  const layers = useMemo(() => groundLayers(tokens, background), [tokens, background]);
  const groundAverage = useMemo(() => averageGround(layers), [layers]);
  const ink = useMemo(
    () => computeInk({ alpha: appearance.value.alpha, ground: groundAverage, tokens }),
    [appearance.value.alpha, groundAverage, tokens],
  );

  useEffect(() => {
    // 指针光要挂 window 的 pointermove、并在注册时量一遍所有玻璃；首帧没人动鼠标，
    // 这两件事挪到首屏画完之后，可交互时间不被它占住。
    return afterFirstPaint(startLighting);
  }, []);

  // 天气层：类型与强度由宿主下发（WeatherSceneKind + 场景强度），这里只负责画。
  // 轻量模式与「天气场景叠加」关掉时整层不画。
  const lightweight = appearance.value.lightweight;
  const lightweightRef = useRef(lightweight);
  lightweightRef.current = lightweight;
  const weatherOn = appearance.value.scene.weather;
  const weatherKind = appearance.value.scene.weatherKind;
  const weatherIntensity = appearance.value.scene.weatherIntensity;
  useEffect(() => {
    const canvas = skyRef.current;
    if (!canvas) return;
    if (sky.current) {
      sky.current.setEnabled(weatherOn && !lightweight);
      return;
    }
    // 这是持续帧成本，等首屏落定再起。
    const cancel = afterFirstPaint(() => {
      sky.current = startAtmosphere(canvas, weatherOn && !lightweightRef.current, prefersReducedMotion(), weatherKind, weatherIntensity);
    });
    return () => {
      cancel();
      sky.current?.dispose();
      sky.current = null;
    };
  }, [lightweight, weatherOn]);

  useEffect(() => {
    sky.current?.setWeather(weatherKind, weatherIntensity);
  }, [weatherKind, weatherIntensity]);

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

  // 键盘：Esc 与 Alt+← 都当返回（宿主的硬件返回键也走这条）。
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape' || (event.altKey && event.key === 'ArrowLeft')) goBack();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [goBack]);

  // 页面色：纯色底那颗颜色与「页面色」都真正到场，页面底与玻璃底色都跟着换。
  // 地面已经由 groundLayers 用同一颗颜色现算，这里只补页面这一层。
  const pageTint = background.pageTintHex.trim();
  const solid = background.mode === 'solid' ? background.solidHex.trim() : '';
  const pageBg = pageTint || solid;

  const worldVars = {
    '--w-alpha': appearance.value.alpha,
    '--w-frost': appearance.value.frost,
    '--w-liquid': appearance.value.liquid,
    ...(pageBg
      ? {
          '--w-bg': pageBg,
          '--w-surface-tint': `color-mix(in oklab, ${pageBg} 62%, transparent)`,
        }
      : {}),
    '--w-ink-display': ink.colors.display,
    '--w-ink-body': ink.colors.body,
    '--w-ink-label': ink.colors.label,
    '--w-ink-muted': ink.colors.muted,
    '--w-ink-faint': ink.colors.faint,
    '--w-ink-on-signal': ink.onSignal,
  } as CSSProperties;

  const renderScreen = (frameRoute: Route) => {
    if (frameRoute.id === 'title') {
      return <TitlePage bridge={bridge} onNavigate={navigate} />;
    }
    if (frameRoute.id === 'settings') {
      return <SettingsPage bridge={bridge} onBack={goBack} onNavigate={navigate} />;
    }
    if (frameRoute.id === 'appearance') {
      return <AppearancePage bridge={bridge} store={appearance} onBack={goBack} />;
    }

    const Screen = screenFor(frameRoute.id);
    if (Screen) {
      return (
        <Suspense fallback={<PageSkeleton />}>
          <Screen
            bridge={bridge}
            path={frameRoute.path}
            query={frameRoute.query}
            navigate={navigate}
            back={goBack}
          />
        </Suspense>
      );
    }

    return (
      <PageShell>
        <PageHeader title={frameRoute.name} onBack={goBack} />
        <div className="screen screen--narrow">
          <p className="k-empty__reason">这一页还没有搬到 Web 端（{frameRoute.path}）。</p>
        </div>
      </PageShell>
    );
  };

  return (
    <div
      className="world"
      data-lightweight={appearance.value.lightweight ? '1' : undefined}
      data-reduced={appearance.value.reducedTransparency ? '1' : undefined}
      style={worldVars}
    >
      <div className="ground" ref={groundRef} style={{ backgroundImage: groundCss(layers) }} />

      <canvas className="sky" ref={skyRef} aria-hidden="true" />

      {route.depth > 0 ? (
        <BackChip
          routeKey={route.path}
          onBack={goBack}
          force={route.id === 'main' || route.id === 'phone'}
        />
      ) : null}

      <PageDeck
        route={route}
        direction={direction}
        render={(frameRoute) => (
          <ErrorBoundary key={frameRoute.path + frameRoute.query.toString()} where={frameRoute.path}>
            {renderScreen(frameRoute)}
          </ErrorBoundary>
        )}
      />

      <ToastHost bridge={bridge} />

      <output hidden id="web-error" />

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

/**
 * 懒加载骨架：页面在下载中时先画页头与几块卡的位置，不闪白。
 */
function PageSkeleton() {
  return (
    <PageShell>
      <div className="ph ph--plain ph--skeleton" aria-busy="true">
        <div className="hd">
          <div className="hd__nav">
            <span className="sk-line sk-line--nav" />
          </div>
          <span className="sk-line sk-line--title" />
          <span className="sk-line sk-line--sub" />
        </div>
      </div>
      <div className="screen">
        <div className="sk-card">
          <span className="sk-line" />
          <span className="sk-line sk-line--short" />
        </div>
        <div className="sk-card">
          <span className="sk-line" />
          <span className="sk-line sk-line--short" />
        </div>
      </div>
    </PageShell>
  );
}
