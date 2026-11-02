import type { Command, PanelState } from './types';

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
              "rejection": "3 matches"
            },
            {
              "index": 1,
              "kind": "role",
              "label": "button \"Assign to me\"",
              "code": "page.GetByRole(AriaRole.Button, new() { Name = \"Assign to me\" })",
              "count": 3,
              "rejection": "3 matches"
            },
            {
              "index": 2,
              "kind": "row",
              "label": "row » \"TCK-1042\" » button \"Assign to me\"",
              "code": "page.GetByRole(AriaRole.Row).Filter(new() { HasText = \"TCK-1042\" }).GetByRole(AriaRole.Button, new() { Name = \"Assign to me\", Exact = true })",
              "count": 1
            },
            {
              "index": 3,
              "kind": "generator",
              "label": "GetByRole(AriaRole.Row, new() { Name = \"TCK-1042 Laptop for a new\" }).GetByRole(AriaRole.Button)",
              "code": "page.GetByRole(AriaRole.Row, new() { Name = \"TCK-1042 Laptop for a new\" }).GetByRole(AriaRole.Button)",
              "count": 1
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
              "rejection": "generated id"
            },
            {
              "index": 1,
              "kind": "testid",
              "label": "test id \"new-ticket\"",
              "code": "page.GetByTestId(\"new-ticket\")",
              "count": 1
            },
            {
              "index": 2,
              "kind": "role",
              "label": "button \"New ticket\"",
              "code": "page.GetByRole(AriaRole.Button, new() { Name = \"New ticket\", Exact = true })",
              "count": 1
            },
            {
              "index": 3,
              "kind": "generator",
              "label": "GetByTestId(\"new-ticket\")",
              "code": "page.GetByTestId(\"new-ticket\")",
              "count": 1
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
              "count": 1
            },
            {
              "index": 1,
              "kind": "role",
              "label": "textbox \"Title\"",
              "code": "page.GetByRole(AriaRole.Dialog, new() { Name = \"New ticket\" }).GetByRole(AriaRole.Textbox, new() { Name = \"Title\", Exact = true })",
              "count": 1
            },
            {
              "index": 2,
              "kind": "generator",
              "label": "GetByRole(AriaRole.Textbox, new() { Name = \"Title\" })",
              "code": "page.GetByRole(AriaRole.Textbox, new() { Name = \"Title\" })",
              "count": 1
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
              "count": 1
            },
            {
              "index": 1,
              "kind": "role",
              "label": "combobox \"Category\"",
              "code": "page.GetByRole(AriaRole.Dialog, new() { Name = \"New ticket\" }).GetByRole(AriaRole.Combobox, new() { Name = \"Category\", Exact = true })",
              "count": 1
            },
            {
              "index": 2,
              "kind": "generator",
              "label": "GetByLabel(\"Category\")",
              "code": "page.GetByLabel(\"Category\")",
              "count": 1
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
              "rejection": "3 matches"
            },
            {
              "index": 1,
              "kind": "nameValue",
              "label": "[name='priority'][value='high']",
              "code": "page.GetByRole(AriaRole.Dialog, new() { Name = \"New ticket\" }).Locator(\"[name='priority'][value='high']\")",
              "count": 1
            },
            {
              "index": 2,
              "kind": "role",
              "label": "radio \"High\"",
              "code": "page.GetByRole(AriaRole.Dialog, new() { Name = \"New ticket\" }).GetByRole(AriaRole.Radio, new() { Name = \"High\", Exact = true })",
              "count": 1
            },
            {
              "index": 3,
              "kind": "roleInGroup",
              "label": "fieldset:has([name='priority']) » radio \"High\"",
              "code": "page.GetByRole(AriaRole.Dialog, new() { Name = \"New ticket\" }).Locator(\"fieldset:has([name='priority'])\").GetByRole(AriaRole.Radio, new() { Name = \"High\", Exact = true })",
              "count": 1
            },
            {
              "index": 4,
              "kind": "generator",
              "label": "GetByRole(AriaRole.Radio, new() { Name = \"High\" })",
              "code": "page.GetByRole(AriaRole.Radio, new() { Name = \"High\" })",
              "count": 1
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
              "count": 1
            },
            {
              "index": 1,
              "kind": "generator",
              "label": "GetByRole(AriaRole.Button, new() { Name = \"Create ticket\" })",
              "code": "page.GetByRole(AriaRole.Button, new() { Name = \"Create ticket\" })",
              "count": 1
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

/** Shows the sample, and logs the commands the panel would send the recorder. */
export function start(): void {
  window.__qaasPanelSend = (json) => console.info('command', JSON.parse(json) as Command);
  window.__qaasPanelShow?.(JSON.stringify(sample));
}
