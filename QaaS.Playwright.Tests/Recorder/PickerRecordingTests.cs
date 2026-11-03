using Microsoft.Playwright;
using QaaS.Playwright.Browser;
using QaaS.Playwright.Recorder;
using QaaS.Playwright.Recorder.Capture;
using QaaS.Playwright.Recorder.TestRun;
using QaaS.Playwright.Tests.EndToEnd;

namespace QaaS.Playwright.Tests.Recorder;

/// <summary>
/// The option pickers of common UI libraries, recorded for real on the test site's imitations of them (<c>/pickers/</c>):
/// a button with only an id opens a dialog with a form, a button there opens a second dialog over it, and the person
/// chooses in it with the mouse as a person does, moving onto the option's inner text and pressing there. Each flow must
/// name the step that chooses after the choice, find the option by what it says (never by its position, a generated id
/// or the dialog's missing name), and pass the panel's Test, which loads the page afresh, with new ids and class names.
/// </summary>
[TestFixture]
[Category("EndToEnd")]
public class PickerRecordingTests
{
    private const int AtOnce = 6;

    private static readonly Picker[] Pickers =
    [
        // Cards of nested divs, an icon, a badge and a split price; the app handles the click on the card; choosing closes
        // the dialog. The step was named after the first dialog's opener with a 2, the card by Playwright's own locator.
        new("cards", "ChooseExpressAsync", """dialog.GetByText("Express", new() { Exact = true }).ClickAsync()""",
            (person, page) => person.ClickAsync(page.GetByText("Express", new() { Exact = true })), Confirms: false),
        // Without even a hand cursor: the click on the inner text was not recorded at all.
        new("tiles", "ChooseExpressAsync", """dialog.GetByText("Express", new() { Exact = true }).ClickAsync()""",
            (person, page) => person.ClickAsync(page.GetByText("Express", new() { Exact = true }))),
        new("carousel", "ChooseExpressAsync",
            """Locator(".slick-slide:not([aria-hidden='true'])").GetByText("Express", new() { Exact = true })""",
            (person, page) => person.ClickAsync(page.Locator(".slick-slide:not(.slick-cloned)").GetByText("Express", new() { Exact = true }))),
        new("mui-select", "ChooseShippingAsync", """Locator("[id='mui-component-select-shipping']")""", async (person, page) =>
        {
            await person.ClickAsync(page.Locator("#mui-component-select-shipping"));
            await person.ClickAsync(page.Locator("li[role=option]").Filter(new() { HasText = "Express" }));
        }),
        new("react-select", "ChooseShippingAsync", """GetByRole(AriaRole.Combobox, new() { Name = "Shipping", Exact = true }).ClickAsync()""",
            async (person, page) =>
            {
                await person.ClickAsync(page.Locator("[class*='-singleValue']"));
                await person.ClickAsync(page.GetByRole(AriaRole.Option, new() { Name = "Express" }));
            }),
        new("react-select-old", "ChooseExpressAsync", """GetByRole(AriaRole.Textbox, new() { Name = "Shipping", Exact = true }).ClickAsync()""",
            async (person, page) =>
            {
                await person.ClickAsync(page.Locator("[class*='-singleValue']"));
                await person.ClickAsync(page.Locator("[id$='-option-1']"));
            }),
        new("antd-select", "ChooseExpressAsync", """Page.GetByText("Express", new() { Exact = true }).ClickAsync()""", async (person, page) =>
        {
            await person.ClickAsync(page.Locator(".ant-select-selection-item"));
            await person.ClickAsync(page.Locator(".ant-select-item-option-content").Filter(new() { HasText = "Express" }));
        }),
        new("headless-listbox", "ChooseShippingAsync", """GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Shipping") })""",
            async (person, page) =>
            {
                await person.ClickAsync(page.Locator("button[aria-haspopup=listbox] span.block"));
                await person.ClickAsync(page.Locator("li[role=option] span").Filter(new() { HasText = "Express" }));
            }),
        new("select2", "ChooseShippingAsync", "dialog.GetByRole(AriaRole.Combobox);", async (person, page) =>
        {
            await person.ClickAsync(page.Locator(".select2-selection__rendered"));
            await person.ClickAsync(page.Locator("li.select2-results__option").Filter(new() { HasText = "Express" }));
        }),
        new("chosen", "ChooseExpressAsync", """Locator("[id='shipping_chosen']").ClickAsync()""", async (person, page) =>
        {
            await person.ClickAsync(page.Locator(".chosen-single span"));
            await person.ClickAsync(page.Locator("li.active-result").Filter(new() { HasText = "Express" }));
        }),
        new("bootstrap-dropdown", "ChooseExpressAsync", """GetByRole(AriaRole.Link, new() { Name = "Express 2 days", Exact = true })""",
            async (person, page) =>
            {
                await person.ClickAsync(page.Locator(".dropdown-toggle"));
                await person.ClickAsync(page.Locator(".dropdown-item .badge").Filter(new() { HasText = "2 days" }));
            }),
        new("native", "ConfirmAsync", "SelectOptionAsync(new SelectOptionValue { Label = Configuration.Shipping })", async (person, page) =>
        {
            await person.ClickAsync(page.Locator("select[name='shipping']"));
            await page.Keyboard.PressAsync("ArrowDown");
            await page.Keyboard.PressAsync("Enter");
            await person.ClickAsync(page.Locator("[name='carrier']"));
            await page.Keyboard.TypeAsync("Air Courier");
        }),
        new("mui-autocomplete", "ChooseShippingAsync", "FillAsync(Configuration.ShippingSearch)", async (person, page) =>
        {
            await person.ClickAsync(page.GetByRole(AriaRole.Combobox));
            await page.Keyboard.PressAsync("Control+A");
            await page.Keyboard.TypeAsync("Exp", new() { Delay = 30 });
            await person.ClickAsync(page.GetByRole(AriaRole.Option, new() { Name = "Express" }));
        }),
        // Two lists of the same options, the closed one faded out but still in the page.
        new("same-options", "ChooseDeliveryWindowAsync",
            """GetByRole(AriaRole.Listbox, new() { Name = "Delivery window", Exact = true })""", async (person, page) =>
            {
                await person.ClickAsync(page.Locator("button[aria-haspopup=listbox] span").Nth(1));
                await person.ClickAsync(page.GetByRole(AriaRole.Listbox, new() { Name = "Delivery window" }).GetByText("Afternoon"));
            }),
        new("blur-menu", "ChooseExpressAsync", """GetByText("Express", new() { Exact = true }).ClickAsync()""", async (person, page) =>
        {
            await person.ClickAsync(page.Locator("[tabindex='0'] span").Filter(new() { HasText = "Standard" }));
            await person.ClickAsync(page.GetByText("Express", new() { Exact = true }));
        }),
        // Only the options in sight are in the page: the flow scrolls the list as the person did.
        new("virtual-list", "ChoosePickupPointAsync", "Mouse.WheelAsync(0, 2000)", async (person, page) =>
        {
            await person.MoveAsync(page.GetByRole(AriaRole.Listbox));
            await page.Mouse.WheelAsync(0, 2000);
            await person.ClickAsync(page.GetByRole(AriaRole.Option, new() { Name = "Old Garden" }));
        }),
        new("shadow-select", "ChooseShippingAsync", """GetByRole(AriaRole.Combobox, new() { NameRegex = new Regex("^Shipping") })""",
            async (person, page) =>
            {
                await person.ClickAsync(page.Locator("sl-select").GetByRole(AriaRole.Combobox));
                await person.ClickAsync(page.Locator("sl-option").Filter(new() { HasText = "Express" }));
            }),
    ];

    [SetUp]
    public void NeedsChrome()
    {
        if (ChromeExecutable.Find() is null) Assert.Ignore("Google Chrome is not installed.");
    }

    [Test]
    public async Task EachPicker_RecordsTheOptionByWhatItSays_InAStepNamedAfterTheChoice_AndTheFlowPassesOnAFreshPage()
    {
        using var site = TestSite.Start();
        using var atOnce = new SemaphoreSlim(AtOnce);
        // Recorded side by side, each in a browser of its own; tested one at a time, as the panel tests, since a test
        // run takes its build out of caches the whole process shares when it ends.
        using var oneTest = new SemaphoreSlim(1);
        var results = await Task.WhenAll(Pickers.Select(async picker =>
        {
            await atOnce.WaitAsync();
            try { return await RecordAndTestAsync(site, picker, oneTest); }
            finally { atOnce.Release(); }
        }));

        Assert.Multiple(() =>
        {
            foreach (var (picker, code, report) in results)
            {
                var opensTheSecond = picker.Name switch
                {
                    "same-options" => "OpenDeliveryAsync", "virtual-list" => "OpenPickupPointAsync", _ => "OpenShippingAsync",
                };
                Assert.That(code, Does.Contain($"private async Task {picker.Step}(ILocator dialog)")
                    .Or.Contain($"private static async Task {picker.Step}(ILocator dialog)"), $"{picker.Name}: the step that chooses");
                Assert.That(code, Does.Contain($"await {opensTheSecond}(newOrderDialog)"), $"{picker.Name}: the step that opens the second dialog");
                Assert.That(code, Does.Contain(picker.Locator), $"{picker.Name}: the option or its field");
                Assert.That(code, Does.Not.Contain(".Nth(").And.Not.Contain(".First").And.Not.Contain("Name = \"\"").And.Not.Contain("2Async"),
                    $"{picker.Name}: no position, no empty name, no numbered step");
                Assert.That(report.Outcome, Is.EqualTo(TestOutcome.Passed),
                    $"{picker.Name}: {report.Failure?.Headline ?? report.Problem}\n{code}");
            }
        });
    }

    // Records the picker's flow as a person would: opens the order with the id-only button, types the customer, opens
    // the choices, chooses, confirms and creates the order; then tests the flow as the panel does.
    private static async Task<(Picker Picker, string Code, TestReport Report)> RecordAndTestAsync(TestSite site, Picker picker,
        SemaphoreSlim oneTest)
    {
        var url = $"{site.Url}/pickers/{picker.Name}";
        var session = await RecordingSession.StartAsync(new RecordingOptions(url)
        {
            LoadStorageStatePath = null, SaveStorageStatePath = null, BrowserArgs = ["--headless=new"],
        }, _ => { });
        try
        {
            var page = session.Page;
            var person = new Person(page);
            await person.ClickAsync(page.Locator("#create-order-redirect-button svg"));
            await person.ClickAsync(page.Locator("[name='customer']"));
            await page.Keyboard.TypeAsync("Dana Lee");
            await person.ClickAsync(page.Locator("#change-shipping-button"));
            await picker.Choose(person, page);
            if (picker.Confirms) await person.ClickAsync(page.GetByRole(AriaRole.Button, new() { Name = "Confirm" }));
            await person.ClickAsync(page.GetByRole(AriaRole.Button, new() { Name = "Create order" }));
            await page.GetByRole(AriaRole.Alert).WaitForAsync();
            for (var wait = 0; wait < 50 && session.Build().Actions.Count(action => action.Kind != "fill") < picker.Clicks; wait++)
                await Task.Delay(100);

            var code = FlowCodeGenerator.Generate(session.Build(), "NewOrder", "Flows").Code;
            await oneTest.WaitAsync();
            try
            {
                await using var tester = new FlowTester(session, new TestOptions { DefaultTimeout = 5000 });
                var report = await tester.RunAsync(RecordRequest.Parse(["record", "new-order", url]), "Flows");
                return (picker, code, report);
            }
            finally
            {
                oneTest.Release();
            }
        }
        finally
        {
            await session.StopAsync();
        }
    }

    /// <summary>
    /// A picker's page, the step that chooses and a line of the code it must have, and what the person does in the
    /// second dialog; then a click on Confirm closes it, unless the choice did.
    /// </summary>
    private sealed record Picker(string Name, string Step, string Locator, Func<Person, IPage, Task> Choose, bool Confirms = true)
    {
        /// <summary>At least the clicks before the last, besides typing: the openers, the choice, Confirm.</summary>
        public int Clicks => Confirms ? 4 : 3;
    }

    /// <summary>A person: moves the pointer onto the middle of an element, rests, presses and lets go.</summary>
    private sealed class Person(IPage page)
    {
        public async Task MoveAsync(ILocator element)
        {
            var box = await element.BoundingBoxAsync() ?? throw new InvalidOperationException("The element does not show.");
            await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2, new() { Steps = 3 });
            await Task.Delay(100);
        }

        public async Task ClickAsync(ILocator element)
        {
            await MoveAsync(element);
            await page.Mouse.DownAsync();
            await Task.Delay(40);
            await page.Mouse.UpAsync();
            // What the page does, a dialog opening, is told with the click before the next one begins.
            await Task.Delay(300);
        }
    }
}
