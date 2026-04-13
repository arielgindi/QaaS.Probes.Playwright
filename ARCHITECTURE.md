# Architecture

## How the probe integrates with QaaS

QaaS has a plugin system based on hooks. Every hook follows the same pattern:

```
C# class (behavior) + YAML (configuration) = hook
```

Our probe fits this pattern exactly:

| QaaS Hook | C# Class | YAML Config Section |
|-----------|----------|---------------------|
| Generator | `FromCSV : BaseGenerator<FromCSVConfig>` | `GeneratorConfiguration:` |
| Assertion | `HermeticByExpectedOutputCount : BaseAssertion<T>` | `AssertionConfiguration:` |
| Probe (Redis) | `FlushAllRedis : BaseProbe<RedisServerProbeConfig>` | `ProbeConfiguration:` |
| **Probe (ours)** | **`PlaywrightFlowProbe : BaseProbe<PlaywrightFlowConfig>`** | **`ProbeConfiguration:`** |

The Runner discovers hooks by scanning assemblies for types implementing the hook interface. Our probe is found the same way — referenced via NuGet or ProjectReference, discovered at runtime.

## Components

```
QaaS.Probes.Playwright/
├── IPlaywrightFlow.cs              ← Interface + BasePlaywrightFlow<T> base class
├── PlaywrightFlowProbe.cs          ← The QaaS probe (browser lifecycle + flow orchestration)
├── Configuration/
│   └── PlaywrightFlowConfig.cs     ← Probe-level YAML config model
└── Engine/
    └── FlowDiscovery.cs            ← Finds flow classes by name across assemblies

QaaS.Probes.Playwright.Recorder/
└── Program.cs                      ← CLI tool that wraps Playwright codegen → C# class
```

## Execution flow

```
1. QaaS Runner reads test.qaas.yaml
   ↓
2. Finds "Probe: PlaywrightFlowProbe" → scans assemblies → finds our class
   ↓
3. Calls LoadAndValidateConfiguration(IConfiguration)
   ├── Binds PlaywrightFlowConfig (BaseUrl, Headless, Flows, etc)
   └── Saves raw IConfiguration (for FlowConfiguration subsection later)
   ↓
4. Calls Run(sessionDataList, dataSourceList)
   ↓
5. Launches Chromium (headless or visible)
   ↓
6. Navigates to BaseUrl
   ↓
7. For each flow name in SetupFlows then Flows:
   ├── FlowDiscovery.Resolve(name) → finds the C# class by name
   ├── Sets Context (logger) and BaseUrl on the flow
   ├── Extracts FlowConfiguration:{name}: subsection from raw IConfiguration
   ├── Calls flow.LoadAndValidateConfiguration(section) → BindToObject<T>
   └── Calls flow.RunAsync(page)
   ↓
8. All flows share the same browser page (cookies, session state persist)
   ↓
9. Browser closes (or stays open if KeepOpen + visible)
```

## Why flows are C# classes (not YAML)

We evaluated both approaches. C# won because:

1. **QaaS pattern**: Every hook stores behavior in C#, config in YAML. No existing hook reads behavior from external files.
2. **Parameterization**: `Configuration.Username` gives compile-time safety. YAML `{{Username}}` silently fails on typos.
3. **Full Playwright API**: C# flows can use if/else, loops, retries, frame navigation — anything Playwright supports.
4. **IDE support**: IntelliSense, refactoring, build errors for broken code.
5. **No interpreter**: The old YAML approach needed a StepExecutor with a switch statement for each action type. C# flows are direct Playwright calls — no translation layer.

## Config binding

The probe has two config layers:

### Layer 1: Probe config (PlaywrightFlowConfig)
Bound by QaaS's standard BaseProbe mechanism:
```yaml
ProbeConfiguration:
  BaseUrl: https://my-app.com    → PlaywrightFlowConfig.BaseUrl
  Headless: false                → PlaywrightFlowConfig.Headless
  Flows: [LoginFlow]             → PlaywrightFlowConfig.Flows
```

### Layer 2: Flow config (per-flow typed record)
Bound at runtime when the flow is resolved:
```yaml
  FlowConfiguration:
    LoginFlow:                   → IConfiguration subsection
      Username: admin            → LoginFlowConfig.Username (via BindToObject<T>)
      Password: secret           → LoginFlowConfig.Password
```

The probe sets `ErrorOnUnknownConfiguration = false` so `FlowConfiguration` doesn't crash the probe-level binding. Each flow sets `ErrorOnUnknownConfiguration = true` so typos in flow config ARE caught.

## Flow discovery

`FlowDiscovery` scans all loaded assemblies for types implementing `IPlaywrightFlow`. This is the same pattern QaaS's `HookProvider` uses to find probes, generators, and assertions.

Flow classes can live anywhere:
- In the test project itself
- In a shared NuGet package (like QaaS.Common.Probes)
- In any referenced assembly

Results are cached after first scan.

## Browser lifecycle

One browser instance per probe run. One page shared across all flows. This means:
- Login cookies from SetupFlows persist to main Flows
- Navigation state carries over
- localStorage/sessionStorage persists

The browser is disposed when the probe finishes, even on failure (using `await using`).
