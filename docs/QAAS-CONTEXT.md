# QaaS Platform Context

This document explains the QaaS (Quality-as-a-Service) platform and how this Playwright probe fits into it. Written for developers (or AI models) working on this codebase for the first time.

## What is QaaS

QaaS is a modular .NET 10.0 test automation platform built by TheSmokeTeam. It has:

- **QaaS.Framework** — the core, provides hook interfaces and execution engine
- **QaaS.Runner** — orchestrates test execution (sessions, publishers, consumers, probes, assertions)
- **QaaS.Mocker** — orchestrates mock servers (HTTP, gRPC, Socket)
- **QaaS.Common.*** — shared hook implementations (generators, assertions, probes, processors)

Everything is configured via YAML. The C# code is the behavior; YAML is the configuration.

## The hook system

QaaS has four hook types. Every hook follows the same pattern: a C# class with a typed configuration record, discovered by class name, configured from YAML.

| Hook | Interface | Purpose | Config Section |
|------|-----------|---------|----------------|
| Generator | `IGenerator` / `BaseGenerator<T>` | Produces test data | `GeneratorConfiguration:` |
| Assertion | `IAssertion` / `BaseAssertion<T>` | Validates results | `AssertionConfiguration:` |
| Probe | `IProbe` / `BaseProbe<T>` | Performs actions (setup/teardown) | `ProbeConfiguration:` |
| Processor | `ITransactionProcessor` | Handles mocker responses | `ProcessorConfiguration:` |

### How hooks work

1. User writes YAML referencing the hook by class name
2. QaaS Runner scans assemblies, finds the class
3. Calls `LoadAndValidateConfiguration(IConfiguration)` — binds YAML to typed C# record
4. Calls the hook's main method (`Generate`, `Assert`, `Run`, `Process`)

### Config binding

All hooks use `BindToObject<T>()` from `QaaS.Framework.Configurations`:
```csharp
Configuration = configuration.BindToObject<TConfiguration>(binderOptions, logger);
```

This converts YAML sections into C# objects. Supports:
- Nested objects (`Team.Lead`)
- Arrays (`string[]`, `MissionConfig[]`)
- Dictionaries (`Dictionary<string, string>`)
- Enums (from string)
- Validation attributes (`[Required]`, `[Range]`, `[MinLength]`)

## QaaS Runner YAML structure

```yaml
MetaData:
  Team: MyTeam
  System: MyApp

DataSources:
  - Name: TestData
    Generator: FromCSV                    # ← Generator hook name
    GeneratorConfiguration:               # ← Bound to FromCSVConfig
      FileSystem:
        Path: TestData

Sessions:
  - Name: MySession
    Publishers:
      - Name: SendMessages
        DataSourceNames: [TestData]
        RabbitMq:
          Host: localhost
          ExchangeName: input

    Consumers:
      - Name: ReceiveMessages
        TimeoutMs: 5000
        RabbitMq:
          Host: localhost
          ExchangeName: output

    Probes:
      - Name: SetupProbe
        Probe: FlushAllRedis              # ← Probe hook name
        ProbeConfiguration:               # ← Bound to RedisServerProbeConfig
          HostNames: [localhost]

Assertions:
  - Name: CheckCounts
    Assertion: HermeticByExpectedOutputCount   # ← Assertion hook name
    SessionNames: [MySession]
    AssertionConfiguration:                    # ← Bound to config record
      OutputNames: [ReceiveMessages]
      ExpectedCount: 100
```

## Existing probe examples

### FlushAllRedis
```csharp
public class FlushAllRedis : BaseRedisProbeWithGlobalDict<RedisServerProbeConfig>
{
    protected override void RunRedisProbe()
    {
        RedisDb.Execute("FLUSHALL");
    }
}
```

### CreateRabbitMqExchanges
```csharp
public class CreateRabbitMqExchanges : BaseRabbitMqObjectsManipulation<CreateRabbitMqExchangesConfig, RabbitMqExchangeConfig>
{
    protected override IEnumerable<RabbitMqExchangeConfig> GetObjectsToManipulateConfigurations()
        => Configuration.Exchanges!;

    protected override void ManipulateObject(IChannel channel, RabbitMqExchangeConfig config)
    {
        channel.ExchangeDeclareAsync(config.Name!, config.Type.ToString(), config.Durable, config.AutoDelete, config.Arguments);
    }
}
```

Config with array of complex objects:
```yaml
ProbeConfiguration:
  Host: localhost
  Port: 5672
  Exchanges:
    - Name: my-exchange
      Type: Fanout
      Durable: true
```

## How our Playwright probe fits in

Our probe follows the exact same pattern:

| Aspect | Redis/RabbitMQ Probes | Our Playwright Probe |
|--------|----------------------|---------------------|
| Base class | `BaseProbe<T>` | `BaseProbe<PlaywrightFlowConfig>` |
| YAML config | `ProbeConfiguration:` | `ProbeConfiguration:` |
| Discovery | By class name | By class name |
| Behavior | C# code | C# flow classes |
| Config binding | `BindToObject<T>()` | `BindToObject<T>()` |

The difference: our probe delegates to flow classes (which also use typed config). This is like how `CreateRabbitMqExchanges` iterates over `Exchanges[]` — except our "exchanges" are Playwright flow classes.

## Key design decisions we made

### Why C# flows instead of YAML
- Every QaaS hook stores behavior in C#. No existing hook reads behavior from files.
- C# gives compile-time safety, IDE support, full Playwright API.
- YAML would require a step interpreter that can never cover all Playwright features.

### Why BasePlaywrightFlow<T> instead of making each flow a full probe
- One probe manages the browser lifecycle (launch, navigate, dispose).
- Flows focus on actions (click, fill, submit) — they don't manage browsers.
- Multiple flows share one browser session (cookies persist across login → actions).

### Why ErrorOnUnknownConfiguration = false on the probe
- The YAML has `FlowConfiguration:` which is NOT a property on `PlaywrightFlowConfig`.
- Without this, QaaS's binder throws on unknown keys.
- The flow configs use `ErrorOnUnknownConfiguration = true` so typos ARE caught at the flow level.

### Why the probe auto-navigates to BaseUrl
- So flows don't hardcode URLs.
- Switch environments by changing one YAML line.
- Recorded flows can skip the GotoAsync or use BaseUrl for sub-pages.

## NuGet packages involved

```
QaaS.Framework.SDK (1.4.0)            ← BaseProbe<T>, IProbe, Context, BinderOptions
QaaS.Framework.Configurations (1.4.0) ← BindToObject<T>(), config binding
Microsoft.Playwright (1.52.0)          ← Browser automation
QaaS.Runner (4.3.0)                    ← Runner that executes the probe (referenced by test projects)
```

## Repo structure

```
QaaS.Probes.Playwright/          ← Shared NuGet package
  IPlaywrightFlow.cs             ← Flow interface + base class
  PlaywrightFlowProbe.cs         ← The QaaS probe
  Configuration/                 ← Probe config model
  Engine/                        ← Flow discovery

QaaS.Probes.Playwright.Recorder/ ← CLI tool for recording
  Program.cs                     ← Wraps Playwright codegen → C# class

QaaS.Probes.Playwright.Tests/    ← NUnit tests

PlaywrightDemo/                  ← Example project
  test.qaas.yaml                 ← Example YAML config
  Flows/                         ← Example flow classes
```
