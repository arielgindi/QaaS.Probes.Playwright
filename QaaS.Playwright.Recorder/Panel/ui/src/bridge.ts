import type { Command, ElementsMessage, PanelState } from './types';

// The panel talks to the recorder (Panel/RecorderPanel.cs) through functions on the window, so it needs no server: the
// recorder calls __qaasPanelShow with each state and __qaasPanelElements with the recorded page's elements, as JSON,
// and exposes __qaasPanelSend for commands.
declare global {
  interface Window {
    __qaasPanelShow?: (json: string) => boolean;
    __qaasPanelElements?: (json: string) => boolean;
    __qaasPanelSend?: (json: string) => void;
  }
}

type Listener<T> = (value: T) => void;

let listener: Listener<PanelState> | null = null;
let latest: PanelState | null = null;
let elementsListener: Listener<ElementsMessage> | null = null;

window.__qaasPanelShow = (json) => {
  latest = JSON.parse(json) as PanelState;
  listener?.(latest);
  return true;
};

// A message that comes before the view listens, as a frozen tree may, waits for it.
let unheard: ElementsMessage | null = null;

window.__qaasPanelElements = (json) => {
  const message = JSON.parse(json) as ElementsMessage;
  if (elementsListener) elementsListener(message);
  else unheard = message;
  return true;
};

/** Calls the listener with the latest state, and with each one after it. */
export function subscribe(next: Listener<PanelState>): () => void {
  listener = next;
  if (latest) next(latest);
  return () => {
    if (listener === next) listener = null;
  };
}

/** Calls the listener with each message about the recorded page's elements. */
export function subscribeElements(next: Listener<ElementsMessage>): () => void {
  elementsListener = next;
  if (unheard) next(unheard);
  unheard = null;
  return () => {
    if (elementsListener === next) elementsListener = null;
  };
}

export function send(command: Command): void {
  window.__qaasPanelSend?.(JSON.stringify(command));
}

/** What to show in the recorded page: a step's element, or one of its candidates, an element of the Elements view, or
 * a candidate of the element picked there. */
export type Highlight = { action: string; candidate?: number } | { node: number; frozen: boolean } | { candidate: number };

let shown = '';
let timer = 0;

/**
 * Highlights it in the recorded page; null hides it. A pointer passing over the panel does not flash the page: a
 * highlight waits a moment, and hiding a little longer.
 */
export function highlight(what: Highlight | null): void {
  const key = what === null ? '' : JSON.stringify(what);
  window.clearTimeout(timer);
  if (key === shown) return;
  timer = window.setTimeout(
    () => {
      shown = key;
      send(what === null ? { type: 'unhighlight' } : ({ type: 'highlight', ...what } as Command));
    },
    what === null ? 160 : 70,
  );
}

let flashTimer = 0;

/** Highlights an action's element for a moment, as when the person moves to it with the keyboard. */
export function flash(action: string): void {
  highlight({ action });
  window.clearTimeout(flashTimer);
  flashTimer = window.setTimeout(() => {
    if (shown === JSON.stringify({ action })) highlight(null);
  }, 1600);
}
