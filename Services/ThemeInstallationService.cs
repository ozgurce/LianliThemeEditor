using System.Text;
using System.Text.RegularExpressions;
using System.IO;

namespace ThemeEditorCSharp.Services;

public sealed class ThemeInstallationService : IThemeInstallationService
{
    public async Task<string> ActivateAsync(
        string requestedId,
        string templatePath,
        string backgroundPath,
        Func<Task<IReadOnlyList<string>>> getRegisteredMatches,
        Func<string, Task<bool>> tryApplyRegistered,
        Func<Task<string>> importAndApply)
    {
        var candidates = BuildActivationCandidates(requestedId, templatePath, backgroundPath).ToList();
        foreach (var id in await getRegisteredMatches())
            if (!candidates.Contains(id, StringComparer.OrdinalIgnoreCase)) candidates.Add(id);
        foreach (var id in candidates)
            if (await tryApplyRegistered(id)) return id;
        return await importAndApply();
    }

    public static IReadOnlyList<string> BuildActivationCandidates(string requestedId, string templatePath, string backgroundPath)
    {
        return new[] { requestedId }
            .Concat(ExtractInternalIds(templatePath))
            .Concat(new[] { Path.GetFileNameWithoutExtension(templatePath), Path.GetFileNameWithoutExtension(backgroundPath) })
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<string> ExtractInternalIds(string templatePath)
    {
        if (!File.Exists(templatePath)) return Array.Empty<string>();
        var text = Encoding.UTF8.GetString(File.ReadAllBytes(templatePath));
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(text, @"(?<id>[A-Za-z0-9_.-]+)\.turtheme", RegexOptions.IgnoreCase))
            ids.Add(match.Groups["id"].Value);
        foreach (Match match in Regex.Matches(text, @"(?<id>LL[A-Za-z0-9]+(?:_\d{8}_\d{6})+)", RegexOptions.IgnoreCase))
            ids.Add(match.Groups["id"].Value);
        return ids.ToList();
    }

    public static IReadOnlyList<string> MatchRegisteredIds(string templatePath, IEnumerable<string> registeredIds)
    {
        if (!File.Exists(templatePath)) return Array.Empty<string>();
        var text = Encoding.UTF8.GetString(File.ReadAllBytes(templatePath));
        var matches = registeredIds
            .Where(id => !string.IsNullOrWhiteSpace(id) && text.Contains(id, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return matches
            .Where(id => !matches.Any(other =>
                other.Length > id.Length &&
                other.Contains(id, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public static string ResolveTemplatePath(string deviceModel, string? templateId)
    {
        if (string.IsNullOrWhiteSpace(deviceModel) || string.IsNullOrWhiteSpace(templateId)) return "";
        var roots = GetTemplateRoots(deviceModel);
        foreach (var root in roots.Where(Directory.Exists))
        {
            var exact = Path.Combine(root, templateId + ".template");
            if (File.Exists(exact)) return exact;
        }
        foreach (var file in roots.Where(Directory.Exists).SelectMany(root => Directory.EnumerateFiles(root, "*.template")).OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                var text = Encoding.UTF8.GetString(File.ReadAllBytes(file));
                if (text.Contains(templateId, StringComparison.OrdinalIgnoreCase)) return file;
            }
            catch (Exception ex) { AppLogger.Error($"Template identity could not be inspected: {file}", ex); }
        }
        return "";
    }

    private static IReadOnlyList<string> GetTemplateRoots(string deviceModel)
    {
        var roots = new List<string>
        {
            Path.Combine(LConnectPaths.ProgramDataRoot, deviceModel, "template"),
            Path.Combine(LConnectPaths.ProgramFilesRoot, "Assets", deviceModel, "template")
        };

        AddSiblingTemplateRoots(roots, LConnectPaths.ProgramDataRoot);
        AddSiblingTemplateRoots(roots, Path.Combine(LConnectPaths.ProgramFilesRoot, "Assets"));

        return roots
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddSiblingTemplateRoots(List<string> roots, string baseDir)
    {
        if (!Directory.Exists(baseDir)) return;
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(baseDir))
            {
                var templateRoot = Path.Combine(dir, "template");
                if (Directory.Exists(templateRoot))
                {
                    roots.Add(templateRoot);
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Template roots could not be scanned: {baseDir}", ex);
        }
    }
}

