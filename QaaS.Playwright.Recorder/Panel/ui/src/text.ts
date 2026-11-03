import type { Action, Candidate, Effect } from './types';

const verbs: Record<string, string> = {
  click: 'Click',
  dblclick: 'Double-click',
  fill: 'Fill',
  check: 'Tick',
  uncheck: 'Untick',
  choose: 'Choose',
  press: 'Press',
  upload: 'Upload to',
  drag: 'Drag',
  navigate: 'Go to',
  newTab: 'Open a tab',
};

/** What the person did, as a verb: Click, Fill, Choose, Tick (a checkbox; Check is a check of what the page shows)... */
export function verbOf(action: Action): string {
  if (action.kind === 'check' && action.widget === 'radio') return 'Pick';
  return verbs[action.kind] ?? action.kind;
}

/** What the action acted on, as the page names it. */
export function subjectOf(action: Action): string {
  if (action.kind === 'press' && !action.element) return action.value ?? 'a key';
  if (action.kind === 'navigate' || action.kind === 'newTab') return action.value ?? 'a page';
  return action.element || action.locator || 'an element';
}

/** The value worth showing under the subject: what was typed, chosen or pressed, if not the subject already. */
export function detailOf(action: Action): string | undefined {
  if (action.kind === 'navigate' || action.kind === 'newTab') return undefined;
  if (action.kind === 'press') return action.element ? action.value : undefined;
  return action.value || undefined;
}

const candidateKinds: Record<string, string> = {
  name: 'Form name',
  nameValue: 'Name and value',
  id: 'Id',
  select: 'Select field',
  testid: 'Test id',
  role: 'Role and name',
  roleInGroup: 'Role in its group',
  row: 'Table row',
  generator: "Playwright's locator",
};

export function candidateKind(candidate: Candidate): string {
  if (candidate.kind === 'role' && candidate.code.includes('NameRegex')) return 'Role and start of name';
  if (candidate.kind === 'role' && candidate.code.includes('Exact = true')) return 'Role and exact name';
  return candidateKinds[candidate.kind] ?? candidate.kind;
}

/** The candidate's status: ✓ 1 match, ✗ found 3, ✗ looks generated. */
export function candidateStatus(candidate: Candidate): { ok: boolean; text: string } {
  if (candidate.rejection) return { ok: false, text: candidate.rejection };
  return { ok: true, text: candidate.count === 1 ? '1 match' : `${candidate.count} matches` };
}

/** A candidate that is found by nothing stable, which the flow must never use. */
export function isUnusable(candidate: Candidate): boolean {
  return !candidate.usable;
}

const effects: Record<string, string> = {
  dialogOpened: 'dialog opened',
  dialogClosed: 'dialog closed',
  urlChanged: 'URL',
  alert: 'alert',
  tabSelected: 'tab selected',
  popup: 'pop-up opened',
  jsDialog: 'browser dialog',
};

export function effectLabel(effect: Effect): string {
  return effects[effect.kind] ?? effect.kind;
}

/** Why the locators before the one chosen lost, without those the element simply has none of: "role: found 3". */
export function shortReasons(reasons?: string[]): string {
  return (reasons ?? []).filter((reason) => !/: (none|no name)$/.test(reason)).slice(0, 3).join('  ·  ');
}

export function plural(count: number, one: string, many = `${one}s`): string {
  return `${count} ${count === 1 ? one : many}`;
}

export function cls(...names: (string | false | null | undefined)[]): string {
  return names.filter(Boolean).join(' ');
}
