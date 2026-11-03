import { useEffect, useRef, useState } from 'preact/hooks';
import { send, subscribeElements } from './bridge';
import type { DomNode, DomPatch, ElementsMessage, Opened } from './types';

// The Elements view's model: the recorded page's elements, live and loaded a level at a time, or as they were when the
// person pressed F4 in the app. It lives in the app, so the tree stays as it was while the person looks at the code.

export interface Elements {
  source: 'live' | 'frozen';
  page: string;
  /** For the frozen tree, when it was frozen. */
  at?: string;
  root: number | null;
  nodes: Map<number, DomNode>;
  children: Map<number, number[]>;
  expanded: Set<number>;
  /** Elements whose locators were found when frozen: what a click may act on. */
  described: Set<number>;
  matches: number[];
  match: number;
  /** How many the search found, of which `matches` are the first. */
  total: number;
  query: string;
  loading: boolean;
  /** The page changed while the person was busy with the tree: it refreshes once they stop. */
  stale: boolean;
  truncated: boolean;
  /** "Interactive only": the wrappers a person does not act on are left out. */
  compact: boolean;
  /** The app is in inspect mode: the tree follows its pointer. */
  inspecting: boolean;
  /** While inspecting, the element under the pointer in the app. */
  pointed?: number;
  /** The row the keys are on; the element picked follows it. */
  selected?: number;
  /** Dialogs, menus and lists that opened on the page, to go to. */
  opened: Opened[];
  /** Counts the elements picked in the app, for the tree to take the keys after each. */
  picks: number;
}

export interface ElementsActions {
  toggle(id: number): void;
  /** Opens the tree to the element, and selects it; `open`, it is opened too. */
  reveal(id: number, open?: boolean): void;
  select(id: number | undefined): void;
  search(query: string): void;
  next(step: 1 | -1): number | null;
  refresh(): void;
  goLive(): void;
  setCompact(on: boolean): void;
  inspect(on?: boolean): void;
  dismiss(id?: number): void;
  /** The person is pointing at or typing in the view: the page's changes wait. */
  touch(): void;
}

const empty = (source: Elements['source'] = 'live', compact = false): Elements => ({
  source,
  page: 'page',
  root: null,
  nodes: new Map(),
  children: new Map(),
  expanded: new Set(),
  described: new Set(),
  matches: [],
  match: -1,
  total: 0,
  query: '',
  loading: true,
  stale: false,
  truncated: false,
  compact,
  inspecting: false,
  opened: [],
  picks: 0,
});

const QUIET_MS = 1500;
const COMPACT_KEY = 'qaas.elements.compact';

function remembered(): boolean {
  try {
    return localStorage.getItem(COMPACT_KEY) === '1';
  } catch {
    return false;
  }
}

/**
 * The model, following the page while <paramref name="active"/>. `onInspected` hears of each element a click picked
 * in the app in inspect mode, and of the app going into it.
 */
export function useElements(active: boolean, onInspected: (event: { picked?: number; on: boolean }) => void): [Elements, ElementsActions] {
  const [model, setModel] = useState<Elements>(() => empty('live', remembered()));
  const current = useRef(model);
  current.current = model;
  const touched = useRef(0);
  const searchTimer = useRef(0);
  const heard = useRef(onInspected);
  heard.current = onInspected;
  // The elements each read of the tree was asked to open, by its number: one opened since keeps what it shows.
  const asked = useRef(new Map<number, Set<number>>());
  const asks = useRef(0);

  const request = (ids: number[], fresh = false, compact = current.current.compact, node?: number) => {
    const seq = ++asks.current;
    asked.current.set(seq, new Set(ids));
    send({ type: 'elements', op: 'tree', ids, on: true, compact, fresh, node, seq });
  };
  const searchFor = (query: string, compact = current.current.compact) =>
    send({ type: 'elements', op: 'search', query, on: true, compact });

  useEffect(
    () =>
      subscribeElements((message) => {
        if (message.kind === 'changed') {
          if (current.current.source === 'live') setModel((was) => ({ ...was, stale: true }));
          return;
        }
        if (message.kind === 'tree' && current.current.source === 'live') {
          const was = current.current;
          // Opened after the tree was asked for, as the tree followed the app's pointer meanwhile: it is read again.
          const ask = asked.current.get(message.seq ?? 0) ?? new Set<number>();
          for (const seq of asked.current.keys()) if (seq <= (message.seq ?? 0)) asked.current.delete(seq);
          const later = was.root === message.data.root && [...was.expanded].some((id) => !ask.has(id) && was.children.has(id));
          const next = receive(was, message, later);
          current.current = next;
          setModel(next);
          if (later) request([...next.expanded]);
          return;
        }
        if (message.kind === 'inspect') {
          const was = current.current;
          const next = inspected(was, message);
          current.current = next;
          setModel(next);
          if (message.picked && message.node !== undefined) heard.current({ picked: message.node, on: false });
          else if (message.on !== was.inspecting) heard.current({ on: message.on });
          return;
        }
        setModel((was) => receive(was, message));
      }),
    [],
  );

  // Open, the view reads the page and follows it; closed, it stops, and what it marked in the app goes, as does inspect
  // mode once the person leaves the view.
  const shown = useRef(active);
  useEffect(() => {
    const left = shown.current && !active;
    shown.current = active;
    if (!active) {
      send({ type: 'elements', op: 'watch', on: false });
      if (left && current.current.inspecting) send({ type: 'elements', op: 'inspect', on: false });
      return;
    }
    if (current.current.source === 'live') request([...current.current.expanded], current.current.root === null);
  }, [active]);

  // The search's matches are marked in the app, the current one stronger.
  const live = active && model.source === 'live';
  const marked = live && model.query.trim() ? model.matches : [];
  const currentMatch = model.match >= 0 ? model.matches[model.match] : undefined;
  const markKey = `${live}|${marked.join(',')}|${currentMatch}`;
  useEffect(() => {
    if (live) send({ type: 'elements', op: 'mark', matches: marked, current: currentMatch });
  }, [markKey]);

  // A change of the page waits until the person has left the tree alone for a moment.
  useEffect(() => {
    if (!active || !model.stale || model.source !== 'live') return;
    const timer = window.setInterval(() => {
      const typing = !!current.current.query.trim() && !!document.activeElement?.closest('[data-elements-search]');
      const busy = Date.now() - touched.current < QUIET_MS || typing;
      if (busy) return;
      window.clearInterval(timer);
      setModel((was) => ({ ...was, stale: false }));
      if (current.current.query.trim()) searchFor(current.current.query);
      else request([...current.current.expanded]);
    }, 300);
    return () => window.clearInterval(timer);
  }, [active, model.stale, model.source]);

  const update = (next: Elements) => {
    current.current = next;
    setModel(next);
  };

  const actions: ElementsActions = {
    toggle(id) {
      const was = current.current;
      const expanded = new Set(was.expanded);
      if (expanded.has(id)) expanded.delete(id);
      else expanded.add(id);
      update({ ...was, expanded });
      if (expanded.has(id) && was.source === 'live' && !was.children.has(id)) request([...expanded]);
    },
    reveal(id, open = false) {
      const was = current.current;
      const expanded = new Set([...was.expanded, ...ancestors(was, id), ...(open ? [id] : [])]);
      update({ ...was, selected: id, expanded, opened: was.opened.filter((each) => each.id !== id) });
      if (open && was.source === 'live' && !was.children.has(id)) request([...expanded]);
    },
    select(id) {
      update({ ...current.current, selected: id });
    },
    search(query) {
      window.clearTimeout(searchTimer.current);
      const was = current.current;
      if (was.source === 'frozen') {
        update(searchFrozen(was, query));
        return;
      }
      update({ ...was, query, matches: query.trim() ? was.matches : [], match: query.trim() ? was.match : -1, total: query.trim() ? was.total : 0 });
      searchTimer.current = window.setTimeout(() => {
        if (query.trim()) searchFor(query);
      }, 220);
    },
    next(step) {
      const was = current.current;
      if (was.matches.length === 0) return null;
      const match = (was.match + step + was.matches.length) % was.matches.length;
      const id = was.matches[match];
      update({ ...was, match, selected: id, expanded: new Set([...was.expanded, ...ancestors(was, id)]) });
      return id;
    },
    refresh() {
      const was = current.current;
      if (was.source === 'frozen') return;
      update({ ...was, stale: false, loading: was.root === null });
      if (was.query.trim()) searchFor(was.query);
      else request([...was.expanded]);
    },
    goLive() {
      const compact = current.current.compact;
      update({ ...empty('live', compact), inspecting: current.current.inspecting });
      request([], true, compact);
    },
    setCompact(on) {
      const was = current.current;
      try {
        localStorage.setItem(COMPACT_KEY, on ? '1' : '0');
      } catch {
        // Remembered for this time only.
      }
      // The frozen tree is shown here without the wrappers or with them; the live one is read again, and the element
      // selected shows in it, or what holds it.
      const next = { ...was, compact: on };
      if (was.source === 'frozen' && was.selected !== undefined) next.expanded = new Set([...was.expanded, ...ancestors(next, was.selected)]);
      update(next);
      if (was.source === 'frozen') return;
      if (was.query.trim()) searchFor(was.query, on);
      else request([...was.expanded], false, on, was.selected);
    },
    inspect(on) {
      send({ type: 'elements', op: 'inspect', on: on ?? !current.current.inspecting });
    },
    dismiss(id) {
      const was = current.current;
      update({ ...was, opened: id === undefined ? [] : was.opened.filter((open) => open.id !== id) });
    },
    touch() {
      touched.current = Date.now();
    },
  };
  return [model, actions];
}

function receive(was: Elements, message: Exclude<ElementsMessage, { kind: 'changed' | 'inspect' }>, merge = false): Elements {
  const { data } = message;
  if (message.kind === 'frozen') {
    const model: Elements = { ...empty('frozen', was.compact), ...maps(data), page: message.page, at: message.at, loading: false };
    model.inspecting = was.inspecting;
    model.described = new Set(message.described);
    model.truncated = !!data.truncated;
    // Opened at what the person froze it for: the first element a click may act on, those in a menu first.
    const first = message.described[0];
    model.expanded = new Set([data.root, ...(first === undefined ? [] : ancestors(model, first))]);
    model.selected = first;
    return model;
  }
  // A frozen tree stays until the person goes back to the live page.
  if (was.source === 'frozen') return was;
  // The tree read in the other mode than the one shown, as the person switched it meanwhile, waits for the next.
  if (!!data.compact !== was.compact) return was;
  const sameDocument = was.root === data.root && was.page === message.page;
  // A search typed before the page's elements came stays.
  const base = sameDocument ? was : { ...empty('live', was.compact), page: message.page, inspecting: was.inspecting, query: was.root === null ? was.query : '' };
  if (message.kind === 'tree') {
    const fresh = maps(data, merge ? base : undefined);
    // Only what is still on the page stays open.
    const expanded = new Set([data.root, ...[...base.expanded].filter((id) => fresh.children.has(id))]);
    const opened = [...(data.opened ?? []), ...base.opened.filter((open) => fresh.nodes.has(open.id) && !data.opened?.some((each) => each.id === open.id))];
    const model: Elements = {
      ...base,
      ...fresh,
      expanded,
      loading: false,
      matches: sameDocument ? base.matches.filter((id) => fresh.nodes.has(id)) : [],
      selected: base.selected !== undefined && fresh.nodes.has(base.selected) ? base.selected : undefined,
      opened: opened.slice(0, 3),
    };
    if (data.revealed !== undefined && fresh.nodes.has(data.revealed)) {
      model.selected = data.revealed;
      model.expanded = new Set([...expanded, ...ancestors(model, data.revealed)]);
    }
    return model;
  }
  // A search adds the way to each match to what is shown, and opens it at the first.
  const merged = maps(data, base);
  const matches = data.matches ?? [];
  // The same search read again, as the page changed, stays at the match the person is on.
  const kept = base.selected === undefined ? -1 : matches.indexOf(base.selected);
  const match = kept >= 0 ? kept : matches.length ? 0 : -1;
  const model: Elements = { ...base, ...merged, matches, match, total: data.total ?? matches.length, loading: false };
  model.expanded = new Set([...base.expanded, data.root, ...(match >= 0 ? ancestors(model, matches[match]) : [])]);
  if (match >= 0) model.selected = matches[match];
  return model;
}

// What the app in inspect mode told: the element under its pointer, the tree opened to it.
function inspected(was: Elements, message: Extract<ElementsMessage, { kind: 'inspect' }>): Elements {
  let model: Elements = { ...was, inspecting: message.on && !message.picked, pointed: message.picked || !message.on ? undefined : message.node };
  if (message.data && was.source === 'live' && !!message.data.compact === was.compact) {
    const sameDocument = was.root === message.data.root && was.page === message.page;
    const base = sameDocument ? model : { ...empty('live', was.compact), page: message.page, inspecting: model.inspecting, pointed: model.pointed };
    model = { ...base, ...maps(message.data, base), loading: false };
  }
  const id = message.node;
  if (id === undefined || !model.nodes.has(id)) return model;
  model.expanded = new Set([...model.expanded, ...ancestors(model, id)]);
  if (message.picked) model = { ...model, selected: id, picks: model.picks + 1 };
  return model;
}

function maps(data: DomPatch, into?: Elements): Pick<Elements, 'root' | 'nodes' | 'children'> {
  const nodes = new Map(into?.nodes);
  const children = new Map(into?.children);
  for (const node of data.nodes) nodes.set(node.id, node);
  for (const [id, list] of Object.entries(data.children)) children.set(Number(id), list);
  return { root: data.root, nodes, children };
}

/** The elements around the one given as the tree shows them, the body first. */
export function ancestors(model: Pick<Elements, 'nodes' | 'source' | 'compact'>, id: number): number[] {
  const found: number[] = [];
  const parentOf = parents(model);
  for (let parent = parentOf(id); parent !== undefined && !found.includes(parent); parent = parentOf(parent)) found.unshift(parent);
  return found;
}

// In a frozen tree "interactive only" is worked out here, from the whole page: an element's parent is the nearest
// element around it that is shown. The live tree comes as the mode has it.
const leaves = (model: Pick<Elements, 'source' | 'compact'>) => model.source === 'frozen' && model.compact;

function parents(model: Pick<Elements, 'nodes' | 'source' | 'compact'>): (id: number) => number | undefined {
  if (!leaves(model)) return (id) => model.nodes.get(id)?.parent;
  return (id) => {
    let parent = model.nodes.get(id)?.parent;
    while (parent !== undefined) {
      const node = model.nodes.get(parent);
      if (!node || node.acts || node.parent === undefined) return parent;
      parent = node.parent;
    }
    return undefined;
  };
}

// An element's children as the tree shows them, each with how many wrappers were left out around it.
function childrenOf(model: Elements): (id: number) => [number, number][] {
  if (!leaves(model)) return (id) => (model.children.get(id) ?? []).map((child) => [child, model.nodes.get(child)?.via ?? 0]);
  return (id) => {
    const found: [number, number][] = [];
    const walk = (from: number, via: number) => {
      for (const child of model.children.get(from) ?? []) {
        const node = model.nodes.get(child);
        if (!node) continue;
        if (node.acts) found.push([child, via]);
        else walk(child, via + 1);
      }
    };
    walk(id, 0);
    return found;
  };
}

// A frozen tree is searched here, by text in what tells its elements apart; a CSS selector needs the live page.
function searchFrozen(was: Elements, query: string): Elements {
  const text = query.trim().toLowerCase();
  if (!text) return { ...was, query, matches: [], match: -1, total: 0 };
  const found = [...was.nodes.values()].filter((node) =>
    [node.tag, node.elementId, node.classes, node.role, node.implicit, node.testId, node.name, node.label, node.accName, node.text].some(
      (value) => value?.toLowerCase().includes(text),
    ),
  );
  // In "interactive only", the element shown for each.
  const parentOf = parents(was);
  const shown = [...new Set(found.map((node) => (leaves(was) && !node.acts ? parentOf(node.id) : node.id)))].filter(
    (id): id is number => id !== undefined && id !== was.root,
  );
  const matches = shown.slice(0, 500);
  const expanded = new Set([...was.expanded, ...(matches.length ? ancestors(was, matches[0]) : [])]);
  return { ...was, query, matches, match: matches.length ? 0 : -1, total: shown.length, expanded, selected: matches[0] ?? was.selected };
}

export interface Row {
  node: DomNode;
  depth: number;
  /** How many wrappers "interactive only" left out around it. */
  via: number;
}

/** The rows the tree shows: each element below an open one, with how deep it is. */
export function rows(model: Elements): Row[] {
  const shown: Row[] = [];
  const kids = childrenOf(model);
  const visit = (id: number, depth: number, via: number) => {
    const node = model.nodes.get(id);
    if (!node) return;
    shown.push({ node, depth, via });
    if (!model.expanded.has(id)) return;
    for (const [child, lifted] of kids(id)) visit(child, depth + 1, lifted);
  };
  if (model.root !== null) visit(model.root, 0, 0);
  return shown;
}

/** Whether the element has children in the tree as shown. */
export function hasChildren(model: Elements, node: DomNode): boolean {
  if (!leaves(model)) return node.count > 0;
  return childrenOf(model)(node.id).length > 0;
}

// Classes a framework generates, which say nothing to a person and change between builds: css-1x2y3z, sc-bdVaJa,
// jss12, a CSS module's _3xYz9 or Button_root__a1B2c.
const generated = /^(css|sc|jss|emotion|styled|svelte)[-\d]|^_[\w-]{5,}$|__[\w-]{5,}$/;

/** The classes worth showing, at most two, and how many others there are. */
export function shortClasses(classes?: string): { shown: string[]; more: number } {
  const all = (classes ?? '').split(/\s+/).filter(Boolean);
  const shown = all.filter((name) => !generated.test(name)).slice(0, 2).map((name) => (name.length > 28 ? `${name.slice(0, 27)}…` : name));
  return { shown, more: all.length - shown.length };
}

/** The element as a short label: its tag and id, else its first class worth showing. */
export function nodeLabel(node: DomNode): string {
  if (node.tag === '#shadow-root') return '#shadow-root';
  if (node.elementId) return `${node.tag}#${node.elementId}`;
  const [first] = shortClasses(node.classes).shown;
  return first ? `${node.tag}.${first}` : node.tag;
}
