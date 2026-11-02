import { useEffect, useMemo, useRef, useState } from 'preact/hooks';
import { tokenizeCSharp } from '../syntax/csharp';
import type { Token } from '../syntax/token';
import { tokenizeYaml } from '../syntax/yaml';
import { cls, plural } from '../text';
import type { Action, Check, Lines } from '../types';
import { Icon } from './icons';
import type { View } from './TopBar';

interface Props {
  view: View;
  code: string;
  yaml: string;
  target: string;
  checks: Check[];
  selected?: Action;
  hovered?: Action;
  /** The action each line of the code was written for. */
  owners: Map<number, string>;
  onSelect(id: string): void;
}

/** The flow's code or YAML, highlighted, with the lines of the selected and hovered actions marked. */
export function CodeView({ view, code, yaml, target, checks, selected, hovered, owners, onSelect }: Props) {
  const [wrap, setWrap] = useState(true);
  const text = view === 'code' ? code : yaml;
  const lines = useMemo(() => {
    const tokens = view === 'code' ? tokenizeCSharp(text) : tokenizeYaml(text);
    return tokens.length > 1 && tokens[tokens.length - 1].length === 0 ? tokens.slice(0, -1) : tokens;
  }, [view, text]);

  const marks = useMemo(() => {
    const byLine = new Map<number, string[]>();
    if (view === 'code') for (const check of checks) if (check.line) byLine.set(check.line, [...(byLine.get(check.line) ?? []), check.message]);
    return byLine;
  }, [view, checks]);

  const inSelected = linesOf(view, yaml, selected);
  const inHovered = hovered === selected ? null : linesOf(view, yaml, hovered);

  const scroller = useRef<HTMLDivElement>(null);
  const first = inSelected ? Math.min(...inSelected) : null;
  const last = inSelected ? Math.max(...inSelected) : null;
  useEffect(() => {
    const box = scroller.current;
    const top = box?.querySelector<HTMLElement>(`[data-line="${first}"]`);
    const bottom = box?.querySelector<HTMLElement>(`[data-line="${last}"]`);
    if (!box || !top || !bottom) return;
    const from = top.offsetTop;
    const to = bottom.offsetTop + bottom.offsetHeight;
    if (from >= box.scrollTop + 8 && to <= box.scrollTop + box.clientHeight - 8) return;
    box.scrollTo({ top: Math.max(0, from - Math.max(48, (box.clientHeight - (to - from)) / 3)), behavior: 'smooth' });
  }, [first, last, view, text]);

  const slash = Math.max(target.lastIndexOf('/'), target.lastIndexOf('\\'));
  const file = view === 'code' ? target.slice(slash + 1) : target.slice(slash + 1).replace(/\.cs$/, '.flow.yaml');
  const toCheck = checks.filter((check) => check.line).length;

  return (
    <section aria-label={view === 'code' ? 'Code' : 'YAML'} class="area-code flex min-h-0 min-w-0 flex-col bg-canvas">
      <div class="flex h-9 shrink-0 items-center gap-2 border-b border-line bg-panel px-3">
        <span class="flex shrink-0 items-center gap-1.5 text-[12.5px] font-medium text-fg">
          <Icon name="file" size={14} class="text-faint" />
          {file}
        </span>
        <span dir="ltr" class="min-w-0 truncate text-[12px] text-faint">
          {target.slice(0, slash + 1)}
        </span>
        <span class="ml-auto flex shrink-0 items-center gap-1">
          {view === 'code' && toCheck > 0 && (
            <span class="mr-1 inline-flex items-center gap-1 rounded-md bg-warn/12 px-1.5 py-0.5 text-[11.5px] font-medium text-warn">
              <Icon name="warn" size={12} />
              {plural(toCheck, 'line')} to check
            </span>
          )}
          <button
            type="button"
            title={wrap ? 'Do not wrap long lines' : 'Wrap long lines'}
            aria-pressed={wrap}
            onClick={() => setWrap(!wrap)}
            class={cls('grid size-7 place-items-center rounded-md transition hover:bg-raised hover:text-fg', wrap ? 'text-accent-fg' : 'text-faint')}
          >
            <Icon name="wrap" size={15} />
          </button>
        </span>
      </div>
      <div ref={scroller} tabIndex={0} class="relative min-h-0 flex-1 overflow-auto font-mono text-[12.5px] leading-5">
        <div class={cls('py-3', !wrap && 'w-max min-w-full')}>
          {lines.map((tokens, index) => {
            const number = index + 1;
            const owner = view === 'code' ? owners.get(number) : undefined;
            return (
              <Line
                key={number}
                number={number}
                tokens={tokens}
                wrap={wrap}
                marks={marks.get(number)}
                state={inSelected?.has(number) ? 'selected' : inHovered?.has(number) ? 'hovered' : undefined}
                onClick={owner ? () => onSelect(owner) : undefined}
              />
            );
          })}
        </div>
      </div>
    </section>
  );
}

interface LineProps {
  number: number;
  tokens: Token[];
  wrap: boolean;
  marks?: string[];
  state?: 'selected' | 'hovered';
  onClick?: () => void;
}

function Line({ number, tokens, wrap, marks, state, onClick }: LineProps) {
  const background = state === 'selected' ? 'var(--line-selected)' : state === 'hovered' ? 'var(--line-hovered)' : 'var(--canvas)';
  const indent = tokens[0]?.kind === 'plain' && !tokens[0].text.trim() ? tokens[0].text.length : 0;
  const body = indent ? tokens.slice(1) : tokens;
  return (
    <div data-line={number} onClick={onClick} class={cls('flex transition-colors duration-150', onClick && 'cursor-pointer')} style={{ background }}>
      <span
        class={cls(
          'sticky left-0 z-[1] flex w-[58px] shrink-0 items-start justify-end gap-1.5 pr-4 text-right text-[11.5px] leading-5 tabular-nums select-none hover:z-20',
          state === 'selected' ? 'text-accent-fg' : 'text-faint',
        )}
        style={{ background, boxShadow: state === 'selected' ? 'inset 2px 0 0 var(--accent)' : undefined }}
      >
        {marks && <Mark messages={marks} />}
        {number}
      </span>
      <span
        dir="ltr"
        class={wrap ? 'min-w-0 flex-1 pr-4 whitespace-pre-wrap [overflow-wrap:anywhere]' : 'pr-8 whitespace-pre'}
        style={wrap ? { paddingLeft: `${indent + 4}ch`, textIndent: '-4ch' } : { paddingLeft: `${indent}ch` }}
      >
        {body.map((token, index) => (
          <span key={index} class={`tk-${token.kind}`}>
            {token.text}
          </span>
        ))}
        {body.length === 0 && ' '}
      </span>
    </div>
  );
}

function Mark({ messages }: { messages: string[] }) {
  return (
    <span class="group/mark relative mt-[3px] text-warn" aria-label={messages.join('; ')} role="img">
      <Icon name="warn" size={13} />
      <span class="pointer-events-none absolute top-1/2 left-full z-30 ml-2 hidden w-max max-w-[380px] -translate-y-1/2 rounded-lg border border-warn/35 bg-panel px-2.5 py-1.5 text-left font-sans text-[12px] leading-snug whitespace-normal text-warn shadow-xl group-hover/mark:block">
        {messages.map((message) => (
          <span key={message} class="block">
            {message}
          </span>
        ))}
      </span>
    </span>
  );
}

// The lines to mark for an action: in the code, those it was written to; in the YAML, its settings.
function linesOf(view: View, yaml: string, action?: Action): Set<number> | null {
  if (!action) return null;
  if (view === 'code') return action.lines ? range(action.lines) : null;
  const names = new Set(action.settings.map((setting) => setting.name));
  const found = new Set<number>();
  yaml.split('\n').forEach((line, index) => {
    const key = /^\s+([^:#\s]+):/.exec(line)?.[1];
    if (key && names.has(key)) found.add(index + 1);
  });
  return found.size ? found : null;
}

function range({ first, last }: Lines): Set<number> {
  return new Set(Array.from({ length: last - first + 1 }, (_, index) => first + index));
}
