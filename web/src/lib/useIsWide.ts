import { useEffect, useState } from 'react';

/** 宽档分界：宽过 900 走双栏组卡（SettingsPage.IsWide）。 */
export function useIsWide(breakpoint = 900): boolean {
  const query = `(min-width: ${breakpoint + 1}px)`;
  const [wide, setWide] = useState(() => window.matchMedia(query).matches);

  useEffect(() => {
    const media = window.matchMedia(query);
    const sync = () => setWide(media.matches);
    sync();
    media.addEventListener('change', sync);
    return () => media.removeEventListener('change', sync);
  }, [query]);

  return wide;
}
