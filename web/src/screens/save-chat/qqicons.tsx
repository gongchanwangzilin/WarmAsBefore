import type { ReactNode } from 'react';

/**
 * QQ 壳的线描图标。
 *
 * 真机客户端里的图标是矢量字形，Web 端没有那套字体，也不能塞第三方图标库，
 * 所以这里用 24 格 viewBox 的描边路径重画：只吃 currentColor，粗细统一 1.7，
 * 颜色跟着字号继承（var(--w-ink-*)、var(--w-signal)），没有一处写死颜色。
 */

export type IconName =
  | 'chat'
  | 'people'
  | 'star'
  | 'folder'
  | 'gear'
  | 'power'
  | 'search'
  | 'plus'
  | 'phone'
  | 'video'
  | 'screen'
  | 'apps'
  | 'more'
  | 'chevron'
  | 'smile'
  | 'scissors'
  | 'image'
  | 'mic'
  | 'gift'
  | 'clock'
  | 'send'
  | 'close'
  | 'bell'
  | 'storage'
  | 'keyboard'
  | 'shield'
  | 'key'
  | 'link'
  | 'palette'
  | 'info'
  | 'menu'
  | 'check';

const PATHS: Record<IconName, ReactNode> = {
  chat: <path d="M21 11.5a8.4 8.4 0 0 1-8.5 8.3 8.9 8.9 0 0 1-3.8-.8L4 20l1.1-4.3a8.2 8.2 0 0 1-1-3.9A8.4 8.4 0 0 1 12.5 3.5 8.4 8.4 0 0 1 21 11.5Z" />,
  people: (
    <>
      <circle cx="9.5" cy="8" r="3.3" />
      <path d="M3.5 20c0-3.3 2.7-5.6 6-5.6s6 2.3 6 5.6" />
      <circle cx="17.5" cy="9.2" r="2.4" />
      <path d="M16.6 14.8c2.4.2 3.9 1.9 3.9 4.4" />
    </>
  ),
  star: <path d="m12 3.8 2.6 5.2 5.8.9-4.2 4.1 1 5.7-5.2-2.7-5.2 2.7 1-5.7L3.6 9.9l5.8-.9Z" />,
  folder: <path d="M3.5 7.3a1.8 1.8 0 0 1 1.8-1.8h3.6l1.8 2.2h7.9a1.8 1.8 0 0 1 1.8 1.8v8.2a1.8 1.8 0 0 1-1.8 1.8H5.3a1.8 1.8 0 0 1-1.8-1.8Z" />,
  gear: (
    <>
      <circle cx="12" cy="12" r="3.1" />
      <path d="M12 3.2v2.1M12 18.7v2.1M3.2 12h2.1M18.7 12h2.1M5.9 5.9l1.5 1.5M16.6 16.6l1.5 1.5M18.1 5.9l-1.5 1.5M7.4 16.6l-1.5 1.5" />
    </>
  ),
  power: (
    <>
      <path d="M12 3.5V11" />
      <path d="M7.2 6.6a7.2 7.2 0 1 0 9.6 0" />
    </>
  ),
  search: (
    <>
      <circle cx="11" cy="11" r="6.2" />
      <path d="m16 16 4 4" />
    </>
  ),
  plus: <path d="M12 5v14M5 12h14" />,
  phone: <path d="M7.2 3.8 9.6 8l-2 1.7a11.4 11.4 0 0 0 6.7 6.7l1.7-2 4.2 2.4-.9 2.6a1.9 1.9 0 0 1-2 .1A16.6 16.6 0 0 1 4.5 6.7a1.9 1.9 0 0 1 .1-2Z" />,
  video: (
    <>
      <rect x="3.2" y="6.4" width="12.4" height="11.2" rx="2.4" />
      <path d="m15.6 11.2 5.2-3v7.6l-5.2-3Z" />
    </>
  ),
  screen: (
    <>
      <rect x="3.2" y="4.8" width="17.6" height="12" rx="2.2" />
      <path d="M8.5 20h7M12 16.8V20" />
      <path d="M12 12.6V8.4M10 10.4l2-2 2 2" />
    </>
  ),
  apps: (
    <>
      <rect x="3.6" y="3.6" width="7" height="7" rx="2" />
      <rect x="13.4" y="3.6" width="7" height="7" rx="2" />
      <rect x="3.6" y="13.4" width="7" height="7" rx="2" />
      <rect x="13.4" y="13.4" width="7" height="7" rx="2" />
    </>
  ),
  more: (
    <>
      <circle cx="5.5" cy="12" r="1.2" />
      <circle cx="12" cy="12" r="1.2" />
      <circle cx="18.5" cy="12" r="1.2" />
    </>
  ),
  chevron: <path d="m9 6 6 6-6 6" />,
  smile: (
    <>
      <circle cx="12" cy="12" r="8.4" />
      <path d="M8.6 14.2a4 4 0 0 0 6.8 0" />
      <circle cx="9.3" cy="9.8" r="0.9" />
      <circle cx="14.7" cy="9.8" r="0.9" />
    </>
  ),
  scissors: (
    <>
      <circle cx="6.5" cy="6.5" r="2.3" />
      <circle cx="6.5" cy="17.5" r="2.3" />
      <path d="m8.4 8.1 11 9.4M19.4 6.5 8.4 15.9" />
    </>
  ),
  image: (
    <>
      <rect x="3.4" y="4.6" width="17.2" height="14.8" rx="2.4" />
      <circle cx="8.6" cy="9.6" r="1.7" />
      <path d="m4.4 17.4 4.8-4.6 3.4 3.2 4-4.2 3.4 3.4" />
    </>
  ),
  mic: (
    <>
      <rect x="9.2" y="3.4" width="5.6" height="10.2" rx="2.8" />
      <path d="M5.8 11.4a6.2 6.2 0 0 0 12.4 0M12 17.6v3" />
    </>
  ),
  gift: (
    <>
      <rect x="3.6" y="9.2" width="16.8" height="11" rx="1.8" />
      <path d="M3.6 13.2h16.8M12 9.2v11" />
      <path d="M12 9.2C10.6 5.6 9.2 4.2 7.6 4.6 6.2 5 6 6.6 7 7.6c.9.9 2.6 1.6 5 1.6Zm0 0c1.4-3.6 2.8-5 4.4-4.6 1.4.4 1.6 2 .6 3-.9.9-2.6 1.6-5 1.6Z" />
    </>
  ),
  clock: (
    <>
      <circle cx="12" cy="12" r="8.2" />
      <path d="M12 7.6V12l3 1.8" />
    </>
  ),
  send: (
    <>
      <path d="m20 4-9 16-2.2-6.4L3.4 11.4Z" />
      <path d="M20 4 8.8 13.6" />
    </>
  ),
  close: <path d="m6 6 12 12M18 6 6 18" />,
  bell: (
    <>
      <path d="M18 15.6V10a6 6 0 1 0-12 0v5.6L4.4 18h15.2Z" />
      <path d="M9.8 18a2.2 2.2 0 0 0 4.4 0" />
    </>
  ),
  storage: (
    <>
      <rect x="3.6" y="4.6" width="16.8" height="6.2" rx="2" />
      <rect x="3.6" y="13.2" width="16.8" height="6.2" rx="2" />
      <path d="M7 7.7h.01M7 16.3h.01" />
    </>
  ),
  keyboard: (
    <>
      <rect x="2.8" y="6.4" width="18.4" height="11.2" rx="2.2" />
      <path d="M6.4 10h.01M9.4 10h.01M12.4 10h.01M15.4 10h.01M18 10h.01M8 13.8h8" />
    </>
  ),
  shield: (
    <>
      <path d="M12 3.4 19 6v5.6c0 4-2.9 7.4-7 9-4.1-1.6-7-5-7-9V6Z" />
      <path d="m9.2 12 2 2 3.6-3.8" />
    </>
  ),
  key: (
    <>
      <circle cx="8.2" cy="12" r="3.4" />
      <path d="M11.6 12H21m-3 0v3m-3-3v2.4" />
    </>
  ),
  link: (
    <>
      <path d="M10.2 13.8a4.4 4.4 0 0 0 6.2 0l2.4-2.4a4.4 4.4 0 0 0-6.2-6.2l-1.3 1.3" />
      <path d="M13.8 10.2a4.4 4.4 0 0 0-6.2 0l-2.4 2.4a4.4 4.4 0 0 0 6.2 6.2l1.3-1.3" />
    </>
  ),
  palette: (
    <>
      <path d="M12 3.6a8.4 8.4 0 1 0 0 16.8c1.4 0 2-.9 2-1.8 0-1.4-1.2-1.8-1.2-3 0-1 .8-1.8 1.8-1.8h1.5a4.3 4.3 0 0 0 4.3-4.3c0-3.2-3.6-5.9-8.4-5.9Z" />
      <circle cx="8" cy="8.6" r="1.1" />
      <circle cx="12.6" cy="7" r="1.1" />
      <circle cx="16.6" cy="9.4" r="1.1" />
    </>
  ),
  info: (
    <>
      <circle cx="12" cy="12" r="8.4" />
      <path d="M12 11v5.4M12 7.8h.01" />
    </>
  ),
  menu: <path d="M4 7h16M4 12h16M4 17h16" />,
  check: <path d="m5 12.6 4.4 4.4L19 6.6" />,
};

export function Icon({ name, className }: { name: IconName; className?: string }) {
  return (
    <svg
      viewBox="0 0 24 24"
      width="1em"
      height="1em"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.7}
      strokeLinecap="round"
      strokeLinejoin="round"
      className={className}
      aria-hidden="true"
      focusable="false"
    >
      {PATHS[name]}
    </svg>
  );
}
