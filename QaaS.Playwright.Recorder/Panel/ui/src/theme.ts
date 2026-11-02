import { useEffect, useLayoutEffect, useState } from 'preact/hooks';

export type Theme = 'dark' | 'light';

const query = '(prefers-color-scheme: light)';
const key = 'qaas-panel-theme';

/** The theme the person chose, else the system's: dark unless it prefers light. */
export function initialTheme(): Theme {
  return stored() ?? (matchMedia(query).matches ? 'light' : 'dark');
}

export function useTheme(): [Theme, () => void] {
  const [chosen, setChosen] = useState<Theme | null>(stored);
  const [system, setSystem] = useState<Theme>(() => (matchMedia(query).matches ? 'light' : 'dark'));

  useEffect(() => {
    const media = matchMedia(query);
    const follow = () => setSystem(media.matches ? 'light' : 'dark');
    media.addEventListener('change', follow);
    return () => media.removeEventListener('change', follow);
  }, []);

  const theme = chosen ?? system;
  useLayoutEffect(() => {
    document.documentElement.dataset.theme = theme;
  }, [theme]);

  const toggle = () => {
    const next = theme === 'dark' ? 'light' : 'dark';
    setChosen(next);
    try {
      localStorage.setItem(key, next);
    } catch {
      // A page without an origin of its own has no storage: the choice lasts until the panel closes.
    }
  };
  return [theme, toggle];
}

function stored(): Theme | null {
  try {
    const value = localStorage.getItem(key);
    return value === 'dark' || value === 'light' ? value : null;
  } catch {
    return null;
  }
}
