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
