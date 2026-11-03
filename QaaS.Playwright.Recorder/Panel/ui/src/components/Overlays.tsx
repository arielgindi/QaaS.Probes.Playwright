import type { ComponentChildren } from 'preact';
import { useEffect, useMemo, useRef, useState } from 'preact/hooks';
import { cls } from '../text';
import { dismissToast, useToast } from '../toast';
import { Icon, type IconName } from './icons';
import { Button, focusOnMount, Kbd } from './ui';

// What floats over the panel: the toast, the command palette (Ctrl+K) and the sheet of keys (?).

/** A command of the panel, as the palette lists it. */
export interface Command {
  id: string;
  label: string;
  group: string;
  icon: IconName;
  keys?: string;
  /** Why it cannot run now; it is listed dimmed. */
  off?: string | false;
  run(): void;
}

export function ToastView() {
  const shown = useToast();
  const [paused, setPaused] = useState(false);
  useEffect(() => {
    if (!shown || paused) return;
    const timer = window.setTimeout(() => dismissToast(shown.id), shown.alert ? 9000 : 6000);
    return () => window.clearTimeout(timer);
  }, [shown?.id, paused]);
  if (!shown) return null;
  const tone = shown.tone === 'bad' || shown.alert ? 'text-bad' : shown.tone === 'ok' ? 'text-ok' : 'text-muted';
  return (
    <div class="pointer-events-none fixed inset-x-0 bottom-10 z-40 flex justify-center px-4">
      <div
        key={shown.id}
        role={shown.alert ? 'alert' : 'status'}
        onMouseEnter={() => setPaused(true)}
        onMouseLeave={() => setPaused(false)}
        class="animate-pop-in pointer-events-auto flex max-w-full min-w-0 items-center gap-2.5 rounded-xl border border-line-strong bg-raised py-1.5 pr-1.5 pl-3 text-body text-fg shadow-e2"
      >
        <Icon name={shown.icon ?? (shown.alert ? 'warn' : 'tick')} size={14} class={cls('shrink-0', tone)} />
        <span dir="auto" class="max-w-[56ch] min-w-0 truncate" title={shown.text}>
          {shown.text}
        </span>
        {shown.action && (
          <Button
            size="sm"
            variant="secondary"
            icon={shown.action.icon}
            onClick={() => {
              shown.action!.run();
              dismissToast(shown.id);
            }}
          >
            {shown.action.label}
          </Button>
        )}
        <Button size="sm" iconOnly icon="cross" label="Dismiss" onClick={() => dismissToast(shown.id)} />
      </div>
    </div>
  );
}

// A dialog over the panel, which Esc or a click outside closes, and which keeps the keys to itself.
function Modal({ label, onClose, children, class: className }: { label: string; onClose(): void; children: ComponentChildren; class?: string }) {
  const box = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const was = document.activeElement as HTMLElement | null;
    // Esc closes it, wherever the focus is.
    const onKey = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return;
      event.preventDefault();
      event.stopPropagation();
      onClose();
    };
    addEventListener('keydown', onKey, true);
    return () => {
      removeEventListener('keydown', onKey, true);
      was?.focus?.();
    };
  }, []);
  return (
    <div class="animate-fade-in fixed inset-0 z-50 flex items-start justify-center bg-black/35 px-4 pt-[12vh] backdrop-blur-[1px]" onMouseDown={(event) => event.target === event.currentTarget && onClose()}>
      <div
        ref={box}
        role="dialog"
        aria-modal="true"
        aria-label={label}
        data-own-keys
        onKeyDown={(event) => {
          if (event.key === 'Escape') {
            event.preventDefault();
            event.stopPropagation();
            onClose();
          }
          event.stopPropagation();
        }}
        class={cls('animate-pop-in flex max-h-[70vh] w-full flex-col overflow-hidden rounded-xl border border-line-strong bg-panel shadow-e3', className)}
      >
        {children}
      </div>
    </div>
  );
}

/** Every command, found by typing, with its key; Enter runs the one marked. */
export function Palette({ commands, onClose }: { commands: Command[]; onClose(): void }) {
  const [query, setQuery] = useState('');
  const [at, setAt] = useState(0);
  const list = useRef<HTMLDivElement>(null);
  const found = useMemo(() => {
    const words = query.toLowerCase().split(/\s+/).filter(Boolean);
    const matching = commands.filter((command) => words.every((word) => `${command.label} ${command.group} ${command.keys ?? ''}`.toLowerCase().includes(word)));
    // What can run now first, in its group's order.
    return [...matching.filter((command) => !command.off), ...matching.filter((command) => command.off)];
  }, [query, commands]);
  useEffect(() => setAt(0), [query]);
  useEffect(() => {
    list.current?.querySelector('[aria-selected="true"]')?.scrollIntoView({ block: 'nearest' });
  }, [at]);
  const run = (command?: Command) => {
    if (!command || command.off) return;
    onClose();
    // Once the palette is gone, so the command's own focus and keys go where they belong.
    window.setTimeout(command.run, 0);
  };
  return (
    <Modal label="Commands" onClose={onClose} class="max-w-[520px]">
      <div class="flex items-center gap-2.5 border-b border-line px-3.5">
        <Icon name="search" size={15} class="shrink-0 text-faint" />
        <input
          ref={focusOnMount}
          value={query}
          spellcheck={false}
          aria-label="Find a command"
          aria-controls="palette-list"
          aria-activedescendant={found[at] ? `command-${found[at].id}` : undefined}
          placeholder="Type a command…"
          onInput={(event) => setQuery(event.currentTarget.value)}
          onKeyDown={(event) => {
            if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
              event.preventDefault();
              setAt((was) => Math.max(0, Math.min(found.length - 1, was + (event.key === 'ArrowDown' ? 1 : -1))));
            } else if (event.key === 'Enter') {
              event.preventDefault();
              run(found[at]);
            }
          }}
          class="h-11 min-w-0 flex-1 bg-transparent text-title text-fg outline-none placeholder:text-faint focus-visible:outline-none"
        />
        <Kbd>Esc</Kbd>
      </div>
      <div ref={list} id="palette-list" role="listbox" aria-label="Commands" class="min-h-0 flex-1 overflow-y-auto p-1.5">
        {found.length === 0 && <p class="px-3 py-6 text-center text-body text-muted">No command matches “{query}”.</p>}
        {found.map((command, index) => (
          <div
            key={command.id}
            id={`command-${command.id}`}
            role="option"
            aria-selected={index === at}
            aria-disabled={!!command.off}
            title={command.off || undefined}
            onMouseMove={() => setAt(index)}
            onClick={() => run(command)}
            class={cls(
              'flex h-9 cursor-pointer items-center gap-2.5 rounded-md px-2.5 text-body',
              index === at && 'bg-raised',
              command.off ? 'cursor-default text-faint' : 'text-fg',
            )}
          >
            <Icon name={command.icon} size={15} class={cls('shrink-0', command.off ? 'text-faint' : 'text-muted')} />
            <span class="min-w-0 flex-1 truncate">{command.label}</span>
            {command.off && index === at && <span class="min-w-0 shrink truncate text-meta text-faint">{command.off}</span>}
            <span class="shrink-0 text-meta text-faint">{command.group}</span>
            {command.keys && <Kbd>{command.keys}</Kbd>}
          </div>
        ))}
      </div>
    </Modal>
  );
}

const sheet: [string, [string, string][]][] = [
  [
    'In the app',
    [
      ['F8', 'Pause or resume'],
      ['F6', 'Check something: a click picks it'],
      ['F7', 'Try: nothing is recorded'],
      ['Ctrl Shift C', 'Pick an element for Elements'],
      ['F4', 'Freeze the elements, e.g. a menu'],
      ['Alt ↑ / Alt ↓', 'Pick the element around, or back'],
      ['F9', 'Hide or show the overlay'],
      ['F2', 'Done, while re-recording'],
      ['Esc', 'Leave check or inspect mode'],
    ],
  ],
  [
    'In the panel',
    [
      ['Ctrl K', 'All commands'],
      ['Ctrl ↵  or  F5', 'Test the flow'],
      ['Ctrl Shift ↵', 'Run to the selected step'],
      ['Esc  or  Shift F5', 'Stop the test'],
      ['↑ / ↓', 'Select the step before or after'],
      ['Delete', 'Delete the selected step'],
      ['Ctrl Z', 'Undo'],
      ['Ctrl S', 'Save'],
      ['?', 'These keys'],
    ],
  ],
  [
    'In the Elements tree',
    [
      ['↑ ↓ ← →', 'Move, open, close'],
      ['↵', 'Make the step marked ↵'],
      ['A letter', 'Search'],
    ],
  ],
];

/** The keys, in the app and in the panel. */
export function KeysSheet({ onClose }: { onClose(): void }) {
  return (
    <Modal label="Keyboard shortcuts" onClose={onClose} class="max-w-[640px]">
      <div class="flex items-center border-b border-line px-4 py-3">
        <h2 class="text-title font-semibold text-fg">Keyboard shortcuts</h2>
        <Button size="sm" iconOnly icon="cross" label="Close" onClick={onClose} autoFocus class="ml-auto" />
      </div>
      <div class="grid min-h-0 gap-x-6 gap-y-4 overflow-y-auto p-4 sm:grid-cols-2">
        {sheet.map(([title, keys]) => (
          <section key={title} aria-label={title}>
            <h3 class="mb-1.5 text-meta font-semibold text-muted">{title}</h3>
            <dl class="space-y-1">
              {keys.map(([key, does]) => (
                <div key={key} class="flex items-center gap-3 text-body">
                  <dt class="w-[118px] shrink-0">
                    <Kbd>{key}</Kbd>
                  </dt>
                  <dd class="min-w-0 text-fg/90">{does}</dd>
                </div>
              ))}
            </dl>
          </section>
        ))}
      </div>
    </Modal>
  );
}

/**
 * A handle between two panes, dragged to resize them, or moved with the arrow keys once focused. `value` is the
 * size it controls, in pixels or as a share; `onChange` hears each new one, kept within `min` and `max`.
 */
export function Splitter(props: {
  label: string;
  orientation: 'vertical' | 'horizontal';
  value: number;
  min: number;
  max: number;
  /** The size grows as the pointer moves right (or down), else as it moves left. */
  grows: 1 | -1;
  step?: number;
  class?: string;
  onChange(value: number): void;
  onDone(value: number): void;
}) {
  const drag = useRef<{ from: number; value: number; last: number } | null>(null);
  const clamp = (value: number) => Math.round(Math.max(props.min, Math.min(props.max, value)));
  const vertical = props.orientation === 'vertical';
  return (
    <div class={cls('relative z-10', props.class)}>
      <div
        role="separator"
        aria-label={props.label}
        aria-orientation={props.orientation}
        aria-valuenow={Math.round(props.value)}
        aria-valuemin={props.min}
        aria-valuemax={props.max}
        tabIndex={0}
        onPointerDown={(event) => {
          event.preventDefault();
          event.currentTarget.setPointerCapture(event.pointerId);
          drag.current = { from: vertical ? event.clientX : event.clientY, value: props.value, last: props.value };
        }}
        onPointerMove={(event) => {
          if (!drag.current) return;
          const moved = (vertical ? event.clientX : event.clientY) - drag.current.from;
          drag.current.last = clamp(drag.current.value + moved * props.grows);
          props.onChange(drag.current.last);
        }}
        onPointerUp={() => {
          if (drag.current) props.onDone(drag.current.last);
          drag.current = null;
        }}
        onDblClick={() => props.onDone(-1)}
        onKeyDown={(event) => {
          const keys = vertical ? ['ArrowLeft', 'ArrowRight'] : ['ArrowUp', 'ArrowDown'];
          if (!keys.includes(event.key)) return;
          event.preventDefault();
          event.stopPropagation();
          const next = clamp(props.value + (props.step ?? 16) * (event.key === keys[1] ? 1 : -1) * props.grows);
          props.onChange(next);
          props.onDone(next);
        }}
        title="Drag to resize; double-click for the default size"
        class={cls(
          'group absolute flex items-center justify-center outline-none',
          vertical ? 'inset-y-0 -left-[3px] w-1.5 cursor-col-resize' : 'inset-x-0 -top-[3px] h-1.5 cursor-row-resize',
        )}
      >
        <span
          class={cls(
            'rounded-full bg-accent-fg opacity-0 transition-opacity group-hover:opacity-60 group-focus-visible:opacity-100 group-active:opacity-100',
            vertical ? 'h-full w-0.5' : 'h-0.5 w-full',
          )}
        />
      </div>
    </div>
  );
}
