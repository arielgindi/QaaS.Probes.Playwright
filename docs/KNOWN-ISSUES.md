# Known issues

What QaaS 4.8 and Playwright do that a suite has to know about.

## QaaS 4.8

The probe works around what it can: it checks its settings itself and fails the session on a mistake, since QaaS
binds a wrong value as a default, only warns about unknown keys and never sees a probe's validation errors; and
parallel probes take turns reading a DataSource, which QaaS would generate once for each. The rest is up to the
suite:

- Every Playwright session needs a `PlaywrightFlowAssertion` that selects it: a failed session no assertion selects,
  or a run without assertions, exits 0. A `SessionNames` entry that matches no session crashes the run (exit 134)
  with no Allure result.
- Failing cases add up to the exit code, so 256 failed cases exit 0 on Linux. End the suite's `Main` with
  `return Bootstrap.New(args).RunAndGetExitCode() == 0 ? 0 : 1;`.
- `act` and then `assert` as two commands: flow outcomes live in memory, so the assertion sees none. Use `run`.
- An action `Stage` inside one session is launch order, not a wait for the earlier actions to finish. Put dependent
  browser steps in one probe's `SetupFlows` and `Flows`, or in sessions with different session `Stage`s.
- A case file overrides lists by index, so a shorter `Flows` keeps the base's extra entries: base `[A, B, C]` and
  case `[X]` run `[X, B, C]`. Files layered with `-w` that add items under the same number merge into one item
  silently: give each item a name instead (`Tickets: { urgent: { ... } }`). QaaS binds named items in name order, and
  so does this plugin, for lists of settings.
- Environment variables override the YAML, and even `-r`; `--no-env` turns that off. `$${...}` is not an escape.
- An empty `-c` folder exits 0 without running anything, and one case with a bad hook stops every case.
- References prefix DataSource names, but not the probe's `DataSourceNames`.
- Failure screenshots reach Allure as `application/octet-stream`, since QaaS takes an attachment's type only from
  its `SerializationType`: Allure offers them as downloads instead of showing them inline.
- QaaS attaches the resolved YAML (`template.yaml`) to every result, so a `BrowserUrl` token or a password set from
  an environment variable shows there in the clear, even when the run passes. Set `SaveTemplate: false` on the
  assertion.
- Failure screenshots stay in memory until the whole run ends, as QaaS keeps every case: a long `-c` run grows by
  each failed flow's PNG; a passing flow adds none.

## Playwright 1.52 and proxies

In the connection Playwright makes to Chrome, whatever the probe does:

- A `ws://` or `wss://` `BrowserUrl` connects directly, ignoring `HTTP_PROXY` and `HTTPS_PROXY`; an `http(s)://` one
  is looked up through the proxy, then connected to directly too. The browser must be reachable without the proxy.
- With `HTTPS_PROXY` set, an `https://` `BrowserUrl`'s certificate is not checked. Use `wss://`.
- Credentials in `HTTP_PROXY` are not sent when looking up an `http://` `BrowserUrl`, so a proxy that needs them
  answers 407.
