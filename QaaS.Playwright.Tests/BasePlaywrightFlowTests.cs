using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Playwright;

namespace QaaS.Playwright.Tests;

public sealed class AccountSettings
{
    public UserSettings? Owner { get; set; }
    public UserSettings[] Members { get; set; } = [];
}

public sealed class UserSettings
{
    [Required]
    public string? Username { get; set; }

    [System.ComponentModel.DataAnnotations.Range(1, 5)]
    public int Attempts { get; set; } = 1;
}

public sealed class AccountFlow : BasePlaywrightFlow<AccountSettings>
{
    public override Task RunAsync(IPage page) => Task.CompletedTask;
}

[TestFixture]
public class BasePlaywrightFlowTests
{
    [TestCase("Owner")]
    [TestCase("Members:0")]
    public void LoadAndValidateConfiguration_InvalidNestedSettings_AreRejectedWithTheirPath(string path)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"FlowConfiguration:AccountFlow:{path}:Username"] = "",
            [$"FlowConfiguration:AccountFlow:{path}:Attempts"] = "99",
        }).Build();

        var errors = new AccountFlow().LoadAndValidateConfiguration(configuration.GetSection("FlowConfiguration:AccountFlow"));

        Assert.That(errors!.Select(error => error.ErrorMessage), Is.EquivalentTo(new[]
        {
            $"FlowConfiguration:AccountFlow:{path}:Username: The Username field is required.",
            $"FlowConfiguration:AccountFlow:{path}:Attempts: The field Attempts must be between 1 and 5.",
        }));
    }
}
