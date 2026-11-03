import { useEffect, useState } from 'preact/hooks';
import { usePref } from './prefs';
import type { Action, TestRun } from './types';

/** Whether the run is going on: getting ready for its first step, or running it. */
export function isGoing(test?: TestRun): boolean {
  return !!test && (isPreparing(test) || test.state === 'running');
}

/** Whether the run is getting ready for its first step: building the flow, opening its window or page, logging in. */
export function isPreparing(test?: TestRun): boolean {
  return !!test && (test.state === 'building' || test.state === 'window' || test.state === 'start' || test.state === 'login');
}

/** A duration as a person reads it: 840 ms, 2.4 s, 31 s, 1 min 5 s. */
export function duration(ms: number): string {
  if (ms < 1000) return `${Math.max(0, Math.round(ms))} ms`;
  if (ms < 10_000) return `${(ms / 1000).toFixed(1)} s`;
  if (ms < 60_000) return `${Math.floor(ms / 1000)} s`;
  return `${Math.floor(ms / 60_000)} min ${Math.floor((ms % 60_000) / 1000)} s`;
}

/** The time now, again every so often while `on`, for a clock that ticks. */
export function useNow(on: boolean, every = 250): number {
  const [now, setNow] = useState(Date.now);
  useEffect(() => {
    if (!on) return;
    setNow(Date.now());
    const timer = window.setInterval(() => setNow(Date.now()), every);
    return () => window.clearInterval(timer);
  }, [on, every]);
  return now;
}

/** The action's number in the run, from 1, as the run goes through the flow's actions in order; 0 when not in it. */
export function numberOf(actions: Action[], id?: string): number {
  return id ? actions.findIndex((action) => action.id === id) + 1 : 0;
}

/** Whether a test runs in slow motion, as the person last chose. */
export function useSlowMotion(): [boolean, () => void] {
  const [slow, setSlow] = usePref('slowMotion', 'off');
  return [slow === 'on', () => setSlow(slow === 'on' ? 'off' : 'on')];
}
