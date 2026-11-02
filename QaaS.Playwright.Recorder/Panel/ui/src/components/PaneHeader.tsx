import type { ComponentChildren } from 'preact';

/** A pane's title bar: its name, and what it shows on the right. */
export function PaneHeader({ title, children }: { title: ComponentChildren; children?: ComponentChildren }) {
  return (
    <div class="flex h-9 shrink-0 items-center gap-2 border-b border-line px-4 text-[11px] font-medium tracking-[0.06em] text-faint uppercase">
      {title}
      <span class="ml-auto flex items-center gap-2 tracking-normal normal-case">{children}</span>
    </div>
  );
}
