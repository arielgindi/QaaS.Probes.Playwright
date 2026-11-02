import type { Command, PanelState } from './types';

// The panel talks to the recorder (Panel/RecorderPanel.cs) through two functions on the window, so it needs no
// server: the recorder calls __qaasPanelShow with each state as JSON, and exposes __qaasPanelSend for commands.
declare global {
  interface Window {
    __qaasPanelShow?: (json: string) => boolean;
    __qaasPanelSend?: (json: string) => void;
  }
}

type Listener = (state: PanelState) => void;

let listener: Listener | null = null;
let latest: PanelState | null = null;

window.__qaasPanelShow = (json) => {
  latest = JSON.parse(json) as PanelState;
  listener?.(latest);
  return true;
};

/** Calls the listener with the latest state, and with each one after it. */
export function subscribe(next: Listener): () => void {
  listener = next;
  if (latest) next(latest);
  return () => {
    if (listener === next) listener = null;
  };
}

export function send(command: Command): void {
  window.__qaasPanelSend?.(JSON.stringify(command));
}

let shown = '';
let timer = 0;

/**
 * Highlights in the recorded page the element of an action, or what one of its candidates finds; null hides it. A
 * pointer passing over the panel does not flash the page: a highlight waits a moment, and hiding a little longer.
 */
export function highlight(action: string | null, candidate?: number): void {
  const key = action === null ? '' : `${action}:${candidate ?? ''}`;
  window.clearTimeout(timer);
  if (key === shown) return;
  timer = window.setTimeout(
    () => {
      shown = key;
      send(action === null ? { type: 'unhighlight' } : { type: 'highlight', action, candidate });
    },
    action === null ? 160 : 70,
  );
}

let flashTimer = 0;

/** Highlights an action's element for a moment, as when the person moves to it with the keyboard. */
export function flash(action: string): void {
  highlight(action);
  window.clearTimeout(flashTimer);
  flashTimer = window.setTimeout(() => {
    if (shown === `${action}:`) highlight(null);
  }, 1600);
}
