import { useEffect, useState } from 'preact/hooks';
import { send } from './bridge';

// What the person chose in the panel: the theme, slow motion, the widths of the panes, the hints already seen. The
// panel's page has no origin of its own, so no storage either: the recorder keeps them (~/.qaas/recorder-panel.json),
// puts them in the page as it opens it, and hears of each change. `npm run dev` keeps them in the browser instead.

declare global {
  interface Window {
    __qaasPanelPrefs?: Record<string, string>;
  }
}

export type PrefKey = 'theme' | 'slowMotion' | 'compactTree' | 'stepsWidth' | 'inspectorWidth' | 'codeShare' | 'seenPalette';

const values: Record<string, string> = { ...(window.__qaasPanelPrefs ?? {}) };
const listeners = new Set<() => void>();

export function getPref(key: PrefKey): string | undefined {
  if (key in values) return values[key];
  try {
    return localStorage.getItem(`qaas-panel.${key}`) ?? undefined;
  } catch {
    return undefined;
  }
}

export function setPref(key: PrefKey, value: string): void {
  if (values[key] === value) return;
  values[key] = value;
  try {
    localStorage.setItem(`qaas-panel.${key}`, value);
  } catch {
    // No storage of its own: the recorder keeps it.
  }
  send({ type: 'prefs', name: key, value });
  for (const listener of listeners) listener();
}

/** The person's choice, and how to change it; `fallback` until they made one. */
export function usePref(key: PrefKey, fallback: string): [string, (value: string) => void] {
  const [, render] = useState(0);
  useEffect(() => {
    const listener = () => render((count) => count + 1);
    listeners.add(listener);
    return () => {
      listeners.delete(listener);
    };
  }, []);
  return [getPref(key) ?? fallback, (value) => setPref(key, value)];
}
