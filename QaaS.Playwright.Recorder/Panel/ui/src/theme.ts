import { useEffect, useLayoutEffect, useState } from 'preact/hooks';
import { getPref, usePref } from './prefs';

export type Theme = 'dark' | 'light';

const query = '(prefers-color-scheme: light)';

/** The theme the person chose, else the system's: dark unless it prefers light. */
export function initialTheme(): Theme {
  return chosen(getPref('theme')) ?? (matchMedia(query).matches ? 'light' : 'dark');
}

export function useTheme(): [Theme, () => void] {
  const [stored, store] = usePref('theme', '');
  const [system, setSystem] = useState<Theme>(() => (matchMedia(query).matches ? 'light' : 'dark'));

  useEffect(() => {
    const media = matchMedia(query);
    const follow = () => setSystem(media.matches ? 'light' : 'dark');
    media.addEventListener('change', follow);
    return () => media.removeEventListener('change', follow);
  }, []);

  const theme = chosen(stored) ?? system;
  useLayoutEffect(() => {
    document.documentElement.dataset.theme = theme;
  }, [theme]);

  return [theme, () => store(theme === 'dark' ? 'light' : 'dark')];
}

function chosen(value: string | undefined): Theme | null {
  return value === 'dark' || value === 'light' ? value : null;
}
