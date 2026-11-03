import type { ComponentChildren } from 'preact';

/** A pane's title bar: its name, how many it holds, and what it offers on the right. */
export function PaneHeader({ title, count, children }: { title: ComponentChildren; count?: number; children?: ComponentChildren }) {
  return (
    <div class="flex h-9 shrink-0 items-center gap-2 border-b border-line px-3 text-meta font-semibold text-muted">
      <span class="flex min-w-0 items-center gap-1.5 truncate">{title}</span>
      {count !== undefined && <span class="rounded-sm bg-raised px-1 text-tag leading-4 font-medium text-muted tabular-nums">{count}</span>}
      <span class="ml-auto flex min-w-0 items-center gap-2 font-normal">{children}</span>
    </div>
  );
}
