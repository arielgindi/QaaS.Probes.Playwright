import { useEffect, useRef, useState } from 'preact/hooks';
import { send, subscribeElements } from './bridge';
import type { DomNode, DomPatch, ElementsMessage } from './types';

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
  query: string;
  loading: boolean;
  /** The page changed while the person was busy with the tree: it refreshes once they stop. */
  stale: boolean;
  truncated: boolean;
}

export interface ElementsActions {
  toggle(id: number): void;
  expandTo(id: number): void;
  search(query: string): void;
  next(step: 1 | -1): number | null;
  refresh(): void;
  goLive(): void;
  /** The person is pointing at or typing in the view: the page's changes wait. */
  touch(): void;
}

const empty = (source: Elements['source'] = 'live'): Elements => ({
  source,
  page: 'page',
  root: null,
  nodes: new Map(),
  children: new Map(),
  expanded: new Set(),
  described: new Set(),
  matches: [],
  match: -1,
  query: '',
  loading: true,
  stale: false,
  truncated: false,
});

const QUIET_MS = 1500;

/** The model, following the page while <paramref name="active"/>. */
export function useElements(active: boolean): [Elements, ElementsActions] {
  const [model, setModel] = useState<Elements>(empty);
  const current = useRef(model);
  current.current = model;
  const touched = useRef(0);
  const searchTimer = useRef(0);

  const request = (ids: number[]) => send({ type: 'elements', op: 'tree', ids, on: true });

  useEffect(
    () =>
      subscribeElements((message) => {
        if (message.kind === 'changed') {
          if (current.current.source === 'live') setModel((was) => ({ ...was, stale: true }));
          return;
        }
        setModel((was) => receive(was, message));
      }),
    [],
  );

  // Open, the view reads the page and follows it; closed, it stops.
  useEffect(() => {
    if (!active) {
      send({ type: 'elements', op: 'watch', on: false });
      return;
    }
    if (current.current.source === 'live') request([...current.current.expanded]);
  }, [active]);

  // A change of the page waits until the person has left the tree alone for a moment.
  useEffect(() => {
    if (!active || !model.stale || model.source !== 'live') return;
    const timer = window.setInterval(() => {
      const busy = Date.now() - touched.current < QUIET_MS || document.activeElement?.closest('[data-elements-search]');
      if (busy) return;
      window.clearInterval(timer);
      setModel((was) => ({ ...was, stale: false }));
      if (current.current.query) send({ type: 'elements', op: 'search', query: current.current.query, on: true });
      else request([...current.current.expanded]);
    }, 300);
    return () => window.clearInterval(timer);
  }, [active, model.stale, model.source]);

  const actions: ElementsActions = {
    toggle(id) {
      const was = current.current;
      const expanded = new Set(was.expanded);
      if (expanded.has(id)) expanded.delete(id);
      else expanded.add(id);
      setModel({ ...was, expanded });
      if (expanded.has(id) && was.source === 'live' && !was.children.has(id)) request([...expanded]);
    },
    expandTo(id) {
      setModel((was) => ({ ...was, expanded: new Set([...was.expanded, ...ancestors(was, id)]) }));
    },
    search(query) {
      window.clearTimeout(searchTimer.current);
      const was = current.current;
      if (was.source === 'frozen') {
        setModel(searchFrozen(was, query));
        return;
      }
      setModel({ ...was, query, matches: query.trim() ? was.matches : [], match: -1 });
      searchTimer.current = window.setTimeout(() => {
        if (query.trim()) send({ type: 'elements', op: 'search', query, on: true });
      }, 220);
    },
    next(step) {
      const was = current.current;
      if (was.matches.length === 0) return null;
      const match = (was.match + step + was.matches.length) % was.matches.length;
      const id = was.matches[match];
      setModel({ ...was, match, expanded: new Set([...was.expanded, ...ancestors(was, id)]) });
      return id;
    },
    refresh() {
      const was = current.current;
      if (was.source === 'frozen') return;
      setModel({ ...was, stale: false, loading: was.root === null });
      if (was.query.trim()) send({ type: 'elements', op: 'search', query: was.query, on: true });
      else request([...was.expanded]);
    },
    goLive() {
      setModel(empty());
      request([]);
    },
    touch() {
      touched.current = Date.now();
    },
  };
  return [model, actions];
}

function receive(was: Elements, message: Exclude<ElementsMessage, { kind: 'changed' }>): Elements {
  const { data } = message;
  if (message.kind === 'frozen') {
    const model: Elements = { ...empty('frozen'), ...maps(data), page: message.page, at: message.at, loading: false };
    model.described = new Set(message.described);
    model.truncated = !!data.truncated;
    // Opened at what the person froze it for: the first element a click may act on, those in a menu first.
    const first = message.described[0];
    model.expanded = new Set([data.root, ...(first === undefined ? [] : ancestors(model, first))]);
    return model;
  }
  // A frozen tree stays until the person goes back to the live page.
  if (was.source === 'frozen') return was;
  const sameDocument = was.root === data.root && was.page === message.page;
  const base = sameDocument ? was : { ...empty(), page: message.page };
  if (message.kind === 'tree') {
    const fresh = maps(data);
    // Only what is still on the page stays open.
    const expanded = new Set([data.root, ...[...base.expanded].filter((id) => fresh.children.has(id))]);
    return { ...base, ...fresh, expanded, loading: false, matches: sameDocument ? base.matches.filter((id) => fresh.nodes.has(id)) : [] };
  }
  // A search adds the way to each match to what is shown, and opens it.
  const merged = maps(data, base);
  const matches = data.matches ?? [];
  const model = { ...base, ...merged, matches, match: matches.length ? 0 : -1, loading: false };
  model.expanded = new Set([...base.expanded, data.root, ...matches.flatMap((id) => ancestors(model, id))]);
  return model;
}

function maps(data: DomPatch, into?: Elements): Pick<Elements, 'root' | 'nodes' | 'children'> {
  const nodes = new Map(into?.nodes);
  const children = new Map(into?.children);
  for (const node of data.nodes) nodes.set(node.id, node);
  for (const [id, list] of Object.entries(data.children)) children.set(Number(id), list);
  return { root: data.root, nodes, children };
}

/** The elements around the one given, the body first. */
export function ancestors(model: Pick<Elements, 'nodes'>, id: number): number[] {
  const found: number[] = [];
  for (let parent = model.nodes.get(id)?.parent; parent !== undefined && !found.includes(parent); parent = model.nodes.get(parent)?.parent)
    found.unshift(parent);
  return found;
}

// A frozen tree is searched here, by text in what tells its elements apart; a CSS selector needs the live page.
function searchFrozen(was: Elements, query: string): Elements {
  const text = query.trim().toLowerCase();
  if (!text) return { ...was, query, matches: [], match: -1 };
  const matches = [...was.nodes.values()]
    .filter((node) =>
      [node.tag, node.elementId, node.classes, node.role, node.testId, node.name, node.label, node.text].some((value) =>
        value?.toLowerCase().includes(text),
      ),
    )
    .slice(0, 200)
    .map((node) => node.id);
  const expanded = new Set([...was.expanded, ...(matches.length ? ancestors(was, matches[0]) : [])]);
  return { ...was, query, matches, match: matches.length ? 0 : -1, expanded };
}

/** The rows the tree shows: each element below an open one, with how deep it is. */
export function rows(model: Elements): { node: DomNode; depth: number }[] {
  const shown: { node: DomNode; depth: number }[] = [];
  const visit = (id: number, depth: number) => {
    const node = model.nodes.get(id);
    if (!node) return;
    shown.push({ node, depth });
    if (!model.expanded.has(id)) return;
    for (const child of model.children.get(id) ?? []) visit(child, depth + 1);
  };
  if (model.root !== null) visit(model.root, 0);
  return shown;
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
