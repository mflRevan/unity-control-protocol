#if UNITY_6000_0_OR_NEWER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UCP.Bridge.Tests
{
    public sealed class UiLintServiceTests
    {
        private const string Root = "Assets/UcpUiLintServiceTests";

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.DeleteAsset(Root);
            AssetDatabase.CreateFolder("Assets", "UcpUiLintServiceTests");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Root);
        }

        [Test]
        public void ValidUxmlAndUss_AreImportedAndCloned()
        {
            var ussPath = Root + "/Valid.uss";
            var uxmlPath = Root + "/Valid.uxml";
            WriteAsset(ussPath, ".probe { width: 100px; }\n");
            WriteAsset(
                uxmlPath,
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\" editor-extension-mode=\"True\">\n" +
                "  <ui:Label name=\"probe\" text=\"Ready\" />\n" +
                "</ui:UXML>\n");

            var result = UiLintService.Run(new Dictionary<string, object>
            {
                ["paths"] = new List<object> { uxmlPath, ussPath },
                ["failOnWarnings"] = true,
                ["maxDiagnostics"] = 50
            });

            Assert.That(result["passed"], Is.True);
            Assert.That(Convert.ToInt32(result["errorCount"]), Is.EqualTo(0));
            Assert.That(Convert.ToInt32(result["warningCount"]), Is.EqualTo(0));
            var assets = ((List<object>)result["assets"])
                .Cast<Dictionary<string, object>>()
                .ToList();
            var uxml = assets.Single(asset => asset["path"].ToString() == uxmlPath);
            Assert.That(uxml["cloneAttempted"], Is.True);
            Assert.That(uxml["cloneSucceeded"], Is.True);
            Assert.That(Convert.ToInt32(uxml["cloneElementCount"]), Is.GreaterThanOrEqualTo(2));
        }

        [TestCase("ImportError", "error")]
        [TestCase("Warning", "warning")]
        [TestCase("Info", null)]
        [TestCase(null, null)]
        public void ImportSeverity_IsClassifiedWithoutEmittingConsoleErrors(
            string flags,
            string expected)
        {
            Assert.That(UiLintService.ClassifyImportSeverity(flags), Is.EqualTo(expected));
        }

        [TestCase(LogType.Error, "error")]
        [TestCase(LogType.Assert, "error")]
        [TestCase(LogType.Exception, "error")]
        [TestCase(LogType.Warning, "warning")]
        [TestCase(LogType.Log, null)]
        public void CloneLogSeverity_IsClassifiedWithoutEmittingConsoleErrors(
            LogType type,
            string expected)
        {
            Assert.That(UiLintService.ClassifyLogSeverity(type), Is.EqualTo(expected));
        }

        [Test]
        public void WarningFailurePolicy_IsExplicit()
        {
            Assert.That(UiLintService.Passes(0, 1, false), Is.True);
            Assert.That(UiLintService.Passes(0, 1, true), Is.False);
            Assert.That(UiLintService.Passes(1, 0, false), Is.False);
        }

        [TestCase(1)]
        [TestCase(3)]
        public void DiagnosticLimit_PreservesErrorsAndCountsEverySeverity(int limit)
        {
            var collector = new UiLintService.DiagnosticCollector(limit);
            for (var index = 0; index < limit + 1; index++)
                collector.Add("warning", "warning-" + index, Root, "test", "warning");
            collector.Add("error", "late-error", Root, "test", "cause of failure");
            Assert.That(collector.ErrorCount, Is.EqualTo(1));
            Assert.That(collector.WarningCount, Is.EqualTo(limit + 1));
            Assert.That(collector.Truncated, Is.True);
            Assert.That(collector.Items, Has.Count.EqualTo(limit));
            var details = collector.Items.Cast<Dictionary<string, object>>().ToList();
            Assert.That(details.Last()["code"], Is.EqualTo("late-error"));
            if (limit > 1)
                Assert.That(details[limit - 2]["code"], Is.EqualTo("warning-" + (limit - 2)));
            for (var index = 0; index < limit; index++)
                collector.Add("error", "error-" + index, Root, "test", "error");
            collector.Add("warning", "dropped", Root, "test", "warning");
            Assert.That(collector.ErrorCount, Is.EqualTo(limit + 1));
            Assert.That(collector.WarningCount, Is.EqualTo(limit + 2));
            Assert.That(collector.Items, Has.Count.EqualTo(limit));
            Assert.That(collector.Items.Cast<Dictionary<string, object>>()
                .All(item => Equals(item["severity"], "error")), Is.True);
            Assert.That(((Dictionary<string, object>)collector.Items[0])["code"], Is.EqualTo("late-error"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ThemeStyleSheets_AreLintedWithTheirDependencies(bool folder)
        {
            WriteAsset(Root + "/Theme.uss", ".probe { width: 100px; }\n");
            WriteAsset(Root + "/Theme.tss", "@import url(\"Theme.uss\");\n");
            var result = UiLintService.Run(new Dictionary<string, object>
            {
                ["paths"] = new List<object> { folder ? Root : Root + "/Theme.tss" },
                ["failOnWarnings"] = true
            });
            Assert.That(result["passed"], Is.True, MiniJson.Serialize(result));
            var assets = ((List<object>)result["assets"]).Cast<Dictionary<string, object>>().ToList();
            Assert.That(assets.Select(asset => asset["path"]),
                Does.Contain(Root + "/Theme.tss").And.Contain(Root + "/Theme.uss"));
            Assert.That(assets.All(asset => Equals(asset["reimported"], true)), Is.True);
        }

        [Test]
        public void ImmutablePackageStyleSheet_UsesExistingImport()
        {
            var path = AssetDatabase.GetAllAssetPaths().FirstOrDefault(candidate =>
                candidate.StartsWith("Packages/", StringComparison.Ordinal) &&
                (candidate.EndsWith(".uss", StringComparison.OrdinalIgnoreCase) ||
                 candidate.EndsWith(".tss", StringComparison.OrdinalIgnoreCase)) &&
                !UiLintService.ShouldReimport(candidate));
            if (path == null)
                Assert.Ignore("No immutable package stylesheet is installed");
            var result = UiLintService.Run(new Dictionary<string, object>
            {
                ["paths"] = new List<object> { path }
            });
            var asset = ((List<object>)result["assets"]).Cast<Dictionary<string, object>>()
                .SingleOrDefault(item => Equals(item["path"], path));
            Assert.That(asset, Is.Not.Null, MiniJson.Serialize(result));
            Assert.That(asset["reimported"], Is.False);
            Assert.That(asset["assetType"], Is.Not.Null);
            Assert.That(((List<object>)result["diagnostics"]).Cast<Dictionary<string, object>>()
                .Any(item => Equals(item["code"], "UI_IMPORT_FAILED")), Is.False);
        }

        private static void WriteAsset(string assetPath, string contents)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var absolutePath = Path.Combine(projectRoot, assetPath);
            File.WriteAllText(absolutePath, contents);
            AssetDatabase.ImportAsset(
                assetPath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        }
    }
}
#endif
