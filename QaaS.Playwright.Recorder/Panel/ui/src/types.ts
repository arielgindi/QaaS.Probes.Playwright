// The state the recorder shows the panel (Panel/PanelView.cs): the flow exactly as it will be saved. What is missing
// from the JSON is undefined.

export interface PanelState {
  /** The flow's class name. */
  flow: string;
  /** Where Save writes the flow, e.g. Flows/NewTicket.cs. */
  target: string;
  /** The flow is there already: Save keeps it and saves the recording beside it. */
  kept?: boolean;
  status: 'recording' | 'saving';
  code: string;
  yaml: string;
  steps: Step[];
  /** Every line to check by hand; one without a line is about the whole flow. */
  checks: Check[];
  /** Why the last command did not work, e.g. a name a flow cannot have. */
  error?: string;
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
}

export interface Effect {
  kind: string;
  text?: string;
}

export interface Setting {
  name: string;
  value: string;
}

export interface Candidate {
  index: number;
  kind: string;
  label: string;
  code: string;
  count: number;
  /** Why it cannot be the locator, e.g. "3 matches" or "generated id". */
  rejection?: string;
  /** Why it needs a look even so. */
  problem?: string;
}

/** What the panel asks the recorder to do. */
export type Command =
  | { type: 'ready' }
  | { type: 'choose'; action: string; candidate: number | null }
  | { type: 'highlight'; action: string; candidate?: number }
  | { type: 'unhighlight' }
  | { type: 'undo' }
  | { type: 'rename'; name: string }
  | { type: 'save' };
