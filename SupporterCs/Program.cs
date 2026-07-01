using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ThemeEditorSupporter;

internal static class Program
{
    private const string DefaultLConnectDir = @"C:\Program Files\Lian-Li\L-Connect 3";
    private const string DefaultProgramData = @"C:\ProgramData\Lian-Li\L-Connect 3";
    private static readonly JavaScriptSerializer Json = new() { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };
    private static readonly System.Drawing.Text.PrivateFontCollection PrivateFonts = new();
    private static Assembly? _themeAssembly;

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            var options = Arguments.Parse(args);
            var app = new SupporterApplication(options);
            app.Run();
            return 0;
        }
        catch (Exception ex)
        {
            var error = Unwrap(ex);
            Console.Error.WriteLine(
                string.Equals(Environment.GetEnvironmentVariable("LIANLI_THEME_SUPPORTER_TRACE"), "1", StringComparison.Ordinal)
                    ? error.ToString()
                    : error.Message);
            return 1;
        }
    }

    private static Exception Unwrap(Exception ex)
    {
        while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
        return ex;
    }

    private sealed class SupporterApplication
    {
        private Arguments _args;
        private readonly string _deviceModel;
        private readonly string _lConnectDir;
        private readonly string _profileDir;
        private string _templateRoot;
        private string _templatePath;

        public SupporterApplication(Arguments args)
        {
            _args = args;
            _deviceModel = args.Get("DeviceModel", "hydroshift-ii-lcd-s");
            _lConnectDir = args.Get("LConnectDir", DefaultLConnectDir);
            _profileDir = args.Get("ProfileDir", Path.Combine(DefaultProgramData, "profile"));
            _templateRoot = args.Get("TemplateRoot", Path.Combine(DefaultProgramData, _deviceModel, "template"));
            _templatePath = args.Get("TemplatePath", "");
        }

        public void Run()
        {
            LoadAssemblies(_lConnectDir);
            EnsureDeviceWorkspace(_deviceModel, _lConnectDir);

            if (_args.Has("ListFonts"))
            {
                ListFonts();
                return;
            }

            if (_args.Has("ExtractMissingPreviews"))
            {
                ExtractMissingPreviews();
                return;
            }

            if (_args.Has("RenderSensorPreview"))
            {
                RenderSensorPreview();
                return;
            }

            ResolveTemplate();
            if (_args.Has("ListGraphStyles"))
            {
                WriteJsonOrLines(GetGraphStyles(), style => $"{style.Label}\t{style.Code}");
                return;
            }

            if (!File.Exists(_templatePath)) throw new FileNotFoundException($"Template not found: {_templatePath}");
            var theme = TemplateSerializer.Load(_templatePath);

            if (_args.Has("ListLayers") || _args.Has("Inspect"))
            {
                WriteTemplate(theme);
                return;
            }

            if (_args.Has("InspectBitmaps"))
            {
                InspectBitmaps(theme);
                return;
            }

            if (_args.Has("RenderGraphPreview"))
            {
                RenderGraphPreview(theme);
                return;
            }

            if (_args.Has("RenderLayerCanvas"))
            {
                RenderLayerCanvas(theme);
                return;
            }

            if (_args.Has("RenderClockCanvas"))
            {
                RenderClockCanvas(theme);
                return;
            }

            if (!_args.Has("NoBackup"))
            {
                var backupBase = _templatePath + ".bak-edit-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                var backup = backupBase;
                var suffix = 1;
                while (File.Exists(backup))
                {
                    backup = backupBase + "-" + suffix++;
                }
                File.Copy(_templatePath, backup, false);
                Console.WriteLine("Backup: " + backup);
            }

            if (_args.HasValue("UpdateThemePreview"))
            {
                UpdateThemePreview(theme, _args.Get("UpdateThemePreview"));
                return;
            }

            if (_args.Has("EnsureBackgroundLayer"))
            {
                EnsureBackgroundLayer(theme, resetMedia: true);
                TemplateSerializer.Save(theme, _templatePath);
                Console.WriteLine("Updated: " + _templatePath);
                return;
            }

            if (_args.HasValue("SetBackgroundMedia")) SetBackground(theme, _args.Get("SetBackgroundMedia"));
            if (_args.HasValue("NormalizeTemplateId")) NormalizeTemplateIdentity(theme, _args.Get("NormalizeTemplateId"));
            if (_args.Has("FastLayerBatch") && _args.HasValue("ApplyLayerBatchJson"))
            {
                ApplyLayerBatch(theme);
                TemplateSerializer.Save(theme, _templatePath);
                Console.WriteLine("Updated: " + _templatePath);
                return;
            }
            ApplyLegacyPrimaryFields(theme);
            ApplyAddOperation(theme);
            ApplyGroupingMetadata(theme);
            ApplyRemoveDuplicateMove(theme);
            ApplyLayerBatch(theme);
            ApplyLayerEdit(theme);
            ApplyShadow(theme);
            MoveGroupingMetadataToEnd(theme);
            RepairDataMetadata(theme);
            RepairFontMetadata(theme);
            TemplateSerializer.Save(theme, _templatePath);
            Console.WriteLine("Updated: " + _templatePath);
        }

        private void ResolveTemplate()
        {
            if (_args.Has("UseActiveTemplate"))
            {
                var activeId = ProfileStore.GetActiveTemplateId(
                    _profileDir,
                    _deviceModel,
                    _lConnectDir);
                var activePath = ProfileStore.ResolveActiveTemplatePath(_deviceModel, _lConnectDir, activeId);
                _templatePath = string.IsNullOrWhiteSpace(activePath)
                    ? Path.Combine(_templateRoot, activeId + ".template")
                    : activePath;
            }
            else if (_args.HasValue("TemplateId"))
            {
                _templatePath = Path.Combine(_templateRoot, _args.Get("TemplateId") + ".template");
            }

            if (File.Exists(_templatePath))
            {
                _templateRoot = Path.GetDirectoryName(_templatePath)!;
                return;
            }

            var id = _args.Get("TemplateId", Path.GetFileNameWithoutExtension(_templatePath));
            if (!string.IsNullOrWhiteSpace(id))
            {
                foreach (var model in GetTemplateSearchModels())
                {
                    foreach (var root in new[]
                             {
                                 Path.Combine(DefaultProgramData, model, "template"),
                                 Path.Combine(_lConnectDir, "Assets", model, "template")
                             })
                    {
                        var candidate = Path.Combine(root, id + ".template");
                        if (!File.Exists(candidate)) continue;
                        _templateRoot = root;
                        _templatePath = candidate;
                        return;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(_templatePath))
            {
                foreach (var name in new[] { "LL021.template", "LL02N.template" })
                {
                    var candidate = Path.Combine(_templateRoot, name);
                    if (!File.Exists(candidate)) continue;
                    _templatePath = candidate;
                    return;
                }
            }
        }

        private void ListFonts()
        {
            var filter = _args.Get("FontFilter");
            using var fonts = new System.Drawing.Text.InstalledFontCollection();
            foreach (var name in fonts.Families.Select(x => x.Name)
                         .Where(x => string.IsNullOrWhiteSpace(filter) || x.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                         .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine(name);
            }
        }

        private void WriteTemplate(object theme)
        {
            var layers = Graphs(theme).Cast<object>().Select((graph, index) => LayerInspector.Read(theme, graph, index, _templatePath)).ToList();
            var templateId = Path.GetFileNameWithoutExtension(_templatePath);
            var profileBackground = ProfileStore.GetTemplateBackground(_profileDir, templateId, _deviceModel);
            var backgroundPath = string.IsNullOrWhiteSpace(profileBackground)
                ? ResolveTemplateBackgroundPath(theme)
                : profileBackground;
            var result = new Dictionary<string, object?>
            {
                ["TemplatePath"] = _templatePath,
                ["TemplateId"] = templateId,
                ["Background"] = Path.GetFileName(backgroundPath),
                ["BackgroundPath"] = backgroundPath,
                ["Layers"] = layers,
                ["ProfileModulars"] = Array.Empty<object>()
            };
            if (_args.Has("Json")) Console.WriteLine(Json.Serialize(result));
            else
            {
                Console.WriteLine("TemplatePath: " + _templatePath);
                Console.WriteLine("TemplateId: " + Path.GetFileNameWithoutExtension(_templatePath));
                foreach (var layer in layers) Console.WriteLine($"{layer["Index"],3} {layer["Type"],20} {layer["DataSource"],18}");
            }
        }

        private void InspectBitmaps(object theme)
        {
            var rows = new List<Dictionary<string, object?>>();
            foreach (var row in InspectObjectBitmaps(theme, "Theme", -1))
            {
                rows.Add(row);
            }

            foreach (var graph in Graphs(theme).Cast<object>().Select((Graph, Index) => new { Graph, Index }))
            {
                rows.AddRange(InspectObjectBitmaps(graph.Graph, graph.Graph.GetType().Name, graph.Index));
            }

            if (_args.Has("Json"))
            {
                Console.WriteLine(Json.Serialize(rows));
                return;
            }

            foreach (var row in rows)
            {
                Console.WriteLine($"{row["Index"],3} {row["Type"],16} {row["Property"],28} {row["Width"],5}x{row["Height"],-5} x={row["X"],5} y={row["Y"],5} zoom={row["ZoomRate"],5} img={row["ImgName"]}");
            }
        }

        private static IEnumerable<Dictionary<string, object?>> InspectObjectBitmaps(object source, string typeName, int index)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var property in source.GetType().GetProperties(flags))
            {
                if (property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                Bitmap? bitmap = null;
                try { bitmap = property.GetValue(source) as Bitmap; } catch { }
                if (bitmap == null)
                {
                    continue;
                }

                yield return CreateBitmapInspectRow(source, typeName, index, property.Name, bitmap);
            }

            foreach (var field in source.GetType().GetFields(flags))
            {
                Bitmap? bitmap = null;
                try { bitmap = field.GetValue(source) as Bitmap; } catch { }
                if (bitmap == null)
                {
                    continue;
                }

                yield return CreateBitmapInspectRow(source, typeName, index, field.Name, bitmap);
            }
        }

        private static Dictionary<string, object?> CreateBitmapInspectRow(
            object source,
            string typeName,
            int index,
            string propertyName,
            Bitmap bitmap) =>
            new()
            {
                ["Index"] = index,
                ["Type"] = typeName,
                ["Property"] = propertyName,
                ["ImgName"] = Reflection.GetString(source, "ImgName"),
                ["VideoName"] = Reflection.GetString(source, "videoName"),
                ["X"] = Reflection.Get(source, "posX"),
                ["Y"] = Reflection.Get(source, "posY"),
                ["ZoomRate"] = Reflection.Get(source, "zoom_rate"),
                ["Width"] = bitmap.Width,
                ["Height"] = bitmap.Height
            };

        private List<GraphStyle> GetGraphStyles()
        {
            var result = new List<GraphStyle>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in ModularRoots())
            {
                if (!Directory.Exists(root)) continue;
                foreach (var file in Directory.GetFiles(root, "*.modular").OrderBy(x => x))
                {
                    try
                    {
                        var theme = TemplateSerializer.Load(file);
                        var graph = Graphs(theme).Cast<object>().FirstOrDefault(IsGraphLayer);
                        if (graph == null) continue;
                        var type = graph.GetType().Name;
                        var code = $"MOD::{Path.GetFileName(file)}::{type}";
                        if (!seen.Add(code)) continue;
                        result.Add(new GraphStyle
                        {
                            Label = Path.GetFileNameWithoutExtension(file),
                            Code = code,
                            Source = "Modular",
                            GraphType = type,
                            TypeName = Reflection.GetString(graph, "TypeName"),
                            SubTypeName = Reflection.GetString(graph, "SubTypeName"),
                            Preview = Path.Combine(Path.GetDirectoryName(_templateRoot)!, "preview",
                                "modular_" + Path.GetFileNameWithoutExtension(file) + ".png")
                        });
                    }
                    catch { }
                }
            }
            return result;
        }

        private IEnumerable<string> ModularRoots()
        {
            yield return Path.Combine(Path.GetDirectoryName(_templateRoot)!, "modulars");
            if (_deviceModel.Equals("vm-9.2-inch", StringComparison.OrdinalIgnoreCase))
            {
                yield return Path.Combine(_lConnectDir, "Assets", "vm-9.2-inch", "modulars", "Landscape");
                yield return Path.Combine(_lConnectDir, "Assets", "vm-9.2-inch", "modulars", "Portrait");
                yield break;
            }

            yield return Path.Combine(_lConnectDir, "Assets", "hydroshift-ii-lcd-s", "modulars");
            yield return Path.Combine(_lConnectDir, "Assets", "hydroshift-ii-lcd-c", "modulars");
            yield return Path.Combine(_lConnectDir, "Assets", "universal-screen-8.8-inch", "modulars", "Landscape");
            yield return Path.Combine(_lConnectDir, "Assets", "universal-screen-8.8-inch", "modulars", "Portrait");
        }

        private IEnumerable<string> GetTemplateSearchModels()
        {
            if (_deviceModel.Equals("vm-9.2-inch", StringComparison.OrdinalIgnoreCase))
            {
                yield return _deviceModel;
                yield break;
            }

            foreach (var model in new[]
                     {
                         _deviceModel,
                         "hydroshift-ii-lcd-s",
                         "hydroshift-ii-lcd-c",
                         "universal-screen-8.8-inch"
                     }.Distinct())
            {
                yield return model;
            }
        }

        private void WriteJsonOrLines<T>(IEnumerable<T> items, Func<T, string> format)
        {
            var list = items.ToList();
            if (_args.Has("Json")) Console.WriteLine(Json.Serialize(list));
            else foreach (var item in list) Console.WriteLine(format(item));
        }

        private void UpdateThemePreview(object theme, string sourcePath)
        {
            if (!File.Exists(sourcePath)) throw new FileNotFoundException("Theme preview image not found: " + sourcePath);
            using var source = new Bitmap(sourcePath);
            var embedded = new Bitmap(source);
            Reflection.Set(theme, "themePic", embedded);
            var previewDir = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(_templatePath)!)!, "preview");
            Directory.CreateDirectory(previewDir);
            var target = Path.Combine(previewDir, "template_" + Path.GetFileNameWithoutExtension(_templatePath) + ".png");
            File.Copy(sourcePath, target, true);
            TemplateSerializer.Save(theme, _templatePath);
            Console.WriteLine("PreviewUpdated: " + target);
        }

        private void NormalizeTemplateIdentity(object theme, string templateId)
        {
            var newId = Regex.Replace(templateId ?? "", @"[^A-Za-z0-9_.-]", "_").Trim('.', '_', '-');
            if (string.IsNullOrWhiteSpace(newId))
            {
                newId = Path.GetFileNameWithoutExtension(_templatePath);
            }

            if (string.IsNullOrWhiteSpace(newId))
            {
                return;
            }

            var oldIds = CollectTemplateIdentityCandidates(theme)
                .Append(Path.GetFileNameWithoutExtension(_templatePath))
                .Where(id => !string.IsNullOrWhiteSpace(id) &&
                             !string.Equals(id, newId, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(id => id.Length)
                .ToList();

            Reflection.TrySet(theme, "name", newId);
            Reflection.TrySet(theme, "Name", newId);
            ReplaceTemplateIdentityStrings(theme, oldIds, newId, new HashSet<object>(ReferenceEqualityComparer.Instance));
            Console.WriteLine("TemplateIdentityNormalized: " + newId);
        }

        private static IEnumerable<string> CollectTemplateIdentityCandidates(object? root)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string? value)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                foreach (Match match in Regex.Matches(value, @"(?<id>[A-Za-z0-9_.-]+)\.turtheme", RegexOptions.IgnoreCase))
                {
                    ids.Add(match.Groups["id"].Value);
                }
                foreach (Match match in Regex.Matches(value, @"(?<id>[A-Za-z0-9_.-]+)\.template", RegexOptions.IgnoreCase))
                {
                    ids.Add(match.Groups["id"].Value);
                }
                foreach (Match match in Regex.Matches(value, @"(?<id>LL[A-Za-z0-9]+(?:_\d{8}_\d{6})+)", RegexOptions.IgnoreCase))
                {
                    ids.Add(match.Groups["id"].Value);
                }
            }

            if (root != null)
            {
                var rootName = Reflection.GetString(root, "name");
                if (!string.IsNullOrWhiteSpace(rootName))
                {
                    ids.Add(rootName);
                }

                var rootNamePascal = Reflection.GetString(root, "Name");
                if (!string.IsNullOrWhiteSpace(rootNamePascal))
                {
                    ids.Add(rootNamePascal);
                }

                CollectStrings(root, Add, new HashSet<object>(ReferenceEqualityComparer.Instance));
            }

            return ids;
        }

        private static void CollectStrings(object? value, Action<string> add, HashSet<object> visited)
        {
            if (value == null) return;
            if (value is string text)
            {
                add(text);
                return;
            }

            var type = value.GetType();
            if (IsLeafType(type) || !visited.Add(value)) return;

            if (value is System.Collections.IDictionary dictionary)
            {
                foreach (System.Collections.DictionaryEntry entry in dictionary)
                {
                    CollectStrings(entry.Key, add, visited);
                    CollectStrings(entry.Value, add, visited);
                }
                return;
            }

            if (value is System.Collections.IEnumerable enumerable && value is not Bitmap)
            {
                foreach (var item in enumerable)
                {
                    CollectStrings(item, add, visited);
                }
            }

            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (property.GetIndexParameters().Length != 0 || !property.CanRead) continue;
                try { CollectStrings(property.GetValue(value), add, visited); } catch { }
            }

            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                try { CollectStrings(field.GetValue(value), add, visited); } catch { }
            }
        }

        private static void ReplaceTemplateIdentityStrings(
            object? value,
            IReadOnlyList<string> oldIds,
            string newId,
            HashSet<object> visited)
        {
            if (value == null || oldIds.Count == 0) return;
            var type = value.GetType();
            if (IsLeafType(type) || value is string || !visited.Add(value)) return;

            if (value is System.Collections.IList list)
            {
                for (var index = 0; index < list.Count; index++)
                {
                    if (list[index] is string itemText)
                    {
                        list[index] = RewriteTemplateIdentityString(itemText, oldIds, newId);
                    }
                    else
                    {
                        ReplaceTemplateIdentityStrings(list[index], oldIds, newId, visited);
                    }
                }
            }
            else if (value is System.Collections.IDictionary dictionary)
            {
                var keys = dictionary.Keys.Cast<object>().ToList();
                foreach (var key in keys)
                {
                    var item = dictionary[key];
                    if (item is string itemText)
                    {
                        dictionary[key] = RewriteTemplateIdentityString(itemText, oldIds, newId);
                    }
                    else
                    {
                        ReplaceTemplateIdentityStrings(item, oldIds, newId, visited);
                    }
                }
            }
            else if (value is System.Collections.IEnumerable enumerable && value is not Bitmap)
            {
                foreach (var item in enumerable)
                {
                    ReplaceTemplateIdentityStrings(item, oldIds, newId, visited);
                }
            }

            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (property.GetIndexParameters().Length != 0 || !property.CanRead) continue;
                try
                {
                    var current = property.GetValue(value);
                    if (current is string currentText)
                    {
                        if (property.CanWrite)
                        {
                            property.SetValue(value, RewriteTemplateIdentityString(currentText, oldIds, newId));
                        }
                    }
                    else
                    {
                        ReplaceTemplateIdentityStrings(current, oldIds, newId, visited);
                    }
                }
                catch { }
            }

            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                try
                {
                    var current = field.GetValue(value);
                    if (current is string currentText)
                    {
                        field.SetValue(value, RewriteTemplateIdentityString(currentText, oldIds, newId));
                    }
                    else
                    {
                        ReplaceTemplateIdentityStrings(current, oldIds, newId, visited);
                    }
                }
                catch { }
            }
        }

        private static string RewriteTemplateIdentityString(string value, IReadOnlyList<string> oldIds, string newId)
        {
            var result = value;
            foreach (var oldId in oldIds)
            {
                if (string.Equals(result, oldId, StringComparison.OrdinalIgnoreCase))
                {
                    result = newId;
                    continue;
                }

                result = Regex.Replace(
                    result,
                    $@"(?<![A-Za-z0-9_.-]){Regex.Escape(oldId)}(?=\.turtheme)",
                    newId,
                    RegexOptions.IgnoreCase);
                result = Regex.Replace(
                    result,
                    $@"(?<![A-Za-z0-9_.-]){Regex.Escape(oldId)}(?=\.template)",
                    newId,
                    RegexOptions.IgnoreCase);
                result = Regex.Replace(
                    result,
                    $@"(?<![A-Za-z0-9_.-]){Regex.Escape(oldId)}(?=(?:[-_][A-Za-z0-9][A-Za-z0-9_.-]*)?\.(?:mp4|h264|png|jpe?g|gif|webp)|[-_]\d)",
                    newId,
                    RegexOptions.IgnoreCase);
                result = Regex.Replace(
                    result,
                    $@"(?<![A-Za-z0-9_.-]){Regex.Escape(oldId)}(?![A-Za-z0-9_.-])",
                    newId,
                    RegexOptions.IgnoreCase);
            }

            return result;
        }

        private static bool IsLeafType(Type type) =>
            type.IsPrimitive ||
            type.IsEnum ||
            type == typeof(decimal) ||
            type == typeof(DateTime) ||
            type == typeof(TimeSpan) ||
            type == typeof(Guid) ||
            type == typeof(Color) ||
            type == typeof(Rectangle) ||
            typeof(Image).IsAssignableFrom(type) ||
            typeof(Font).IsAssignableFrom(type) ||
            type.FullName?.StartsWith("System.Windows.", StringComparison.Ordinal) == true;

        private void ExtractMissingPreviews()
        {
            var templateRoot = _args.Get("TemplateRoot", _templateRoot);
            var thumbnailRoot = _args.Get(
                "ThumbnailRoot",
                Path.Combine(Path.GetDirectoryName(templateRoot) ?? templateRoot, "preview-cache"));
            if (!Directory.Exists(templateRoot))
            {
                return;
            }

            Directory.CreateDirectory(thumbnailRoot);
            foreach (var templatePath in Directory.GetFiles(templateRoot, "*.template"))
            {
                var outputPath = Path.Combine(
                    thumbnailRoot,
                    Path.GetFileNameWithoutExtension(templatePath) + ".png");
                if (File.Exists(outputPath) &&
                    File.GetLastWriteTimeUtc(outputPath) >= File.GetLastWriteTimeUtc(templatePath))
                {
                    continue;
                }

                try
                {
                    var theme = TemplateSerializer.Load(templatePath);
                    var preview = Reflection.Get(theme, "themePic");
                    if (preview is Image image)
                    {
                        image.Save(outputPath, ImageFormat.Png);
                        Console.WriteLine("PreviewExtracted: " + outputPath);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        "PreviewExtractFailed: " + templatePath + " - " + ex.Message);
                }
            }
        }

        private void SetBackground(object theme, string source)
        {
            var mediaName = Path.GetFileName(source);
            var path = source;
            if (File.Exists(source))
            {
                var extension = Path.GetExtension(source).ToLowerInvariant();
                var allowed = new[] { ".mp4", ".gif", ".h264", ".png", ".jpg", ".jpeg" };
                if (!allowed.Contains(extension)) throw new InvalidOperationException("Unsupported background media type: " + extension);
                var videoDir = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(_templatePath)!)!, "video");
                Directory.CreateDirectory(videoDir);
                path = Path.Combine(videoDir, mediaName);
                if (!Path.GetFullPath(source).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                    CopyWithRetry(source, path);

                path = SyncUploadedBackgroundMedia(source, 0);
                ProfileStore.SetTemplateBackground(_profileDir, Path.GetFileNameWithoutExtension(_templatePath), path);
            }

            Reflection.TrySet(theme, "videoName", Path.GetFileName(path));
            var themeMediaPath = Path.ChangeExtension(path, ".h264");
            if (!File.Exists(source))
            {
                themeMediaPath = path;
            }
            else if (!File.Exists(themeMediaPath))
            {
                themeMediaPath = path;
            }
            foreach (var name in new[] { "videoPath", "o_videoPath", "videoPath2", "videoPath3" })
                Reflection.TrySet(theme, name, themeMediaPath);
            foreach (var graph in EnsureBackgroundLayer(theme, resetMedia: false))
            {
                Reflection.TrySet(graph, "ImgName", Path.GetFileName(path));
                Reflection.TrySet(graph, "videoName", Path.GetFileName(path));
                Reflection.TrySet(graph, "FilePath", themeMediaPath);
                Reflection.TrySet(graph, "zoom_rate", 1.0);
            }
            SetThemeZoomRate(theme, 1.0);
            Console.WriteLine("BackgroundPath: " + path);
        }

        private List<object> EnsureBackgroundLayer(object theme, bool resetMedia)
        {
            var list = Graphs(theme);
            var animations = list.Cast<object>()
                .Where(layer => layer.GetType().Name == "GraphAnimation")
                .ToList();
            if (animations.Count == 0)
            {
                var sample = FindSampleAcrossTemplates("GraphAnimation")
                             ?? throw new InvalidOperationException("No GraphAnimation sample exists.");
                var layer = TemplateSerializer.Clone(sample);
                list.Insert(0, layer);
                animations.Add(layer);
            }

            foreach (var graph in animations)
            {
                Reflection.TrySet(graph, "hide", false);
                Reflection.TrySet(graph, "zoom_rate", 1.0);
                if (!resetMedia) continue;
                foreach (var name in new[] { "ImgName", "videoName", "FilePath", "Path", "ImagePath" })
                {
                    Reflection.TrySet(graph, name, "");
                }
            }

            if (resetMedia)
            {
                foreach (var name in new[] { "videoName", "videoPath", "o_videoPath", "videoPath2", "videoPath3" })
                {
                    Reflection.TrySet(theme, name, "");
                }
            }

            return animations;
        }

        private void ApplyLegacyPrimaryFields(object theme)
        {
            var list = Graphs(theme);
            if (list.Count > 1)
            {
                var value = list[1]!;
                SetIfProvided(value, "posX", "ValueX");
                SetIfProvided(value, "posY", "ValueY");
                SetFontIfProvided(value, "size", "ValueSize");
                SetFontColorIfProvided(value, "ValueColor");
                if (_args.HasValue("DataSource")) SetDataSource(value, _args.Get("DataSource"));
            }
            if (list.Count > 3)
            {
                var degree = list[3]!;
                SetIfProvided(degree, "posX", "DegreeX");
                SetIfProvided(degree, "posY", "DegreeY");
                SetFontIfProvided(degree, "size", "DegreeSize");
                SetFontColorIfProvided(degree, "DegreeColor");
            }
        }

        private void ApplyAddOperation(object theme)
        {
            var addText = _args.HasValue("AddText");
            var addImage = _args.HasValue("AddImage");
            var addClock = _args.Has("AddClock");
            var addGraph = _args.HasValue("AddProgressBar");
            var addSensor = _args.Has("AddSensor");
            var addData = _args.HasValue("AddDataSource") && !addGraph && !addClock && !addSensor;
            if (new[] { addText, addData, addImage, addClock, addGraph, addSensor }.Count(x => x) > 1)
                throw new InvalidOperationException("Only one layer can be added per operation.");

            if (addText || addData)
            {
                var sample = FindSampleGraph(theme, "GraphItem") ?? throw new InvalidOperationException("No GraphItem sample exists.");
                var layer = TemplateSerializer.Clone(sample);
                Reflection.TrySet(layer, "posX", _args.GetInt("AddX", 240));
                Reflection.TrySet(layer, "posY", _args.GetInt("AddY", 240));
                var font = Reflection.Get(layer, "fontConfig");
                if (font != null)
                {
                    Reflection.TrySet(font, "size", _args.GetInt("AddSize", 40));
                    if (_args.HasValue("AddColor")) Reflection.TrySet(font, "color", ColorParser.Parse(_args.Get("AddColor")));
                    if (_args.HasValue("AddFont")) SetFont(theme, layer, _args.Get("AddFont"));
                    if (_args.Has("AddBold")) Reflection.TrySet(font, "isBold", true);
                    var alignment = Reflection.Get(font, "alignment");
                    if (alignment != null) Reflection.TrySet(alignment, "index", _args.GetInt("AddAlignmentIndex", 1));
                }
                if (addText)
                {
                    SetDataSource(layer, "StaticText");
                    SetDataValue(layer, _args.Get("AddText"));
                    Reflection.TrySet(layer, "TypeName", "Text");
                }
                else
                {
                    SetDataSource(layer, _args.Get("AddDataSource"));
                    if (_args.HasValue("AddFormat")) SetDataFormat(layer, _args.Get("AddFormat"));
                    Reflection.TrySet(layer, "TypeName", "Data");
                }
                Graphs(theme).Add(layer);
            }
            else if (addGraph)
            {
                var layer = NewGraphFromStyle(_args.Get("AddProgressBar"));
                SetDataSource(layer, _args.Get("AddDataSource", "CPULOAD"));
                Reflection.TrySet(layer, "posX", _args.GetInt("AddX", 240));
                Reflection.TrySet(layer, "posY", _args.GetInt("AddY", 240));
                SetGraphColors(layer, _args.Get("AddFrontColor", "#FFFFFF"), _args.Get("AddBackColor", "#20FFFFFF"));
                Graphs(theme).Add(layer);
            }
            else if (addImage)
            {
                AddImage(theme, _args.Get("AddImage"));
            }
            else if (addClock)
            {
                AddClock(theme, _args.Get("AddClock"));
            }
            else if (addSensor)
            {
                var layer = NewSensorLayer(
                    _args.Get("AddSensorStyle", "Ring2"),
                    _args.Get("AddSensorType", "CPULoad"),
                    _args.Get("AddSensorColor1", "#2A00FF"),
                    _args.Get("AddSensorColor2", "#00FFEE"),
                    _args.Get("AddSensorBgColor", "#00454D"),
                    _args.Get("AddSensorTextColor", "#FFFFFF"),
                    _args.Get("AddSensorFont", "Noto Sans TC"),
                    _args.Get("AddSensorTopFontColor", _args.Get("AddSensorTextColor", "#FFFFFF")),
                    _args.Get("AddSensorBottomFontColor", _args.Get("AddSensorTextColor", "#FFFFFF")));
                Reflection.TrySet(layer, "posX", _args.GetInt("AddX", 40));
                Reflection.TrySet(layer, "posY", _args.GetInt("AddY", 40));
                SetSensorZoom(layer, _args.GetDouble("AddSensorZoom", 0.5));
                Reflection.TrySet(layer, "enabled", true);
                Reflection.TrySet(layer, "hide", false);
                var dataSource = SensorDataSource(_args.Get("AddSensorType", "CPULoad"));
                SetDataSource(layer, dataSource);
                SetDataValue(layer, _args.Get("AddSensorValue", "52"));
                Graphs(theme).Add(layer);
            }
        }

        private void ApplyGroupingMetadata(object theme)
        {
            if (!_args.Has("SetGroupingMetadata")) return;

            const string marker = "__LIAN_EDITOR_GROUPS_V1__";
            var value = _args.Get("SetGroupingMetadata");
            var list = Graphs(theme);
            object? metadataLayer = null;
            foreach (var graph in list.Cast<object>())
            {
                var data = Reflection.Get(graph, "m_data");
                if (Reflection.GetString(data, "Value").StartsWith(marker, StringComparison.Ordinal))
                {
                    metadataLayer = graph;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                if (metadataLayer != null) list.Remove(metadataLayer);
                return;
            }

            if (metadataLayer == null)
            {
                var sample = FindSampleGraph(theme, "GraphItem");
                if (sample != null)
                {
                    metadataLayer = TemplateSerializer.Clone(sample);
                }
                else
                {
                    metadataLayer = Activator.CreateInstance(ThemeType("ThemeEngine.GraphItem"), new object[] { "Text" })
                                    ?? throw new InvalidOperationException("Could not create editor metadata layer.");
                    var data = Activator.CreateInstance(ThemeType("ThemeEngine.M_Data"), new object[] { "StaticText" });
                    var font = Activator.CreateInstance(ThemeType("ThemeEngine.FontConfig"));
                    Reflection.TrySet(metadataLayer, "m_data", data);
                    Reflection.TrySet(metadataLayer, "fontConfig", font);
                }
                list.Add(metadataLayer);
            }

            SetStaticText(metadataLayer, value);
            Reflection.TrySet(metadataLayer, "hide", true);
            Reflection.TrySet(metadataLayer, "posX", -10000);
            Reflection.TrySet(metadataLayer, "posY", -10000);
            Reflection.TrySet(metadataLayer, "TypeName", "Text");
            Reflection.TrySet(metadataLayer, "SubTypeName", "EditorMetadata");
        }

        private static void MoveGroupingMetadataToEnd(object theme)
        {
            const string marker = "__LIAN_EDITOR_GROUPS_V1__";
            var list = Graphs(theme);
            object? metadataLayer = null;
            foreach (var graph in list.Cast<object>())
            {
                var data = Reflection.Get(graph, "m_data");
                if (Reflection.GetString(data, "Value").StartsWith(marker, StringComparison.Ordinal))
                {
                    metadataLayer = graph;
                    break;
                }
            }
            if (metadataLayer == null || ReferenceEquals(list[list.Count - 1], metadataLayer)) return;
            list.Remove(metadataLayer);
            list.Add(metadataLayer);
        }

        private void RenderSensorPreview()
        {
            ThemeType("ThemeEngine.ThemeEngine").GetMethod("Init", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
            var styleName = _args.Get("SensorStyle", "Ring2");
            var sensorName = _args.Get("SensorType", "CPULoad");
            var output = _args.Get("Output", Path.Combine(Path.GetTempPath(), "LianLiThemeEditor", "sensor-previews", Guid.NewGuid() + ".png"));
            var layer = NewSensorLayer(
                styleName,
                sensorName,
                _args.Get("SensorColor1", "#FFFFFF"),
                _args.Get("SensorColor2", "#00FFEE"),
                _args.Get("SensorBgColor", "#202020"),
                _args.Get("SensorTextColor", "#FFFFFF"),
                _args.Get("SensorFont", "Noto Sans TC"),
                _args.Get("SensorTopFontColor", _args.Get("SensorTextColor", "#FFFFFF")),
                _args.Get("SensorBottomFontColor", _args.Get("SensorTextColor", "#FFFFFF")));
            SetDataValue(layer, _args.Get("SensorValue", "52"));
            var drawStyle = Reflection.Get(layer, "drawStyle") ?? throw new InvalidOperationException("Sensor draw style was not created.");
            var method = drawStyle.GetType().GetMethod("GetValImage", new[] { typeof(int), typeof(FontFamily) })
                         ?? throw new InvalidOperationException("Sensor preview renderer was not found.");
            var valueText = _args.Get("SensorValue", "52");
            if (!int.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                value = 52;
            }
            using var bitmap = method.Invoke(drawStyle, new object?[] { value, Reflection.Get(layer, "cachedFont") }) as Bitmap
                               ?? throw new InvalidOperationException("Sensor preview could not be rendered.");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            bitmap.SetResolution(96f, 96f);
            bitmap.Save(output, ImageFormat.Png);
            Console.WriteLine(output);
        }

        private void RenderGraphPreview(object theme)
        {
            ApplyLayerEdit(theme);
            var list = Graphs(theme);
            var index = _args.GetInt("LayerIndex");
            ValidateIndex(list, index);
            var layer = list[index]!;
            if (!IsGraphLayer(layer))
            {
                throw new InvalidOperationException("Layer is not a ThemeEngine graph layer.");
            }
            ApplyRawLayerEdit(layer);
            var data = Reflection.Get(layer, "m_data");
            var currentValue = Reflection.GetString(data, "Value");
            if (string.IsNullOrWhiteSpace(currentValue) || currentValue == "0")
            {
                SetDataValue(layer, _args.Get("PreviewValue", "53"));
            }

            var rendered = LayerInspector.GraphPreviewPath(layer, _templatePath);
            if (string.IsNullOrWhiteSpace(rendered) || !File.Exists(rendered))
            {
                throw new InvalidOperationException("Graph preview could not be rendered.");
            }

            var output = _args.Get("Output", rendered);
            if (!string.Equals(output, rendered, StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.Copy(rendered, output, true);
                rendered = output;
            }
            Console.WriteLine(rendered);
        }

        private void RenderLayerCanvas(object theme)
        {
            var list = Graphs(theme);
            var index = _args.GetInt("LayerIndex");
            ValidateIndex(list, index);
            var layer = TemplateSerializer.Clone(list[index]!);
            ApplyRawLayerEdit(layer);

            var width = Math.Max(1, _args.GetInt("CanvasWidth", 1920));
            var height = Math.Max(1, _args.GetInt("CanvasHeight", 480));
            var output = _args.Get("Output", Path.Combine(Path.GetTempPath(), "LianLiThemeEditor", "layer-canvas.png"));
            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var render = layer.GetType().GetMethod("Render", new[] { typeof(Graphics), typeof(bool), typeof(bool), typeof(bool) });
                render?.Invoke(layer, new object[] { graphics, true, true, false });
            }
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            bitmap.SetResolution(96f, 96f);
            bitmap.Save(output, ImageFormat.Png);
            Console.WriteLine(output);
        }

        private void ApplyRawLayerEdit(object layer)
        {
            if (!_args.HasValue("RawPath")) return;
            var target = ResolveRawTarget(layer, _args.Get("RawPath"));
            if (target.Object == null || string.IsNullOrWhiteSpace(target.Member))
            {
                throw new InvalidOperationException("RawPath could not be resolved: " + _args.Get("RawPath"));
            }
            SetRawMember(target.Object, target.Member, _args.Get("RawValue"));
        }

        private static (object? Object, string Member) ResolveRawTarget(object layer, string path)
        {
            var parts = (path ?? "").Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return (null, "");
            object? current = layer;
            var start = 0;
            if (parts[0].Equals("layer", StringComparison.OrdinalIgnoreCase))
            {
                start = 1;
            }
            else if (parts[0].Equals("data", StringComparison.OrdinalIgnoreCase))
            {
                current = Reflection.Get(layer, "m_data");
                start = 1;
            }
            else if (parts[0].Equals("font", StringComparison.OrdinalIgnoreCase))
            {
                current = Reflection.Get(layer, "fontConfig");
                start = 1;
            }
            else if (parts[0].Equals("alignment", StringComparison.OrdinalIgnoreCase))
            {
                current = Reflection.Get(Reflection.Get(layer, "fontConfig"), "alignment");
                start = 1;
            }
            else if (parts[0].Equals("styleInfo", StringComparison.OrdinalIgnoreCase))
            {
                current = Reflection.Get(layer, "styleInfo");
                start = 1;
            }

            for (var i = start; i < parts.Length - 1; i++)
            {
                current = Reflection.Get(current, parts[i]);
            }
            return (current, parts.Last());
        }

        private static void SetRawMember(object target, string member, string value)
        {
            var type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            var property = type.GetProperty(member, flags);
            if (property != null && property.CanWrite)
            {
                property.SetValue(target, ConvertRawValue(value, property.PropertyType));
                return;
            }
            var field = type.GetField(member, flags) ?? type.GetField($"<{member}>k__BackingField", flags);
            if (field != null)
            {
                field.SetValue(target, ConvertRawValue(value, field.FieldType));
                return;
            }
            throw new MissingMemberException(type.FullName, member);
        }

        private static object? ConvertRawValue(string value, Type targetType)
        {
            var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (underlying == typeof(string)) return value;
            if (underlying == typeof(bool)) return bool.Parse(value);
            if (underlying == typeof(int)) return int.Parse(value, CultureInfo.InvariantCulture);
            if (underlying == typeof(byte)) return byte.Parse(value, CultureInfo.InvariantCulture);
            if (underlying == typeof(float)) return float.Parse(value, CultureInfo.InvariantCulture);
            if (underlying == typeof(double)) return double.Parse(value, CultureInfo.InvariantCulture);
            if (underlying == typeof(Color)) return ColorParser.Parse(value);
            if (underlying.IsEnum) return Enum.Parse(underlying, value, true);
            if (underlying.IsGenericType && underlying.GetGenericTypeDefinition() == typeof(Queue<>) &&
                underlying.GetGenericArguments()[0] == typeof(string))
            {
                return new Queue<string>(Regex.Split(value, @"[,; ]+").Where(x => x.Length > 0));
            }
            if (value.Equals("null", StringComparison.OrdinalIgnoreCase)) return null;
            return value;
        }

        private void RenderClockCanvas(object theme)
        {
            var list = Graphs(theme);
            var index = _args.GetInt("LayerIndex");
            ValidateIndex(list, index);
            var layer = TemplateSerializer.Clone(list[index]!);
            if (!string.Equals(layer.GetType().Name, "GraphClock", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Layer is not a ThemeEngine GraphClock layer.");
            }

            var data = Reflection.Get(layer, "m_data");
            var previewValue = _args.Get("PreviewValue", "23");
            SetDataValue(layer, previewValue);
            if (data != null && double.TryParse(_args.Get("PreviewRate", "0.23"), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var previewRate))
            {
                Reflection.TrySet(data, "Rate", previewRate);
            }

            var width = Math.Max(1, _args.GetInt("CanvasWidth", 480));
            var height = Math.Max(1, _args.GetInt("CanvasHeight", 480));
            var output = _args.Get("Output", Path.Combine(Path.GetTempPath(), "LianLiThemeEditor", "clock-canvas.png"));
            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                var render = layer.GetType().GetMethod("Render", new[] { typeof(Graphics), typeof(bool), typeof(bool), typeof(bool) })
                             ?? throw new InvalidOperationException("GraphClock.Render was not found.");
                render.Invoke(layer, new object[] { graphics, true, true, false });
            }

            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            bitmap.SetResolution(96f, 96f);
            bitmap.Save(output, ImageFormat.Png);
            Console.WriteLine(output);
        }

        private object NewSensorLayer(
            string styleName,
            string sensorName,
            string color1,
            string color2,
            string bgColor,
            string textColor,
            string fontFamily,
            string? topTextColor = null,
            string? bottomTextColor = null)
        {
            ThemeType("ThemeEngine.ThemeEngine").GetMethod("Init", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
            var styleType = Enum.Parse(ThemeType("ThemeEngine.Contr.StyleTypes"), styleName, true);
            var sensorType = Enum.Parse(ThemeType("ThemeEngine.Contr.SenSorTypes"), sensorName, true);
            var styleInfoType = ThemeType("ThemeEngine.Contr.StyleInfo");
            var styleInfo = Activator.CreateInstance(styleInfoType)
                            ?? throw new InvalidOperationException("StyleInfo could not be created.");
            ApplySensorStyleInfo(styleInfo, color1, color2, bgColor, textColor, fontFamily, topTextColor, bottomTextColor);

            var graphSensorType = ThemeType("ThemeEngine.GraphSensor");
            var layer = Activator.CreateInstance(graphSensorType, styleType, sensorType, styleInfo)
                        ?? throw new InvalidOperationException("GraphSensor could not be created.");
            if (Reflection.Get(layer, "styleInfo") is { } layerStyleInfo)
            {
                ApplySensorStyleInfo(layerStyleInfo, color1, color2, bgColor, textColor, fontFamily, topTextColor, bottomTextColor);
            }
            Reflection.TrySet(layer, "TypeName", "Sensor");
            Reflection.TrySet(layer, "SubTypeName", styleName);
            Reflection.TrySet(layer, "DisplayName", $"Sensor {styleName}");

            var mData = Activator.CreateInstance(ThemeType("ThemeEngine.M_Data"), SensorDataSource(sensorName))
                        ?? throw new InvalidOperationException("M_Data could not be created.");
            Reflection.Set(layer, "m_data", mData);
            return layer;
        }

        private static void ApplySensorStyleInfo(object styleInfo, string color1, string color2, string bgColor, string textColor, string fontFamily, string? topTextColor = null, string? bottomTextColor = null)
        {
            Reflection.TrySet(styleInfo, "Color1", ColorParser.Parse(color1));
            Reflection.TrySet(styleInfo, "Color2", ColorParser.Parse(color2));
            Reflection.TrySet(styleInfo, "BgColor", ColorParser.Parse(bgColor));
            Reflection.TrySet(styleInfo, "MainFontColor", ColorParser.Parse(textColor));
            Reflection.TrySet(styleInfo, "FontTopColor", ColorParser.Parse(topTextColor ?? textColor));
            Reflection.TrySet(styleInfo, "FontBottomColor", ColorParser.Parse(bottomTextColor ?? textColor));
            Reflection.TrySet(styleInfo, "FontFamily", fontFamily);
        }

        private static Type ThemeType(string name) =>
            _themeAssembly?.GetType(name, throwOnError: true)
            ?? throw new InvalidOperationException($"ThemeEngine type was not found: {name}");

        private static string SensorDataSource(string sensorName) => sensorName.ToUpperInvariant() switch
        {
            "CPULOAD" => "CPULOAD",
            "CPUTEMPERATURE" => "CPUTEMP",
            "CPUTEMPERATUREF" => "CPUTEMP_F",
            "GPULOAD" => "GPULOAD",
            "GPUTEMPERATURE" => "GPUTEMP",
            "GPUTEMPERATUREF" => "GPUTEMP_F",
            "FANRPM" => "CPUFAN",
            _ => sensorName
        };

        private void AddImage(object theme, string sourcePath)
        {
            if (!File.Exists(sourcePath)) throw new FileNotFoundException("Image not found: " + sourcePath);
            var sample = FindSampleAcrossTemplates("GraphImage") ?? throw new InvalidOperationException("No GraphImage sample exists.");
            var layer = TemplateSerializer.Clone(sample);
            var imageDir = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(_templatePath)!)!, "image");
            Directory.CreateDirectory(imageDir);
            var target = Path.Combine(imageDir, Path.GetFileName(sourcePath));
            CopyWithRetry(sourcePath, target);
            Reflection.TrySet(layer, "posX", _args.GetInt("AddX", 240));
            Reflection.TrySet(layer, "posY", _args.GetInt("AddY", 240));
            Reflection.TrySet(layer, "ImgName", Path.GetFileName(target));
            Reflection.TrySet(layer, "FilePath", target);
            Reflection.TrySet(layer, "Path", target);
            Reflection.TrySet(layer, "ImagePath", target);
            using var bitmap = new Bitmap(target);
            EmbedImage(layer, bitmap, _args.GetInt("AddSize", bitmap.Width));
            Graphs(theme).Add(layer);
        }

        private void AddClock(object theme, string sourcePath)
        {
            if (!File.Exists(sourcePath)) throw new FileNotFoundException("Clock hand image not found: " + sourcePath);
            var data = Activator.CreateInstance(ThemeType("ThemeEngine.M_Data"), _args.Get("AddDataSource", "TIME"))
                       ?? throw new InvalidOperationException("Clock data could not be created.");
            var layer = Activator.CreateInstance(ThemeType("ThemeEngine.GraphClock"), data)
                        ?? throw new InvalidOperationException("GraphClock could not be created.");

            var imageDir = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(_templatePath)!)!, "image");
            Directory.CreateDirectory(imageDir);
            var target = Path.Combine(imageDir, Path.GetFileName(sourcePath));
            CopyWithRetry(sourcePath, target);
            using var bitmap = new Bitmap(target);
            var requestedSize = Math.Max(1, _args.GetInt("AddSize", bitmap.Width));
            var zoom = requestedSize / (double)Math.Max(1, bitmap.Width);
            var renderedWidth = Math.Max(1, (int)Math.Round(bitmap.Width * zoom));
            var renderedHeight = Math.Max(1, (int)Math.Round(bitmap.Height * zoom));

            Reflection.TrySet(layer, "centerX", _args.GetInt("AddX", 240));
            Reflection.TrySet(layer, "centerY", _args.GetInt("AddY", 240));
            Reflection.TrySet(layer, "posX", -renderedWidth / 2);
            Reflection.TrySet(layer, "posY", -renderedHeight / 2);
            Reflection.TrySet(layer, "angle", 0);
            Reflection.TrySet(layer, "endAngle", 360);
            Reflection.TrySet(layer, "offset", 0f);
            Reflection.TrySet(layer, "AngleOffset", 0);
            Reflection.TrySet(layer, "enabled", true);
            Reflection.TrySet(layer, "hide", false);
            Reflection.TrySet(layer, "TypeName", "Clock");
            Reflection.TrySet(layer, "DisplayName", "Gauge");
            Reflection.TrySet(layer, "ImgName", Path.GetFileName(target));
            Reflection.TrySet(layer, "zoom_rate", zoom);
            if (_args.HasValue("AddFormat")) SetDataFormat(layer, _args.Get("AddFormat"));
            EmbedImage(layer, bitmap, renderedWidth);
            Graphs(theme).Add(layer);
        }

        private void ApplyRemoveDuplicateMove(object theme)
        {
            var list = Graphs(theme);
            if (_args.HasValue("RemoveLayerIndex"))
            {
                var index = _args.GetInt("RemoveLayerIndex");
                ValidateIndex(list, index);
                if (list[index]!.GetType().Name == "GraphAnimation" && !_args.Has("ForceRemoveBaseLayer"))
                    throw new InvalidOperationException("Background animation layer cannot be removed.");
                list.RemoveAt(index);
            }
            if (_args.HasValue("DuplicateLayerIndex"))
            {
                var index = _args.GetInt("DuplicateLayerIndex");
                ValidateIndex(list, index);
                var clone = TemplateSerializer.Clone(list[index]!);
                Reflection.TrySet(clone, "posX", Reflection.GetInt(clone, "posX") + 10);
                Reflection.TrySet(clone, "posY", Reflection.GetInt(clone, "posY") + 10);
                list.Insert(index + 1, clone);
            }
            if (_args.HasValue("MoveLayerIndex"))
            {
                var index = _args.GetInt("MoveLayerIndex");
                ValidateIndex(list, index);
                var target = _args.Get("MoveLayerDirection").Equals("Up", StringComparison.OrdinalIgnoreCase) ? index - 1 : index + 1;
                ValidateIndex(list, target);
                if (list[index]!.GetType().Name == "GraphAnimation" || list[target]!.GetType().Name == "GraphAnimation")
                    throw new InvalidOperationException("Background animation layer cannot be reordered.");
                var item = list[index];
                list[index] = list[target];
                list[target] = item;
            }
        }

        private void ApplyLayerEdit(object theme)
        {
            if (!_args.HasValue("LayerIndex")) return;
            var list = Graphs(theme);
            var index = _args.GetInt("LayerIndex");
            ValidateIndex(list, index);
            var layer = list[index]!;

            if (_args.HasValue("LayerGraphStyle"))
            {
                var replacement = NewGraphFromStyle(_args.Get("LayerGraphStyle"));
                CopyPositionAndData(layer, replacement);
                list[index] = replacement;
                layer = replacement;
            }

            if (layer.GetType().Name == "GraphSensor" &&
                (_args.HasValue("LayerSensorStyle") || _args.HasValue("LayerSensorType")))
            {
                var replacement = NewSensorLayer(
                    _args.Get("LayerSensorStyle", Reflection.GetString(layer, "styleType")),
                    _args.Get("LayerSensorType", Reflection.GetString(layer, "senSorType")),
                    _args.Get("LayerSensorColor1", GetStyleInfoColor(layer, "Color1", "#2A00FF")),
                    _args.Get("LayerSensorColor2", GetStyleInfoColor(layer, "Color2", "#00FFEE")),
                    _args.Get("LayerSensorBgColor", GetStyleInfoColor(layer, "BgColor", "#00454D")),
                    _args.Get("LayerSensorMainFontColor", GetStyleInfoColor(layer, "MainFontColor", "#FFFFFF")),
                    _args.Get("LayerSensorFont", GetStyleInfoString(layer, "FontFamily", "Noto Sans TC")),
                    _args.Get("LayerSensorTopFontColor", GetStyleInfoColor(layer, "FontTopColor", "#FFFFFF")),
                    _args.Get("LayerSensorBottomFontColor", GetStyleInfoColor(layer, "FontBottomColor", "#FFFFFF")));
                CopyPositionAndData(layer, replacement);
                Reflection.TrySet(replacement, "posX", Reflection.GetInt(layer, "posX"));
                Reflection.TrySet(replacement, "posY", Reflection.GetInt(layer, "posY"));
                Reflection.TrySet(replacement, "ZoomRate", Reflection.Get(layer, "ZoomRate"));
                list[index] = replacement;
                layer = replacement;
            }

            foreach (var pair in new[]
                     {
                         ("LayerX", "posX"), ("LayerY", "posY"), ("LayerWidth", "width"), ("LayerHeight", "height"),
                         ("LayerRadius", "radius"), ("LayerDiameter", "diameter"), ("LayerThickness", "archWidth"),
                         ("LayerDirection", "direction"), ("LayerLineWidth", "lineWidth"), ("LayerColumnWidth", "columnWidth"),
                         ("LayerBorderWidth", "borderWidth"), ("LayerInnerCircleRadius", "InnerCircleRadius"),
                         ("LayerSplitBlockWidth", "SplitBlockWidth"), ("LayerSplitBlankWidth", "SplitBlankWidth"),
                         ("LayerMinValue", "minValue"), ("LayerMaxValue", "maxValue"), ("LayerStartPercentage", "startPer"), ("LayerTotalAngle", "totalAngel"),
                         ("LayerTypeName", "TypeName"), ("LayerSubTypeName", "SubTypeName")
                         ,("LayerClockCenterX", "centerX"), ("LayerClockCenterY", "centerY"),
                         ("LayerClockAngle", "angle"), ("LayerClockEndAngle", "endAngle"),
                         ("LayerClockOffset", "offset"), ("LayerClockOriginX", "o_X"), ("LayerClockOriginY", "o_Y")
                      })
            {
                SetIfProvided(layer, pair.Item2, pair.Item1);
            }

            foreach (var pair in new[]
                     {
                         ("LayerHide", "hide"), ("LayerUseGradient", "useGradient"), ("LayerUseSubsection", "useSubsection"),
                         ("LayerFillBack", "fillBack"), ("LayerRevert", "revert"), ("LayerTransparentBackground", "trBack"),
                         ("LayerInvertDirection", "rollDirection"), ("LayerUseBlock", "useBlock"),
                         ("LayerRingBorder", "HasRingBorder"), ("LayerRound", "round")
                         ,("LayerClockMoveOrigin", "moveOpoint")
                     })
            {
                if (_args.HasValue(pair.Item1)) Reflection.TrySet(layer, pair.Item2, _args.GetBool(pair.Item1));
            }

            if (_args.HasValue("LayerDataSource")) SetDataSource(layer, _args.Get("LayerDataSource"));
            if (_args.Has("LayerText"))
            {
                if (layer.GetType().Name == "GraphSensor")
                {
                    SetDataValue(layer, _args.Get("LayerText"));
                }
                else
                {
                    SetStaticText(layer, _args.Get("LayerText"));
                }
            }
            if (_args.Has("LayerFormat")) SetDataFormat(layer, _args.Get("LayerFormat"));

            var font = Reflection.Get(layer, "fontConfig");
            if (font != null)
            {
                if (_args.HasValue("LayerSize")) Reflection.TrySet(font, "size", _args.GetInt("LayerSize"));
                if (_args.HasValue("LayerColor")) Reflection.TrySet(font, "color", ColorParser.Parse(_args.Get("LayerColor")));
                if (_args.HasValue("LayerFont")) SetFont(theme, layer, _args.Get("LayerFont"));
                if (_args.HasValue("LayerBold")) Reflection.TrySet(font, "isBold", _args.GetBool("LayerBold"));
                if (_args.HasValue("LayerItalic")) Reflection.TrySet(font, "IsItalic", _args.GetBool("LayerItalic"));
                if (_args.HasValue("LayerFontInterval")) Reflection.TrySet(font, "interval", _args.GetDouble("LayerFontInterval"));
                if (_args.HasValue("LayerFontGradientColor")) Reflection.TrySet(font, "GrColor", ColorParser.Parse(_args.Get("LayerFontGradientColor")));
                if (_args.HasValue("LayerFontGradientDirection")) Reflection.TrySet(font, "GrDirection", _args.GetInt("LayerFontGradientDirection"));
                var alignment = Reflection.Get(font, "alignment");
                if (alignment != null && _args.HasValue("LayerAlignmentIndex"))
                    Reflection.TrySet(alignment, "index", _args.GetInt("LayerAlignmentIndex"));
            }

            ApplyColor(layer, "LayerFrontColor", "FrontColor", "LineColor", "FillColor");
            ApplyColor(layer, "LayerBackColor", "BackColor", "BorderColor");
            ApplyColor(layer, "LayerLineColor", "LineColor");
            ApplyColor(layer, "LayerFillColor", "FillColor");
            ApplyColor(layer, "LayerBorderColor", "BorderColor");
            ApplyColor(layer, "LayerGradientColor", "GradientColor");
            if (_args.HasValue("LayerFrontAlpha")) Reflection.TrySet(layer, "FrontAlpha", (byte)Math.Min(255, _args.GetInt("LayerFrontAlpha")));
            if (_args.HasValue("LayerBackAlpha")) Reflection.TrySet(layer, "BackAlpha", (byte)Math.Min(255, _args.GetInt("LayerBackAlpha")));
            if (_args.HasValue("LayerFillAlpha"))
            {
                var old = Reflection.Get(layer, "FillColor");
                if (old is Color color) Reflection.TrySet(layer, "FillColor", Color.FromArgb(Math.Min(255, _args.GetInt("LayerFillAlpha")), color));
            }
            if (layer.GetType().Name == "GraphSensor") ApplySensorLayerEdit(layer);

            if (_args.HasValue("LayerImgName")) UpdateLayerImage(layer, _args.Get("LayerImgName"));
            if (_args.HasValue("LayerZoomRate")) UpdateLayerZoom(theme, layer, _args.GetDouble("LayerZoomRate"));
            if (_args.HasValue("LayerRotate")) UpdateLayerRotation(theme, layer, index, _args.GetInt("LayerRotate"));
            if (_args.HasValue("LayerRect"))
            {
                var parts = Regex.Split(_args.Get("LayerRect"), @"[,; ]+").Where(x => x.Length > 0).Select(int.Parse).ToArray();
                if (parts.Length != 4) throw new InvalidOperationException("LayerRect must be x,y,width,height");
                Reflection.TrySet(layer, "rect", new Rectangle(parts[0], parts[1], parts[2], parts[3]));
            }
        }

        private void ApplyLayerBatch(object theme)
        {
            if (!_args.HasValue("ApplyLayerBatchJson")) return;
            var path = _args.Get("ApplyLayerBatchJson");
            if (!File.Exists(path)) throw new FileNotFoundException("Layer batch file not found.", path);
            var batch = Json.Deserialize<List<List<string>>>(File.ReadAllText(path, Encoding.UTF8))
                        ?? new List<List<string>>();
            var parentArgs = _args;
            try
            {
                foreach (var layerArgs in batch)
                {
                    _args = Arguments.Parse(layerArgs.ToArray());
                    ApplyLayerEdit(theme);
                }
            }
            finally
            {
                _args = parentArgs;
            }
        }

        private void ApplyShadow(object theme)
        {
            if (!_args.HasValue("AddShadowForLayer")) return;
            var list = Graphs(theme);
            var index = _args.GetInt("AddShadowForLayer");
            ValidateIndex(list, index);
            var source = list[index]!;
            var shadow = TemplateSerializer.Clone(source);
            Reflection.TrySet(shadow, "posX", Reflection.GetInt(source, "posX") + _args.GetInt("ShadowOffsetX", 4));
            Reflection.TrySet(shadow, "posY", Reflection.GetInt(source, "posY") + _args.GetInt("ShadowOffsetY", 4));
            var font = Reflection.Get(shadow, "fontConfig");
            if (font != null) Reflection.TrySet(font, "color", ColorParser.Parse(_args.Get("ShadowColor", "#66000000")));
            Reflection.TrySet(shadow, "useGradient", false);
            list.Insert(index, shadow);
        }

        private void ApplyColor(object layer, string argument, params string[] properties)
        {
            if (!_args.HasValue(argument)) return;
            var color = ColorParser.Parse(_args.Get(argument));
            foreach (var property in properties) Reflection.TrySet(layer, property, color);
        }

        private void ApplySensorLayerEdit(object layer)
        {
            var styleInfo = Reflection.Get(layer, "styleInfo");
            if (styleInfo != null)
            {
                SetSensorStyleColor(styleInfo, "LayerSensorColor1", "Color1");
                SetSensorStyleColor(styleInfo, "LayerSensorColor2", "Color2");
                SetSensorStyleColor(styleInfo, "LayerSensorBgColor", "BgColor");
                SetSensorStyleColor(styleInfo, "LayerSensorMainFontColor", "MainFontColor");
                SetSensorStyleColor(styleInfo, "LayerSensorTopFontColor", "FontTopColor");
                SetSensorStyleColor(styleInfo, "LayerSensorBottomFontColor", "FontBottomColor");
                if (_args.HasValue("LayerSensorFont")) Reflection.TrySet(styleInfo, "FontFamily", _args.Get("LayerSensorFont"));
            }
            if (_args.HasValue("LayerSensorZoom")) SetSensorZoom(layer, _args.GetDouble("LayerSensorZoom"));

            if (_args.HasValue("LayerText")) SetDataValue(layer, _args.Get("LayerText"));
        }

        private static void SetSensorZoom(object layer, double zoom)
        {
            var value = (float)Math.Max(0.01, zoom);
            Reflection.TrySet(layer, "ZoomRate", value);
            if (Reflection.Get(layer, "styleInfo") is { } styleInfo)
            {
                Reflection.TrySet(styleInfo, "ZoomRate", value);
            }
        }

        private void SetSensorStyleColor(object styleInfo, string argument, string property)
        {
            if (_args.HasValue(argument))
            {
                Reflection.TrySet(styleInfo, property, ColorParser.Parse(_args.Get(argument)));
            }
        }

        private static string GetStyleInfoColor(object layer, string property, string fallback)
        {
            var styleInfo = Reflection.Get(layer, "styleInfo");
            return styleInfo == null ? fallback : Reflection.Get(styleInfo, property)?.ToString() ?? fallback;
        }

        private static string GetStyleInfoString(object layer, string property, string fallback)
        {
            var styleInfo = Reflection.Get(layer, "styleInfo");
            var value = styleInfo == null ? "" : Reflection.GetString(styleInfo, property);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private void UpdateLayerImage(object layer, string mediaName)
        {
            Reflection.TrySet(layer, "ImgName", mediaName);
            Reflection.TrySet(layer, "videoName", mediaName);
            if (layer.GetType().Name == "GraphAnimation")
            {
                var videoPath = ResolveAnimationMediaPath(mediaName);
                var videoName = Path.GetFileName(videoPath);
                Reflection.TrySet(layer, "ImgName", videoName);
                Reflection.TrySet(layer, "videoName", videoName);
                Reflection.TrySet(layer, "FilePath", videoPath);
                Reflection.TrySet(layer, "Path", videoPath);
                Reflection.TrySet(layer, "ImagePath", videoPath);
                return;
            }

            var imagePath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(_templatePath)!)!, "image", mediaName);
            Reflection.TrySet(layer, "FilePath", imagePath);
            Reflection.TrySet(layer, "Path", imagePath);
            Reflection.TrySet(layer, "ImagePath", imagePath);
            if (File.Exists(imagePath))
            {
                using var bitmap = new Bitmap(imagePath);
                var zoom = Reflection.GetDouble(layer, "zoom_rate", 1);
                EmbedImage(layer, bitmap, Math.Max(1, (int)Math.Round(bitmap.Width * zoom)));
            }
        }

        private string ResolveAnimationMediaPath(string mediaName)
        {
            if (string.IsNullOrWhiteSpace(mediaName))
            {
                return mediaName;
            }

            if (Path.IsPathRooted(mediaName) && File.Exists(mediaName))
            {
                return mediaName;
            }

            var videoDir = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(_templatePath)!)!, "video");
            var videoPath = Path.Combine(videoDir, Path.GetFileName(mediaName));
            if (File.Exists(videoPath))
            {
                return videoPath;
            }

            var h264Path = Path.ChangeExtension(videoPath, ".h264");
            return File.Exists(h264Path) ? h264Path : videoPath;
        }

        private void UpdateLayerZoom(object theme, object layer, double zoom)
        {
            if (layer.GetType().Name == "GraphAnimation")
            {
                SetThemeZoomRate(theme, zoom);
            }
            Reflection.TrySet(layer, "zoom_rate", zoom);
            var name = Reflection.GetString(layer, "ImgName");
            var path = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(_templatePath)!)!, "image", name);
            if (!File.Exists(path)) return;
            using var bitmap = new Bitmap(path);
            EmbedImage(layer, bitmap, Math.Max(1, (int)Math.Round(bitmap.Width * zoom)));
        }

        private static void SetThemeZoomRate(object theme, double zoom)
        {
            Reflection.TrySet(theme, "ZoomRate", zoom);
            Reflection.TrySet(theme, "_ZoomRate", zoom);
        }

        private void UpdateLayerRotation(object theme, object layer, int index, int rotation)
        {
            if (layer.GetType().Name == "GraphImage")
            {
                var metadata = EditorMetadata.Load(_templatePath);
                var previous = metadata.ImageRotations.TryGetValue(index.ToString(), out var value) ? value : 0;
                var delta = ((rotation - previous) % 360 + 360) % 360;
                RotateEmbeddedBitmaps(layer, delta);
                metadata.ImageRotations[index.ToString()] = rotation;
                metadata.Save(_templatePath);
            }
            else if (layer.GetType().Name == "GraphAnimation")
            {
                var metadata = EditorMetadata.Load(_templatePath);
                var requested = ((rotation % 4) + 4) % 4;
                var previous = metadata.BackgroundRotation;
                var deltaDegrees = ((requested - previous + 4) % 4) * 90;
                if (deltaDegrees != 0)
                {
                    var source = ProfileStore.GetTemplateBackground(_profileDir, Path.GetFileNameWithoutExtension(_templatePath), _deviceModel);
                    if (string.IsNullOrWhiteSpace(source)) source = ThemeBackgroundPath(theme);
                    if (File.Exists(source))
                    {
                        var rotated = SyncUploadedBackgroundMedia(source, deltaDegrees);
                        ProfileStore.SetTemplateBackground(_profileDir, Path.GetFileNameWithoutExtension(_templatePath), rotated);
                        Reflection.TrySet(layer, "FilePath", rotated);
                        Reflection.TrySet(layer, "videoName", Path.GetFileName(rotated));
                        Reflection.TrySet(theme, "videoName", Path.GetFileName(rotated));
                        var devicePath = Path.ChangeExtension(rotated, ".h264");
                        if (!File.Exists(devicePath)) devicePath = rotated;
                        foreach (var name in new[] { "videoPath", "o_videoPath", "videoPath2", "videoPath3" })
                            Reflection.TrySet(theme, name, devicePath);
                    }
                }
                metadata.BackgroundRotation = requested;
                metadata.Save(_templatePath);
                Reflection.TrySet(layer, "ration", rotation);
            }
            Reflection.TrySet(layer, "rotate", rotation);
        }

        private static void RotateEmbeddedBitmaps(object layer, int degrees)
        {
            var flip = degrees switch
            {
                90 => RotateFlipType.Rotate90FlipNone,
                180 => RotateFlipType.Rotate180FlipNone,
                270 => RotateFlipType.Rotate270FlipNone,
                _ => RotateFlipType.RotateNoneFlipNone
            };
            foreach (var name in new[] { "bitmap", "O_bitmap", "S_bitmap" })
            {
                if (Reflection.Get(layer, name) is not Bitmap bitmap) continue;
                var clone = new Bitmap(bitmap);
                clone.RotateFlip(flip);
                Reflection.TrySet(layer, name, clone);
            }
        }

        private static void EmbedImage(object layer, Bitmap source, int targetWidth)
        {
            var ratio = source.Height / (double)Math.Max(1, source.Width);
            var targetHeight = Math.Max(1, (int)Math.Round(targetWidth * ratio));
            var resized = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(resized))
            {
                graphics.DrawImage(source, 0, 0, targetWidth, targetHeight);
            }
            Reflection.TrySet(layer, "bitmap", new Bitmap(resized));
            Reflection.TrySet(layer, "O_bitmap", new Bitmap(source));
            Reflection.TrySet(layer, "S_bitmap", new Bitmap(resized));
        }

        private object NewGraphFromStyle(string code)
        {
            if (code.Equals("DynamicStatus", StringComparison.OrdinalIgnoreCase))
            {
                var mData = Activator.CreateInstance(ThemeType("ThemeEngine.M_Data"), "CPULOAD")
                            ?? throw new InvalidOperationException("M_Data could not be created.");
                return Activator.CreateInstance(ThemeType("ThemeEngine.GraphDynamicBar"), mData)
                       ?? throw new InvalidOperationException("GraphDynamicBar could not be created.");
            }
            if (!code.StartsWith("MOD::", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unsupported graph style: " + code);
            var parts = code.Split(new[] { "::" }, StringSplitOptions.None);
            if (parts.Length < 3) throw new InvalidOperationException("Invalid graph style: " + code);
            foreach (var root in ModularRoots())
            {
                var path = Path.Combine(root, parts[1]);
                if (!File.Exists(path)) continue;
                var modular = TemplateSerializer.Load(path);
                var graph = Graphs(modular).Cast<object>().FirstOrDefault(x => x.GetType().Name == parts[2]);
                if (graph != null) return TemplateSerializer.Clone(graph);
            }
            throw new FileNotFoundException("Modular graph not found: " + parts[1]);
        }

        private object? FindSampleGraph(object theme, string type) =>
            Graphs(theme).Cast<object>().FirstOrDefault(x => x.GetType().Name == type) ?? FindSampleAcrossTemplates(type);

        private object? FindSampleAcrossTemplates(string type)
        {
            foreach (var root in new[] { _templateRoot, Path.Combine(_lConnectDir, "Assets", _deviceModel, "template") })
            {
                if (!Directory.Exists(root)) continue;
                foreach (var path in Directory.GetFiles(root, "*.template"))
                {
                    try
                    {
                        var sample = Graphs(TemplateSerializer.Load(path)).Cast<object>().FirstOrDefault(x => x.GetType().Name == type);
                        if (sample != null) return sample;
                    }
                    catch { }
                }
            }
            return null;
        }

        private static void CopyPositionAndData(object source, object target)
        {
            Reflection.TrySet(target, "posX", Reflection.GetInt(source, "posX"));
            Reflection.TrySet(target, "posY", Reflection.GetInt(source, "posY"));
            var data = Reflection.Get(source, "m_data");
            if (data != null) Reflection.TrySet(target, "m_data", TemplateSerializer.Clone(data));
        }

        private static void SetGraphColors(object graph, string front, string back)
        {
            var frontColor = ColorParser.Parse(front);
            var backColor = ColorParser.Parse(back);
            foreach (var name in new[] { "FrontColor", "LineColor", "FillColor" }) Reflection.TrySet(graph, name, frontColor);
            foreach (var name in new[] { "BackColor", "BorderColor" }) Reflection.TrySet(graph, name, backColor);
        }

        private void SetIfProvided(object target, string property, string argument)
        {
            if (!_args.HasValue(argument)) return;
            Reflection.TrySet(target, property, _args.Get(argument));
        }

        private void SetFontIfProvided(object layer, string property, string argument)
        {
            if (!_args.HasValue(argument)) return;
            var font = Reflection.Get(layer, "fontConfig");
            if (font != null) Reflection.TrySet(font, property, _args.Get(argument));
        }

        private void SetFontColorIfProvided(object layer, string argument)
        {
            if (!_args.HasValue(argument)) return;
            var font = Reflection.Get(layer, "fontConfig");
            if (font != null) Reflection.TrySet(font, "color", ColorParser.Parse(_args.Get(argument)));
        }

        private static void SetFont(object theme, object layer, string name)
        {
            var font = Reflection.Get(layer, "fontConfig");
            if (font == null || string.IsNullOrWhiteSpace(name)) return;

            var family = ResolveFontFamily(name);
            Reflection.TrySet(font, "name", family?.Name ?? name);
            if (family != null) Reflection.TrySet(font, "font", family);
            Reflection.TrySet(font, "uninstalled", false);
            AddNeededFont(theme, family?.Name ?? name);
        }

        private static FontFamily? ResolveFontFamily(string name)
        {
            foreach (var candidate in FontNameCandidates(name))
            {
                try
                {
                    var family = new FontFamily(candidate);
                    if (family.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
                        NormalizeFontName(family.Name).Equals(NormalizeFontName(candidate), StringComparison.OrdinalIgnoreCase))
                    {
                        return family;
                    }
                }
                catch { }
            }

            foreach (var file in LConnectFontFiles())
            {
                try
                {
                    var beforeCount = PrivateFonts.Families.Length;
                    PrivateFonts.AddFontFile(file);
                    var family = PrivateFonts.Families.Skip(beforeCount).FirstOrDefault(item =>
                        FontNameCandidates(name).Any(candidate =>
                            item.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
                            NormalizeFontName(item.Name).Equals(NormalizeFontName(candidate), StringComparison.OrdinalIgnoreCase)));
                    if (family != null) return family;
                }
                catch { }
            }

            return null;
        }

        private static IEnumerable<string> FontNameCandidates(string name)
        {
            yield return name;
            var clean = Regex.Replace(name, @"\.[0-9a-f]{6,}$", "", RegexOptions.IgnoreCase);
            if (!clean.Equals(name, StringComparison.OrdinalIgnoreCase)) yield return clean;
            clean = clean.Replace('_', ' ').Replace('-', ' ');
            yield return clean;
            clean = Regex.Replace(clean, @"\b(it|italic|regular|bold|medium|light)\b", "", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\s+", " ").Trim();
            if (!string.IsNullOrWhiteSpace(clean)) yield return clean;
        }

        private static string NormalizeFontName(string name)
        {
            var value = Regex.Replace(name ?? "", @"\.[0-9a-f]{6,}$", "", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"\b(it|italic|regular|bold|medium|light)\b", "", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"[^a-z0-9]+", "", RegexOptions.IgnoreCase);
            return value.ToLowerInvariant();
        }

        private static IEnumerable<string> LConnectFontFiles()
        {
            foreach (var dir in new[]
                         {
                         Path.Combine(DefaultLConnectDir, "fonts"),
                         Path.Combine(DefaultLConnectDir, "Assets", "ga2v", "fonts"),
                         Path.Combine(DefaultLConnectDir, "Assets", "tl-sensor", "assets")
                     })
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.GetFiles(dir, "*.*")
                             .Where(path => path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
                                            path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)))
                {
                    yield return file;
                }
            }
        }

        private static void AddNeededFont(object theme, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            if (Reflection.Get(theme, "needFontList") is not IList list)
            {
                list = new List<string>();
                Reflection.TrySet(theme, "needFontList", list);
            }

            foreach (var item in list)
            {
                if (item?.ToString()?.Equals(name, StringComparison.OrdinalIgnoreCase) == true) return;
            }

            list.Add(name);
        }

        private static void SetStaticText(object layer, string text)
        {
            SetDataSource(layer, "StaticText");
            SetDataValue(layer, text);
            Reflection.TrySet(layer, "TypeName", "Text");
        }

        private static void SetDataSource(object layer, string source)
        {
            var data = Reflection.Get(layer, "m_data");
            if (data == null) return;
            Reflection.TrySet(data, "DataName", source);
            Reflection.TrySet(data, "DisplayName", DataDisplayName(source));
            Reflection.TrySet(data, "Sanma_Eng_Name", DataDisplayName(source));
            if (source != "StaticText") SetDataValue(layer, "0");
        }

        private static void SetDataValue(object layer, string value)
        {
            var data = Reflection.Get(layer, "m_data");
            if (data == null) return;
            Reflection.TrySet(data, "Value", value);
            Reflection.TrySet(data, "ValueWithUnit", value);
        }

        private static void SetDataFormat(object layer, string format)
        {
            var data = Reflection.Get(layer, "m_data");
            if (data == null) return;
            format = NormalizeDataFormat(Reflection.GetString(data, "DataName"), format);
            Reflection.TrySet(data, "SubName", format);
            Reflection.TrySet(data, "content", format);
            if (Reflection.FindMethod(data, "ResetValue") is MethodInfo reset) reset.Invoke(data, null);
        }

        private static string NormalizeDataFormat(string dataSource, string format)
        {
            if (!string.Equals(dataSource, "TIME", StringComparison.OrdinalIgnoreCase)) return format;
            return (format ?? "").Trim() switch
            {
                "00:00" or "HH:mm" => "h:m",
                "Hour:Minute" => "h:m",
                "00:00:00" or "HH:MM:SS" or "H:M:S" or "HH:mm:ss" => "h:m:s",
                "Hour:Minute:Second" => "h:m:s",
                var value => value
            };
        }

        private static string DataDisplayName(string source) => source switch
        {
            "CPUTEMP" => "Cpu Temp", "CPUTEMP_F" => "Cpu Temp F", "CPULOAD" => "Cpu Usage",
            "GPUTEMP" => "Gpu Temp", "GPUTEMP_F" => "Gpu Temp F", "GPULOAD" => "Gpu Usage",
            "FPS_AVG" => "Average FPS", "StaticText" => "StaticText", _ => source
        };

        private static void RepairDataMetadata(object theme)
        {
            foreach (var graph in Graphs(theme).Cast<object>())
            {
                var data = Reflection.Get(graph, "m_data");
                if (data == null) continue;
                var source = Reflection.GetString(data, "DataName");
                if (string.IsNullOrWhiteSpace(source)) continue;
                Reflection.TrySet(data, "DisplayName", DataDisplayName(source));
                Reflection.TrySet(data, "Sanma_Eng_Name", DataDisplayName(source));
                if (Reflection.Get(data, "DataQueue") == null)
                {
                    var queueType = typeof(Queue<>).MakeGenericType(typeof(string));
                    Reflection.TrySet(data, "DataQueue", Activator.CreateInstance(queueType));
                }
            }
        }

        private static void RepairFontMetadata(object theme)
        {
            foreach (var graph in Graphs(theme).Cast<object>())
            {
                var font = Reflection.Get(graph, "fontConfig");
                var name = Reflection.GetString(font, "name");
                if (font == null || string.IsNullOrWhiteSpace(name)) continue;
                var family = ResolveFontFamily(name);
                if (family == null) continue;
                Reflection.TrySet(font, "name", family.Name);
                Reflection.TrySet(font, "font", family);
                Reflection.TrySet(font, "uninstalled", false);
                AddNeededFont(theme, family.Name);
            }
        }

        private static IList Graphs(object theme) =>
            Reflection.Get(theme, "GraphList") as IList ?? throw new InvalidOperationException("Theme GraphList not found.");

        private static bool IsGraphLayer(object graph) =>
            new[] { "GraphStatuBar", "GraphArchBar", "GraphLine", "GraphDynamicBar" }.Contains(graph.GetType().Name);

        private static void ValidateIndex(IList list, int index)
        {
            if (index < 0 || index >= list.Count) throw new IndexOutOfRangeException($"LayerIndex {index} not found. Current layer count: {list.Count}");
        }

        private static string ThemeBackgroundName(object theme)
        {
            foreach (var name in new[] { "videoName", "videoPath", "o_videoPath", "videoPath2", "videoPath3" })
            {
                var value = Reflection.GetString(theme, name);
                if (!string.IsNullOrWhiteSpace(value)) return Path.GetFileName(value);
            }
            return "";
        }

        private static string ThemeBackgroundPath(object theme)
        {
            foreach (var name in new[] { "videoPath", "o_videoPath", "videoPath2", "videoPath3", "videoName" })
            {
                var value = Reflection.GetString(theme, name);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            return "";
        }

        private string ResolveTemplateBackgroundPath(object theme)
        {
            var backgroundPath = ThemeBackgroundPath(theme);
            if (File.Exists(backgroundPath))
            {
                return backgroundPath;
            }

            var graphAnimationBackground = ResolveGraphAnimationBackgroundPath(theme);
            if (!string.IsNullOrWhiteSpace(graphAnimationBackground))
            {
                return graphAnimationBackground;
            }

            var embeddedBackground = ExtractEmbeddedBackgroundImage(theme);
            if (!string.IsNullOrWhiteSpace(embeddedBackground))
            {
                return embeddedBackground;
            }

            var resolvedMedia = ResolveTemplateMediaPath(backgroundPath);
            return string.IsNullOrWhiteSpace(resolvedMedia) ? backgroundPath : resolvedMedia;
        }

        private string ResolveGraphAnimationBackgroundPath(object theme)
        {
            foreach (var graph in Graphs(theme).Cast<object>().Where(graph => graph.GetType().Name == "GraphAnimation"))
            {
                foreach (var value in new[]
                         {
                             Reflection.GetString(graph, "FilePath"),
                             Reflection.GetString(graph, "videoPath"),
                             Reflection.GetString(graph, "o_videoPath"),
                             Reflection.GetString(graph, "ImgName"),
                             Reflection.GetString(graph, "videoName")
                         })
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    if (File.Exists(value))
                    {
                        return value;
                    }

                    var resolved = ResolveTemplateMediaPath(value);
                    if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved))
                    {
                        return resolved;
                    }
                }
            }

            return "";
        }

        private string ResolveTemplateMediaPath(string mediaName)
        {
            if (string.IsNullOrWhiteSpace(mediaName))
            {
                return "";
            }

            var fileName = Path.GetFileName(mediaName);
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(baseName))
            {
                return "";
            }

            var lConnectAssetRoot = Path.Combine(_lConnectDir, "Assets");
            var searchModels = new List<string> { _deviceModel };
            if (_deviceModel.Equals("hydroshift-ii-lcd-s", StringComparison.OrdinalIgnoreCase))
            {
                searchModels.Add("hydroshift-ii-lcd-c");
            }
            else if (_deviceModel.Equals("hydroshift-ii-lcd-c", StringComparison.OrdinalIgnoreCase))
            {
                searchModels.Add("hydroshift-ii-lcd-s");
            }
            else if (_deviceModel.Equals("vm-9.2-inch", StringComparison.OrdinalIgnoreCase))
            {
                searchModels.Add("universal-screen-8.8-inch");
            }

            var roots = new List<string>();
            var lcdRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(_templatePath))!)!;
            foreach (var model in searchModels.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                roots.Add(Path.Combine(DefaultProgramData, model, "video"));
                roots.Add(Path.Combine(DefaultProgramData, model, "image"));
                roots.Add(Path.Combine(DefaultProgramData, model, "theme"));
                roots.Add(Path.Combine(_lConnectDir, "Assets", model, "video"));
                roots.Add(Path.Combine(_lConnectDir, "Assets", model, "image"));
                roots.Add(Path.Combine(_lConnectDir, "Assets", model, "theme"));
                roots.Add(Path.Combine(lConnectAssetRoot, model, "video"));
                roots.Add(Path.Combine(lConnectAssetRoot, model, "image"));
                roots.Add(Path.Combine(lConnectAssetRoot, model, "theme"));
            }
            roots.Add(Path.Combine(lcdRoot, "video"));
            roots.Add(Path.Combine(lcdRoot, "image"));
            roots.Add(Path.Combine(lcdRoot, "theme"));

            foreach (var root in roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var exact = Path.Combine(root, fileName);
                if (File.Exists(exact))
                {
                    return exact;
                }

                try
                {
                    var match = Directory.EnumerateFiles(root, baseName + "*.*")
                        .Where(path => IsSupportedBackgroundMediaFile(path) && IsBackgroundNameCandidate(path, baseName))
                        .OrderBy(path => Path.GetExtension(path).Equals(".h264", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                        .ThenByDescending(path => new FileInfo(path).Length)
                        .FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(match))
                    {
                        return match;
                    }
                }
                catch { }
            }

            return "";
        }

        private string ExtractEmbeddedBackgroundImage(object theme)
        {
            if (Reflection.Get(theme, "BgImg") is not Bitmap bitmap ||
                bitmap.Width < 64 ||
                bitmap.Height < 64)
            {
                return "";
            }

            try
            {
                var directory = Path.Combine(Path.GetTempPath(), "LianLiThemeEditor", "embedded-backgrounds");
                Directory.CreateDirectory(directory);
                var output = Path.Combine(
                    directory,
                    Path.GetFileNameWithoutExtension(_templatePath) + "-background.png");
                using var copy = new Bitmap(bitmap);
                copy.Save(output, ImageFormat.Png);
                return output;
            }
            catch
            {
                return "";
            }
        }

        private static bool IsSupportedBackgroundMediaFile(string path) =>
            path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".h264", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);

        private static bool IsBackgroundNameCandidate(string path, string baseName)
        {
            var candidateBaseName = Path.GetFileNameWithoutExtension(path);
            if (string.Equals(candidateBaseName, baseName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!candidateBaseName.StartsWith(baseName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var suffix = candidateBaseName.Length > baseName.Length
                ? candidateBaseName.Substring(baseName.Length)
                : "";
            return suffix.StartsWith("_", StringComparison.Ordinal) ||
                   suffix.StartsWith("-", StringComparison.Ordinal);
        }

        private string SyncUploadedBackgroundMedia(string sourcePath, int rotationDegrees)
        {
            var templateId = Path.GetFileNameWithoutExtension(_templatePath);
            var lcdRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(_templatePath))!)!;
            var programDataRoot = Path.GetDirectoryName(lcdRoot)!;
            var deviceName = Path.GetFileName(lcdRoot);
            var uploadDir = Path.Combine(programDataRoot, "uploaded", deviceName, "template-background");
            var previewDir = Path.Combine(lcdRoot, "preview");
            Directory.CreateDirectory(uploadDir);
            Directory.CreateDirectory(previewDir);

            var safeId = Regex.Replace(templateId, @"[^A-Za-z0-9_.-]", "_");
            var unique = safeId + "-" + DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var targetMp4 = Path.Combine(uploadDir, unique + ".mp4");
            var targetH264 = Path.Combine(uploadDir, unique + ".h264");
            var fixedMp4 = Path.Combine(uploadDir, safeId + ".mp4");
            var fixedH264 = Path.Combine(uploadDir, safeId + ".h264");
            var preview = Path.Combine(previewDir, "template_" + templateId + ".png");
            var tempMp4 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mp4");
            var tempH264 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".h264");
            var tempPreview = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
            var ffmpeg = Path.Combine(_lConnectDir, "x64", "ffmpeg.exe");
            if (!File.Exists(ffmpeg)) ffmpeg = "ffmpeg";
            var normalizedJpegInput = "";
            var preparedSourcePath = NormalizeJpegOrientationToPng(sourcePath);
            if (!string.Equals(preparedSourcePath, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                normalizedJpegInput = preparedSourcePath;
                sourcePath = preparedSourcePath;
            }

            var canvasWidth = Math.Max(1, _args.GetInt("CanvasWidth", 480));
            var canvasHeight = Math.Max(1, _args.GetInt("CanvasHeight", 480));
            var isUniversal88 = string.Equals(deviceName, "universal-screen-8.8-inch", StringComparison.OrdinalIgnoreCase);
            var autoRotation = rotationDegrees == 0
                ? GetAutoBackgroundRotationDegrees(ffmpeg, sourcePath, canvasWidth, canvasHeight)
                : 0;
            var normalizedRotation = (((rotationDegrees + autoRotation) % 360) + 360) % 360;
            var rotation = normalizedRotation switch
            {
                90 => "transpose=clock,",
                180 => "hflip,vflip,",
                270 => "transpose=cclock,",
                _ => ""
            };
            var filter = rotation +
                         $"scale={canvasWidth}:{canvasHeight}:force_original_aspect_ratio=increase:flags=lanczos," +
                         $"crop={canvasWidth}:{canvasHeight},setsar=1,fps=30,format=yuv420p";
            if (isUniversal88)
            {
                var targetWidth = canvasWidth >= canvasHeight ? 1920 : 480;
                var targetHeight = canvasWidth >= canvasHeight ? 480 : 1920;
                filter = rotation + $"scale={targetWidth}:{targetHeight},setsar=1";
            }
            var h264Filter = isUniversal88
                ? canvasWidth >= canvasHeight
                    ? "transpose=clock,scale=480:1920,setsar=1"
                    : "scale=480:1920,setsar=1"
                : filter;
            var encoder = isUniversal88
                ? new[]
                {
                    "-an", "-r", "24", "-c:v", "libx264", "-preset", "ultrafast",
                    "-threads", "0", "-x264opts", "bframes=0", "-pix_fmt", "yuv420p"
                }
                : new[]
            {
                "-an", "-c:v", "libx264", "-preset", "ultrafast",
                "-x264opts", "bframes=0", "-profile:v", "baseline",
                "-level", "3.1", "-refs", "1", "-b:v", "2400k",
                "-tune", "zerolatency", "-pix_fmt", "yuv420p"
            };
            var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            try
            {
                if (extension == ".h264")
                {
                    RunFfmpeg(ffmpeg, GetBackgroundInputArguments(sourcePath, extension).Prepend("-y").Concat(new[] { "-vf", filter }).Concat(encoder).Concat(new[] { "-movflags", "+faststart", tempMp4 }));
                    RunFfmpeg(ffmpeg, new[] { "-y", "-i", tempMp4, "-vf", h264Filter }.Concat(encoder).Concat(new[] { "-f", "h264", tempH264 }));
                    RunFfmpeg(ffmpeg, GetBackgroundInputArguments(sourcePath, extension).Prepend("-y").Concat(new[] { "-vf", filter, "-frames:v", "1", tempPreview }));
                }
                else if (extension is ".png" or ".jpg" or ".jpeg")
                {
                    RunFfmpeg(ffmpeg, new[] { "-y", "-loop", "1", "-i", sourcePath, "-t", "1", "-vf", filter }.Concat(encoder).Concat(new[] { "-movflags", "+faststart", tempMp4 }));
                    RunFfmpeg(ffmpeg, new[] { "-y", "-i", tempMp4, "-vf", h264Filter }.Concat(encoder).Concat(new[] { "-f", "h264", tempH264 }));
                    RunFfmpeg(ffmpeg, new[] { "-y", "-i", sourcePath, "-vf", filter, "-frames:v", "1", tempPreview });
                }
                else
                {
                    RunFfmpeg(ffmpeg, GetBackgroundInputArguments(sourcePath, extension).Prepend("-y").Concat(new[] { "-vf", filter }).Concat(encoder).Concat(new[] { "-movflags", "+faststart", tempMp4 }));
                    RunFfmpeg(ffmpeg, new[] { "-y", "-i", tempMp4, "-vf", h264Filter }.Concat(encoder).Concat(new[] { "-f", "h264", tempH264 }));
                    RunFfmpeg(ffmpeg, GetBackgroundInputArguments(sourcePath, extension).Prepend("-y").Concat(new[] { "-vf", filter, "-frames:v", "1", tempPreview }));
                }
                File.Copy(tempMp4, targetMp4, true);
                File.Copy(tempH264, targetH264, true);
                // L-Connect can keep the fixed template-id files open while the
                // LCD is rendering. The unique pair above is the authoritative
                // media for this update, so a locked compatibility copy must not
                // make the whole background operation fail.
                TryCopyWithRetry(tempMp4, fixedMp4);
                TryCopyWithRetry(tempH264, fixedH264);
                TryCopyWithRetry(tempPreview, preview);
                return targetMp4;
            }
            finally
            {
                foreach (var path in new[] { tempMp4, tempH264, tempPreview, normalizedJpegInput })
                    try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }

        private static string NormalizeJpegOrientationToPng(string sourcePath)
        {
            var extension = Path.GetExtension(sourcePath);
            if (!extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                return sourcePath;
            }

            using var image = Image.FromFile(sourcePath);
            ApplyExifOrientation(image);
            var output = Path.Combine(
                Path.GetTempPath(),
                Path.GetFileNameWithoutExtension(sourcePath) + "-oriented-" + Guid.NewGuid().ToString("N") + ".png");
            image.Save(output, ImageFormat.Png);
            return output;
        }

        private static void ApplyExifOrientation(Image image)
        {
            const int orientationId = 0x0112;
            if (!image.PropertyIdList.Contains(orientationId))
            {
                return;
            }

            try
            {
                var property = image.GetPropertyItem(orientationId);
                if (property?.Value == null || property.Value.Length < 2)
                {
                    return;
                }

                var orientation = ReadExifOrientation(property.Value);
                var rotateFlip = orientation switch
                {
                    2 => RotateFlipType.RotateNoneFlipX,
                    3 => RotateFlipType.Rotate180FlipNone,
                    4 => RotateFlipType.Rotate180FlipX,
                    5 => RotateFlipType.Rotate90FlipX,
                    6 => RotateFlipType.Rotate90FlipNone,
                    7 => RotateFlipType.Rotate270FlipX,
                    8 => RotateFlipType.Rotate270FlipNone,
                    _ => RotateFlipType.RotateNoneFlipNone
                };
                if (rotateFlip != RotateFlipType.RotateNoneFlipNone)
                {
                    image.RotateFlip(rotateFlip);
                }
                image.RemovePropertyItem(orientationId);
            }
            catch
            {
            }
        }

        private static ushort ReadExifOrientation(byte[] value)
        {
            var littleEndian = BitConverter.ToUInt16(value, 0);
            if (littleEndian >= 1 && littleEndian <= 8)
            {
                return littleEndian;
            }

            var bigEndian = (ushort)((value[0] << 8) | value[1]);
            return bigEndian >= 1 && bigEndian <= 8
                ? bigEndian
                : littleEndian;
        }

        private static int GetAutoBackgroundRotationDegrees(
            string ffmpeg,
            string sourcePath,
            int canvasWidth,
            int canvasHeight)
        {
            try
            {
                if (!TryGetMediaDimensions(ffmpeg, sourcePath, out var mediaWidth, out var mediaHeight))
                {
                    return 0;
                }

                var canvasLandscape = canvasWidth >= canvasHeight;
                var mediaLandscape = mediaWidth >= mediaHeight;
                if (canvasLandscape == mediaLandscape)
                {
                    return 0;
                }

                return canvasLandscape ? 90 : 270;
            }
            catch
            {
                return 0;
            }
        }

        private static IEnumerable<string> GetBackgroundInputArguments(string sourcePath, string extension)
        {
            if (extension == ".h264")
            {
                yield return "-f";
                yield return "h264";
            }
            else
            {
                yield return "-noautorotate";
            }

            yield return "-i";
            yield return sourcePath;
        }

        private static bool TryGetMediaDimensions(
            string ffmpeg,
            string sourcePath,
            out int width,
            out int height)
        {
            width = 0;
            height = 0;
            var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (extension is ".png" or ".jpg" or ".jpeg")
            {
                using var bitmap = new Bitmap(sourcePath);
                width = bitmap.Width;
                height = bitmap.Height;
                return width > 0 && height > 0;
            }

            var start = new ProcessStartInfo
            {
                FileName = ffmpeg,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            var arguments = new List<string> { "-hide_banner" };
            if (extension == ".h264")
            {
                arguments.Add("-f");
                arguments.Add("h264");
            }
            arguments.Add("-i");
            arguments.Add(sourcePath);
            start.Arguments = string.Join(" ", arguments.Select(QuoteArgument));

            using var process = Process.Start(start);
            if (process == null)
            {
                return false;
            }

            var stderr = process.StandardError.ReadToEnd();
            _ = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            var match = Regex.Match(stderr, @"Video:.*?(\d{2,5})x(\d{2,5})", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return false;
            }

            width = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            height = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            if (TryGetMediaRotationDegrees(stderr, out var rotation) &&
                Math.Abs(rotation) % 180 == 90)
            {
                (width, height) = (height, width);
            }
            return width > 0 && height > 0;
        }

        private static bool TryGetMediaRotationDegrees(string ffmpegOutput, out int degrees)
        {
            degrees = 0;
            var match = Regex.Match(
                ffmpegOutput,
                @"rotation\s+of\s+(-?\d+(?:\.\d+)?)\s+degrees",
                RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                match = Regex.Match(
                    ffmpegOutput,
                    @"rotate\s*:\s*(-?\d+(?:\.\d+)?)",
                    RegexOptions.IgnoreCase);
            }

            if (!match.Success ||
                !double.TryParse(
                    match.Groups[1].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value))
            {
                return false;
            }

            degrees = ((int)Math.Round(value) % 360 + 360) % 360;
            return true;
        }

        private static void RunFfmpeg(string executable, IEnumerable<string> arguments)
        {
            var start = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                Arguments = string.Join(" ", arguments.Select(QuoteArgument))
            };
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start FFmpeg.");
            var stderr = process.StandardError.ReadToEnd();
            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
        }

        private static string QuoteArgument(string value) =>
            value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0 ? value : "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static class LayerInspector
    {
        public static Dictionary<string, object?> Read(object theme, object graph, int index, string templatePath)
        {
            var data = Reflection.Get(graph, "m_data");
            var font = Reflection.Get(graph, "fontConfig");
            var alignment = font == null ? null : Reflection.Get(font, "alignment");
            var type = graph.GetType().Name;
            return new Dictionary<string, object?>
            {
                ["Index"] = index, ["Type"] = type,
                ["DataSource"] = Reflection.GetString(data, "DataName"),
                ["DataRate"] = Reflection.Get(data, "Rate"),
                ["Text"] = Reflection.GetString(data, "Value"),
                ["ValueWithUnit"] = Reflection.GetString(data, "ValueWithUnit"),
                ["ShowUnit"] = Reflection.GetBool(data, "ShowUnit"),
                ["Format"] = First(Reflection.GetString(data, "content"), Reflection.GetString(data, "SubName")),
                ["Hide"] = Reflection.GetBool(graph, "hide"), ["X"] = Reflection.Get(graph, "posX"), ["Y"] = Reflection.Get(graph, "posY"),
                ["Size"] = Reflection.Get(font, "size"), ["Font"] = Reflection.Get(font, "name"),
                ["Bold"] = Reflection.GetBool(font, "isBold"), ["Italic"] = Reflection.GetBool(font, "IsItalic"),
                ["Alignment"] = alignment?.ToString() ?? "", ["AlignmentIndex"] = Reflection.Get(alignment, "index"),
                ["AlignmentName"] = Reflection.Get(alignment, "displayName"), ["FontInterval"] = Reflection.Get(font, "interval"),
                ["FontOrgSize"] = Reflection.Get(font, "orgSize"), ["FontGradientColor"] = Reflection.Get(font, "GrColor")?.ToString() ?? "",
                ["FontGradientDirection"] = Reflection.Get(font, "GrDirection"), ["FontWidth"] = Reflection.Get(graph, "FontWidth"),
                ["LineHeight"] = Reflection.Get(graph, "LineHeight"), ["Color"] = Reflection.Get(font, "color")?.ToString() ?? "",
                ["Media"] = First(Reflection.GetString(graph, "ImgName"), Reflection.GetString(graph, "videoName"),
                    Path.GetFileName(Reflection.GetString(graph, "FilePath"))),
                ["MediaPath"] = type == "GraphSensor"
                    ? SensorImagePath(graph, templatePath)
                    : IsGraphLayer(graph) ? GraphPreviewPath(graph, templatePath)
                    : type != "GraphAnimation" ? ImagePath(graph, templatePath) : "",
                ["Width"] = Reflection.Get(graph, "width"), ["Height"] = Reflection.Get(graph, "height"),
                ["Radius"] = Reflection.Get(graph, "radius"), ["Diameter"] = Reflection.Get(graph, "diameter"),
                ["Thickness"] = Reflection.Get(graph, "archWidth"),
                ["FrontColor"] = First(Reflection.Get(graph, "FrontColor")?.ToString(), Reflection.Get(graph, "LineColor")?.ToString()),
                ["BackColor"] = First(Reflection.Get(graph, "BackColor")?.ToString(), Reflection.Get(graph, "BorderColor")?.ToString()),
                ["LineColor"] = Reflection.Get(graph, "LineColor")?.ToString() ?? "",
                ["FillColor"] = Reflection.Get(graph, "FillColor")?.ToString() ?? "",
                ["BorderColor"] = Reflection.Get(graph, "BorderColor")?.ToString() ?? "",
                ["UseGradient"] = Reflection.GetBool(graph, "useGradient"),
                ["GradientColor"] = Reflection.Get(graph, "GradientColor")?.ToString() ?? "",
                ["ZoomRate"] = GetZoomRate(theme, graph), ["Rotate"] = GetRotation(graph, index, templatePath),
                ["ClockCenterX"] = Reflection.Get(graph, "centerX"), ["ClockCenterY"] = Reflection.Get(graph, "centerY"),
                ["ClockAngle"] = Reflection.Get(graph, "angle"), ["ClockEndAngle"] = Reflection.Get(graph, "endAngle"),
                ["ClockOffset"] = Reflection.Get(graph, "offset"), ["ClockRateOffset"] = null, ["ClockMoveOrigin"] = Reflection.GetBool(graph, "moveOpoint"),
                ["ClockOriginX"] = Reflection.Get(graph, "o_X"), ["ClockOriginY"] = Reflection.Get(graph, "o_Y"),
                ["Rect"] = RectText(Reflection.Get(graph, "rect")), ["Direction"] = Reflection.Get(graph, "direction"),
                ["LineWidth"] = Reflection.Get(graph, "lineWidth"), ["ColumnWidth"] = Reflection.Get(graph, "columnWidth"),
                ["BorderWidth"] = Reflection.Get(graph, "borderWidth"), ["InnerCircleRadius"] = Reflection.Get(graph, "InnerCircleRadius"),
                ["SplitBlockWidth"] = Reflection.Get(graph, "SplitBlockWidth"), ["SplitBlankWidth"] = Reflection.Get(graph, "SplitBlankWidth"),
                ["UseSubsection"] = Reflection.GetBool(graph, "useSubsection"), ["FillBack"] = Reflection.GetBool(graph, "fillBack"),
                ["Revert"] = Reflection.GetBool(graph, "revert"), ["FrontAlpha"] = Reflection.Get(graph, "FrontAlpha"),
                ["BackAlpha"] = Reflection.Get(graph, "BackAlpha"), ["TransparentBackground"] = Reflection.GetBool(graph, "trBack"),
                ["MinValue"] = Reflection.Get(graph, "minValue"), ["MaxValue"] = Reflection.Get(graph, "maxValue"),
                ["InvertDirection"] = Reflection.GetBool(graph, "rollDirection"),
                ["StartPercentage"] = Reflection.Get(graph, "startPer"), ["TotalAngle"] = Reflection.Get(graph, "totalAngel"),
                ["UseBlock"] = Reflection.GetBool(graph, "useBlock"), ["RingBorder"] = Reflection.GetBool(graph, "HasRingBorder"),
                ["Round"] = Reflection.GetBool(graph, "round"), ["SubTypeName"] = Reflection.GetString(graph, "SubTypeName"),
                ["TypeName"] = Reflection.GetString(graph, "TypeName"), ["GraphStyle"] = Reflection.GetString(graph, "DisplayName"),
                ["SensorStyle"] = Reflection.GetString(graph, "styleType"),
                ["SensorType"] = Reflection.GetString(graph, "senSorType"),
                ["SensorColor1"] = Reflection.Get(graph, "styleInfo") is { } styleInfo1 ? Reflection.Get(styleInfo1, "Color1")?.ToString() ?? "" : "",
                ["SensorColor2"] = Reflection.Get(graph, "styleInfo") is { } styleInfo2 ? Reflection.Get(styleInfo2, "Color2")?.ToString() ?? "" : "",
                ["SensorBgColor"] = Reflection.Get(graph, "styleInfo") is { } styleInfo3 ? Reflection.Get(styleInfo3, "BgColor")?.ToString() ?? "" : "",
                ["SensorMainFontColor"] = Reflection.Get(graph, "styleInfo") is { } styleInfo4 ? Reflection.Get(styleInfo4, "MainFontColor")?.ToString() ?? "" : "",
                ["SensorTopFontColor"] = Reflection.Get(graph, "styleInfo") is { } styleInfo5 ? Reflection.Get(styleInfo5, "FontTopColor")?.ToString() ?? "" : "",
                ["SensorBottomFontColor"] = Reflection.Get(graph, "styleInfo") is { } styleInfo6 ? Reflection.Get(styleInfo6, "FontBottomColor")?.ToString() ?? "" : "",
                ["SensorFontFamily"] = Reflection.Get(graph, "styleInfo") is { } styleInfo7 ? Reflection.GetString(styleInfo7, "FontFamily") : "",
                ["SensorZoomRate"] = Reflection.Get(graph, "styleInfo") is { } styleInfo8 ? Reflection.Get(styleInfo8, "ZoomRate") : "",
                ["WritableProperties"] = Reflection.WritableNames(graph), ["WritableFontProperties"] = Reflection.WritableNames(font)
            };
        }

        private static string ImagePath(object graph, string templatePath)
        {
            var direct = First(Reflection.GetString(graph, "FilePath"), Reflection.GetString(graph, "Path"), Reflection.GetString(graph, "ImagePath"));
            if (File.Exists(direct)) return direct;
            var name = Reflection.GetString(graph, "ImgName");
            var candidate = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(templatePath)!)!, "image", name);
            if (File.Exists(candidate)) return candidate;
            foreach (var property in new[] { "O_bitmap", "bitmap" })
            {
                if (Reflection.Get(graph, property) is not Bitmap bitmap) continue;
                try
                {
                    var directory = Path.Combine(Path.GetTempPath(), "LianLiThemeEditor", "embedded-images");
                    Directory.CreateDirectory(directory);
                    var mediaName = First(
                        Reflection.GetString(graph, "ImgName"),
                        Reflection.GetString(graph, "videoName"),
                        graph.GetType().Name);
                    var preview = Path.Combine(
                        directory,
                        Path.GetFileNameWithoutExtension(templatePath) + "-" +
                        Path.GetFileNameWithoutExtension(mediaName) + "-" +
                        Reflection.GetInt(graph, "posX") + "-" +
                        Reflection.GetInt(graph, "posY") + ".png");
                    using var copy = new Bitmap(bitmap);
                    copy.Save(preview, ImageFormat.Png);
                    return preview;
                }
                catch { }
            }
            return candidate;
        }

        private static string SensorImagePath(object graph, string templatePath)
        {
            try
            {
                var data = Reflection.Get(graph, "m_data");
                var valueText = Reflection.GetString(data, "Value");
                if (!int.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                {
                    value = 52;
                }
                var styleInfo = CloneSensorStyleInfo(graph);
                var originalStyleInfo = CloneSensorStyleInfo(graph);
                NormalizeSensorPreviewZoom(graph, styleInfo);
                NormalizeSensorPreviewZoom(graph, originalStyleInfo);
                var styleType = Enum.Parse(ThemeType("ThemeEngine.Contr.StyleTypes"), Reflection.GetString(graph, "styleType"), true);
                var sensorType = Enum.Parse(ThemeType("ThemeEngine.Contr.SenSorTypes"), Reflection.GetString(graph, "senSorType"), true);
                var drawStyle = Activator.CreateInstance(ThemeType("ThemeEngine.Contr.DrawStyle"), styleType, sensorType, styleInfo)
                                ?? throw new InvalidOperationException("Sensor draw style could not be created.");
                CopySensorStyleInfo(originalStyleInfo, styleInfo);
                var method = drawStyle.GetType().GetMethod("GetValImage", new[] { typeof(int), typeof(FontFamily) });
                if (method == null) return "";
                using var bitmap = method.Invoke(drawStyle, new object?[] { value, GetSensorFontFamily(graph, styleInfo) }) as Bitmap;
                if (bitmap == null) return "";

                var directory = Path.Combine(Path.GetTempPath(), "LianLiThemeEditor", "sensor-previews");
                Directory.CreateDirectory(directory);
                var style = Reflection.GetString(graph, "styleType");
                var sensor = Reflection.GetString(graph, "senSorType");
                var key = string.Join("-",
                    Path.GetFileNameWithoutExtension(templatePath),
                    Reflection.GetInt(graph, "posX"),
                    Reflection.GetInt(graph, "posY"),
                    style,
                    sensor,
                    value,
                    Reflection.Get(styleInfo, "Color1"),
                    Reflection.Get(styleInfo, "Color2"),
                    Reflection.Get(styleInfo, "BgColor"),
                    Reflection.Get(styleInfo, "MainFontColor"),
                    Reflection.Get(styleInfo, "FontTopColor"),
                    Reflection.Get(styleInfo, "FontBottomColor"),
                    Reflection.GetString(styleInfo, "FontFamily"),
                    Reflection.Get(styleInfo, "ZoomRate"));
                var preview = Path.Combine(
                    directory,
                    SafeFilePart($"{Path.GetFileNameWithoutExtension(templatePath)}-{style}-{sensor}-{value}-{Math.Abs(key.GetHashCode()).ToString(CultureInfo.InvariantCulture)}") + ".png");
                if (File.Exists(preview)) File.Delete(preview);
                bitmap.SetResolution(96f, 96f);
                bitmap.Save(preview, ImageFormat.Png);
                return preview;
            }
            catch
            {
                return "";
            }
        }

        private static object CloneSensorStyleInfo(object graph)
        {
            var source = Reflection.Get(graph, "styleInfo");
            var styleInfo = Activator.CreateInstance(ThemeType("ThemeEngine.Contr.StyleInfo"))
                            ?? throw new InvalidOperationException("StyleInfo could not be created.");
            foreach (var property in new[] { "Color1", "Color2", "BgColor", "MainFontColor", "FontTopColor", "FontBottomColor", "FontFamily", "ZoomRate" })
            {
                var value = Reflection.Get(source, property);
                if (value != null) Reflection.TrySet(styleInfo, property, value);
            }
            return styleInfo;
        }

        private static void CopySensorStyleInfo(object source, object target)
        {
            foreach (var property in new[] { "Color1", "Color2", "BgColor", "MainFontColor", "FontTopColor", "FontBottomColor", "FontFamily", "ZoomRate" })
            {
                var value = Reflection.Get(source, property);
                if (value != null) Reflection.TrySet(target, property, value);
            }
        }

        private static void NormalizeSensorPreviewZoom(object graph, object styleInfo)
        {
            var styleZoom = Reflection.GetDouble(styleInfo, "ZoomRate", 0);
            if (styleZoom > 0.01) return;

            var layerZoom = Reflection.GetDouble(graph, "ZoomRate", 0);
            if (layerZoom <= 0.01) layerZoom = 0.5;
            Reflection.TrySet(styleInfo, "ZoomRate", (float)layerZoom);
        }

        private static FontFamily? GetSensorFontFamily(object graph, object styleInfo)
        {
            if (Reflection.Get(graph, "cachedFont") is FontFamily cached) return cached;
            var name = Reflection.GetString(styleInfo, "FontFamily");
            if (!string.IsNullOrWhiteSpace(name))
            {
                try { return new FontFamily(name); } catch { }
            }
            return null;
        }

        public static string GraphPreviewPath(object graph, string templatePath)
        {
            try
            {
                var frame = GetGraphPreviewFrame(graph);
                if (frame.Size.Width <= 0 || frame.Size.Height <= 0) return "";
                var clone = TemplateSerializer.Clone(graph);
                var data = Reflection.Get(clone, "m_data");
                var previewValue = Reflection.GetString(data, "Value");
                if (string.IsNullOrWhiteSpace(previewValue) || previewValue == "0")
                {
                    previewValue = "53";
                    SetPreviewDataValue(clone, previewValue);
                }
                PrepareGraphLinePreviewData(clone, previewValue);
                Reflection.TrySet(clone, "posX", frame.OffsetX);
                Reflection.TrySet(clone, "posY", frame.OffsetY);
                using var bitmap = new Bitmap(frame.Size.Width, frame.Size.Height, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    var render = clone.GetType().GetMethod("Render", new[] { typeof(Graphics), typeof(bool), typeof(bool), typeof(bool) });
                    render?.Invoke(clone, new object[] { graphics, true, true, false });
                }
                var directory = Path.Combine(Path.GetTempPath(), "LianLiThemeEditor", "graph-previews");
                Directory.CreateDirectory(directory);
                var key = string.Join("-",
                    Path.GetFileNameWithoutExtension(templatePath),
                    graph.GetType().Name,
                    Reflection.GetInt(graph, "posX"),
                    Reflection.GetInt(graph, "posY"),
                    Reflection.Get(graph, "width"),
                    Reflection.Get(graph, "height"),
                    Reflection.Get(graph, "diameter"),
                    Reflection.Get(graph, "archWidth"),
                    Reflection.Get(graph, "FrontColor"),
                    Reflection.Get(graph, "BackColor"),
                    Reflection.Get(graph, "GradientColor"),
                    Reflection.Get(graph, "LineColor"),
                    Reflection.Get(graph, "BorderColor"),
                    Reflection.Get(graph, "FillColor"),
                    Reflection.GetString(Reflection.Get(graph, "m_data"), "DataName"),
                    previewValue,
                    frame.Size.Width,
                    frame.Size.Height,
                    frame.OffsetX,
                    frame.OffsetY);
                var preview = Path.Combine(directory, SafeFilePart(key) + ".png");
                bitmap.SetResolution(96f, 96f);
                bitmap.Save(preview, ImageFormat.Png);
                return preview;
            }
            catch
            {
                return "";
            }
        }

        private static GraphPreviewCanvas GetGraphPreviewFrame(object graph)
        {
            var type = graph.GetType().Name;
            if (type == "GraphArchBar")
            {
                var d = Math.Max(1, Reflection.GetInt(graph, "diameter"));
                var archWidth = Math.Max(0, Reflection.GetInt(graph, "archWidth"));
                var border = Math.Max(0, Reflection.GetInt(graph, "borderWidth"));
                var line = Math.Max(0, Reflection.GetInt(graph, "lineWidth"));
                var padding = Math.Max(6, Math.Max(archWidth, Math.Max(border, line)) / 2 + 6);
                return new GraphPreviewCanvas(new Size(d + padding * 2, d + padding * 2), padding, padding);
            }
            var width = Math.Max(1, Reflection.GetInt(graph, "width"));
            var height = Math.Max(1, Reflection.GetInt(graph, "height"));
            var padX = 0;
            var padY = 0;
            if (type == "GraphDynamicBar")
            {
                var knob = Math.Max(0, Reflection.GetInt(graph, "InnerCircleRadius"));
                var overflow = Math.Max(0, (knob - height) / 2);
                padX = Math.Max(6, overflow + 6);
                padY = Math.Max(6, overflow + 6);
            }
            else
            {
                var line = Math.Max(0, Reflection.GetInt(graph, "lineWidth"));
                var border = Math.Max(0, Reflection.GetInt(graph, "borderWidth"));
                var padding = Math.Max(0, Math.Max(line, border) / 2 + 2);
                padX = padding;
                padY = padding;
            }
            return new GraphPreviewCanvas(new Size(width + padX * 2, height + padY * 2), padX, padY);
        }

        private readonly struct GraphPreviewCanvas
        {
            public GraphPreviewCanvas(Size size, int offsetX, int offsetY)
            {
                Size = size;
                OffsetX = offsetX;
                OffsetY = offsetY;
            }

            public Size Size { get; }
            public int OffsetX { get; }
            public int OffsetY { get; }
        }

        private static bool IsGraphLayer(object graph)
        {
            var type = graph.GetType().Name;
            return type is "GraphStatuBar" or "GraphArchBar" or "GraphLine" or "GraphDynamicBar";
        }

        private static void SetPreviewDataValue(object layer, string value)
        {
            var data = Reflection.Get(layer, "m_data");
            if (data == null) return;
            Reflection.TrySet(data, "Value", value);
            Reflection.TrySet(data, "ValueWithUnit", value);
        }

        private static void PrepareGraphLinePreviewData(object layer, string previewValue)
        {
            if (layer.GetType().Name != "GraphLine") return;
            var data = Reflection.Get(layer, "m_data");
            if (data == null) return;

            var width = Math.Max(1, Reflection.GetInt(layer, "width"));
            var columnWidth = Math.Max(1, Reflection.GetInt(layer, "columnWidth", 5));
            var sampleCount = Math.Max(2, width / columnWidth);
            var maxValue = Math.Max(1.0, Reflection.GetDouble(layer, "maxValue", 100.0));
            var queue = new Queue<string>();
            for (var i = 0; i < sampleCount; i++)
            {
                var t = sampleCount <= 1 ? 0.0 : i / (double)(sampleCount - 1);
                var wave = 0.5 + 0.5 * Math.Sin((t * Math.PI * 4.0) - (Math.PI / 2.0));
                var value = Math.Round(maxValue * (0.18 + wave * 0.78));
                queue.Enqueue(value.ToString(CultureInfo.InvariantCulture));
            }

            Reflection.TrySet(data, "DataQueue", queue);
            Reflection.TrySet(data, "queueLen", sampleCount.ToString(CultureInfo.InvariantCulture));
            SetPreviewDataValue(layer, previewValue);
        }

        private static Type ThemeType(string name) =>
            _themeAssembly?.GetType(name, throwOnError: true)
            ?? throw new InvalidOperationException($"ThemeEngine type was not found: {name}");

        private static string SafeFilePart(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var safe = new string((value ?? "").Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
            return safe.Length <= 180 ? safe : safe.Substring(0, 180) + "-" + Math.Abs(safe.GetHashCode()).ToString(CultureInfo.InvariantCulture);
        }

        private static object GetZoomRate(object theme, object graph)
        {
            if (graph.GetType().Name == "GraphAnimation")
            {
                var graphZoom = Reflection.Get(graph, "zoom_rate");
                if (graphZoom != null)
                {
                    return NormalizeZoomRate(graphZoom);
                }

                var themeZoom = Reflection.Get(theme, "ZoomRate") ?? Reflection.Get(theme, "_ZoomRate");
                return NormalizeZoomRate(themeZoom);
            }

            return Reflection.Get(graph, "zoom_rate") ?? "";
        }

        private static object NormalizeZoomRate(object? value)
        {
            if (value == null)
            {
                return 1.0;
            }

            if (double.TryParse(
                    Convert.ToString(value, CultureInfo.InvariantCulture),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var zoom) ||
                double.TryParse(
                    value.ToString(),
                    NumberStyles.Float,
                    CultureInfo.CurrentCulture,
                    out zoom))
            {
                return zoom <= 0 ? 1.0 : zoom;
            }

            return 1.0;
        }

        private static object GetRotation(object graph, int index, string path)
        {
            var metadata = EditorMetadata.Load(path);
            if (graph.GetType().Name == "GraphImage" && metadata.ImageRotations.TryGetValue(index.ToString(), out var rotation)) return rotation;
            if (graph.GetType().Name == "GraphAnimation") return metadata.BackgroundRotation;
            return Reflection.Get(graph, "rotate") ?? Reflection.Get(graph, "ration") ?? "";
        }

        private static string RectText(object? value) =>
            value is Rectangle rect ? $"{rect.X},{rect.Y},{rect.Width},{rect.Height}" : "";

        private static string First(params string?[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
    }

    private static class TemplateSerializer
    {
        public static object Load(string path)
        {
            byte[] bytes;
            using (var input = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var buffer = new MemoryStream())
            {
                input.CopyTo(buffer);
                bytes = buffer.ToArray();
            }
            return ConvertFromBytes(bytes)
                ?? throw new InvalidDataException($"L-Connect ThemeEngine could not read template: {path}");
        }

        public static void Save(object theme, string path)
        {
            var temp = path + ".tmp-csharp";
            try
            {
                File.WriteAllBytes(temp, ConvertToBytes(theme));
                File.Copy(temp, path, true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        public static object Clone(object value)
        {
            return ConvertFromBytes(ConvertToBytes(value))
                ?? throw new InvalidDataException("L-Connect ThemeEngine could not clone the template layer.");
        }

        private static byte[] ConvertToBytes(object value)
        {
            var utilsType = RequireThemeType("ThemeEngine.Utils");
            var toBytes = utilsType.GetMethod("ChangeObjectToByte", BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException(utilsType.FullName, "ChangeObjectToByte");
            return toBytes.Invoke(null, new[] { value }) as byte[]
                ?? throw new InvalidDataException("L-Connect ThemeEngine could not serialize the template layer.");
        }

        private static object? ConvertFromBytes(byte[] bytes)
        {
            var utilsType = RequireThemeType("ThemeEngine.Utils");
            var fromBytes = utilsType.GetMethod("ChangeByteToObject", BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException(utilsType.FullName, "ChangeByteToObject");
            return fromBytes.Invoke(null, new object[] { bytes });
        }

        private static Type RequireThemeType(string name) =>
            _themeAssembly?.GetType(name, throwOnError: true)
            ?? throw new InvalidOperationException($"L-Connect ThemeEngine type was not found: {name}");
    }

    private static class Reflection
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

        public static object? Get(object? target, string name)
        {
            if (target == null) return null;
            var type = target.GetType();
            var property = type.GetProperty(name, Flags);
            if (property != null) try { return property.GetValue(target); } catch { }
            var field = type.GetField(name, Flags) ?? type.GetField($"<{name}>k__BackingField", Flags);
            if (field != null) try { return field.GetValue(target); } catch { }
            return null;
        }

        public static string GetString(object? target, string name) => Get(target, name)?.ToString() ?? "";
        public static int GetInt(object? target, string name, int fallback = 0) => ConvertValue(Get(target, name), fallback);
        public static double GetDouble(object? target, string name, double fallback = 0) => ConvertValue(Get(target, name), fallback);
        public static bool GetBool(object? target, string name) => ConvertValue(Get(target, name), false);

        public static bool TrySet(object? target, string name, object? value)
        {
            if (target == null) return false;
            var type = target.GetType();
            var property = type.GetProperty(name, Flags);
            if (property != null)
            {
                try { property.SetValue(target, ChangeType(value, property.PropertyType)); return true; } catch { }
            }
            var field = type.GetField(name, Flags) ?? type.GetField($"<{name}>k__BackingField", Flags);
            if (field != null)
            {
                try { field.SetValue(target, ChangeType(value, field.FieldType)); return true; } catch { }
            }
            return false;
        }

        public static void Set(object target, string name, object? value)
        {
            if (!TrySet(target, name, value)) throw new MissingMemberException(target.GetType().FullName, name);
        }

        public static MethodInfo? FindMethod(object target, string name) => target.GetType().GetMethod(name, Flags);

        public static string[] WritableNames(object? target)
        {
            if (target == null) return Array.Empty<string>();
            var names = target.GetType().GetProperties(Flags).Where(x => x.CanWrite).Select(x => x.Name)
                .Concat(target.GetType().GetFields(Flags).Where(x => !x.IsInitOnly).Select(x => x.Name))
                .Where(x => !x.Contains("k__BackingField")).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
            return names;
        }

        private static T ConvertValue<T>(object? value, T fallback)
        {
            if (value == null) return fallback;
            try { return (T)ChangeType(value, typeof(T))!; } catch { return fallback; }
        }

        private static object? ChangeType(object? value, Type targetType)
        {
            if (value == null) return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
            var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (underlying.IsInstanceOfType(value)) return value;
            if (underlying.IsEnum)
            {
                if (value is string text && !int.TryParse(text, out _)) return Enum.Parse(underlying, text, true);
                return Enum.ToObject(underlying, Convert.ToInt32(value, CultureInfo.InvariantCulture));
            }
            if (underlying == typeof(bool)) return bool.Parse(value.ToString()!);
            if (underlying == typeof(Color) && value is string colorText) return ColorParser.Parse(colorText);
            return Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
        }
    }

    private static class ColorParser
    {
        public static Color Parse(string text)
        {
            var value = (text ?? "").Trim();
            var wrapper = Regex.Match(value, @"Color \[(.+)\]");
            if (wrapper.Success) value = wrapper.Groups[1].Value.Trim();
            var argb = Regex.Match(value, @"A=(\d+),\s*R=(\d+),\s*G=(\d+),\s*B=(\d+)");
            if (argb.Success) return Color.FromArgb(int.Parse(argb.Groups[1].Value), int.Parse(argb.Groups[2].Value),
                int.Parse(argb.Groups[3].Value), int.Parse(argb.Groups[4].Value));
            if (value.StartsWith("#"))
            {
                var hex = value.Substring(1);
                if (hex.Length == 6) return Color.FromArgb(255, Convert.ToInt32(hex.Substring(0, 2), 16),
                    Convert.ToInt32(hex.Substring(2, 2), 16), Convert.ToInt32(hex.Substring(4, 2), 16));
                if (hex.Length == 8) return Color.FromArgb(Convert.ToInt32(hex.Substring(0, 2), 16),
                    Convert.ToInt32(hex.Substring(2, 2), 16), Convert.ToInt32(hex.Substring(4, 2), 16),
                    Convert.ToInt32(hex.Substring(6, 2), 16));
            }
            var named = Color.FromName(value);
            return named.IsKnownColor || named.IsNamedColor ? named : Color.White;
        }
    }

    private sealed class Arguments
    {
        private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);

        public static Arguments Parse(string[] args)
        {
            var result = new Arguments();
            for (var i = 0; i < args.Length; i++)
            {
                var token = args[i];
                if (!token.StartsWith("-")) continue;
                var name = token.TrimStart('-');
                string? value = null;
                if (i + 1 < args.Length &&
                    (!args[i + 1].StartsWith("-") ||
                     double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
                    value = args[++i];
                result._values[name] = value;
            }
            return result;
        }

        public bool Has(string name) => _values.ContainsKey(name);
        public bool HasValue(string name) => _values.TryGetValue(name, out var value) && value != null;
        public string Get(string name, string fallback = "") => _values.TryGetValue(name, out var value) && value != null ? value : fallback;
        public int GetInt(string name, int fallback = 0) => int.TryParse(Get(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
        public double GetDouble(string name, double fallback = 0) => double.TryParse(Get(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
        public bool GetBool(string name, bool fallback = false) => bool.TryParse(Get(name), out var value) ? value : fallback;
    }

    private sealed class GraphStyle
    {
        public string Label { get; set; } = "";
        public string Code { get; set; } = "";
        public string Source { get; set; } = "";
        public string GraphType { get; set; } = "";
        public string TypeName { get; set; } = "";
        public string SubTypeName { get; set; } = "";
        public string Preview { get; set; } = "";
    }

    private sealed class EditorMetadata
    {
        public int BackgroundRotation { get; set; }
        public Dictionary<string, int> ImageRotations { get; set; } = new();

        public static EditorMetadata Load(string templatePath)
        {
            var path = templatePath + ".themeeditor.json";
            try { return File.Exists(path) ? Json.Deserialize<EditorMetadata>(File.ReadAllText(path, Encoding.UTF8)) ?? new EditorMetadata() : new EditorMetadata(); }
            catch { return new EditorMetadata(); }
        }

        public void Save(string templatePath) => File.WriteAllText(templatePath + ".themeeditor.json", Json.Serialize(this), Encoding.UTF8);
    }

    private static class ProfileStore
    {
        public static string GetTemplateBackground(string profileDir, string templateId, string deviceModel)
        {
            foreach (var file in ProfileFiles(profileDir))
            {
                try
                {
                    var json = Read(file);
                    if (json.TryGetValue("TemplateCustomBackgrounds", out var backgrounds) &&
                        backgrounds is Dictionary<string, object> map &&
                        map.TryGetValue(templateId, out var mapped) &&
                        BackgroundMatches(mapped?.ToString(), deviceModel))
                        return mapped?.ToString() ?? "";

                    if (json.TryGetValue("SelectedTemplateId", out var selected) &&
                        selected?.ToString() == templateId &&
                        json.TryGetValue("CustomTheme", out var customValue) &&
                        customValue is Dictionary<string, object> custom)
                    {
                        foreach (var key in new[] { "AppliedImageVideoPath", "SelectedImageVideoPath" })
                        {
                            if (custom.TryGetValue(key, out var value) && BackgroundMatches(value?.ToString(), deviceModel))
                                return value?.ToString() ?? "";
                        }
                    }
                }
                catch { }
            }
            return "";
        }

        public static string GetActiveTemplateId(string profileDir, string deviceModel, string lConnectDir)
        {
            var logTemplateId = GetLatestAppliedTemplateIdFromLogs(deviceModel);
            if (TemplateExistsForActiveId(deviceModel, lConnectDir, logTemplateId))
            {
                return logTemplateId;
            }

            foreach (var file in ProfileFiles(profileDir))
            {
                try
                {
                    var json = Read(file);
                    if (json.TryGetValue("SelectedTemplateId", out var value) && value != null && !string.IsNullOrWhiteSpace(value.ToString()))
                    {
                        var templateId = value.ToString()!;
                        if (TemplateExistsForActiveId(deviceModel, lConnectDir, templateId))
                        {
                            return templateId;
                        }
                    }
                }
                catch { }
            }

            throw new InvalidOperationException(
                $"No active template was found for {deviceModel}.");
        }

        private static string GetLatestAppliedTemplateIdFromLogs(string deviceModel)
        {
            var tag = GetLogDeviceTag(deviceModel);
            if (string.IsNullOrWhiteSpace(tag))
            {
                return "";
            }

            var logDir = Path.Combine(DefaultProgramData, "logs");
            if (!Directory.Exists(logDir))
            {
                return "";
            }

            var pattern = new Regex(@"\[" + Regex.Escape(tag) + @"\]\s+Template\s+'(?<id>[^']+)'\s+applied", RegexOptions.IgnoreCase);
            foreach (var file in Directory.GetFiles(logDir, "L-Connect-Service-*.log")
                         .OrderByDescending(File.GetLastWriteTimeUtc)
                         .Take(5))
            {
                string[] lines;
                try
                {
                    lines = ReadSharedLogLines(file);
                }
                catch
                {
                    continue;
                }

                for (var i = lines.Length - 1; i >= 0; i--)
                {
                    var match = pattern.Match(lines[i]);
                    if (match.Success)
                    {
                        return match.Groups["id"].Value.Trim();
                    }
                }
            }

            return "";
        }

        private static string[] ReadSharedLogLines(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var lines = new List<string>();
            while (reader.ReadLine() is { } line)
            {
                lines.Add(line);
            }

            return lines.ToArray();
        }

        private static string GetLogDeviceTag(string deviceModel)
        {
            if (deviceModel.Equals("hydroshift-ii-lcd-s", StringComparison.OrdinalIgnoreCase))
            {
                return "HydroShift II LCD-S";
            }

            if (deviceModel.Equals("hydroshift-ii-lcd-c", StringComparison.OrdinalIgnoreCase))
            {
                return "HydroShift II LCD-C";
            }

            if (deviceModel.Equals("universal-screen-8.8-inch", StringComparison.OrdinalIgnoreCase))
            {
                return "Universal Screen";
            }

            if (deviceModel.Equals("vm-9.2-inch", StringComparison.OrdinalIgnoreCase))
            {
                return "8.8 inch";
            }

            return "";
        }

        private static bool TemplateExistsForActiveId(string deviceModel, string lConnectDir, string templateId)
        {
            return !string.IsNullOrWhiteSpace(ResolveActiveTemplatePath(deviceModel, lConnectDir, templateId));
        }

        public static string ResolveActiveTemplatePath(string deviceModel, string lConnectDir, string templateId)
        {
            foreach (var root in ActiveTemplateRoots(deviceModel, lConnectDir).Where(Directory.Exists))
            {
                var candidate = Path.Combine(root, templateId + ".template");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return "";
        }

        private static IEnumerable<string> ActiveTemplateRoots(string deviceModel, string lConnectDir)
        {
            yield return Path.Combine(DefaultProgramData, deviceModel, "template");
            yield return Path.Combine(lConnectDir, "Assets", deviceModel, "template");
        }

        public static void SetTemplateBackground(string profileDir, string templateId, string path)
        {
            foreach (var file in ProfileFiles(profileDir))
            {
                try
                {
                    var json = Read(file);
                    var selectedMatches =
                        json.TryGetValue("SelectedTemplateId", out var selected) &&
                        selected?.ToString() == templateId;
                    var backgroundEntryExists =
                        json.TryGetValue("TemplateCustomBackgrounds", out var existingBackgrounds) &&
                        existingBackgrounds is Dictionary<string, object> existingMap &&
                        existingMap.ContainsKey(templateId);
                    if (!selectedMatches && !backgroundEntryExists) continue;
                    if (!json.TryGetValue("TemplateCustomBackgrounds", out var backgrounds) || backgrounds is not Dictionary<string, object> map)
                    {
                        map = new Dictionary<string, object>();
                        json["TemplateCustomBackgrounds"] = map;
                    }
                    map[templateId] = path;
                    Write(file, json);
                    return;
                }
                catch { }
            }
        }

        private static IEnumerable<string> ProfileFiles(string profileDir) =>
            Directory.Exists(profileDir)
                ? Directory.GetFiles(profileDir).OrderByDescending(File.GetLastWriteTimeUtc)
                : Enumerable.Empty<string>();

        private static Dictionary<string, object> Read(string path)
        {
            var bytes = File.ReadAllBytes(path);
            string text;
            if (bytes.Length >= 2 && bytes[0] == 0x1f && bytes[1] == 0x8b)
            {
                using var source = new MemoryStream(bytes);
                using var gzip = new GZipStream(source, CompressionMode.Decompress);
                using var reader = new StreamReader(gzip, Encoding.UTF8);
                text = reader.ReadToEnd();
            }
            else text = Encoding.UTF8.GetString(bytes);
            return Json.Deserialize<Dictionary<string, object>>(text);
        }

        private static void Write(string path, Dictionary<string, object> data)
        {
            var gzip = File.ReadAllBytes(path).Take(2).SequenceEqual(new byte[] { 0x1f, 0x8b });
            var text = Json.Serialize(data);
            if (!gzip) { File.WriteAllText(path, text, Encoding.UTF8); return; }
            using var file = File.Create(path);
            using var stream = new GZipStream(file, CompressionMode.Compress);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(text);
        }

        private static bool BackgroundMatches(string? path, string deviceModel)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var match = Regex.Match(
                path,
                @"hydroshift-ii-lcd-[sc]|universal-screen-8\.8-inch|vm-9\.2-inch",
                RegexOptions.IgnoreCase);
            return !match.Success || match.Value.Equals(deviceModel, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void LoadAssemblies(string lConnectDir)
    {
        var themePath = Path.Combine(lConnectDir, "lianli.ThemeEngine.dll");
        var lcdPath = Path.Combine(lConnectDir, "lianli.lcd207.dll");
        if (!File.Exists(themePath)) throw new FileNotFoundException("ThemeEngine DLL not found: " + themePath);
        _themeAssembly = Assembly.LoadFrom(themePath);
        AppDomain.CurrentDomain.AssemblyResolve += (_, eventArgs) =>
        {
            var requested = new AssemblyName(eventArgs.Name);
            if (requested.Name.Equals("lianli.ThemeEngine", StringComparison.OrdinalIgnoreCase)) return _themeAssembly;
            var candidate = Path.Combine(lConnectDir, requested.Name + ".dll");
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        };
        if (File.Exists(lcdPath)) Assembly.LoadFrom(lcdPath);
    }

    private static void EnsureDeviceWorkspace(string model, string lConnectDir)
    {
        var root = Path.Combine(DefaultProgramData, model);
        var assets = Path.Combine(lConnectDir, "Assets", model);
        foreach (var name in new[] { "template", "modulars", "theme", "video", "image", "preview", "temp", "wireless-template" })
        {
            var target = Path.Combine(root, name);
            Directory.CreateDirectory(target);
            var source = Path.Combine(assets, name);
            if (!Directory.Exists(source)) continue;
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = file.Substring(source.Length).TrimStart('\\', '/');
                var destination = Path.Combine(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (!File.Exists(destination)) File.Copy(file, destination);
            }
        }
    }

    private static void CopyWithRetry(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        Exception? last = null;
        for (var i = 0; i < 5; i++)
        {
            try { File.Copy(source, destination, true); return; }
            catch (Exception ex) { last = ex; System.Threading.Thread.Sleep(100 * (i + 1)); }
        }
        throw last ?? new IOException("Copy failed.");
    }

    private static bool TryCopyWithRetry(string source, string destination)
    {
        try
        {
            CopyWithRetry(source, destination);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}

internal sealed class ReferenceEqualityComparer : IEqualityComparer<object>
{
    public static readonly ReferenceEqualityComparer Instance = new();

    public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

    public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
}
