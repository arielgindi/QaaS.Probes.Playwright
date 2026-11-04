namespace QaaS.Playwright.Recorder.Capture;

/// <summary>Where a recording, or a test of it, starts once the login flow logged in.</summary>
internal static class StartPage
{
    /// <summary>
    /// Why the page stays at <paramref name="at"/>, where the login left it, rather than going to
    /// <paramref name="startUrl"/>: it is there, or <paramref name="startUrl"/> is the app's root (its address alone, or
    /// the login's <paramref name="loginBaseUrl"/>) and the login left the app on a page of its own, where the tests'
    /// next flow starts too. Null when it goes to <paramref name="startUrl"/>; "" when it is there already.
    /// </summary>
    public static string? StaysAfterLogin(string at, string startUrl, string? loginBaseUrl)
    {
        if (!Uri.TryCreate(at, UriKind.Absolute, out var page) || !Uri.TryCreate(startUrl, UriKind.Absolute, out var start)) return null;
        if (Same(page, start)) return "";
        var root = start.AbsolutePath is "" or "/"
                   || loginBaseUrl is not null && Uri.TryCreate(loginBaseUrl, UriKind.Absolute, out var baseUrl) && Same(start, baseUrl);
        var inApp = page.Scheme is "http" or "https" && string.Equals(page.GetLeftPart(UriPartial.Authority),
            start.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
        return root && inApp
            ? $"The login left the app on {page.PathAndQuery}: recording starts there, as you started on the app's root " +
              $"({startUrl}), and your tests' next flow starts there too. To start elsewhere, go there before you act."
            : null;
    }

    // The same page, whatever its fragment or a slash at the end.
    private static bool Same(Uri one, Uri other) =>
        string.Equals(Trimmed(one), Trimmed(other), StringComparison.OrdinalIgnoreCase);

    private static string Trimmed(Uri uri) => uri.GetLeftPart(UriPartial.Query).TrimEnd('/');
}
