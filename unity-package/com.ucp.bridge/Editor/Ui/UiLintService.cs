#if UNITY_6000_0_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UCP.Bridge
{
    internal static class UiLintService
    {
        private const int DefaultMaxDiagnostics = 200;
        private const int AbsoluteMaxDiagnostics = 1000;

        internal static Dictionary<string, object> Run(Dictionary<string, object> parameters)
        {
            if (parameters == null || !parameters.TryGetValue("paths", out var pathsValue))
                throw new ArgumentException("Missing 'paths' parameter");

            var requestedPaths = ReadStringList(pathsValue, "paths");
            if (requestedPaths.Count == 0)
                throw new ArgumentException("'paths' must contain at least one path");

            var failOnWarnings = ReadBool(parameters, "failOnWarnings", false);
            var maxDiagnostics = ReadInt(
                parameters,
                "maxDiagnostics",
                DefaultMaxDiagnostics,
                1,
                AbsoluteMaxDiagnostics);

            var diagnostics = new DiagnosticCollector(maxDiagnostics);
            var fixtureReports = new List<object>();
            var pendingAssets = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var requestedPath in requestedPaths)
                CollectRequestedPath(requestedPath, pendingAssets, fixtureReports, diagnostics);

            var inspectedAssets = new HashSet<string>(StringComparer.Ordinal);
            var assetReports = new List<object>();

            while (pendingAssets.Count > 0)
            {
                var assetPath = pendingAssets.Min;
                pendingAssets.Remove(assetPath);
                if (!inspectedAssets.Add(assetPath))
                    continue;

                var report = InspectAsset(assetPath, diagnostics);
                assetReports.Add(report);

                if (!report.TryGetValue("dependencies", out var dependenciesValue) ||
                    dependenciesValue is not IList dependencies)
                {
                    continue;
                }

                foreach (var dependencyValue in dependencies)
                {
                    var dependency = dependencyValue as string;
                    if (IsUiAssetPath(dependency) && !inspectedAssets.Contains(dependency))
                        pendingAssets.Add(dependency);
                }
            }

            var passed = Passes(diagnostics.ErrorCount, diagnostics.WarningCount, failOnWarnings);

            return new Dictionary<string, object>
            {
                ["passed"] = passed,
                ["failOnWarnings"] = failOnWarnings,
                ["requestedPaths"] = requestedPaths.Cast<object>().ToList(),
                ["assetCount"] = assetReports.Count,
                ["fixtureCount"] = fixtureReports.Count,
                ["errorCount"] = diagnostics.ErrorCount,
                ["warningCount"] = diagnostics.WarningCount,
                ["diagnosticCount"] = diagnostics.ErrorCount + diagnostics.WarningCount,
                ["diagnosticsTruncated"] = diagnostics.Truncated,
                ["diagnostics"] = diagnostics.Items,
                ["assets"] = assetReports,
                ["fixtures"] = fixtureReports
            };
        }

        private static void CollectRequestedPath(
            string requestedPath,
            SortedSet<string> assets,
            List<object> fixtureReports,
            DiagnosticCollector diagnostics)
        {
            string assetPath;
            try
            {
                assetPath = NormalizeProjectPath(requestedPath);
            }
            catch (ArgumentException ex)
            {
                diagnostics.Add("error", "UI_PATH_INVALID", requestedPath, "request", ex.Message);
                return;
            }

            if (AssetDatabase.IsValidFolder(assetPath))
            {
                var childPaths = AssetDatabase.FindAssets(string.Empty, new[] { assetPath })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .OrderBy(path => path, StringComparer.Ordinal);
                foreach (var childPath in childPaths)
                {
                    if (IsUiAssetPath(childPath))
                        assets.Add(childPath);
                    else if (IsFixturePath(childPath))
                        CollectFixture(childPath, assets, fixtureReports, diagnostics);
                }

                return;
            }

            if (IsFixturePath(assetPath))
            {
                CollectFixture(assetPath, assets, fixtureReports, diagnostics);
                return;
            }

            if (!IsUiAssetPath(assetPath))
            {
                diagnostics.Add(
                    "error",
                    "UI_PATH_UNSUPPORTED",
                    assetPath,
                    "request",
                    "Expected a .uxml, .uss, .tss, .ucp-ui.json, or project folder path");
                return;
            }

            if (!AssetExists(assetPath))
            {
                diagnostics.Add("error", "UI_ASSET_NOT_FOUND", assetPath, "request", "Asset not found");
                return;
            }

            assets.Add(assetPath);
        }

        private static void CollectFixture(
            string fixturePath,
            SortedSet<string> assets,
            List<object> fixtureReports,
            DiagnosticCollector diagnostics)
        {
            if (!AssetExists(fixturePath))
            {
                diagnostics.Add("error", "UI_FIXTURE_NOT_FOUND", fixturePath, "fixture", "Fixture not found");
                return;
            }

            try
            {
                var report = UiScenarioLoader.ValidateFixture(fixturePath);
                fixtureReports.Add(report);

                if (report.TryGetValue("dependencies", out var dependenciesValue) &&
                    dependenciesValue is IList dependencies)
                {
                    foreach (var dependencyValue in dependencies)
                    {
                        if (dependencyValue is not Dictionary<string, object> dependency ||
                            !dependency.TryGetValue("path", out var pathValue) ||
                            pathValue == null)
                        {
                            continue;
                        }

                        var dependencyPath = pathValue.ToString();
                        if (IsUiAssetPath(dependencyPath))
                            assets.Add(dependencyPath);
                    }
                }
            }
            catch (UiScenarioException ex)
            {
                fixtureReports.Add(new Dictionary<string, object>
                {
                    ["valid"] = false,
                    ["path"] = fixturePath,
                    ["error"] = ex.ToDictionary()
                });
                diagnostics.Add("error", ex.Code, fixturePath, "fixture", ex.Message, ex.Location);
            }
            catch (Exception ex)
            {
                fixtureReports.Add(new Dictionary<string, object>
                {
                    ["valid"] = false,
                    ["path"] = fixturePath
                });
                diagnostics.Add("error", "UI_FIXTURE_INVALID", fixturePath, "fixture", ex.Message);
            }
        }

        private static Dictionary<string, object> InspectAsset(
            string assetPath,
            DiagnosticCollector diagnostics)
        {
            var report = new Dictionary<string, object>
            {
                ["path"] = assetPath,
                ["guid"] = AssetDatabase.AssetPathToGUID(assetPath),
                ["assetType"] = null,
                ["importerType"] = null,
                ["contentHash"] = string.Empty,
                ["importedWithErrors"] = false,
                ["importedWithWarnings"] = false,
                ["reimported"] = false,
                ["cloneAttempted"] = false,
                ["cloneSucceeded"] = false,
                ["cloneElementCount"] = 0,
                ["dependencies"] = new List<object>()
            };

            if (!AssetExists(assetPath))
            {
                diagnostics.Add("error", "UI_ASSET_NOT_FOUND", assetPath, "request", "Asset not found");
                return report;
            }

            try
            {
                if (ShouldReimport(assetPath))
                {
                    AssetDatabase.ImportAsset(
                        assetPath,
                        ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                    report["reimported"] = true;
                }
                report["contentHash"] = AssetDatabase.GetAssetDependencyHash(assetPath).ToString();
            }
            catch (Exception ex)
            {
                diagnostics.Add("error", "UI_IMPORT_FAILED", assetPath, "import", ex.Message);
                return report;
            }

            var dependencies = AssetDatabase.GetDependencies(assetPath, true)
                .Where(IsUiAssetPath)
                .OrderBy(value => value, StringComparer.Ordinal)
                .Cast<object>()
                .ToList();
            report["dependencies"] = dependencies;

            var importer = AssetImporter.GetAtPath(assetPath);
            report["importerType"] = importer == null ? null : importer.GetType().FullName;

            var importHadError = false;
            var importHadWarning = false;
            var importLog = AssetImporter.GetImportLog(assetPath);
            if (importLog?.logEntries != null)
            {
                foreach (var entry in importLog.logEntries)
                {
                    var severity = ClassifyImportSeverity(entry.flags.ToString());
                    if (severity == null)
                        continue;

                    importHadError |= severity == "error";
                    importHadWarning |= severity == "warning";
                    diagnostics.Add(
                        severity,
                        severity == "error" ? "UI_IMPORT_ERROR" : "UI_IMPORT_WARNING",
                        assetPath,
                        "import",
                        entry.message,
                        entry.line,
                        entry.file,
                        entry.context == null ? null : entry.context.name);
                }
            }

            var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            report["assetType"] = asset == null ? null : asset.GetType().FullName;
            if (asset == null)
            {
                diagnostics.Add("error", "UI_ASSET_LOAD_FAILED", assetPath, "import", "Asset did not load");
                return report;
            }

            var importedWithErrors = false;
            var importedWithWarnings = false;

            if (asset is StyleSheet styleSheet)
            {
                importedWithErrors = styleSheet.importedWithErrors;
                importedWithWarnings = styleSheet.importedWithWarnings;
                report["contentHash"] = styleSheet.contentHash.ToString();
            }
            else if (asset is VisualTreeAsset treeAsset)
            {
                importedWithErrors = treeAsset.importedWithErrors;
                importedWithWarnings = treeAsset.importedWithWarnings;
                report["contentHash"] = treeAsset.contentHash.ToString();
                ProbeClone(treeAsset, assetPath, report, diagnostics);
            }

            report["importedWithErrors"] = importedWithErrors;
            report["importedWithWarnings"] = importedWithWarnings;

            if (importedWithErrors && !importHadError)
            {
                diagnostics.Add(
                    "error",
                    "UI_IMPORT_ERROR",
                    assetPath,
                    "import",
                    "Unity reports that the asset imported with errors");
            }

            if (importedWithWarnings && !importHadWarning)
            {
                diagnostics.Add(
                    "warning",
                    "UI_IMPORT_WARNING",
                    assetPath,
                    "import",
                    "Unity reports that the asset imported with warnings");
            }

            return report;
        }

        private static void ProbeClone(
            VisualTreeAsset treeAsset,
            string assetPath,
            Dictionary<string, object> report,
            DiagnosticCollector diagnostics)
        {
            report["cloneAttempted"] = true;
            var cloneRoot = new VisualElement();

            void CaptureCloneLog(string message, string stackTrace, LogType type)
            {
                var severity = ClassifyLogSeverity(type);
                if (severity == null)
                    return;

                diagnostics.Add(
                    severity,
                    severity == "error" ? "UI_UXML_CLONE_ERROR" : "UI_UXML_CLONE_WARNING",
                    assetPath,
                    "clone",
                    message);
            }

            Application.logMessageReceived += CaptureCloneLog;
            try
            {
                treeAsset.CloneTree(cloneRoot);
                report["cloneSucceeded"] = true;
                report["cloneElementCount"] = CountElements(cloneRoot);
            }
            catch (Exception ex)
            {
                report["cloneSucceeded"] = false;
                diagnostics.Add(
                    "error",
                    "UI_UXML_CLONE_EXCEPTION",
                    assetPath,
                    "clone",
                    ex.GetBaseException().Message);
            }
            finally
            {
                Application.logMessageReceived -= CaptureCloneLog;
            }
        }

        private static int CountElements(VisualElement root)
        {
            var count = 1;
            for (var index = 0; index < root.hierarchy.childCount; index++)
                count += CountElements(root.hierarchy[index]);
            return count;
        }

        private static string NormalizeProjectPath(string value)
        {
            return UiScenarioLoader.NormalizeAssetPath(value, null, "$request.paths");
        }

        private static bool AssetExists(string assetPath)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return AssetDatabase.LoadMainAssetAtPath(assetPath) != null ||
                   File.Exists(Path.Combine(projectRoot, assetPath));
        }

        internal static bool ShouldReimport(string assetPath)
        {
            if (!assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                return true;
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
            // Immutable packages already have imported artifacts. Inspect those
            // artifacts and logs without forcing work on the package cache.
            return package != null &&
                   (package.source == UnityEditor.PackageManager.PackageSource.Embedded ||
                    package.source == UnityEditor.PackageManager.PackageSource.Local);
        }

        private static bool IsUiAssetPath(string path)
        {
            return path != null &&
                   (path.EndsWith(".uxml", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".uss", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".tss", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsFixturePath(string path)
        {
            return path != null && path.EndsWith(".ucp-ui.json", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool Passes(int errorCount, int warningCount, bool failOnWarnings)
        {
            return errorCount == 0 && (!failOnWarnings || warningCount == 0);
        }

        internal static string ClassifyImportSeverity(string flags)
        {
            if (string.IsNullOrEmpty(flags))
                return null;
            if (flags.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
                return "error";
            if (flags.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0)
                return "warning";
            return null;
        }

        internal static string ClassifyLogSeverity(LogType type)
        {
            if (type == LogType.Warning)
                return "warning";
            if (type == LogType.Error || type == LogType.Assert || type == LogType.Exception)
                return "error";
            return null;
        }

        private static List<string> ReadStringList(object value, string field)
        {
            if (value is not List<object> values)
                throw new ArgumentException($"'{field}' must be an array of strings");

            var result = new List<string>(values.Count);
            foreach (var item in values)
            {
                if (item is not string text || string.IsNullOrWhiteSpace(text))
                    throw new ArgumentException($"'{field}' must contain only non-empty strings");
                result.Add(text);
            }

            return result;
        }

        private static bool ReadBool(Dictionary<string, object> values, string field, bool fallback)
        {
            if (!values.TryGetValue(field, out var value) || value == null)
                return fallback;
            if (value is bool boolean)
                return boolean;
            throw new ArgumentException($"'{field}' must be a boolean");
        }

        private static int ReadInt(
            Dictionary<string, object> values,
            string field,
            int fallback,
            int minimum,
            int maximum)
        {
            if (!values.TryGetValue(field, out var value) || value == null)
                return fallback;

            int parsed;
            try
            {
                parsed = Convert.ToInt32(value);
            }
            catch (Exception)
            {
                throw new ArgumentException($"'{field}' must be an integer");
            }

            if (parsed < minimum || parsed > maximum)
                throw new ArgumentException($"'{field}' must be between {minimum} and {maximum}");
            return parsed;
        }

        internal sealed class DiagnosticCollector
        {
            private readonly int _limit;

            internal DiagnosticCollector(int limit)
            {
                _limit = limit;
            }

            internal int ErrorCount { get; private set; }
            internal int WarningCount { get; private set; }
            internal bool Truncated => ErrorCount + WarningCount > Items.Count;
            internal List<object> Items { get; } = new();

            internal void Add(
                string severity,
                string code,
                string assetPath,
                string source,
                string message,
                object line = null,
                string file = null,
                string context = null)
            {
                if (severity == "error")
                    ErrorCount++;
                else if (severity == "warning")
                    WarningCount++;

                if (Items.Count >= _limit)
                {
                    if (severity != "error")
                        return;
                    var warningIndex = Items.FindLastIndex(item =>
                        item is Dictionary<string, object> diagnostic &&
                        Equals(diagnostic["severity"], "warning"));
                    if (warningIndex < 0)
                        return;
                    Items.RemoveAt(warningIndex);
                }

                Items.Add(new Dictionary<string, object>
                {
                    ["severity"] = severity,
                    ["code"] = code,
                    ["assetPath"] = assetPath,
                    ["source"] = source,
                    ["message"] = message,
                    ["line"] = line,
                    ["file"] = file,
                    ["context"] = context
                });
            }
        }
    }
}
#endif
