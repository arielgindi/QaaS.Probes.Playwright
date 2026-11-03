import { useEffect, useMemo, useRef } from 'preact/hooks';
import { highlight } from '../bridge';
import { type Elements as Model, type ElementsActions, ancestors, rows, shortClasses } from '../elements';
import { cls, plural } from '../text';
import type { DomNode } from '../types';
import { Icon } from './icons';

interface Props {
  model: Model;
  actions: ElementsActions;
  /** The element picked, whose locators the inspector shows. */
  picked?: number;
  onPick(id: number): void;
}

/** The recorded page's elements as a tree, to pick one a hover cannot reach or single out. */
export function ElementsView({ model, actions, picked, onPick }: Props) {
  const tree = useRef<HTMLDivElement>(null);
  const shown = useMemo(() => rows(model), [model]);
  const matches = useMemo(() => new Set(model.matches), [model.matches]);
  const current = model.match >= 0 ? model.matches[model.match] : undefined;
  const frozen = model.source === 'frozen';

  // The current match, or the element picked, in view.
  const focus = current ?? picked;
  useEffect(() => {
    tree.current?.querySelector(`[data-node="${focus}"]`)?.scrollIntoView({ block: 'nearest' });
  }, [focus, shown.length]);

  const move = (step: 1 | -1) => {
    const id = actions.next(step);
    if (id !== null) onPick(id);
  };

  // ↑ ↓ move through the rows, → opens and ← closes, as in a browser's own.
  const onKey = (event: KeyboardEvent) => {
    actions.touch();
    const at = shown.findIndex((row) => row.node.id === picked);
    const row = shown[at];
    const go = (index: number) => {
      const next = shown[Math.max(0, Math.min(shown.length - 1, index))];
      if (next) onPick(next.node.id);
    };
    if (event.key === 'ArrowDown') go(at + 1);
    else if (event.key === 'ArrowUp') go(at < 0 ? 0 : at - 1);
    else if (event.key === 'ArrowRight' && row && row.node.count > 0) {
      if (!model.expanded.has(row.node.id)) actions.toggle(row.node.id);
      else go(at + 1);
    } else if (event.key === 'ArrowLeft' && row) {
      if (model.expanded.has(row.node.id)) actions.toggle(row.node.id);
      else if (row.node.parent !== undefined) onPick(row.node.parent);
    } else return;
    event.preventDefault();
  };

  return (
    <section aria-label="Elements" class="area-code flex min-h-0 min-w-0 flex-col bg-canvas">
      <div class="flex h-11 shrink-0 items-center gap-2 border-b border-line bg-panel px-3">
        <label
          data-elements-search
          class="flex h-8 min-w-0 flex-1 items-center gap-2 rounded-lg border border-line bg-canvas px-2.5 transition focus-within:border-accent/60 focus-within:shadow-[0_0_0_3px_color-mix(in_oklab,var(--accent)_18%,transparent)]"
        >
          <Icon name="search" size={14} class="shrink-0 text-faint" />
          <input
            value={model.query}
            spellcheck={false}
            placeholder={frozen ? 'Find by text, id, class or role' : 'Find by text, id or CSS selector'}
            onInput={(event) => actions.search(event.currentTarget.value)}
            onKeyDown={(event) => {
              actions.touch();
              if (event.key === 'Enter') {
                event.preventDefault();
                move(event.shiftKey ? -1 : 1);
              } else if (event.key === 'Escape') actions.search('');
            }}
            class="min-w-0 flex-1 bg-transparent text-[12.5px] text-fg outline-none placeholder:text-faint focus-visible:outline-none"
          />
          {model.query.trim() && (
            <span class="flex shrink-0 items-center gap-0.5 text-[11.5px] text-muted tabular-nums">
              {model.matches.length ? `${model.match + 1} of ${model.matches.length}` : 'No match'}
              <IconButton title="Previous match (Shift+Enter)" disabled={!model.matches.length} onClick={() => move(-1)} icon="up" />
              <IconButton title="Next match (Enter)" disabled={!model.matches.length} onClick={() => move(1)} icon="down" />
            </span>
          )}
        </label>
        {!frozen && (
          <button
            type="button"
            onClick={actions.refresh}
            title={model.stale ? 'The page changed: read it again' : 'Read the page again'}
            class="relative grid size-8 shrink-0 place-items-center rounded-lg text-muted transition hover:bg-raised hover:text-fg"
          >
            <Icon name="refresh" size={15} />
            {model.stale && <span class="absolute top-1.5 right-1.5 size-1.5 rounded-full bg-accent" />}
          </button>
        )}
      </div>
      {frozen && (
        <div class="flex shrink-0 items-center gap-2.5 border-b border-info/25 bg-info/8 px-3 py-2 text-[12px] text-info">
          <Icon name="freeze" size={14} class="shrink-0" />
          <span class="min-w-0 flex-1 leading-snug">
            <span class="font-medium">Frozen at {model.at}</span>
            <span class="text-info/80"> · {model.page} as it was when you pressed F4</span>
          </span>
          <button type="button" onClick={actions.goLive} class="shrink-0 rounded-md px-2 py-1 font-medium transition hover:bg-info/15">
            Back to live
          </button>
        </div>
      )}
      <div
        ref={tree}
        role="tree"
        aria-label="The page's elements"
        tabIndex={0}
        data-own-keys
        onKeyDown={onKey}
        onPointerMove={actions.touch}
        onWheel={actions.touch}
        onMouseLeave={() => highlight(null)}
        class="min-h-0 flex-1 overflow-auto py-1.5 font-mono text-[12px] leading-[22px] focus-visible:outline-offset-[-2px]"
      >
        {model.loading && model.root === null ? (
          <Placeholder title="Reading the page's elements…" spinner />
        ) : shown.length === 0 ? (
          <Placeholder title="No page to show" text="The recorded window has no page open." />
        ) : (
          <div class="w-max min-w-full">
            {shown.map(({ node, depth }) => (
              <Row
                key={node.id}
                node={node}
                depth={depth}
                open={model.expanded.has(node.id)}
                picked={node.id === picked}
                match={matches.has(node.id)}
                current={node.id === current}
                described={model.described.has(node.id)}
                onToggle={() => actions.toggle(node.id)}
                onPick={() => onPick(node.id)}
                onHover={() => highlight({ node: node.id, frozen })}
              />
            ))}
          </div>
        )}
      </div>
      <div class="flex h-7 shrink-0 items-center gap-2 border-t border-line bg-panel px-3 text-[11.5px] text-faint">
        {frozen ? (
          <span class="truncate">
            {plural(model.described.size, 'element')} a click may act on were located when frozen
            {model.truncated && ' · only the first 15,000 elements were kept'}
          </span>
        ) : (
          <span class="truncate">
            <kbd class="mr-1.5 rounded border border-line bg-canvas px-1 font-sans text-[10.5px] leading-4 text-muted">F4</kbd>
            in the app freezes its elements, e.g. with a menu open
          </span>
        )}
        {picked !== undefined && model.nodes.has(picked) && <Path model={model} id={picked} onPick={onPick} />}
      </div>
    </section>
  );
}

interface RowProps {
  node: DomNode;
  depth: number;
  open: boolean;
  picked: boolean;
  match: boolean;
  current: boolean;
  described: boolean;
  onToggle(): void;
  onPick(): void;
  onHover(): void;
}

function Row({ node, depth, open, picked, match, current, described, onToggle, onPick, onHover }: RowProps) {
  const shadow = node.tag === '#shadow-root';
  const { shown: classes, more } = shortClasses(node.classes);
  return (
    <div
      role="treeitem"
      aria-selected={picked}
      aria-expanded={node.count > 0 ? open : undefined}
      data-node={node.id}
      onClick={onPick}
      onMouseEnter={onHover}
      onDblClick={node.count > 0 ? onToggle : undefined}
      class={cls(
        'group relative flex cursor-default items-center gap-1.5 pr-4 whitespace-nowrap',
        picked ? 'bg-accent/14' : current ? 'bg-warn/16' : match ? 'bg-warn/7' : 'hover:bg-raised',
        node.hidden && !picked && 'opacity-55',
      )}
      style={{ paddingLeft: `${10 + depth * 14}px` }}
    >
      {picked && <span class="absolute inset-y-0 left-0 w-0.5 bg-accent" />}
      <button
        type="button"
        tabIndex={-1}
        aria-hidden="true"
        onClick={(event) => {
          event.stopPropagation();
          onToggle();
        }}
        class={cls('grid size-4 shrink-0 place-items-center rounded text-faint transition hover:text-fg', node.count === 0 && 'invisible')}
      >
        <Icon name="chevron" size={12} class={cls('transition-transform duration-150', open && 'rotate-90')} />
      </button>
      {shadow ? (
        <span class="text-faint italic">#shadow-root</span>
      ) : (
        <span class="flex items-center">
          <span class="tk-kw">{node.tag}</span>
          {node.elementId && <span class="tk-fn">#{node.elementId}</span>}
          {classes.map((name) => (
            <span key={name} class="tk-type opacity-80">
              .{name}
            </span>
          ))}
          {more > 0 && <span class="pl-0.5 text-faint">+{more}</span>}
        </span>
      )}
      {node.role && <Attr name="role" value={node.role} />}
      {node.testId && <Attr name="testid" value={node.testId} />}
      {node.name && <Attr name="name" value={node.name} />}
      {node.label && <Attr name="label" value={node.label} />}
      {node.text && (
        <span dir="auto" class="max-w-[46ch] truncate font-sans text-[12px] text-fg/75">
          “{node.text}”
        </span>
      )}
      {node.hidden && <span class="font-sans text-[10.5px] text-faint">hidden</span>}
      {described && (
        <span title="A click may act on it: its locators were found when frozen" class="grid size-3.5 place-items-center text-info">
          <Icon name="target" size={11} />
        </span>
      )}
    </div>
  );
}

function Attr({ name, value }: { name: string; value: string }) {
  return (
    <span class="inline-flex max-w-[28ch] items-center rounded border border-line bg-panel px-1 font-sans text-[10.5px] leading-4">
      <span class="text-faint">{name}=</span>
      <span dir="auto" class="truncate text-muted">
        {value}
      </span>
    </span>
  );
}

// Where the picked element is, as a trail to pick an element around it.
function Path({ model, id, onPick }: { model: Model; id: number; onPick(id: number): void }) {
  const trail = [...ancestors(model, id), id].slice(-4);
  return (
    <span class="ml-auto flex min-w-0 items-center gap-1 font-mono text-[11px]">
      {trail.map((step, index) => {
        const node = model.nodes.get(step);
        return (
          <span key={step} class="flex min-w-0 items-center gap-1">
            {index > 0 && <Icon name="chevron" size={10} class="shrink-0 text-faint" />}
            <button type="button" onClick={() => onPick(step)} class={cls('truncate rounded px-1 transition hover:bg-raised', step === id ? 'text-fg' : 'text-muted')}>
              {node?.tag}
              {node?.elementId && `#${node.elementId}`}
            </button>
          </span>
        );
      })}
    </span>
  );
}

function IconButton({ title, disabled, onClick, icon }: { title: string; disabled: boolean; onClick(): void; icon: 'up' | 'down' }) {
  return (
    <button
      type="button"
      title={title}
      aria-label={title}
      disabled={disabled}
      onClick={(event) => {
        event.preventDefault();
        onClick();
      }}
      class="grid size-5 place-items-center rounded text-muted transition hover:bg-raised hover:text-fg disabled:opacity-40"
    >
      <Icon name={icon} size={13} />
    </button>
  );
}

function Placeholder({ title, text, spinner }: { title: string; text?: string; spinner?: boolean }) {
  return (
    <div class="flex h-full flex-col items-center justify-center gap-2 px-8 text-center font-sans">
      {spinner && <span class="size-4 animate-spin-slow rounded-full border-2 border-line-strong border-t-accent" />}
      <p class="text-[13px] font-medium text-fg">{title}</p>
      {text && <p class="max-w-[34ch] text-[12.5px] text-muted">{text}</p>}
    </div>
  );
}
