import type { ComponentChildren } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';
import type { Theme } from '../theme';
import { cls } from '../text';
import type { Status } from '../types';
import { Icon, type IconName } from './icons';

export type View = 'code' | 'yaml' | 'elements';

interface Props {
  flow: string;
  /** Changes with each state, so the field shows the name the recorder took. */
  stamp: object;
  status: Status;
  canSave: boolean;
  /** What Undo takes back; none when there is nothing. */
  undo?: string;
  /** The recorded page is in check mode. */
  checking: boolean;
  view: View;
  theme: Theme;
  /** A test runs. */
  testing: boolean;
  canTest: boolean;
  slow: boolean;
  onTest(): void;
  onStopTest(): void;
  onSlow(): void;
  onRename(name: string): void;
  onUndo(): void;
  onCheck(): void;
  onSave(): void;
  onPause(): void;
  onView(view: View): void;
  onTheme(): void;
}

export function TopBar(props: Props) {
  const saving = props.status === 'saving';
  const paused = props.status === 'paused';
  return (
    <header class="flex h-12 shrink-0 items-center gap-2 border-b border-line bg-panel pr-2.5 pl-3">
      <Status status={props.status} />
      <span class="mx-1 h-5 w-px shrink-0 bg-line" />
      <FlowName flow={props.flow} stamp={props.stamp} onRename={props.onRename} />
      <div class="ml-auto flex shrink-0 items-center gap-1">
        <Segmented
          label="Show"
          value={props.view}
          options={[
            ['code', 'Code', 'file'],
            ['yaml', 'YAML', 'list'],
            ['elements', 'Elements', 'elements'],
          ]}
          onChange={props.onView}
        />
        <span class="mx-1.5 h-5 w-px bg-line" />
        <TestButton testing={props.testing} disabled={!props.canTest} onTest={props.onTest} onStop={props.onStopTest} />
        <Button
          onClick={props.onSlow}
          title={props.slow ? 'Slow motion is on: a test waits 500 ms before each action. Click to test at full speed.' : 'Slow motion: a test waits 500 ms before each action, to watch it'}
          active={props.slow}
          tone="accent"
          square
        >
          <Icon name="gauge" size={15} />
        </Button>
        <span class="mx-1.5 h-5 w-px bg-line" />
        <Button
          onClick={props.onCheck}
          disabled={saving || (props.status !== 'recording' && !props.checking)}
          title={props.checking
            ? 'Leave check mode (F6, or Esc in the app)'
            : 'Check something in the app: the next click there picks an element, then you choose what the flow checks of it (F6)'}
          active={props.checking}
          tone="ok"
        >
          <Icon name="checkCircle" size={15} />
          <span class="max-[820px]:hidden">Check</span>
          <kbd class={cls('rounded border px-1 font-sans text-[10.5px] leading-4 font-medium max-[1180px]:hidden', props.checking ? 'border-ok/30 text-ok/80' : 'border-line bg-canvas text-faint')}>F6</kbd>
        </Button>
        <Button
          onClick={props.onPause}
          disabled={saving || props.status === 'login' || props.status === 'testing'}
          title={paused ? 'Resume recording (F8 in the app)' : 'Pause recording: what you do in the app is not recorded (F8 in the app)'}
          active={paused}
        >
          <Icon name={paused ? 'play' : 'pause'} size={15} />
          <span class="max-[820px]:hidden">{paused ? 'Resume' : 'Pause'}</span>
        </Button>
        <Button onClick={props.onUndo} disabled={!props.undo || saving} title={props.undo ? `Undo: ${props.undo} (Ctrl+Z). The app stays as it is.` : 'Nothing to undo'}>
          <Icon name="undo" size={15} />
          <span class="max-[820px]:hidden">Undo</span>
        </Button>
        <Button onClick={props.onTheme} title={props.theme === 'dark' ? 'Light theme' : 'Dark theme'} square>
          <Icon name={props.theme === 'dark' ? 'sun' : 'moon'} size={15} />
        </Button>
        <button
          type="button"
          onClick={props.onSave}
          disabled={saving || !props.canSave}
          title="End the recording and save the flow (Ctrl+S)"
          class="ml-1 inline-flex h-8 items-center gap-2 rounded-lg bg-accent px-3 text-[12.5px] font-semibold text-on-accent shadow-[0_1px_0_0_rgb(255_255_255/0.12)_inset,0_1px_2px_rgb(0_0_0/0.2)] transition hover:brightness-110 active:brightness-95 disabled:opacity-50 disabled:hover:brightness-100"
        >
          {saving && <span class="size-3 animate-spin-slow rounded-full border-[1.5px] border-on-accent border-t-transparent" />}
          <span>{saving ? 'Saving' : 'Save'}</span>
          {!saving && <kbd class="rounded bg-white/18 px-1.5 font-sans text-[10.5px] leading-[18px] font-medium text-on-accent/90 max-[820px]:hidden">Ctrl S</kbd>}
        </button>
      </div>
    </header>
  );
}

const statuses: Record<Status, { text: string; dot: string }> = {
  recording: { text: 'Recording', dot: 'bg-rec animate-rec' },
  paused: { text: 'Paused', dot: 'bg-warn' },
  login: { text: 'Logging in', dot: 'bg-info animate-pulse-soft' },
  testing: { text: 'Testing', dot: 'bg-accent animate-pulse-soft' },
  saving: { text: 'Saving', dot: 'bg-accent' },
};

// Test, which turns into Stop while a test runs.
function TestButton({ testing, disabled, onTest, onStop }: { testing: boolean; disabled: boolean; onTest(): void; onStop(): void }) {
  const base =
    'inline-flex h-8 shrink-0 items-center justify-center gap-1.5 rounded-lg border text-[12.5px] font-medium transition max-[820px]:w-8 max-[820px]:px-0 disabled:pointer-events-none disabled:opacity-40';
  if (testing)
    return (
      <button type="button" onClick={onStop} title="Stop the test (Esc or Shift+F5)" aria-label="Stop the test" class={cls(base, 'border-bad/40 bg-bad/10 px-2.5 text-bad hover:bg-bad/16')}>
        <Icon name="stop" size={12} fill="currentColor" />
        <span class="max-[820px]:hidden">Stop</span>
      </button>
    );
  return (
    <button
      type="button"
      onClick={onTest}
      disabled={disabled}
      title="Test the flow: run it as it will be saved, in a window of its own, and see each step pass or fail (Ctrl+Enter or F5)"
      aria-label="Test the flow"
      class={cls(base, 'border-line-strong bg-raised pr-2 pl-2.5 text-fg shadow-[0_1px_2px_rgb(0_0_0/0.1)] hover:border-faint/60 hover:brightness-[1.04]')}
    >
      <Icon name="play" size={13} fill="currentColor" class="text-ok" />
      <span class="max-[820px]:hidden">Test</span>
      <kbd class="rounded border border-line bg-canvas px-1 font-sans text-[10.5px] leading-4 font-medium text-faint max-[1180px]:hidden">Ctrl ↵</kbd>
    </button>
  );
}

function Status({ status }: { status: Status }) {
  const { text, dot } = statuses[status];
  return (
    <span role="status" class="flex shrink-0 items-center gap-2 rounded-full py-1 pr-2.5 pl-2 text-[12px] font-medium text-muted">
      <span class={cls('size-2 rounded-full', dot, status === 'paused' && 'rounded-[2px]')} />
      <span class="max-[560px]:sr-only">{text}</span>
    </span>
  );
}

// The flow's class name, edited in place; Enter or leaving the field renames it, Escape keeps the name.
function FlowName({ flow, stamp, onRename }: { flow: string; stamp: object; onRename(name: string): void }) {
  const [draft, setDraft] = useState(flow);
  const input = useRef<HTMLInputElement>(null);
  useEffect(() => {
    if (document.activeElement !== input.current) setDraft(flow);
  }, [flow, stamp]);

  const commit = () => {
    const name = draft.trim();
    if (name && name !== flow) onRename(name);
    else setDraft(flow);
  };

  return (
    <label
      title="Rename the flow"
      class="group flex min-w-0 items-center rounded-md border border-transparent px-1.5 py-1 transition focus-within:border-line-strong focus-within:bg-raised hover:bg-raised"
    >
      <span class="sr-only">Flow name</span>
      <input
        ref={input}
        value={draft}
        spellcheck={false}
        onInput={(event) => setDraft(event.currentTarget.value)}
        onBlur={commit}
        onKeyDown={(event) => {
          if (event.key === 'Enter') event.currentTarget.blur();
          if (event.key === 'Escape') {
            setDraft(flow);
            requestAnimationFrame(() => input.current?.blur());
          }
        }}
        class="min-w-[4ch] max-w-[30ch] truncate bg-transparent text-[14px] font-semibold tracking-[-0.01em] text-fg outline-none [field-sizing:content] focus-visible:outline-none"
      />
      <span class="pl-px text-[14px] text-faint">.cs</span>
      <Icon name="pencil" size={13} class="ml-2 shrink-0 text-faint opacity-0 transition group-hover:opacity-100 group-focus-within:opacity-0" />
    </label>
  );
}

function Segmented<T extends string>(props: { label: string; value: T; options: [T, string, IconName][]; onChange(value: T): void }) {
  return (
    <div role="radiogroup" aria-label={props.label} class="flex h-8 items-center gap-px rounded-lg border border-line bg-canvas p-0.5">
      {props.options.map(([value, text, icon]) => (
        <button
          type="button"
          role="radio"
          aria-checked={props.value === value}
          title={text}
          onClick={() => props.onChange(value)}
          class={cls(
            'inline-flex h-full items-center gap-1.5 rounded-md px-2.5 text-[12px] font-medium transition',
            props.value === value ? 'bg-raised text-fg shadow-[0_0_0_1px_var(--line-strong),0_1px_2px_rgb(0_0_0/0.12)]' : 'text-muted hover:text-fg',
          )}
        >
          <Icon name={icon} size={13} class={cls('min-[1000px]:hidden', props.value === value ? 'text-fg' : 'text-faint')} />
          <span class="max-[999px]:sr-only">{text}</span>
        </button>
      ))}
    </div>
  );
}

function Button(props: {
  onClick(): void;
  disabled?: boolean;
  title: string;
  square?: boolean;
  active?: boolean;
  tone?: 'warn' | 'accent' | 'ok';
  children: ComponentChildren;
}) {
  return (
    <button
      type="button"
      onClick={props.onClick}
      disabled={props.disabled}
      title={props.title}
      aria-label={props.title}
      aria-pressed={props.active}
      class={cls(
        'inline-flex h-8 shrink-0 items-center justify-center gap-1.5 rounded-lg text-[12.5px] font-medium transition disabled:pointer-events-none disabled:opacity-40',
        props.active
          ? props.tone === 'accent'
            ? 'bg-accent/14 text-accent-fg hover:bg-accent/20'
            : props.tone === 'ok'
              ? 'bg-ok/14 text-ok shadow-[inset_0_0_0_1px_color-mix(in_oklab,var(--ok)_30%,transparent)] hover:bg-ok/20'
              : 'bg-warn/14 text-warn hover:bg-warn/20'
          : 'text-muted hover:bg-raised hover:text-fg',
        props.square ? 'w-8' : 'px-2.5 max-[820px]:w-8 max-[820px]:px-0',
      )}
    >
      {props.children}
    </button>
  );
}
