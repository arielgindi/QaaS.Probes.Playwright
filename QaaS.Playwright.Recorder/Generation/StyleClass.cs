using System.Text.RegularExpressions;

namespace QaaS.Playwright.Recorder.Generation;

/// <summary>
/// Class names a style library generates, which change from one build to the next and so never make a locator: emotion's
/// <c>css-1x2y3z-control</c> and <c>css-1x2y3z</c>, styled-components' <c>sc-bdVaJa</c> and its hashes, JSS's <c>jss12</c>,
/// MUI's makeStyles <c>makeStyles-root-12</c> and <c>MuiBox-root-34</c>, CSS modules' <c>Button_primary__3xYz1</c>, and
/// other hash-like names. A library's own names, such as <c>oxd-select-text</c> or <c>MuiButton-root</c>, are stable.
/// </summary>
internal static partial class StyleClass
{
    public static bool IsHashed(string name) =>
        Hashed().IsMatch(name) || (name.Any(char.IsAsciiDigit) && name.Length >= 5 && !name.Contains('-') && UnstableId.IsGuidLike(name));

    [GeneratedRegex(@"^(css|emotion|e|sc|styled|jsx|svelte|astro|tw)-[0-9a-zA-Z]{4,}(-.*)?$|^jss\d+$|^[A-Za-z][A-Za-z0-9]*-[A-Za-z0-9]+-\d+$" +
                    @"|^[A-Za-z][A-Za-z0-9]*_[A-Za-z0-9-]+__[A-Za-z0-9_-]{5}$|^_[0-9a-zA-Z]{5,}$")]
    private static partial Regex Hashed();
}
