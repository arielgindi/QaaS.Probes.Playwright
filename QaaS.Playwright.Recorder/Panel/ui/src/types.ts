// The state the recorder shows the panel (Panel/PanelView.cs): the flow exactly as it will be saved. What is missing
// from the JSON is undefined.

export interface PanelState {
  /** The flow's class name. */
  flow: string;
  /** Where Save writes the flow, e.g. Flows/NewTicket.cs. */
  target: string;
  /** The flow is there already: Save keeps it and saves the recording beside it. */
  kept?: boolean;
  /** Paused by the person, or while the login flow runs, nothing is recorded. */
  status: Status;
  /** Why the recording is paused when the person did not pause it, e.g. the login flow failed. */
  notice?: string;
  /** The login flow the recording runs after. */
  login?: string;
  /** The element picked in the Elements view, located as a recorded action's element is. */
  element?: PickedElement;
  /** The flow's test run going on, or the last one. */
  test?: TestRun;
  /** Whether the flow writes its auto checks: as turned here, else as the command line says. */
  autoChecks?: boolean;
  /** The recorded page is in check mode: a click there picks an element to check. */
  checking?: boolean;
  /** What Undo takes back, e.g. "Restore the click on “Save”"; none when there is nothing. */
  undo?: string;
  /** The recording this one continues: a divider after its last action left, and a banner while it replays. */
  continued?: { label: string; after?: string; banner?: string };
  code: string;
  yaml: string;
  steps: Step[];
  /** Every line to check by hand; one without a line is about the whole flow. */
  checks: Check[];
  /** Why the last command did not work, e.g. a name a flow cannot have. */
  error?: string;
}

export type Status = 'recording' | 'paused' | 'login' | 'testing' | 'saving';

/** A test run of the flow as recorded so far (Panel/PanelView.cs, PanelTest). */
export interface TestRun {
  /** Changes with each run. */
  run: number;
  state: TestState;
  /** How many actions the run goes through: all, or up to upTo. */
  total: number;
  /** The action "Run to here" runs to, including it. */
  upTo?: string;
  slowMotion?: boolean;
  /** The login flow that runs first. */
  login?: string;
  /** When it started, in Unix milliseconds. */
  started: number;
  /** How long the browser's part took, once it ended. */
  ms?: number;
  /** The lines of the statement running; in a loop, of its body's statement, which every repeat runs. */
  running?: Lines;
  /** Each action's state, by its id. */
  steps: Record<string, TestStep>;
  failure?: TestFailure;
  /** Why it could not run, e.g. no .NET SDK, and more about it. */
  problem?: string;
  problemDetail?: string;
  warnings: string[];
  /** The flow changed since: the results are of the code as it was then. */
  stale?: boolean;
}

export type TestState = 'building' | 'login' | 'running' | 'passed' | 'failed' | 'stopped' | 'error';

export interface TestStep {
  state: 'running' | 'passed' | 'failed' | 'skipped';
  ms?: number;
  /** When it began, in Unix milliseconds, while it runs. */
  since?: number;
}

export interface TestFailure {
  /** The failure on one line, without Playwright's call log. */
  message: string;
  /** As the probe's report says it: with what it waited for, on which page. */
  headline: string;
  /** The action whose lines failed; none when the run failed before its first. */
  action?: string;
  /** The line of the code that failed, as it will be saved. */
  line?: number;
  waitingFor?: string;
  url?: string;
  /** The probe's failure screenshot, as a data: URL. */
  screenshot?: string;
  callLog?: string;
  /** The login flow, when it is what failed. */
  inLogin?: string;
  /** What the recorder listed to check by hand in the action's lines. */
  checks: string[];
  hints: string[];
}

export interface Lines {
  first: number;
  last: number;
}

export interface Check {
  line?: number;
  message: string;
}

/** A method of the flow: the actions on one page or in one dialog. */
export interface Step {
  name: string;
  dialog?: string;
  lines?: Lines;
  actions: Action[];
}

export interface Action {
  id: string;
  kind: string;
  widget?: string;
  /** What the element is called on the page; may be right-to-left. */
  element?: string;
  /** What was typed, chosen, pressed or opened; may be right-to-left. */
  value?: string;
  /** The locator the flow uses, as a person reads it. */
  locator?: string;
  page: string;
  dialog?: string;
  lines?: Lines;
  effects: Effect[];
  checks: string[];
  settings: Setting[];
  candidates: Candidate[];
  /** The candidate the person chose, the one the flow uses, and the one the recorder would use. */
  chosen?: number;
  picked?: number;
  auto?: number;
  /** Added in the Elements view rather than done in the app. */
  added?: boolean;
  /** The checks after it: its auto checks, those dropped too, and those the person chose. */
  expects?: Expect[];
}

/** A check after an action: an auto check by its key, or one the person chose, by its id. */
export interface Expect {
  key: string;
  /** What it expects, e.g. "dialog closes", "has text “Save”". */
  text: string;
  /** Of what, for a check the person chose: the element as its locator reads. */
  of?: string;
  auto: boolean;
  dropped?: boolean;
}

export interface PickedElement {
  node: number;
  page: string;
  name?: string;
  tag: string;
  locator?: string;
  dialog?: string;
  candidates: Candidate[];
  chosen?: number;
  picked?: number;
  auto?: number;
  /** Not on the page now, e.g. an item of a menu that closed: its locators were found when frozen. */
  gone?: boolean;
  fillable?: boolean;
}

/** An element of the recorded page, as the Elements view lists it. */
export interface DomNode {
  id: number;
  parent?: number;
  /** The tag, or #shadow-root. */
  tag: string;
  elementId?: string;
  classes?: string;
  role?: string;
  testId?: string;
  name?: string;
  type?: string;
  /** aria-label, or placeholder. */
  label?: string;
  /** Its own text, not its children's; may be right-to-left. */
  text?: string;
  /** How many children it has. */
  count: number;
  hidden?: boolean;
  fillable?: boolean;
}

/** Nodes, and the children of those whose children came with them. */
export interface DomPatch {
  root: number;
  nodes: DomNode[];
  children: Record<string, number[]>;
  matches?: number[];
  truncated?: boolean;
}

/** What the recorder sends the Elements view. */
export type ElementsMessage =
  | { kind: 'tree' | 'search'; page: string; url: string; data: DomPatch }
  | { kind: 'frozen'; page: string; at: string; data: DomPatch; described: number[] }
  | { kind: 'changed' };

export interface Effect {
  kind: string;
  text?: string;
}

export interface Setting {
  name: string;
  /** As YAML: quoted text, a number or a flag. */
  value: string;
  /** Its part in the action, which an edit names: value, search, option, shown or row. */
  role: string;
  /** Its value as text, to edit; for a date relative to today, the days. */
  text?: string;
  type: 'string' | 'bool' | 'int' | 'decimal';
  /** It may be renamed and typed another way; in a loop only its value may change. */
  renamable: boolean;
  /** Whether it is typed unique per run; absent when it cannot be. */
  unique?: boolean;
  /** The date relative to today it is typed as. */
  relative?: DateShift;
  /** A date typed within a year of the recording: what typing it relative to today gives. */
  suggest?: DateShift;
  /** The value recorded, when it was given another. */
  recorded?: string;
}

export interface DateShift {
  format: string;
  days: number;
}

export interface Candidate {
  index: number;
  kind: string;
  label: string;
  code: string;
  count: number;
  /** Why it cannot be the locator, e.g. "found 2 (1 hidden)" or "looks generated". */
  rejection?: string;
  /** Whether it may be chosen at all: false for a generated id or an unknown role. */
  usable: boolean;
  /** Why it needs a look even so. */
  problem?: string;
}

/** What the panel asks the recorder to do. */
export type Command =
  | { type: 'ready' }
  | { type: 'choose'; action: string; candidate: number | null }
  | { type: 'highlight'; action: string; candidate?: number }
  | { type: 'highlight'; node: number; frozen: boolean }
  | { type: 'highlight'; candidate: number }
  | { type: 'unhighlight' }
  | { type: 'undo' }
  | { type: 'delete'; action: string }
  | { type: 'renameStep'; action: string; name: string }
  | { type: 'editValue'; action: string; role: string; name?: string; value?: string; reset?: boolean; unique?: boolean; relative?: boolean }
  | { type: 'dropCheck'; action: string; key: string; drop: boolean }
  | { type: 'autoChecks'; on: boolean }
  | { type: 'checkMode'; on?: boolean }
  | { type: 'rename'; name: string }
  | { type: 'save' }
  | { type: 'pause' }
  | { type: 'test'; action?: string; slow: boolean }
  | { type: 'stopTest' }
  | { type: 'clearTest' }
  | { type: 'elements'; op: 'tree'; ids: number[]; on: boolean }
  | { type: 'elements'; op: 'search'; query: string; on: boolean }
  | { type: 'elements'; op: 'watch'; on: boolean }
  | { type: 'elements'; op: 'frozen' }
  | { type: 'pick'; node?: number; frozen?: boolean }
  | { type: 'choosePicked'; candidate: number | null }
  | { type: 'useForStep'; action: string }
  | { type: 'addClick'; action?: string }
  | { type: 'addFill'; action?: string; value: string };
