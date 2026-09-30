using System.Xml;
using System.Xml.Linq;

namespace QaaS.Playwright.Recorder;

/// <summary>
/// The namespace for a class saved in a folder, as the IDE would name it: the nearest .csproj's RootNamespace (or file
/// name) followed by the folder's path inside the project. <c>Flows</c> when no project contains the folder.
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
           typeof uint ulong unchecked unsafe ushort using virtual void volatile while
           """.Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries),
    ];

    public static string Of(string directory)
    {
        var folder = new DirectoryInfo(Path.GetFullPath(directory));
        for (var ancestor = folder; ancestor is not null; ancestor = ancestor.Parent)
        {
            var project = ancestor.GetFiles("*.csproj").FirstOrDefault();
            if (project is null) continue;

            var root = RootNamespaceOf(project) ?? Path.GetFileNameWithoutExtension(project.Name);
            var inside = Path.GetRelativePath(ancestor.FullName, folder.FullName);
            return Sanitize(inside == "." ? root : $"{root}.{inside.Replace(Path.DirectorySeparatorChar, '.')}");
        }

        return "Flows";
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
