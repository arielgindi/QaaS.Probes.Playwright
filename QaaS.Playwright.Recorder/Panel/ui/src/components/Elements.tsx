import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'preact/hooks';
import { highlight } from '../bridge';
import { type Elements as Model, type ElementsActions, type Row as TreeRow, ancestors, hasChildren, nodeLabel, rows, shortClasses } from '../elements';
import { cls, plural } from '../text';
import { Icon, type IconName } from './icons';
import { Button, EmptyState, Kbd as Key } from './ui';

interface Props {
  model: Model;
  actions: ElementsActions;
  /** The element picked, whose locators the inspector shows. */
  picked?: number;
  /** Picks the element: at once, or once the keys rest on it. */
  onPick(id: number, soon?: boolean): void;
  /** Enter: what the inspector offers first for the element picked. */
  onPrimary(): void;
}

const ROW = 22;
const OVERSCAN = 16;

/** The recorded page's elements as a tree, to pick one a hover cannot reach or single out. */
export function ElementsView({ model, actions, picked, onPick, onPrimary }: Props) {
  const tree = useRef<HTMLDivElement>(null);
  const search = useRef<HTMLInputElement>(null);
  const shown = useMemo(() => rows(model), [model]);
  const matches = useMemo(() => new Set(model.matches), [model.matches]);
  const current = model.match >= 0 ? model.matches[model.match] : undefined;
  const frozen = model.source === 'frozen';
  const selected = model.selected ?? picked;
  const index = useMemo(() => new Map(shown.map((row, at) => [row.node.id, at])), [shown]);

  // Only the rows in view are drawn, so a page of thousands of elements stays quick.
  const [view, setView] = useState({ top: 0, height: 600 });
  useLayoutEffect(() => {
    const element = tree.current;
    if (!element) return;
    const measure = () => setView({ top: element.scrollTop, height: element.clientHeight });
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(element);
    return () => observer.disconnect();
  }, []);
  const first = Math.max(0, Math.floor(view.top / ROW) - OVERSCAN);
  const last = Math.min(shown.length, Math.ceil((view.top + view.height) / ROW) + OVERSCAN);

  // The row at the index in view: just enough, or in the middle when it is far.
  const bringIntoView = (at: number | undefined, middle = false) => {
    const element = tree.current;
    if (!element || at === undefined) return;
    const top = at * ROW + 6;
    const bottom = top + ROW;
    const height = element.clientHeight;
    if (top >= element.scrollTop && bottom <= element.scrollTop + height) return;
    const far = top < element.scrollTop - height || bottom > element.scrollTop + 2 * height;
    element.scrollTop = middle || far ? top - height / 2 : top < element.scrollTop ? top - ROW : bottom - height + ROW;
  };
  useEffect(() => bringIntoView(selected === undefined ? undefined : index.get(selected)), [selected, shown.length]);
  useEffect(() => bringIntoView(model.pointed === undefined ? undefined : index.get(model.pointed), true), [model.pointed, shown.length]);
  // The match the search is at is picked, as the first one is as it is found.
  useEffect(() => {
    if (current !== undefined) onPick(current, true);
  }, [current]);
  // Picked in the app: the keys go on in the tree, Enter making it a step.
  useEffect(() => {
    if (model.picks) tree.current?.focus();
  }, [model.picks]);

  const move = (step: 1 | -1) => {
    const id = actions.next(step);
    if (id !== null) onPick(id, true);
  };
  const choose = (id: number, soon = false) => {
    actions.select(id);
    onPick(id, soon);
  };

  // As a browser's own: ↑ ↓ move through the rows, → opens and ← closes, Enter makes the element a step, a letter
  // starts a search, and Esc clears it.
  const onKey = (event: KeyboardEvent) => {
    actions.touch();
    const at = selected === undefined ? -1 : (index.get(selected) ?? -1);
    const row = shown[at];
    const go = (to: number) => {
      const next = shown[Math.max(0, Math.min(shown.length - 1, to))];
      if (next) choose(next.node.id, true);
    };
    const page = Math.max(1, Math.floor(view.height / ROW) - 1);
    const command = event.ctrlKey || event.metaKey;
    if (event.key === 'ArrowDown') go(at + 1);
    else if (event.key === 'ArrowUp') go(at < 0 ? 0 : at - 1);
    else if (event.key === 'PageDown') go(at + page);
    else if (event.key === 'PageUp') go(at - page);
    else if (event.key === 'Home') go(0);
    else if (event.key === 'End') go(shown.length - 1);
    else if (event.key === 'ArrowRight' && row && hasChildren(model, row.node)) {
      if (!model.expanded.has(row.node.id)) actions.toggle(row.node.id);
      else go(at + 1);
    } else if (event.key === 'ArrowLeft' && row) {
      const parent = ancestors(model, row.node.id).at(-1);
      if (model.expanded.has(row.node.id) && hasChildren(model, row.node)) actions.toggle(row.node.id);
      else if (parent !== undefined) choose(parent, true);
    } else if (event.key === 'Enter' && !command && row) onPrimary();
    else if (event.key === 'Escape' && model.query) actions.search('');
    else if (event.key === 'Escape' && model.inspecting) actions.inspect(false);
    else if ((event.key === 'f' && command) || (event.key.length === 1 && !command && !event.altKey && event.key !== ' ')) {
      // Typing starts a search, with what was typed.
      if (!command) actions.search(event.key);
      search.current?.focus();
    } else return;
    event.preventDefault();
  };

  const path = selected !== undefined && model.nodes.has(selected) ? [...ancestors(model, selected), selected] : [];
  return (
    <section aria-label="Elements" class="area-code flex min-h-0 min-w-0 flex-col bg-canvas">
      <div class="flex h-11 shrink-0 items-center gap-1 border-b border-line bg-panel px-2">
        <ToolButton
          icon="inspect"
          pressed={model.inspecting}
          disabled={frozen && !model.inspecting}
          title={model.inspecting ? 'Stop picking in the app (Ctrl+Shift+C, or Esc there)' : 'Pick an element in the app (Ctrl+Shift+C)'}
          onClick={() => actions.inspect()}
        />
        <label
          data-elements-search
          class="mx-1 flex h-8 min-w-0 flex-1 items-center gap-2 rounded-md border border-line-strong bg-canvas px-2.5 transition focus-within:border-accent-fg/60 focus-within:shadow-[0_0_0_3px_color-mix(in_oklab,var(--accent)_18%,transparent)]"
        >
          <Icon name="search" size={14} class="shrink-0 text-faint" />
          <input
            ref={search}
            value={model.query}
            spellcheck={false}
            aria-label="Find an element"
            placeholder={frozen ? 'Find by text, id, class or role' : 'Find by text, id, CSS or role=button'}
            onInput={(event) => actions.search(event.currentTarget.value)}
            onKeyDown={(event) => {
              actions.touch();
              if (event.key === 'Enter') {
                event.preventDefault();
                move(event.shiftKey ? -1 : 1);
              } else if (event.key === 'Escape') {
                event.preventDefault();
                if (model.query) actions.search('');
                else tree.current?.focus();
              } else if (event.key === 'ArrowDown') {
                event.preventDefault();
                tree.current?.focus();
              }
            }}
            class="min-w-0 flex-1 bg-transparent text-body text-fg outline-none placeholder:text-faint focus-visible:outline-none"
          />
          {model.query.trim() && (
            <span class="flex shrink-0 items-center gap-0.5 text-meta text-muted tabular-nums">
              <span aria-live="polite">
                {model.matches.length
                  ? `${model.match + 1} of ${model.total > model.matches.length ? `${model.matches.length}+` : model.matches.length}`
                  : model.loading ? '…' : '0 of 0'}
              </span>
              <IconButton title="Previous match (Shift+Enter)" disabled={!model.matches.length} onClick={() => move(-1)} icon="up" />
              <IconButton title="Next match (Enter)" disabled={!model.matches.length} onClick={() => move(1)} icon="down" />
            </span>
          )}
        </label>
        <ToolButton
          icon="filter"
          pressed={model.compact}
          title={model.compact ? 'Show every element' : 'Interactive only: leave out the wrappers a person does not act on'}
          onClick={() => actions.setCompact(!model.compact)}
        />
        {!frozen && (
          <ToolButton icon="refresh" title={model.stale ? 'The page changed: read it again' : 'Read the page again'} onClick={actions.refresh} dot={model.stale} />
        )}
      </div>
      {frozen && (
        <div class="flex h-9 shrink-0 items-center gap-2 border-b border-line bg-info/[0.06] pr-1.5 pl-3 text-meta">
          <Icon name="freeze" size={13} class="shrink-0 text-info" />
          <span class="min-w-0 flex-1 truncate">
            <span class="font-semibold text-info">Frozen at {model.at}</span>
            <span class="text-muted"> · {model.page} as it was when you pressed F4</span>
          </span>
          <Button size="sm" variant="secondary" onClick={actions.goLive}>
            Back to live
          </Button>
        </div>
      )}
      {model.query.trim() && !model.loading && model.matches.length === 0 && (
        <div role="status" class="flex h-8 shrink-0 items-center gap-2 border-b border-line bg-panel px-3 text-meta text-muted">
          <Icon name="search" size={12} class="shrink-0 text-faint" />
          <span class="min-w-0 truncate">
            No element matches <span class="font-mono text-fg/85">{model.query.trim()}</span>
            {frozen ? ': a frozen page is searched by text, id, class or role.' : '. Try its text, an id, CSS or role=button.'}
          </span>
        </div>
      )}
      {model.opened.length > 0 && !frozen && (
        <div class="flex h-9 shrink-0 items-center gap-1.5 overflow-x-auto border-b border-line bg-panel pr-1.5 pl-3 text-meta whitespace-nowrap">
          <span class="text-muted">Open on the page</span>
          {model.opened.map((open) => (
            <button
              key={open.id}
              type="button"
              dir="auto"
              title="Show it in the tree"
              onMouseEnter={() => highlight({ node: open.id, frozen })}
              onMouseLeave={() => highlight(null)}
              onClick={() => {
                actions.reveal(open.id, true);
                onPick(open.id);
                tree.current?.focus();
              }}
              class="inline-flex h-6 max-w-[34ch] items-center gap-1 rounded-full border border-line-strong bg-raised px-2 text-fg transition hover:border-faint/70"
            >
              <span class="truncate">{open.label}</span>
              <Icon name="chevron" size={11} class="shrink-0" />
            </button>
          ))}
          <Button size="sm" iconOnly icon="cross" label="Dismiss" onClick={() => actions.dismiss()} class="ml-auto" />
        </div>
      )}
      <div
        ref={tree}
        role="tree"
        aria-label="The page's elements"
        aria-activedescendant={selected !== undefined && index.has(selected) ? `node-${selected}` : undefined}
        tabIndex={0}
        data-own-keys
        onKeyDown={onKey}
        onScroll={(event) => {
          const element = event.currentTarget;
          setView({ top: element.scrollTop, height: element.clientHeight });
        }}
        onPointerMove={actions.touch}
        onWheel={actions.touch}
        onMouseLeave={() => highlight(null)}
        class="min-h-0 flex-1 overflow-auto font-mono text-meta leading-[22px] focus-visible:outline-offset-[-2px]"
      >
        {model.loading && model.root === null ? (
          <Skeleton />
        ) : shown.length === 0 ? (
          <EmptyState icon="elements" title="No page to show" class="h-full font-sans">
            The recorded window has no page open.
          </EmptyState>
        ) : (
          <div class="relative w-max min-w-full" style={{ height: `${shown.length * ROW + 12}px` }}>
            <div style={{ transform: `translateY(${first * ROW + 6}px)` }}>
              {shown.slice(first, last).map((row) => (
                <Row
                  key={row.node.id}
                  row={row}
                  open={model.expanded.has(row.node.id)}
                  parent={hasChildren(model, row.node)}
                  picked={row.node.id === selected}
                  pointed={row.node.id === model.pointed}
                  match={matches.has(row.node.id)}
                  current={row.node.id === current}
                  described={model.described.has(row.node.id)}
                  onToggle={() => actions.toggle(row.node.id)}
                  onPick={() => choose(row.node.id)}
                  onHover={() => highlight({ node: row.node.id, frozen })}
                />
              ))}
            </div>
          </div>
        )}
      </div>
      <div class="flex h-7 shrink-0 items-center gap-2 border-t border-line bg-panel pl-2 text-meta text-muted">
        {path.length > 0 ? (
          <Breadcrumb model={model} path={path} onPick={(id) => choose(id)} />
        ) : frozen ? (
          <span class="truncate px-1">
            {plural(model.described.size, 'element')} a click may act on were located when frozen
            {model.truncated && ' · only the first 15,000 elements were kept'}
          </span>
        ) : (
          <span class="flex min-w-0 items-center gap-1.5 truncate px-1">
            <Key>Ctrl Shift C</Key> picks in the app · <Key>F4</Key> there freezes the page, e.g. with a menu open
          </span>
        )}
        <span class="ml-auto hidden shrink-0 items-center gap-1.5 pr-3 xl:flex">
          <Key>↑↓←→</Key> move <Key>↵</Key> make a step
        </span>
      </div>
    </section>
  );
}

interface RowProps {
  row: TreeRow;
  open: boolean;
  parent: boolean;
  picked: boolean;
  pointed: boolean;
  match: boolean;
  current: boolean;
  described: boolean;
  onToggle(): void;
  onPick(): void;
  onHover(): void;
}

function Row({ row: { node, depth, via }, open, parent, picked, pointed, match, current, described, onToggle, onPick, onHover }: RowProps) {
  const shadow = node.tag === '#shadow-root';
  const { shown: classes, more } = shortClasses(node.classes);
  // What tells it apart, short and dimmed: its role, its name, its own text, else the start of the text inside it.
  const role = node.role ?? (node.implicit && node.implicit !== node.tag ? node.implicit : undefined);
  const name = node.accName ?? (node.label && node.label !== node.text ? node.label : undefined);
  return (
    <div
      id={`node-${node.id}`}
      role="treeitem"
      aria-selected={picked}
      aria-expanded={parent ? open : undefined}
      aria-level={depth + 1}
      data-node={node.id}
      onClick={onPick}
      onMouseEnter={onHover}
      onDblClick={parent ? onToggle : undefined}
      class={cls(
        'group relative flex h-[22px] cursor-default items-center gap-1.5 pr-4 whitespace-nowrap',
        picked ? 'bg-accent/14' : current ? 'bg-warn/18' : pointed ? 'bg-accent/9' : match ? 'bg-warn/8' : 'hover:bg-raised',
        pointed && 'outline outline-1 -outline-offset-1 outline-accent/60',
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
        class={cls('grid size-4 shrink-0 place-items-center rounded text-faint transition hover:text-fg', !parent && 'invisible')}
      >
        <Icon name="chevron" size={12} class={cls('transition-transform duration-150', open && 'rotate-90')} />
      </button>
      {via > 0 && (
        <span title={`${plural(via, 'wrapper')} around it left out`} class="-ml-1 font-sans text-meta text-faint">
          ⋯
        </span>
      )}
      {shadow ? (
        <span class="text-faint italic">#shadow-root</span>
      ) : (
        <span class="flex items-center">
          <span class="tk-kw">{node.tag}</span>
          {node.elementId && <span class="tk-fn">#{node.elementId}</span>}
          {classes.map((each) => (
            <span key={each} class="tk-type opacity-75">
              .{each}
            </span>
          ))}
          {more > 0 && <span class="pl-0.5 text-faint">+{more}</span>}
        </span>
      )}
      <span class="flex min-w-0 items-center gap-1.5 font-sans text-meta">
        {role && <span class="text-muted">{role}</span>}
        {name && (
          <span dir="auto" class="max-w-[36ch] truncate text-fg/70">
            “{name}”
          </span>
        )}
        {node.text && (
          <span dir="auto" class="max-w-[46ch] truncate text-fg/85">
            {name ? node.text : `“${node.text}”`}
          </span>
        )}
        {!node.text && !name && node.glimpse && (
          <span dir="auto" class="max-w-[40ch] truncate text-faint italic">
            {node.glimpse}
          </span>
        )}
        {node.testId && <span class="text-faint">testid={node.testId}</span>}
        {node.name && node.name !== node.elementId && <span class="text-faint">name={node.name}</span>}
        {node.hidden && <span class="text-tag text-faint">hidden</span>}
        {node.framed && node.tag === 'body' && <span class="text-tag text-faint">frame</span>}
      </span>
      {described && (
        <span title="A click may act on it: its locators were found when frozen" class="grid size-3.5 place-items-center text-info">
          <Icon name="target" size={11} />
        </span>
      )}
    </div>
  );
}

// Where the element is, from the body down: point at a step to see it in the app, click it to pick it.
function Breadcrumb({ model, path, onPick }: { model: Model; path: number[]; onPick(id: number): void }) {
  const trail = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (trail.current) trail.current.scrollLeft = trail.current.scrollWidth;
  }, [path.at(-1)]);
  const frozen = model.source === 'frozen';
  return (
    <div ref={trail} aria-label="Where the element is" class="flex min-w-0 flex-1 items-center gap-0.5 overflow-x-auto font-mono text-meta [scrollbar-width:none]" onMouseLeave={() => highlight(null)}>
      {path.map((id, at) => {
        const node = model.nodes.get(id);
        if (!node) return null;
        const end = at === path.length - 1;
        const role = node.role ?? node.implicit;
        return (
          <span key={id} class="flex shrink-0 items-center gap-0.5">
            {at > 0 && <Icon name="chevron" size={10} class="shrink-0 text-faint" />}
            <button
              type="button"
              onClick={() => onPick(id)}
              onMouseEnter={() => highlight({ node: id, frozen })}
              class={cls('rounded px-1 leading-5 transition hover:bg-raised', end ? 'bg-accent/10 text-fg' : 'text-muted')}
            >
              {nodeLabel(node)}
              {end && role && <span class="font-sans text-faint"> {role}</span>}
            </button>
          </span>
        );
      })}
    </div>
  );
}

function ToolButton({ icon, title, pressed, disabled, dot, onClick }: { icon: IconName; title: string; pressed?: boolean; disabled?: boolean; dot?: boolean; onClick(): void }) {
  return (
    <button
      type="button"
      title={title}
      aria-label={title}
      aria-pressed={pressed}
      disabled={disabled}
      onClick={onClick}
      class={cls(
        'relative grid size-8 shrink-0 place-items-center rounded-md transition disabled:opacity-40',
        pressed
          ? icon === 'inspect'
            ? 'bg-inspect/14 text-inspect shadow-[inset_0_0_0_1px_color-mix(in_oklab,var(--inspect)_40%,transparent)]'
            : 'bg-accent/15 text-accent-fg shadow-[inset_0_0_0_1px_color-mix(in_oklab,var(--accent-fg)_35%,transparent)]'
          : 'text-muted hover:bg-raised hover:text-fg',
      )}
    >
      <Icon name={icon} size={15} />
      {dot && <span class="absolute top-1.5 right-1.5 size-1.5 rounded-full bg-accent" />}
    </button>
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

// The tree's first rows while the page's elements are read.
function Skeleton() {
  return (
    <div aria-label="Reading the page's elements" class="space-y-2 px-3 py-3">
      {[72, 48, 60, 40, 66, 52, 30].map((width, row) => (
        <div key={row} class="flex items-center gap-2" style={{ paddingLeft: `${(row % 3) * 14}px` }}>
          <span class="skeleton h-3 w-3" />
          <span class="skeleton h-3" style={{ width: `${width}%` }} />
        </div>
      ))}
    </div>
  );
}
