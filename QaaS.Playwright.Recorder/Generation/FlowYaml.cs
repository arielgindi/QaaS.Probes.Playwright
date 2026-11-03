using System.Text;

namespace QaaS.Playwright.Recorder.Generation;

/// <summary>The flow's recorded values as the <c>FlowConfiguration</c> block of a QaaS probe.</summary>
internal static class FlowYaml
{
    /// <param name="className">The flow's class name.</param>
    /// <param name="settings">Its settings.</param>
    /// <param name="secrets">
    /// Writes the values of the secrets recorded, for a test run only; else each is the placeholder of an environment
    /// variable (<c>${PASSWORD}</c>), as no file of the flow holds a secret.
    /// </param>
    public static string Write(string className, IReadOnlyList<Setting> settings, bool secrets = false)
    {
        var yaml = new StringBuilder()
            .Append($"# The values recorded in {className}. Put this block under the ProbeConfiguration of the probe\n")
            .Append("# that runs the flow, and change the values for each case.\n")
            .Append("FlowConfiguration:\n")
            .Append($"  {className}:");
        if (settings.Count == 0) return yaml.Append(" {}\n").ToString();
        yaml.Append('\n');
        foreach (var setting in settings)
        {
            if (setting.Items is not { } items)
            {
                yaml.Append($"    {setting.Name}: {(setting.Secret && !secrets ? $"'{Placeholder(setting)}'" : Literal(setting))}\n");
                continue;
            }

            // A loop's list, an item per line; a record's fields under its dash.
            yaml.Append($"    {setting.Name}:{(items.Count == 0 ? " []" : "")}\n");
            foreach (var item in items)
            {
                if (setting.ItemType is null)
                {
                    yaml.Append($"      - {Literal(item[0])}\n");
                    continue;
                }

                for (var index = 0; index < item.Count; index++)
                    yaml.Append($"      {(index == 0 ? "- " : "  ")}{setting.Fields![index].Name}: {Literal(item[index])}\n");
            }
        }

        return yaml.ToString();
    }

    /// <summary>
    /// The QaaS placeholder the YAML reads a secret from: the environment variable named after the setting,
    /// <c>LoginPassword</c> giving <c>${LOGIN_PASSWORD}</c>.
    /// </summary>
    public static string Placeholder(Setting setting) => "${" + EnvironmentName(setting.Name) + "}";

    /// <summary>The environment variable of a setting: <c>LoginPassword</c> gives <c>LOGIN_PASSWORD</c>.</summary>
    public static string EnvironmentName(string name) =>
        string.Join('_', StepNamer.Words(name).Select(word => word.ToUpperInvariant()));

    /// <summary>
    /// A value as YAML: a flag as <c>true</c> or <c>false</c>, a number as it is, text in single quotes, so that no text
    /// reads as a number, a date or a flag; text with a line break or another control character in double quotes, escaped.
    /// </summary>
    public static string Scalar(Setting setting)
    {
        if (setting.Secret) return $"'{Placeholder(setting)}'";
        return Literal(setting);
    }

    private static string Literal(Setting setting)
    {
        if (setting.Flag is { } flag) return flag ? "true" : "false";
        if (setting.IsNumber && setting.Value is { } number) return number;
        var text = setting.Value ?? "";
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
