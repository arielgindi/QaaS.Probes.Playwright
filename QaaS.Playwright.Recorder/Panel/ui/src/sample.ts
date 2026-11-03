import { isGoing } from './test';
import type { Command, DomNode, DomPatch, PanelState, PickedElement, TestRun } from './types';

// A recording of a helpdesk's ticket form, as the recorder shows it, for working on the panel with `npm run dev`.
const sample: PanelState = {
  "flow": "NewTicket",
  "target": "Flows/NewTicket.cs",
  "status": "recording",
  "code": "using System.Text.RegularExpressions;\nusing Microsoft.Playwright;\nusing QaaS.Playwright;\nusing static Microsoft.Playwright.Assertions;\n\nnamespace Helpdesk.Tests.Flows;\n\n/// <summary>\n/// Recorded on https://helpdesk.example/helpdesk. The values it types and chooses are the settings in\n/// <see cref=\"NewTicketConfig\"/>: give each case its own under FlowConfiguration:NewTicket:.\n/// </summary>\npublic sealed class NewTicket : BasePlaywrightFlow<NewTicketConfig>\n{\n    public override async Task RunAsync(IPage page)\n    {\n        var dialog = await OpenNewTicketAsync(page);\n        await FillNewTicketAsync(dialog);\n        await CreateTicketAsync(dialog);\n    }\n\n    private async Task<ILocator> OpenNewTicketAsync(IPage page)\n    {\n        await page.GetByRole(AriaRole.Row).Filter(new() { HasText = Configuration.Ticket }).GetByRole(AriaRole.Button, new() { Name = \"Assign to me\", Exact = true }).ClickAsync();\n        await Expect(page.GetByRole(AriaRole.Alert).Filter(new() { HasText = $\"{Configuration.Ticket} assigned to you\" })).ToBeVisibleAsync();\n        await page.GetByTestId(\"new-ticket\").ClickAsync();\n        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = \"New ticket\" });\n        await Expect(dialog).ToBeVisibleAsync();\n        return dialog;\n    }\n\n    private async Task FillNewTicketAsync(ILocator dialog)\n    {\n        await dialog.Locator(\"[name='title']\").FillAsync(Configuration.Title);\n        await dialog.Locator(\"[name='category']\").SelectOptionAsync(new SelectOptionValue { Label = Configuration.Category });\n        await dialog.GetByRole(AriaRole.Radio, new() { Name = Configuration.Priority, Exact = true }).CheckAsync();\n    }\n\n    private static async Task CreateTicketAsync(ILocator dialog)\n    {\n        await dialog.GetByRole(AriaRole.Button, new() { Name = \"Create ticket\", Exact = true }).ClickAsync();\n        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 30000 });\n        await Expect(dialog.Page).ToHaveURLAsync(new Regex(\"/tickets/[^/]+$\"));\n        await Expect(dialog.Page.GetByRole(AriaRole.Alert).Filter(new() { HasText = \"Ticket\" })).ToBeVisibleAsync();\n    }\n}\n\n/// <summary>The values recorded in <see cref=\"NewTicket\"/>; FlowConfiguration:NewTicket: sets them per case.</summary>\npublic sealed record NewTicketConfig\n{\n    public string Ticket { get; init; } = \"TCK-1042\";\n    public string Title { get; init; } = \"Printer on floor 3 is jammed\";\n    public string Category { get; init; } = \"Software\";\n    public string Priority { get; init; } = \"High\";\n}\n",
  "yaml": "# The values recorded in NewTicket. Put this block under the ProbeConfiguration of the probe\n# that runs the flow, and change the values for each case.\nFlowConfiguration:\n  NewTicket:\n    Ticket: 'TCK-1042'\n    Title: 'Printer on floor 3 is jammed'\n    Category: 'Software'\n    Priority: 'High'\n",
  "steps": [
    {
      "name": "OpenNewTicketAsync",
      "lines": {
        "first": 21,
        "last": 29
      },
      "actions": [
        {
          "id": "a1",
          "kind": "click",
          "element": "Assign to me",
          "locator": "row » \"TCK-1042\" » button \"Assign to me\"",
          "page": "page",
          "lines": {
            "first": 23,
            "last": 24
          },
          "effects": [
            {
              "kind": "alert",
              "text": "TCK-1042 assigned to you"
            }
          ],
          "checks": [],
          "settings": [
            {
              "name": "Ticket",
              "value": "'TCK-1042'",
              "role": "row",
              "text": "TCK-1042",
              "type": "string",
              "renamable": true
            }
          ],
          "candidates": [
            {
              "index": 0,
              "kind": "role",
              "label": "button \"Assign to me\"",
              "code": "page.GetByRole(AriaRole.Button, new() { Name = \"Assign to me\", Exact = true })",
              "count": 3,
              "rejection": "found 3",
              "usable": true
            },
            {
              "index": 1,
              "kind": "role",
              "label": "button \"Assign to me\"",
              "code": "page.GetByRole(AriaRole.Button, new() { Name = \"Assign to me\" })",
              "count": 3,
              "rejection": "found 3",
              "usable": true
            },
            {
              "index": 2,
              "kind": "row",
              "label": "row » \"TCK-1042\" » button \"Assign to me\"",
              "code": "page.GetByRole(AriaRole.Row).Filter(new() { HasText = \"TCK-1042\" }).GetByRole(AriaRole.Button, new() { Name = \"Assign to me\", Exact = true })",
              "count": 1,
              "usable": true
            },
            {
              "index": 3,
              "kind": "generator",
              "label": "GetByRole(AriaRole.Row, new() { Name = \"TCK-1042 Laptop for a new\" }).GetByRole(AriaRole.Button)",
              "code": "page.GetByRole(AriaRole.Row, new() { Name = \"TCK-1042 Laptop for a new\" }).GetByRole(AriaRole.Button)",
              "count": 1,
              "usable": true
            }
          ],
          "picked": 2,
          "auto": 2
        },
        {
          "id": "a2",
          "kind": "click",
          "element": "New ticket",
          "locator": "test id \"new-ticket\"",
          "page": "page",
          "lines": {
            "first": 25,
            "last": 28
          },
          "effects": [
            {
              "kind": "dialogOpened",
              "text": "New ticket"
            }
          ],
          "checks": [],
          "settings": [],
          "candidates": [
            {
              "index": 0,
              "kind": "id",
              "label": "[id=':r5:']",
              "code": "page.Locator(\"[id=':r5:']\")",
              "count": 1,
              "rejection": "looks generated",
              "usable": false
            },
            {
              "index": 1,
              "kind": "testid",
              "label": "test id \"new-ticket\"",
              "code": "page.GetByTestId(\"new-ticket\")",
              "count": 1,
              "usable": true
            },
            {
              "index": 2,
              "kind": "role",
              "label": "button \"New ticket\"",
              "code": "page.GetByRole(AriaRole.Button, new() { Name = \"New ticket\", Exact = true })",
              "count": 1,
              "usable": true
            },
            {
              "index": 3,
              "kind": "generator",
              "label": "GetByTestId(\"new-ticket\")",
              "code": "page.GetByTestId(\"new-ticket\")",
              "count": 1,
              "usable": true
            }
          ],
          "picked": 1,
          "auto": 1
        }
      ]
    },
    {
      "name": "FillNewTicketAsync",
      "dialog": "New ticket",
      "lines": {
        "first": 31,
        "last": 36
      },
      "actions": [
        {
          "id": "a3",
          "kind": "fill",
          "widget": "text",
          "element": "Title",
          "value": "Printer on floor 3 is jammed",
          "locator": "[name='title']",
          "page": "page",
          "dialog": "New ticket",
          "lines": {
            "first": 33,
            "last": 33
          },
          "effects": [],
          "checks": [],
          "suggestions": [
            {
              "key": "replaced:a3",
              "text": "Title is filled again later (“Printer jammed”) before anything used “Printer on floor 3 is jammed”: the flow needs only the last.",
              "apply": "Leave it out"
            }
          ],
          "settings": [
            {
              "name": "Title",
              "value": "'Printer on floor 3 is jammed'",
              "role": "value",
              "text": "Printer on floor 3 is jammed",
              "type": "string",
              "renamable": true,
              "unique": false
            }
          ],
          "candidates": [
            {
              "index": 0,
              "kind": "name",
              "label": "[name='title']",
              "code": "page.GetByRole(AriaRole.Dialog, new() { Name = \"New ticket\" }).Locator(\"[name='title']\")",
              "count": 1,
              "usable": true
            },
            {
              "index": 1,
              "kind": "role",
              "label": "textbox \"Title\"",
              "code": "page.GetByRole(AriaRole.Dialog, new() { Name = \"New ticket\" }).GetByRole(AriaRole.Textbox, new() { Name = \"Title\", Exact = true })",
              "count": 1,
              "usable": true
            },
            {
              "index": 2,
              "kind": "generator",
              "label": "GetByRole(AriaRole.Textbox, new() { Name = \"Title\" })",
              "code": "page.GetByRole(AriaRole.Textbox, new() { Name = \"Title\" })",
              "count": 1,
              "usable": true
            }
          ],
          "picked": 0,
          "auto": 0
        },
        {
          "id": "a4",
          "kind": "choose",
          "widget": "native",
          "element": "Category",
          "value": "Software",
          "locator": "[name='category']",
          "page": "page",
          "dialog": "New ticket",
          "lines": {
            "first": 34,
            "last": 34
          },
          "effects": [],
          "checks": [],
          "settings": [
            {
              "name": "Category",
              "value": "'Software'",
              "role": "value",
              "text": "Software",
              "type": "string",
              "renamable": true
            }
          ],
          "candidates": [
            {
              "index": 0,
              "kind": "name",
              "label": "[name='category']",
              "code": "page.GetByRole(AriaRole.Dialog, new() { Name = \"New ticket\" }).Locator(\"[name='category']\")",
              "count": 1,
              "usable": true
            },
            {
              "index": 1,
              "kind": "role",
              "label": "combobox \"Category\"",
              "code": "page.GetByRole(AriaRole.Dialog, new() { Name = \"New ticket\" }).GetByRole(AriaRole.Combobox, new() { Name = \"Category\", Exact = true })",
              "count": 1,
              "usable": true
            },
            {
              "index": 2,
              "kind": "generator",
              "label": "GetByLabel(\"Category\")",
              "code": "page.GetByLabel(\"Category\")",
              "count": 1,
              "usable": true
            }
          ],
          "picked": 0,
          "auto": 0
        },
        {
          "id": "a5",
          "kind": "check",
          "widget": "radio",
          "element": "High",
          "locator": "radio \"High\"",
          "page": "page",
          "dialog": "New ticket",
          "lines": {
            "first": 35,
            "last": 35
          },
          "effects": [],
          "checks": [],
          "settings": [
            {
              "name": "Priority",
              "value": "'High'",
              "role": "value",
              "text": "High",
              "type": "string",
              "renamable": true
            }
          ],
          "candidates": [
            {
              "index": 0,
              "kind": "name",
              "label": "[name='priority']",
              "code": "page.GetByRole(AriaRole.Dialog, new() { Name = \"New ticket\" }).Locator(\"[name='priority']\")",
              "count": 3,
              "rejection": "found 3",
              "usable": true
            },
            {
              "index": 1,
              "kind": "nameValue",
              "label": "[name='priority'][value='high']",
              "code": "page.GetByRole(AriaRole.Dialog, new() { Name = \"New ticket\" }).Locator(\"[name='priority'][value='high']\")",
              "count": 1,
              "usable": true
            },
            {
              "index": 2,
              "kind": "role",
              "label": "radio \"High\"",
              "code": "page.GetByRole(AriaRole.Dialog, new() { Name = \"New ticket\" }).GetByRole(AriaRole.Radio, new() { Name = \"High\", Exact = true })",
              "count": 1,
              "usable": true
            },
            {
              "index": 3,
              "kind": "roleInGroup",
              "label": "fieldset:has([name='priority']) » radio \"High\"",
              "code": "page.GetByRole(AriaRole.Dialog, new() { Name = \"New ticket\" }).Locator(\"fieldset:has([name='priority'])\").GetByRole(AriaRole.Radio, new() { Name = \"High\", Exact = true })",
              "count": 1,
              "usable": true
            },
            {
              "index": 4,
              "kind": "generator",
              "label": "GetByRole(AriaRole.Radio, new() { Name = \"High\" })",
              "code": "page.GetByRole(AriaRole.Radio, new() { Name = \"High\" })",
              "count": 1,
              "usable": true
            }
          ],
          "picked": 2,
          "auto": 2
        }
      ]
    },
    {
      "name": "CreateTicketAsync",
      "dialog": "New ticket",
      "lines": {
        "first": 38,
        "last": 44
      },
      "actions": [
        {
          "id": "a6",
          "kind": "click",
          "element": "Create ticket",
          "locator": "button \"Create ticket\"",
          "page": "page",
          "dialog": "New ticket",
          "lines": {
            "first": 40,
            "last": 43
          },
          "effects": [
            {
              "kind": "dialogClosed",
              "text": "New ticket"
            },
            {
              "kind": "alert",
              "text": "Ticket TCK-1043 created"
            },
            {
              "kind": "urlChanged",
              "text": "/tickets/[id]"
            }
          ],
          "checks": [],
          "settings": [],
          "candidates": [
            {
              "index": 0,
              "kind": "role",
              "label": "button \"Create ticket\"",
              "code": "page.GetByRole(AriaRole.Dialog, new() { Name = \"New ticket\" }).GetByRole(AriaRole.Button, new() { Name = \"Create ticket\", Exact = true })",
              "count": 1,
              "usable": true
            },
            {
              "index": 1,
              "kind": "generator",
              "label": "GetByRole(AriaRole.Button, new() { Name = \"Create ticket\" })",
              "code": "page.GetByRole(AriaRole.Button, new() { Name = \"Create ticket\" })",
              "count": 1,
              "usable": true
            }
          ],
          "picked": 0,
          "auto": 0
        }
      ]
    }
  ],
  "checks": [],
  "kept": false
};

// The checks after the sample's actions: auto checks, one dropped, and one the person chose; the title may be typed
// unique per run.
{
  const byId = Object.fromEntries(sample.steps.flatMap((step) => step.actions).map((action) => [action.id, action]));
  byId.a1.expects = [{ key: 'alert:TCK-1042 assigned to you', text: 'alert “TCK-1042 assigned to you”', auto: true }];
  byId.a2.expects = [{ key: 'opened', text: 'dialog shows', auto: true }];
  byId.a6.expects = [
    { key: 'closed:New ticket', text: 'dialog closes', auto: true },
    { key: 'url', text: 'URL /tickets/[id]', auto: true, dropped: true },
    { key: 'alert:Ticket TCK-1043 created', text: 'alert “Ticket TCK-1043 created”', auto: true },
    { key: 'c1', text: 'a row of “Printer on floor 3 is jammed”', auto: false },
  ];
  Object.assign(sample, { autoChecks: true, undo: 'Remove the last step' });
}

// The helpdesk's page as the Elements view lists it: a table of tickets, and a row's menu, open in a portal.
type Spec = [tag: string, attributes: Partial<DomNode>, children?: Spec[]];
const page: Spec = ['body', {}, [
  ['div', { elementId: 'root', classes: 'app css-1q2w3e' }, [
    ['header', { classes: 'MuiAppBar-root MuiPaper-root css-k008qs', role: 'banner' }, [
      ['h1', { classes: 'title', text: 'Helpdesk' }],
      ['button', { elementId: ':r5:', testId: 'new-ticket', classes: 'MuiButton-root MuiButton-contained', text: 'New ticket' }],
    ]],
    ['main', { classes: 'content' }, [
      ['table', { classes: 'MuiTable-root' }, [
        ['thead', {}, [['tr', {}, [['th', { text: 'Ticket' }], ['th', { text: 'Subject' }], ['th', { text: 'Owner' }]]]]],
        ['tbody', {}, ['TCK-1040 Password reset', 'TCK-1041 VPN drops every hour', 'TCK-1042 Laptop for a new starter'].map((row) => {
          const [key, ...subject] = row.split(' ');
          return ['tr', { classes: 'MuiTableRow-root', implicit: 'row', accName: `${row} ⋮` }, [['td', { text: key, implicit: 'cell' }], ['td', { text: subject.join(' '), implicit: 'cell' }],
            ['td', {}, [['button', { label: `Actions for ${key}`, classes: 'MuiIconButton-root', text: '⋮' }]]]]] as Spec;
        })],
      ]],
    ]],
  ]],
  ['div', { classes: 'MuiPopover-root MuiMenu-root', role: 'presentation' }, [
    ['div', { classes: 'MuiBackdrop-root MuiBackdrop-invisible', hidden: true }],
    ['div', { classes: 'MuiPaper-root MuiMenu-paper css-3dzjca' }, [
      ['ul', { role: 'menu', classes: 'MuiList-root MuiMenu-list', label: 'Ticket actions' }, ['Edit', 'Assign to me', 'Close ticket'].map(
        (item) => ['li', { role: 'menuitem', classes: 'MuiMenuItem-root MuiButtonBase-root', text: item }] as Spec,
      )],
    ]],
  ]],
]];

function patch(): DomPatch {
  const nodes: DomNode[] = [];
  const children: Record<string, number[]> = {};
  let next = 0;
  const add = ([tag, attributes, kids = []]: Spec, parent?: number): number => {
    const id = ++next;
    nodes.push({ id, parent, tag, count: kids.length, ...attributes });
    children[id] = kids.map((kid) => add(kid, id));
    return id;
  };
  return { root: add(page), nodes, children };
}

const closeTicket: PickedElement = {
  node: 0,
  page: 'page',
  name: 'Close ticket',
  tag: 'li',
  locator: 'menuitem "Close ticket"',
  gone: true,
  picked: 0,
  auto: 0,
  candidates: [
    { index: 0, kind: 'role', label: 'menuitem "Close ticket"', code: 'page.GetByRole(AriaRole.Menuitem, new() { Name = "Close ticket", Exact = true })', count: 1, usable: true },
    { index: 1, kind: 'generator', label: 'GetByRole(AriaRole.Menuitem, new() { Name = "Close ticket" })', code: 'page.GetByRole(AriaRole.Menuitem, new() { Name = "Close ticket" })', count: 1, usable: true },
  ],
  // The lines each step on it would write, as the recorder previews them.
  previews: [
    {
      kind: 'use',
      was: 'await page.GetByRole(AriaRole.Button, new() { Name = "Actions for TCK-1042" }).ClickAsync();',
      code: 'await page.GetByRole(AriaRole.Menuitem, new() { Name = "Close ticket", Exact = true }).ClickAsync();',
    },
    { kind: 'click', code: 'await page.GetByRole(AriaRole.Menuitem, new() { Name = "Close ticket", Exact = true }).ClickAsync();' },
    { kind: 'check', code: 'await Expect(page.GetByRole(AriaRole.Menuitem, new() { Name = "Close ticket", Exact = true })).ToHaveTextAsync("Close ticket"); // check' },
  ],
  checks: [
    { kind: 'text', name: 'Has text', value: 'Close ticket' },
    { kind: 'visible', name: 'Visible' },
    { kind: 'hidden', name: 'Gone' },
  ],
  check: 0,
  primary: 'click',
};

// A failure screenshot: the helpdesk's ticket dialog, drawn small.
const screenshot = `data:image/svg+xml;utf8,${encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" width="1920" height="1080" viewBox="0 0 1920 1080">
  <rect width="1920" height="1080" fill="#f4f5f7"/><rect width="1920" height="72" fill="#1f2a44"/>
  <text x="40" y="47" font-family="Segoe UI, sans-serif" font-size="28" fill="#fff">Helpdesk</text>
  <rect x="40" y="120" width="1840" height="900" rx="10" fill="#fff" stroke="#dfe2e7"/>
  ${[0, 1, 2].map((row) => `<rect x="80" y="${200 + row * 90}" width="1760" height="64" rx="6" fill="${row === 2 ? '#eef2ff' : '#fafbfc'}" stroke="#e5e7eb"/><text x="110" y="${241 + row * 90}" font-family="Segoe UI, sans-serif" font-size="24" fill="#374151">TCK-104${row} · ${['Password reset', 'VPN drops every hour', 'Laptop for a new starter'][row]}</text>`).join('')}
  <rect x="0" y="0" width="1920" height="1080" fill="#0b1020" opacity="0.35"/>
  <rect x="610" y="250" width="700" height="520" rx="14" fill="#fff"/>
  <text x="650" y="315" font-family="Segoe UI, sans-serif" font-size="30" font-weight="600" fill="#111827">New ticket</text>
  <rect x="650" y="360" width="620" height="56" rx="8" fill="#fff" stroke="#d1d5db"/><text x="670" y="396" font-family="Segoe UI, sans-serif" font-size="22" fill="#6b7280">Title</text>
  <rect x="650" y="440" width="620" height="56" rx="8" fill="#fff" stroke="#d1d5db"/><text x="670" y="476" font-family="Segoe UI, sans-serif" font-size="22" fill="#6b7280">Category</text>
  <rect x="1090" y="690" width="180" height="52" rx="8" fill="#4f46e5"/><text x="1118" y="724" font-family="Segoe UI, sans-serif" font-size="22" fill="#fff">Create ticket</text>
</svg>`)}`;

const order = ['a1', 'a2', 'a3', 'a4', 'a5', 'a6'];

const failure: TestRun['failure'] = {
  message: 'Timeout 30000ms exceeded.',
  headline: `Timeout 30000ms exceeded (waiting for Locator("[name='category']")) on https://helpdesk.example/helpdesk`,
  action: 'a4',
  line: 34,
  waitingFor: `Locator("[name='category']")`,
  url: 'https://helpdesk.example/helpdesk',
  screenshot,
  callLog: `- waiting for Locator("[name='category']")\n- locator resolved to <select name="category">…</select>\n- attempting select option action\n- waiting for element to be visible and enabled\n- element is not enabled\n- retrying select option action`,
  checks: [],
  hints: [],
};

/** A test run of the sample in one of its states, as `?test=<state>` shows it. */
function testIn(state: string): TestRun | undefined {
  const at = (count: number, last?: TestRun['steps'][string]) =>
    Object.fromEntries(order.slice(0, count).map((id, index) => [id, index === count - 1 && last ? last : { state: 'passed' as const, ms: [412, 186, 233, 145, 98, 1290][index] }]));
  const started = Date.now() - 2300;
  switch (state) {
    case 'building':
    case 'window':
    case 'start':
      return { run: 1, state, total: 6, started, since: Date.now() - 400, steps: {}, warnings: [] };
    case 'running':
      return { run: 1, state: 'running', total: 6, started, steps: at(3, { state: 'running', since: Date.now() - 700 }), warnings: [], slowMotion: true };
    case 'passed':
      return { run: 1, state: 'passed', total: 6, started, ms: 2364, steps: at(6), warnings: [] };
    case 'failed':
      return { run: 1, state: 'failed', total: 6, started, ms: 31840, steps: { ...at(3), a4: { state: 'failed', ms: 30012 }, a5: { state: 'skipped' }, a6: { state: 'skipped' } }, failure, warnings: [] };
    case 'error':
      return {
        run: 1, state: 'error', total: 6, started, steps: {}, warnings: [],
        problem: 'Testing builds the flow with the .NET 10 SDK, which is not installed here (recording needs only the .NET runtime). Install the SDK from https://dot.net, then test again.',
      };
    default:
      return undefined;
  }
}

// `?mode=` opens on a mode: paused, trying, login, checking, replaying, rerecording or done.
function modeIn(mode: string): Partial<PanelState> {
  const part = { part: 'FillNewTicketAsync', after: false, fresh: [] as string[], at: 'a3' };
  switch (mode) {
    case 'paused':
      return { status: 'paused' };
    case 'trying':
      return { status: 'trying', tried: [{ ...sample.steps[0].actions[0], id: 't1', tried: true, reasons: ['id: none', 'testid: none', 'role: found 3'] }] };
    case 'login':
      return { status: 'login', login: 'LoginFlow', steps: [], code: '', loginProgress: { text: 'Logging in with LoginFlow… 4 s, on /signin', last: 'typed in “Username”', slow: false } };
    case 'checking':
      return { checking: true };
    case 'replaying':
      return { status: 'testing', rerecord: { ...part, phase: 'replaying' } };
    case 'rerecording':
      return { rerecord: { ...part, phase: 'recording', fresh: ['a4'] } };
    case 'done':
      return { status: 'paused', rerecord: { ...part, phase: 'done', paused: true } };
    default:
      return {};
  }
}

/** Shows the sample, and logs the commands the panel would send the recorder, answering those about the page's elements. */
export function start(): void {
  const tree = patch();
  const menuItems = tree.nodes.filter((node) => node.role === 'menuitem').map((node) => node.id);
  const initial = testIn(new URLSearchParams(location.search).get('test') ?? '');
  const going = isGoing(initial);
  let current: PanelState = { ...sample, status: going ? 'testing' : sample.status, test: initial, ...modeIn(new URLSearchParams(location.search).get('mode') ?? '') };
  const show = (state: PanelState) => {
    current = state;
    window.__qaasPanelShow?.(JSON.stringify(state));
  };
  // A test run that goes through the steps as the recorder's would, and fails at the Category with `?fail`.
  let timer = 0;
  const simulate = (upTo: string | undefined, slow: boolean) => {
    const total = upTo ? order.indexOf(upTo) + 1 : order.length;
    const run: TestRun = { run: (current.test?.run ?? 0) + 1, state: 'building', total, upTo, slowMotion: slow, started: Date.now(), since: Date.now(), steps: {}, warnings: [] };
    show({ ...current, status: 'testing', test: run });
    const phase = (state: TestRun['state']) => show({ ...current, test: { ...current.test!, state, since: Date.now() } });
    let index = -1;
    let began = Date.now();
    const next = () => {
      const steps = { ...current.test!.steps };
      if (index >= 0) steps[order[index]] = { state: 'passed', ms: Date.now() - began };
      index++;
      if (location.search.includes('fail') && order[index] === 'a4') {
        steps.a4 = { state: 'failed', ms: 1200 };
        for (const id of order.slice(index + 1)) steps[id] = { state: 'skipped' };
        show({ ...current, status: 'recording', test: { ...current.test!, state: 'failed', steps, failure, ms: Date.now() - run.started } });
        return;
      }
      if (index >= total) {
        for (const id of order.slice(total)) steps[id] = { state: 'skipped' };
        show({ ...current, status: 'recording', test: { ...current.test!, state: 'passed', steps, ms: Date.now() - run.started } });
        return;
      }
      began = Date.now();
      steps[order[index]] = { state: 'running', since: began };
      show({ ...current, test: { ...current.test!, state: 'running', steps } });
      timer = window.setTimeout(next, (slow ? 900 : 350) + Math.random() * 400);
    };
    // The build, its window, its start page, then the steps, as the recorder's run tells each.
    timer = window.setTimeout(() => {
      phase('window');
      timer = window.setTimeout(() => {
        phase('start');
        timer = window.setTimeout(next, 500);
      }, 300);
    }, 250);
  };
  const elements = (message: object) => window.__qaasPanelElements?.(JSON.stringify(message));
  window.__qaasPanelSend = (json) => {
    const command = JSON.parse(json) as Command;
    console.info('command', command);
    if (command.type === 'elements' && command.op === 'tree') elements({ kind: 'tree', page: 'page', url: 'https://helpdesk.example/helpdesk', data: tree });
    if (command.type === 'elements' && command.op === 'search') {
      const query = command.query.toLowerCase();
      const matches = tree.nodes.filter((node) => [node.text, node.elementId, node.label].some((value) => value?.toLowerCase().includes(query))).map((node) => node.id);
      elements({ kind: 'search', page: 'page', url: 'https://helpdesk.example/helpdesk', data: { ...tree, matches } });
    }
    if (command.type === 'pick') show({ ...current, element: command.node === undefined ? undefined : { ...closeTicket, node: command.node, gone: !!command.frozen } });
    if (command.type === 'test') simulate(command.action, command.slow);
    if (command.type === 'stopTest') {
      window.clearTimeout(timer);
      const steps = Object.fromEntries(order.map((id) => [id, current.test!.steps[id]?.state === 'passed' ? current.test!.steps[id] : { state: 'skipped' as const }]));
      show({ ...current, status: 'recording', test: { ...current.test!, state: 'stopped', steps, ms: Date.now() - current.test!.started } });
    }
    if (command.type === 'clearTest') show({ ...current, test: undefined });
    // The modes, as the recorder goes in and out of them.
    if (command.type === 'pause') show({ ...current, status: current.status === 'paused' ? 'recording' : 'paused' });
    if (command.type === 'try') show({ ...current, status: current.status === 'trying' ? 'recording' : 'trying', tried: current.status === 'trying' ? undefined : [] });
    if (command.type === 'checkMode') show({ ...current, checking: command.on ?? !current.checking });
    if (command.type === 'rerecordDone' || command.type === 'rerecordCancel' || command.type === 'rerecordDismiss') show({ ...current, rerecord: undefined, status: 'recording' });
  };
  show(current);
  // `?frozen` shows the menu as if F4 froze it.
  if (location.search.includes('frozen')) elements({ kind: 'frozen', page: 'page', at: '10:42:07', data: tree, described: menuItems });
}
