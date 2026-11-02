using System.Text;

namespace QaaS.Playwright.Recorder.Generation;

/// <summary>The flow's recorded values as the <c>FlowConfiguration</c> block of a QaaS probe.</summary>
internal static class FlowYaml
{
    public static string Write(string className, IReadOnlyList<Setting> settings)
    {
        var yaml = new StringBuilder()
            .Append($"# The values recorded in {className}. Put this block under the ProbeConfiguration of the probe\n")
            .Append("# that runs the flow, and change the values for each case.\n")
            .Append("FlowConfiguration:\n")
            .Append($"  {className}:");
        if (settings.Count == 0) return yaml.Append(" {}\n").ToString();
        yaml.Append('\n');
        foreach (var setting in settings)
            yaml.Append($"    {setting.Name}: {Scalar(setting)}\n");
        return yaml.ToString();
    }

    /// <summary>
    /// A value as YAML: a flag as <c>true</c> or <c>false</c>, text in single quotes, so that no text reads as a number,
    /// a date or a flag; text with a line break or another control character in double quotes, escaped.
    /// </summary>
    public static string Scalar(Setting setting)
    {
        if (setting.Flag is { } flag) return flag ? "true" : "false";
        var text = setting.Text ?? "";
        if (!text.Any(char.IsControl)) return $"'{text.Replace("'", "''", StringComparison.Ordinal)}'";
        var escaped = new StringBuilder("\"");
        foreach (var character in text)
            escaped.Append(character switch
            {
                '\\' => @"\\",
                '"' => "\\\"",
                '\n' => @"\n",
                '\r' => @"\r",
                '\t' => @"\t",
                _ when char.IsControl(character) => $@"\x{(int)character:X2}",
                _ => character.ToString(),
            });
        return escaped.Append('"').ToString();
    }
}
