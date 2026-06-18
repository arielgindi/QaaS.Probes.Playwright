using Microsoft.Extensions.Configuration;
using Microsoft.Playwright;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Playwright.Engine;

namespace QaaS.Playwright.Tests;

public record TestFlowConfig
{
    public string? Message { get; set; }
}

public class TestFlow : BasePlaywrightFlow<TestFlowConfig>
{
    public static int CallCount { get; set; }

    public override Task RunAsync(IPage page)
    {
        CallCount++;
        return Task.CompletedTask;
    }
}

public class AnotherFlow : BasePlaywrightFlow<TestFlowConfig>
{
    public override Task RunAsync(IPage page) => Task.CompletedTask;
}

// An open generic flow cannot be constructed without a type argument; discovery must never select it.
public class GenericFlow<T> : BasePlaywrightFlow<TestFlowConfig>
{
    public override Task RunAsync(IPage page) => Task.CompletedTask;
}

[TestFixture]
public class FlowDiscoveryTests
{
    [Test]
    public void Resolve_ExistingFlow_ReturnsInstance()
    {
        var flow = FlowDiscovery.Resolve("TestFlow");
        Assert.That(flow, Is.InstanceOf<TestFlow>());
    }

    [Test]
    public void Resolve_CaseInsensitive_ReturnsInstance()
    {
        var flow = FlowDiscovery.Resolve("testflow");
        Assert.That(flow, Is.InstanceOf<TestFlow>());
    }

    [Test]
    public void Resolve_AnotherFlow_ReturnsInstance()
    {
        var flow = FlowDiscovery.Resolve("AnotherFlow");
        Assert.That(flow, Is.InstanceOf<AnotherFlow>());
    }

    [Test]
    public void Resolve_NonExistent_ThrowsWithHelpfulMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => FlowDiscovery.Resolve("DoesNotExist"));
        Assert.That(ex!.Message, Does.Contain("DoesNotExist"));
        Assert.That(ex.Message, Does.Contain("IPlaywrightFlow"));
    }

    [Test]
    public void Resolve_OpenGenericFlow_NotSelected()
    {
        // An open generic is excluded by discovery, so it surfaces as "not found" rather than a construction crash.
        var ex = Assert.Throws<InvalidOperationException>(() => FlowDiscovery.Resolve("GenericFlow"));
        Assert.That(ex!.Message, Does.Contain("not found"));
    }

    [Test]
    public void Flow_LoadAndValidateConfiguration_BindsConfig()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Message"] = "Hello from YAML"
            })
            .Build();

        var flow = new TestFlow
        {
            Context = new Context { Logger = null! }
        };
        flow.LoadAndValidateConfiguration(config);

        Assert.That(flow.Configuration.Message, Is.EqualTo("Hello from YAML"));
    }
}
