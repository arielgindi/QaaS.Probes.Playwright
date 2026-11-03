import type { ComponentChildren, JSX } from 'preact';
import { useEffect, useRef } from 'preact/hooks';
import { cls } from '../text';
import { Icon, type IconName } from './icons';

// The panel's few building blocks, so every button, key and tag looks the same wherever it is.

/**
 * The button hierarchy: primary (the one main action, solid), secondary (bordered), ghost (quiet, for the many) and
 * danger. A mode's toggle is a ghost button that, pressed, takes the mode's colour.
 */
export type Variant = 'primary' | 'secondary' | 'ghost' | 'danger';
export type Size = 'sm' | 'md';
export type Tone = 'rec' | 'warn' | 'info' | 'accent' | 'try' | 'ok' | 'inspect' | 'bad' | 'muted';

const variants: Record<Variant, string> = {
  primary: 'bg-accent text-on-accent font-semibold shadow-e1 hover:brightness-110 active:brightness-95',
  secondary: 'border border-line-strong bg-raised text-fg shadow-e1 hover:border-faint/70',
  ghost: 'text-muted hover:bg-raised hover:text-fg',
  danger: 'border border-bad/40 bg-bad/10 text-bad hover:bg-bad/16',
};

// A pressed toggle, in its mode's colour.
const pressed: Record<Tone, string> = {
  rec: 'bg-rec/14 text-rec shadow-[inset_0_0_0_1px_color-mix(in_oklab,var(--rec)_35%,transparent)]',
  warn: 'bg-warn/14 text-warn shadow-[inset_0_0_0_1px_color-mix(in_oklab,var(--warn)_35%,transparent)]',
  info: 'bg-info/14 text-info shadow-[inset_0_0_0_1px_color-mix(in_oklab,var(--info)_35%,transparent)]',
  accent: 'bg-accent/16 text-accent-fg shadow-[inset_0_0_0_1px_color-mix(in_oklab,var(--accent-fg)_35%,transparent)]',
  try: 'bg-try/14 text-try shadow-[inset_0_0_0_1px_color-mix(in_oklab,var(--try)_35%,transparent)]',
  ok: 'bg-ok/14 text-ok shadow-[inset_0_0_0_1px_color-mix(in_oklab,var(--ok)_35%,transparent)]',
  inspect: 'bg-inspect/14 text-inspect shadow-[inset_0_0_0_1px_color-mix(in_oklab,var(--inspect)_35%,transparent)]',
  bad: 'bg-bad/14 text-bad shadow-[inset_0_0_0_1px_color-mix(in_oklab,var(--bad)_35%,transparent)]',
  muted: 'bg-raised text-fg shadow-[inset_0_0_0_1px_var(--line-strong)]',
};

export const toneText: Record<Tone, string> = {
  rec: 'text-rec',
  warn: 'text-warn',
  info: 'text-info',
  accent: 'text-accent-fg',
  try: 'text-try',
  ok: 'text-ok',
  inspect: 'text-inspect',
  bad: 'text-bad',
  muted: 'text-muted',
};

export const toneBg: Record<Tone, string> = {
  rec: 'bg-rec',
  warn: 'bg-warn',
  info: 'bg-info',
  accent: 'bg-accent-fg',
  try: 'bg-try',
  ok: 'bg-ok',
  inspect: 'bg-inspect',
  bad: 'bg-bad',
  muted: 'bg-faint',
};

interface ButtonProps {
  variant?: Variant;
  size?: Size;
  /** Pressed, as a mode's toggle that is on, in its colour. */
  pressed?: boolean;
  tone?: Tone;
  icon?: IconName;
  /** Only the icon shows; the label is its accessible name and tooltip. */
  iconOnly?: boolean;
  /** The label hides below this width, leaving the icon: a narrow panel stays on one line. */
  collapse?: 'md' | 'lg';
  keys?: string;
  /** The key shows only in a wide panel, as in the top bar; else always, as in the status strip. */
  keysWide?: boolean;
  title?: string;
  label?: string;
  disabled?: boolean;
  type?: 'button' | 'submit';
  class?: string;
  autoFocus?: boolean;
  onClick?(event: MouseEvent): void;
  onKeyDown?(event: KeyboardEvent): void;
  children?: ComponentChildren;
}

/**
 * Focuses the element as it appears. A page honours `autofocus` only once, so a field or a button that appears later,
 * as each dialog's does, takes the focus this way: `ref={focusOnMount}`.
 */
export const focusOnMount = (element: HTMLElement | null) => element?.focus();

export function Button(props: ButtonProps) {
  const self = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    if (props.autoFocus) self.current?.focus();
  }, []);
  const variant = props.variant ?? 'ghost';
  const size = props.size ?? 'md';
  const square = props.iconOnly && !props.children;
  const label = props.label ?? (typeof props.children === 'string' ? props.children : undefined);
  return (
    <button
      type={props.type ?? 'button'}
      disabled={props.disabled}
      title={props.title ?? (props.iconOnly ? label : undefined)}
      aria-label={props.iconOnly || props.collapse ? (props.title ?? label) : undefined}
      aria-pressed={props.pressed}
      ref={self}
      onClick={props.onClick}
      onKeyDown={props.onKeyDown}
      class={cls(
        'inline-flex shrink-0 items-center justify-center gap-1.5 rounded-md font-medium whitespace-nowrap transition select-none disabled:pointer-events-none disabled:opacity-40',
        size === 'sm' ? 'h-6 text-meta' : 'h-8 text-body',
        square ? (size === 'sm' ? 'w-6' : 'w-8') : size === 'sm' ? 'px-2' : 'px-2.5',
        props.collapse === 'md' && 'max-[1059px]:w-8 max-[1059px]:px-0',
        props.collapse === 'lg' && 'max-[959px]:w-8 max-[959px]:px-0',
        props.pressed ? pressed[props.tone ?? 'muted'] : variants[variant],
        props.class,
      )}
    >
      {props.icon && <Icon name={props.icon} size={size === 'sm' ? 13 : 15} class="shrink-0" />}
      {!square && props.children !== undefined && (
        <span class={cls(props.collapse === 'md' && 'max-[1059px]:sr-only', props.collapse === 'lg' && 'max-[959px]:sr-only')}>{props.children}</span>
      )}
      {props.keys && (
        <Kbd class={cls(props.keysWide && 'max-[1279px]:hidden', variant === 'primary' && !props.pressed && 'border-white/25 bg-white/15 text-on-accent/90')}>{props.keys}</Kbd>
      )}
    </button>
  );
}

/** A key, as the person presses it: "F8", "Ctrl K". */
export function Kbd({ children, class: className }: { children: string; class?: string }) {
  return (
    <kbd class={cls('inline-flex h-4 min-w-4 items-center justify-center rounded-sm border border-line-strong bg-canvas px-1 text-tag leading-none font-medium text-muted', className)}>
      {children}
    </kbd>
  );
}

/** A short label beside a name: "new", "added", "tried", "auto". */
export function Tag({ tone = 'muted', title, children }: { tone?: Tone; title?: string; children: ComponentChildren }) {
  return (
    <span
      title={title}
      class={cls(
        'inline-flex h-4 shrink-0 items-center rounded-sm px-1 text-tag leading-none font-medium',
        tone === 'muted' ? 'bg-raised text-muted shadow-[inset_0_0_0_1px_var(--line)]' : cls(toneText[tone], 'bg-current/10'),
      )}
    >
      {children}
    </span>
  );
}

export function Spinner({ size = 14, class: className }: { size?: number; class?: string }) {
  return (
    <span
      aria-hidden="true"
      class={cls('inline-block shrink-0 animate-spin-slow rounded-full border-[1.5px] border-current border-t-transparent', className)}
      style={{ width: size, height: size }}
    />
  );
}

/** A mode's mark: a dot in its colour, pulsing while it records, a spinner while it works. */
export function ModeMark({ tone, busy, pulse }: { tone: Tone; busy?: boolean; pulse?: boolean }) {
  if (busy) return <Spinner size={12} class={toneText[tone]} />;
  return (
    <span class={cls('grid size-3 shrink-0 place-items-center', toneText[tone])}>
      <span class={cls('size-2 rounded-full', toneBg[tone], pulse && 'animate-rec')} />
    </span>
  );
}

/** A section's title inside a pane, with what it offers on the right. */
export function SectionTitle({ children, aside }: { children: ComponentChildren; aside?: ComponentChildren }) {
  return (
    <div class="mb-2 flex min-h-5 items-center gap-2 text-meta font-semibold text-muted">
      {children}
      {aside && <span class="ml-auto flex items-center gap-1.5 font-normal">{aside}</span>}
    </div>
  );
}

/** What a pane shows when it has nothing to show yet: an icon, a line, and what to do. */
export function EmptyState({ icon, title, children, class: className }: { icon: IconName; title: string; children?: ComponentChildren; class?: string }) {
  return (
    <div class={cls('flex flex-col items-center justify-center px-8 text-center', className)}>
      <span class="mb-3 grid size-10 place-items-center rounded-xl border border-line bg-raised text-faint">
        <Icon name={icon} size={18} />
      </span>
      <p class="text-title font-medium text-fg">{title}</p>
      {children && <div class="mt-1 max-w-[34ch] text-body text-muted">{children}</div>}
    </div>
  );
}

export function Link({ disabled, onClick, children, title }: { disabled?: boolean; onClick(): void; children: ComponentChildren; title?: string }) {
  return (
    <button
      type="button"
      disabled={disabled}
      onClick={onClick}
      title={title}
      class="rounded-sm px-1 text-meta font-medium text-accent-fg transition hover:underline disabled:opacity-50"
    >
      {children}
    </button>
  );
}

export type DivProps = JSX.HTMLAttributes<HTMLDivElement>;
