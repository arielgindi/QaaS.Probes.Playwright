using System.Xml;
using System.Xml.Linq;

namespace QaaS.Playwright.Recorder;

/// <summary>
/// The namespace for a class saved in a folder, as the IDE would name it: the RootNamespace (or file name) of the nearest
/// .csproj the flow can be built in, one that references QaaS.Playwright, followed by the folder's path inside it.
/// <c>Flows</c> when no such project contains the folder: a project of something else above it, such as a tool or a
/// scratch folder's, never names the flow.
/// </summary>
internal static class ProjectNamespace
{
    // A folder named like one, e.g. "internal", needs an @ to be a namespace.
    private static readonly HashSet<string> Keywords =
    [
        .. """
           abstract as base bool break byte case catch char checked class const continue decimal default delegate do
           double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface
           internal is lock long namespace new null object operator out override params private protected public
           readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try
           typeof uint ulong unchecked unsafe ushort using virtual void volatile while __arglist __makeref __reftype
           __refvalue
           """.Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries),
    ];

    public static string Of(string directory)
    {
        var folder = new DirectoryInfo(Path.GetFullPath(directory));
        for (var ancestor = folder; ancestor is not null; ancestor = ancestor.Parent)
        {
            // The folder is made when the flow is saved, so it may not be there yet.
            if (!ancestor.Exists) continue;
            var project = ancestor.GetFiles("*.csproj").FirstOrDefault(ReferencesQaasPlaywright);
            if (project is null) continue;

            var root = RootNamespaceOf(project) ?? Path.GetFileNameWithoutExtension(project.Name);
            var inside = Path.GetRelativePath(ancestor.FullName, folder.FullName);
            return Sanitize(inside == "." ? root : $"{root}.{inside.Replace(Path.DirectorySeparatorChar, '.')}");
        }

        return "Flows";
    }

    // A package, a project that is there, or a DLL of QaaS.Playwright, in the project or its Directory.Build.props.
    private static bool ReferencesQaasPlaywright(FileInfo project)
    {
        var files = new List<FileInfo> { project };
        for (var folder = project.Directory; folder is not null; folder = folder.Parent)
            if (folder.GetFiles("Directory.Build.props").FirstOrDefault() is { } props) files.Add(props);
        foreach (var file in files)
        {
            try
            {
                foreach (var reference in XDocument.Load(file.FullName).Descendants()
                             .Where(element => element.Name.LocalName is "PackageReference" or "ProjectReference" or "Reference"))
                {
                    var include = reference.Attribute("Include")?.Value.Trim() ?? "";
                    var name = Path.GetFileName(include.Replace('\\', '/'));
                    if (name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        name = Path.GetFileNameWithoutExtension(name);
                    if (!name.Equals("QaaS.Playwright", StringComparison.OrdinalIgnoreCase)) continue;
                    if (reference.Name.LocalName != "ProjectReference"
                        || File.Exists(Path.Combine(file.DirectoryName!, include.Replace('\\', Path.DirectorySeparatorChar))))
                        return true;
                }
            }
            catch (Exception failure) when (failure is XmlException or IOException or UnauthorizedAccessException)
            {
                // Not a project file we can read: it names nothing.
            }
        }

        return false;
    }

    private static string Sanitize(string dottedName) =>
        string.Join('.', dottedName.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(SanitizeSegment));

    private static string SanitizeSegment(string segment)
    {
        var cleaned = string.Concat(segment.Where(character => char.IsLetterOrDigit(character) || character == '_'));
        if (cleaned.Length == 0) return "Flows";
        if (Keywords.Contains(cleaned)) return "@" + cleaned;
        return char.IsDigit(cleaned[0]) ? "_" + cleaned : cleaned;
    }

    private static string? RootNamespaceOf(FileInfo project)
    {
        try
        {
            return XDocument.Load(project.FullName).Descendants("RootNamespace").FirstOrDefault()?.Value.Trim();
        }
        catch (Exception failure) when (failure is XmlException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
