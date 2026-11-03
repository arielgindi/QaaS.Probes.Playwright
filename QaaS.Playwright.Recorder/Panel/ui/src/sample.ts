import type { Command, DomNode, DomPatch, PanelState, PickedElement } from './types';

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
              "value": "'TCK-1042'"
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
          "settings": [
            {
              "name": "Title",
              "value": "'Printer on floor 3 is jammed'"
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
              "value": "'Software'"
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
              "value": "'High'"
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
          return ['tr', { classes: 'MuiTableRow-root' }, [['td', { text: key }], ['td', { text: subject.join(' ') }],
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
};

/** Shows the sample, and logs the commands the panel would send the recorder, answering those about the page's elements. */
export function start(): void {
  const tree = patch();
  const menuItems = tree.nodes.filter((node) => node.role === 'menuitem').map((node) => node.id);
  const show = (state: PanelState) => window.__qaasPanelShow?.(JSON.stringify(state));
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
    if (command.type === 'pick') show({ ...sample, element: command.node === undefined ? undefined : { ...closeTicket, node: command.node, gone: !!command.frozen } });
  };
  show(sample);
  // `?frozen` shows the menu as if F4 froze it.
  if (location.search.includes('frozen')) elements({ kind: 'frozen', page: 'page', at: '10:42:07', data: tree, described: menuItems });
}
