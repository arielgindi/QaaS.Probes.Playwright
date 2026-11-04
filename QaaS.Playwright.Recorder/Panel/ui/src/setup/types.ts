import type { ModeView } from '../types';

// What the recorder shows on its start screen and once a flow is saved (Setup/SetupView.cs), and what the screen asks.

export type Tab = 'new' | 'continue' | 'recovered';

export interface Draft {
  tab: Tab;
  name: string;
  url: string;
  ifExists: 'unset' | 'continue' | 'replace';
  continueFlow?: string;
  recovered?: string;
  logIn: boolean;
  autoChecks: boolean;
  keepRecording: boolean;
  outputDir: string;
}

export interface SetupView {
  screen: 'setup' | 'done';
  mode: ModeView;
  version: string;
  project?: { folder: string; name: string; found: string; namespace?: string; gone: boolean };
  recent: { folder: string; name: string; when: string; gone: boolean }[];
  draft: Draft;
  /** The class and the file the name becomes. */
  name?: { className: string; target: string };
  /** The address with https:// in front, when it was typed without one. */
  fixUrl?: string;
  urls: { url: string; from: string }[];
  flows: { name: string; detail: string; kept: boolean; why?: string }[];
  recovered: { path: string; flow: string; detail: string; here: boolean }[];
  login: LoginView;
  problems: Record<string, string>;
  willDo: string;
  startLabel: string;
  canStart: boolean;
  /** Chrome is opening. */
  starting: boolean;
  /** Why the last recording did not start, or what went wrong. */
  notice?: string;
  why?: { depth: number; text: string }[];
  browse?: BrowseView;
  done?: DoneView;
}

export interface LoginView {
  state: 'none' | 'looking' | 'found' | 'choose';
  flow?: string;
  detail?: string;
  baseUrl?: string;
  flows: { name: string; detail: string }[];
  projects: { file: string; name: string }[];
  settings?: string;
  settingsFiles: { file: string; name: string }[];
  values: { name: string; from: string; missing: boolean; secret: boolean; value: string }[];
  stale?: string;
  buildCommand?: string;
  building: boolean;
  buildLog?: string;
  problems: string[];
  /** Why no login flow is offered. */
  none?: string;
}

export interface BrowseView {
  path: string;
  parent?: string;
  mode: 'folder' | 'dll' | 'yaml';
  entries: { name: string; path: string; folder: boolean; detail?: string }[];
  places: { name: string; path: string }[];
  /** What the folder is, e.g. that a test project is found in it. */
  note?: string;
  error?: string;
}

export interface DoneView {
  saved: boolean;
  flow: string;
  summary: string;
  files: { path: string; what: string }[];
  notes: string[];
  checks: string[];
  error?: string;
}

export type SetupCommand =
  | { type: 'ready' }
  | { type: 'setup'; field: string; value: string | boolean }
  | { type: 'start' }
  | { type: 'build' }
  | { type: 'stopBuild' }
  | { type: 'refresh' }
  | { type: 'why'; on: boolean }
  | { type: 'browse'; path?: string; mode?: BrowseView['mode'] }
  | { type: 'browseClose' }
  | { type: 'use'; path: string; mode: BrowseView['mode'] }
  | { type: 'forget'; folder: string }
  | { type: 'recovered'; path: string; act: 'save' | 'continue' | 'discard' }
  | { type: 'again' }
  | { type: 'continueSaved' }
  | { type: 'quit' }
  | { type: 'openFolder' }
  | { type: 'prefs'; name: string; value: string };
