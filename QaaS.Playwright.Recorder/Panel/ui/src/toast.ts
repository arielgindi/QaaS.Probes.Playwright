import { useEffect, useState } from 'preact/hooks';
import type { IconName } from './components/icons';

/** A short message at the bottom of the panel, with at most one action, e.g. Undo. */
export interface Toast {
  text: string;
  icon?: IconName;
  tone?: 'neutral' | 'ok' | 'bad';
  action?: { label: string; icon?: IconName; run(): void };
  /** Says why something did not work: read out at once, and it stays longer. */
  alert?: boolean;
}

type Shown = Toast & { id: number };

// One toast at a time: a new one takes the place of the one showing, so they never pile up.
let shown: Shown | null = null;
let seq = 0;
const listeners = new Set<(toast: Shown | null) => void>();

export function toast(next: Toast): void {
  shown = { ...next, id: ++seq };
  for (const listener of listeners) listener(shown);
}

/** Hides the toast showing; with an id, only when it is still that one. */
export function dismissToast(id?: number): void {
  if (!shown || (id !== undefined && shown.id !== id)) return;
  shown = null;
  for (const listener of listeners) listener(null);
}

export function useToast(): Shown | null {
  const [current, setCurrent] = useState(shown);
  useEffect(() => {
    listeners.add(setCurrent);
    return () => {
      listeners.delete(setCurrent);
    };
  }, []);
  return current;
}
