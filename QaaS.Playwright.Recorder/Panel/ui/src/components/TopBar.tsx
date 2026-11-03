import { useEffect, useRef, useState } from 'preact/hooks';
import type { Theme } from '../theme';
import { cls } from '../text';
import type { Status } from '../types';
import { Icon, type IconName } from './icons';
import { Button } from './ui';

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
  onTry(): void;
  onSave(): void;
  onPause(): void;
  onView(view: View): void;
  onTheme(): void;
  onPalette(): void;
  onKeys(): void;
}

/**
 * The flow's name, what the middle shows, and the commands: the modes a key away (Pause, Check, Try), Undo, Test and
 * Save. A narrow panel keeps their icons, and every command is also in the palette (Ctrl+K).
 */
export function TopBar(props: Props) {
  const saving = props.status === 'saving';
  const paused = props.status === 'paused';
  const trying = props.status === 'trying';
  return (
    <header class="flex h-12 shrink-0 items-center gap-2 border-b border-line bg-panel pr-2 pl-3">
      <FlowName flow={props.flow} stamp={props.stamp} onRename={props.onRename} />
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
      <div class="ml-auto flex shrink-0 items-center gap-0.5">
        <Button
          icon={paused ? 'play' : 'pause'}
          collapse="lg"
          keys="F8"
          keysWide
          pressed={paused}
          tone="warn"
          disabled={saving || props.status === 'login' || props.status === 'testing' || trying}
          title={paused ? 'Resume recording (F8)' : 'Pause recording: what you do in the app is not recorded (F8)'}
          onClick={props.onPause}
        >
          {paused ? 'Resume' : 'Pause'}
        </Button>
        <Button
          icon="checkCircle"
          collapse="lg"
          keys="F6"
          keysWide
          pressed={props.checking}
          tone="ok"
          disabled={saving || (props.status !== 'recording' && !props.checking)}
          title={props.checking ? 'Leave check mode (F6, or Esc in the app)' : 'Check something in the app: the next click there picks an element, then you choose the check (F6)'}
          onClick={props.onCheck}
        >
          Check
        </Button>
        <Button
          icon="flask"
          collapse="lg"
          keys="F7"
          keysWide
          pressed={trying}
          tone="try"
          disabled={saving || !(props.status === 'recording' || paused || trying)}
          title={trying ? 'Stop trying, and record again (F7)' : 'Try things in the app: nothing is recorded, and each action shows how it would be written (F7)'}
          onClick={props.onTry}
        >
          Try
        </Button>
        <Divider />
        <Button
          icon="undo"
          collapse="md"
          disabled={!props.undo || saving}
          title={props.undo ? `Undo: ${props.undo} (Ctrl+Z). The app stays as it is.` : 'Nothing to undo'}
          onClick={props.onUndo}
        >
          Undo
        </Button>
        <Divider />
        <TestButton testing={props.testing} disabled={!props.canTest} slow={props.slow} onTest={props.onTest} onStop={props.onStopTest} onSlow={props.onSlow} />
        <Button
          variant="primary"
          disabled={saving || !props.canSave}
          title="End the recording and save the flow (Ctrl+S)"
          keys={saving ? undefined : 'Ctrl S'}
          keysWide
          onClick={props.onSave}
          class="ml-1.5"
        >
          {saving ? 'Saving…' : 'Save'}
        </Button>
        <Divider />
        <Button iconOnly icon="command" label="Commands (Ctrl+K)" onClick={props.onPalette} />
        <Button iconOnly icon="keyboard" label="Keyboard shortcuts (?)" onClick={props.onKeys} class="max-[719px]:hidden" />
        <Button iconOnly icon={props.theme === 'dark' ? 'sun' : 'moon'} label={props.theme === 'dark' ? 'Light theme' : 'Dark theme'} onClick={props.onTheme} class="max-[719px]:hidden" />
      </div>
    </header>
  );
}

function Divider() {
  return <span aria-hidden="true" class="mx-1 h-5 w-px shrink-0 bg-line max-[639px]:mx-0.5" />;
}

// Test, which turns into Stop while a test runs, with slow motion beside it.
function TestButton({ testing, disabled, slow, onTest, onStop, onSlow }: { testing: boolean; disabled: boolean; slow: boolean; onTest(): void; onStop(): void; onSlow(): void }) {
  return (
    <span class="flex shrink-0 items-center rounded-md border border-line-strong bg-raised shadow-e1">
      {testing ? (
        <button
          type="button"
          onClick={onStop}
          title="Stop the test (Esc or Shift+F5)"
          aria-label="Stop the test"
          class="inline-flex h-[30px] items-center gap-1.5 rounded-l-md px-2.5 text-body font-medium text-bad transition hover:bg-bad/10 max-[959px]:px-2"
        >
          <Icon name="stop" size={12} fill="currentColor" />
          <span class="max-[959px]:sr-only">Stop</span>
        </button>
      ) : (
        <button
          type="button"
          onClick={onTest}
          disabled={disabled}
          title="Test the flow: run it as it will be saved, in a window of its own, and see each step pass or fail (Ctrl+Enter or F5)"
          aria-label="Test the flow"
          class="inline-flex h-[30px] items-center gap-1.5 rounded-l-md pr-2 pl-2.5 text-body font-medium text-fg transition hover:bg-line/60 disabled:pointer-events-none disabled:opacity-40 max-[959px]:px-2"
        >
          <Icon name="play" size={12} fill="currentColor" class="text-ok" />
          <span class="max-[959px]:sr-only">Test</span>
          <kbd class="inline-flex h-4 items-center rounded-sm border border-line-strong bg-canvas px-1 text-tag leading-none font-medium text-muted max-[1279px]:hidden">Ctrl ↵</kbd>
        </button>
      )}
      <span aria-hidden="true" class="h-4 w-px bg-line-strong" />
      <button
        type="button"
        onClick={onSlow}
        aria-pressed={slow}
        title={slow ? 'Slow motion is on: a test waits 500 ms before each action. Click to test at full speed.' : 'Slow motion: a test waits 500 ms before each action, to watch it'}
        aria-label="Slow motion"
        class={cls('grid h-[30px] w-7 place-items-center rounded-r-md transition hover:bg-line/60', slow ? 'text-accent-fg' : 'text-faint hover:text-fg')}
      >
        <Icon name="gauge" size={14} />
      </button>
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
      class="group -ml-1 flex min-w-[9ch] shrink items-center rounded-md border border-transparent px-1.5 py-1 transition focus-within:border-line-strong focus-within:bg-raised hover:bg-raised"
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
        class="min-w-[4ch] max-w-[30ch] flex-1 truncate bg-transparent text-title font-semibold tracking-[-0.01em] text-fg outline-none [field-sizing:content] focus-visible:outline-none"
      />
      <span class="shrink-0 pl-px text-title text-faint max-[639px]:hidden">.cs</span>
      <Icon name="pencil" size={12} class="ml-1.5 shrink-0 text-faint opacity-0 transition group-hover:opacity-100 group-focus-within:opacity-0 max-[639px]:hidden" />
    </label>
  );
}

function Segmented<T extends string>(props: { label: string; value: T; options: [T, string, IconName][]; onChange(value: T): void }) {
  return (
    <div role="radiogroup" aria-label={props.label} class="ml-1 flex h-8 shrink-0 items-center gap-px rounded-md border border-line bg-canvas p-0.5">
      {props.options.map(([value, text, icon]) => (
        <button
          type="button"
          role="radio"
          aria-checked={props.value === value}
          aria-label={text}
          title={text}
          onClick={() => props.onChange(value)}
          class={cls(
            'inline-flex h-full items-center gap-1.5 rounded-[5px] px-2.5 text-meta font-medium transition max-[999px]:px-2',
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
