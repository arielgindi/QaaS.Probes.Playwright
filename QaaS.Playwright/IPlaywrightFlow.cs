using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Playwright;
using QaaS.Framework.SDK.ContextObjects;

namespace QaaS.Playwright;

/// <summary>
/// Contract for a browser flow. The probe discovers implementations by class name (the same pattern as QaaS's
/// HookProvider) and calls <see cref="RunAsync"/> with an active Playwright page.
///
/// Implement flows by inheriting <see cref="BasePlaywrightFlow{TConfiguration}"/> rather than this interface.
/// </summary>
public interface IPlaywrightFlow
{
    /// <summary>QaaS execution context — provides the logger and run-scoped shared state.</summary>
    Context Context { get; set; }

    /// <summary>
    /// The base URL from the probe's configuration. The probe navigates the (shared) page here once, before the
    /// first flow; use it to build URLs so the same flow works across environments.
    /// </summary>
    string BaseUrl { get; set; }

    /// <summary>
    /// Called by the probe before <see cref="RunAsync"/>. Binds the flow's <c>FlowConfiguration</c> section to its
    /// typed configuration record and validates it.
    /// </summary>
    /// <returns>Validation failures, or an empty/null list when the configuration is valid.</returns>
    List<ValidationResult>? LoadAndValidateConfiguration(IConfiguration configuration);

    /// <summary>
    /// Runs the browser flow. All flows in a probe run share one page in order, so cookies and session state
    /// persist; the page is wherever the previous flow left it (the first flow starts on <see cref="BaseUrl"/>).
    /// </summary>
    Task RunAsync(IPage page);
}
