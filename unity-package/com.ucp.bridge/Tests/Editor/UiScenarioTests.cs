#if UNITY_6000_0_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace UCP.Bridge.Tests
{
    public sealed class UiScenarioTests
    {
        private const string TestFolder = "Assets/UcpUiScenarioTests";
        private const string DocumentPath = TestFolder + "/Document.uxml";
        private const string RowPath = TestFolder + "/Row.uxml";

        [SetUp]
        public void SetUp()
        {
            DeleteTestFolder();
            Directory.CreateDirectory(AbsolutePath(TestFolder));
            File.WriteAllText(
                AbsolutePath(DocumentPath),
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><ui:VisualElement name=\"document-root\" /></ui:UXML>");
            File.WriteAllText(
                AbsolutePath(RowPath),
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><ui:VisualElement name=\"row-root\"><ui:Label name=\"row-label\" text=\"pending\"><Bindings><ui:DataBinding property=\"text\" data-source-path=\"name\" binding-mode=\"ToTarget\" /></Bindings></ui:Label></ui:VisualElement></ui:UXML>");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        [TearDown]
        public void TearDown()
        {
            DeleteTestFolder();
        }

        [Test]
        public void Resolve_RejectsUnknownFixtureFields()
        {
            var fixturePath = WriteFixture(
                "Unknown.ucp-ui.json",
                "{\"schemaVersion\":0,\"document\":\"./Document.uxml\",\"unexpected\":true,\"states\":{\"default\":{}}}");

            var exception = Assert.Throws<UiScenarioException>(() => UiScenarioLoader.Resolve(
                new Dictionary<string, object> { ["target"] = fixturePath }));

            Assert.That(exception.Code, Is.EqualTo("ui.unknown-field"));
            Assert.That(exception.Location, Does.EndWith(".unexpected"));
        }

        [TestCase("Packages/com.example.ui/Styles/../Theme.tss", null, "Packages/com.example.ui/Theme.tss")]
        [TestCase("./Theme.tss", "Packages/com.example.ui/Panel.ucp-ui.json", "Packages/com.example.ui/Theme.tss")]
        [TestCase("Assets/UI/../Panel.uxml", null, "Assets/Panel.uxml")]
        public void NormalizeAssetPath_PreservesVirtualRoots(string path, string fixture, string expected)
        {
            Assert.That(UiScenarioLoader.NormalizeAssetPath(path, fixture, "$test"), Is.EqualTo(expected));
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            Assert.That(UiScenarioLoader.NormalizeAssetPath(Path.Combine(projectRoot, expected), null, "$test"),
                Is.EqualTo(expected));
        }

        [TestCase("Packages/../../outside.uss")]
        [TestCase("Assets/../Library/Theme.tss")]
        [TestCase("Packages/com.example/../../Library/Theme.tss")]
        public void NormalizeAssetPath_RejectsEscapesAfterCollapsingSegments(string path)
        {
            Assert.Throws<UiScenarioException>(() => UiScenarioLoader.NormalizeAssetPath(path, null, "$test"));
        }

        [Test]
        public void Resolve_RejectsMoreThanSixtyFourStates()
        {
            var states = new Dictionary<string, object>();
            for (var index = 0; index < 65; index++)
                states["state-" + index] = new Dictionary<string, object>();

            var fixturePath = WriteFixture(
                "TooManyStates.ucp-ui.json",
                MiniJson.Serialize(new Dictionary<string, object>
                {
                    ["schemaVersion"] = 0,
                    ["document"] = "./Document.uxml",
                    ["defaultState"] = "state-0",
                    ["states"] = states
                }));

            var exception = Assert.Throws<UiScenarioException>(() => UiScenarioLoader.Resolve(
                new Dictionary<string, object> { ["target"] = fixturePath },
                allStates: true));

            Assert.That(exception.Code, Is.EqualTo("ui.states-limit"));
            Assert.That(exception.Location, Does.EndWith("#states"));
        }

        [Test]
        public void Resolve_RejectsViewportAreaForFixtureAndPartialOverride()
        {
            var oversizedPath = WriteFixture(
                "OversizedViewport.ucp-ui.json",
                "{\"schemaVersion\":0,\"document\":\"./Document.uxml\",\"viewport\":{\"width\":4096,\"height\":2049},\"states\":{\"default\":{}}}");

            var fixtureException = Assert.Throws<UiScenarioException>(() => UiScenarioLoader.Resolve(
                new Dictionary<string, object> { ["target"] = oversizedPath }));
            Assert.That(fixtureException.Code, Is.EqualTo("ui.viewport-area"));
            Assert.That(fixtureException.Location, Does.EndWith("#viewport"));

            var boundaryPath = WriteFixture(
                "BoundaryViewport.ucp-ui.json",
                "{\"schemaVersion\":0,\"document\":\"./Document.uxml\",\"viewport\":{\"width\":4096,\"height\":2048},\"states\":{\"default\":{}}}");
            Assert.That(UiScenarioLoader.Resolve(
                new Dictionary<string, object> { ["target"] = boundaryPath })[0].Viewport.Height,
                Is.EqualTo(2048));

            var requestException = Assert.Throws<UiScenarioException>(() => UiScenarioLoader.Resolve(
                new Dictionary<string, object>
                {
                    ["target"] = boundaryPath,
                    ["viewport"] = new Dictionary<string, object> { ["height"] = 2049L }
                }));
            Assert.That(requestException.Code, Is.EqualTo("ui.viewport-area"));
            Assert.That(requestException.Location, Is.EqualTo("$request.viewport"));
        }

        [Test]
        public void Resolve_SelectsDefaultAndMergesNestedRequestData()
        {
            var fixturePath = WriteFixture(
                "States.ucp-ui.json",
                "{"
                + "\"schemaVersion\":0,"
                + "\"document\":\"./Document.uxml\","
                + "\"defaultState\":\"populated\","
                + "\"data\":{\"nested\":{\"shared\":\"base\",\"count\":1}},"
                + "\"states\":{"
                + "\"empty\":{\"data\":{\"items\":[]}},"
                + "\"populated\":{\"viewport\":{\"width\":800,\"height\":500},\"data\":{\"nested\":{\"count\":2},\"items\":[{\"name\":\"One\"}]}}"
                + "}}");

            var resolved = UiScenarioLoader.Resolve(new Dictionary<string, object>
            {
                ["target"] = fixturePath,
                ["data"] = new Dictionary<string, object>
                {
                    ["nested"] = new Dictionary<string, object> { ["count"] = 9L }
                }
            });

            Assert.That(resolved, Has.Count.EqualTo(1));
            Assert.That(resolved[0].StateName, Is.EqualTo("populated"));
            Assert.That(resolved[0].DocumentPath, Is.EqualTo(DocumentPath));
            Assert.That(resolved[0].Viewport.Width, Is.EqualTo(800));
            Assert.That(resolved[0].Viewport.Height, Is.EqualTo(500));
            var nested = (Dictionary<string, object>)resolved[0].Data["nested"];
            Assert.That(nested["shared"], Is.EqualTo("base"));
            Assert.That(nested["count"], Is.TypeOf<int>().And.EqualTo(9));

            var all = UiScenarioLoader.Resolve(
                new Dictionary<string, object> { ["target"] = fixturePath },
                allStates: true);
            Assert.That(all.Select(scenario => scenario.StateName), Is.EqualTo(new[] { "empty", "populated" }));
        }

        [Test]
        public void Resolve_NormalizesOnlySafeIntegersToInt32()
        {
            var fixturePath = WriteFixture(
                "Numbers.ucp-ui.json",
                "{\"schemaVersion\":0,\"document\":\"./Document.uxml\",\"data\":{\"small\":42,\"large\":2147483648,\"items\":[-1,2147483647]},\"states\":{\"default\":{}}}");

            var scenario = UiScenarioLoader.Resolve(
                new Dictionary<string, object> { ["target"] = fixturePath })[0];
            var items = (List<object>)scenario.Data["items"];

            Assert.That(scenario.Data["small"], Is.TypeOf<int>().And.EqualTo(42));
            Assert.That(scenario.Data["large"], Is.TypeOf<long>().And.EqualTo(2147483648L));
            Assert.That(items[0], Is.TypeOf<int>().And.EqualTo(-1));
            Assert.That(items[1], Is.TypeOf<int>().And.EqualTo(int.MaxValue));
        }

        [Test]
        public void Apply_BuildsRepeatAndOwnsListViewBindingLifecycle()
        {
            var document = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(DocumentPath);
            var rowTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(RowPath);
            Assert.That(document, Is.Not.Null);
            Assert.That(rowTemplate, Is.Not.Null);

            var items = new List<object>
            {
                Item("One"),
                Item("Two"),
                Item("Three")
            };
            var data = new Dictionary<string, object> { ["items"] = items };
            var collections = new List<UiCollectionDefinition>
            {
                new UiCollectionDefinition("#repeat", UiCollectionMode.Repeat, "/items", RowPath, rowTemplate, null, "$test.repeat"),
                new UiCollectionDefinition("#list", UiCollectionMode.ListView, "/items", RowPath, rowTemplate, 24f, "$test.list")
            };
            var scenario = new UiResolvedScenario(
                DocumentPath,
                null,
                "default",
                DocumentPath,
                document,
                data,
                Array.Empty<UiSetOperation>(),
                collections,
                new UiViewport(),
                new UiSettleOptions());

            var root = new VisualElement();
            var repeat = new VisualElement { name = "repeat" };
            var list = new ListView { name = "list" };
            root.Add(repeat);
            root.Add(list);

            var report = UiScenarioApplier.Apply(root, scenario);

            Assert.That(repeat.childCount, Is.EqualTo(3));
            Assert.That(report.RepeatItemCount, Is.EqualTo(3));
            Assert.That(list.itemsSource, Is.SameAs(items));
            Assert.That(list.makeItem, Is.Not.Null);
            Assert.That(list.bindItem, Is.Not.Null);
            Assert.That(list.unbindItem, Is.Not.Null);
            Assert.That(list.destroyItem, Is.Not.Null);
            Assert.That(list.virtualizationMethod, Is.EqualTo(CollectionVirtualizationMethod.FixedHeight));
            Assert.That(list.fixedItemHeight, Is.EqualTo(24f));

            var row = list.makeItem();
            list.bindItem(row, 1);
            Assert.That(row.dataSource, Is.SameAs(items[1]));

            var metadata = UiScenarioApplier.GetCollectionMetadata(root);
            Assert.That(metadata, Has.Count.EqualTo(2));
            var repeatMetadata = metadata.Single(item => item.Mode == UiCollectionMode.Repeat);
            var listMetadata = metadata.Single(item => item.Mode == UiCollectionMode.ListView);
            Assert.That(repeatMetadata.LogicalItemCount, Is.EqualTo(3));
            Assert.That(repeatMetadata.RealizedRowCount, Is.EqualTo(3));
            Assert.That(repeatMetadata.BoundRowCount, Is.EqualTo(3));
            Assert.That(listMetadata.LogicalItemCount, Is.EqualTo(3));
            Assert.That(listMetadata.RealizedRowCount, Is.EqualTo(1));
            Assert.That(listMetadata.BoundRowCount, Is.EqualTo(1));

            list.bindItem(row, 2);
            Assert.That(listMetadata.RealizedRowCount, Is.EqualTo(1));
            Assert.That(listMetadata.BoundRowCount, Is.EqualTo(1));
            var boundRows = (List<object>)listMetadata.ToDictionary()["boundRows"];
            Assert.That(((Dictionary<string, object>)boundRows[0])["index"], Is.EqualTo(2));

            list.unbindItem(row, 2);
            Assert.That(row.dataSource, Is.Null);
            Assert.That(listMetadata.BoundRowCount, Is.Zero);

            list.bindItem(row, 0);
            Assert.That(listMetadata.BoundRowCount, Is.EqualTo(1));
            list.destroyItem(row);
            Assert.That(row.dataSource, Is.Null);
            Assert.That(listMetadata.RealizedRowCount, Is.Zero);
            Assert.That(listMetadata.BoundRowCount, Is.Zero);
            Assert.That(report.BindingRewriteCount, Is.GreaterThanOrEqualTo(4));
        }

        [Test]
        public void Apply_RejectsAmbiguousSelectors()
        {
            var document = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(DocumentPath);
            var root = new VisualElement();
            var first = new VisualElement();
            var second = new VisualElement();
            first.AddToClassList("duplicate");
            second.AddToClassList("duplicate");
            root.Add(first);
            root.Add(second);

            var scenario = new UiResolvedScenario(
                DocumentPath,
                null,
                "default",
                DocumentPath,
                document,
                new Dictionary<string, object>(),
                new[] { new UiSetOperation(".duplicate", "enabled", true, "$test.set") },
                Array.Empty<UiCollectionDefinition>(),
                new UiViewport(),
                new UiSettleOptions());

            var exception = Assert.Throws<UiScenarioException>(() => UiScenarioApplier.Apply(root, scenario));
            Assert.That(exception.Code, Is.EqualTo("ui.selector-ambiguous"));
        }

        [UnityTest]
        public IEnumerator InspectAndAudit_IncludeRealizedListViewRowsAndBindingFailures()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Realized ListView binding coverage requires an Editor graphics device");

            var previousFocus = EditorWindow.focusedWindow;
            var window = UiHostWindow.Open(320, 240);
            try
            {
                var root = window.ContentRoot;
                Binding.SetPanelLogLevel(root.panel, BindingLogLevel.None);
                var list = new ListView { name = "rows" };
                list.style.height = 160;
                root.Add(list);
                var scenario = new UiResolvedScenario(
                    DocumentPath, null, "default", DocumentPath,
                    AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(DocumentPath),
                    new Dictionary<string, object>
                    {
                        ["items"] = new List<object> { Item("One"), new Dictionary<string, object>() }
                    },
                    Array.Empty<UiSetOperation>(),
                    new[]
                    {
                        new UiCollectionDefinition("#rows", UiCollectionMode.ListView, "/items", RowPath,
                            AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(RowPath), 24f, "$test.list")
                    },
                    new UiViewport(320, 240), new UiSettleOptions());
                var report = UiScenarioApplier.Apply(root, scenario);
                var metadata = UiScenarioApplier.GetCollectionMetadata(root);
                List<Label> labels = null;
                var deadline = EditorApplication.timeSinceStartup + 10;
                while (EditorApplication.timeSinceStartup < deadline)
                {
                    window.Repaint();
                    EditorApplication.QueuePlayerLoopUpdate();
                    yield return null;
                    labels = root.Query<Label>("row-label").ToList();
                    if (labels.Any(label => label.text == "One") && labels.Any(label =>
                            label.TryGetLastBindingToUIResult("text", out var result) &&
                            result.status == BindingStatus.Failure))
                        break;
                }
                Assert.That(labels, Has.Count.EqualTo(2), "Both visible rows should be realized");
                Assert.That(labels.Any(label => label.text == "One"), Is.True, "Valid row binding should resolve");
                Assert.That(labels.Any(label =>
                    label.TryGetLastBindingToUIResult("text", out var result) &&
                    result.status == BindingStatus.Failure), Is.True, "Missing row data should fail binding");

                var snapshot = UiVisualTreeInspector.Inspect(root,
                    UiInspectOptions.Parse(new Dictionary<string, object> { ["query"] = "#row-label" }), metadata);
                Assert.That(snapshot["matchCount"], Is.EqualTo(2));
                Assert.That(snapshot["returnedElementCount"], Is.EqualTo(2));
                var fullSnapshot = UiVisualTreeInspector.Inspect(list,
                    UiInspectOptions.Parse(new Dictionary<string, object> { ["depth"] = 64 }), metadata);
                Assert.That(MiniJson.Serialize(fullSnapshot), Does.Contain("row-label"));
                var boundedSnapshot = UiVisualTreeInspector.Inspect(list,
                    UiInspectOptions.Parse(new Dictionary<string, object> { ["depth"] = 64, ["max"] = 2 }), metadata);
                Assert.That(boundedSnapshot["returnedElementCount"], Is.EqualTo(2));
                Assert.That(boundedSnapshot["truncated"], Is.True);

                var before = UiGeometrySampler.Measure(root);
                labels[0].text = "Changed row";
                Assert.That(UiGeometrySampler.Measure(root).Hash, Is.Not.EqualTo(before.Hash),
                    "Row changes must invalidate geometry stability");

                var audit = UiAudit.Run(root, report, metadata);
                Assert.That(audit["passed"], Is.False);
                var errors = (List<object>)audit["errors"];
                Assert.That(errors.OfType<Dictionary<string, object>>().Any(error =>
                    Equals(error["code"], "binding_failure") &&
                    Equals(((Dictionary<string, object>)error["element"])["name"], "row-label")), Is.True);
                Assert.That(((List<object>)audit["warnings"]).OfType<Dictionary<string, object>>().Any(warning =>
                    Equals(warning["code"], "duplicate_name")), Is.False,
                    "Repeated names in managed rows must still be exempt from duplicate-name warnings");

                for (var index = 0; index < 200; index++)
                    report.AddDiagnostic("warning", "test", "warning before binding audit");
                var truncatedAudit = UiAudit.Run(root, report, metadata);
                Assert.That(truncatedAudit["passed"], Is.False);
                Assert.That(truncatedAudit["errorCount"], Is.EqualTo(audit["errorCount"]));
                Assert.That((List<object>)truncatedAudit["warnings"],
                    Has.Count.EqualTo(200 - ((List<object>)audit["errors"]).Count));
                Assert.That(MiniJson.Serialize(truncatedAudit["errors"]),
                    Is.EqualTo(MiniJson.Serialize(audit["errors"])));
                Assert.That(truncatedAudit["diagnosticsTruncated"], Is.True);
            }
            finally
            {
                if (window != null && window.rootVisualElement.panel != null)
                    Binding.ResetPanelLogLevel(window.rootVisualElement.panel);
                UiHostWindow.CloseAndDestroy(window);
                if (previousFocus != null)
                    previousFocus.Focus();
            }
        }

        [UnityTest]
        public IEnumerator Audit_VisibleEmptyListViewSkipsInternalFocusTargetsButChecksAuthoredControls()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Empty ListView layout coverage requires an Editor graphics device");

            var previousFocus = EditorWindow.focusedWindow;
            var window = UiHostWindow.Open(320, 240);
            try
            {
                var root = window.ContentRoot;
                var list = new ListView { name = "rows", itemsSource = new List<object>() };
                list.style.height = 160;
                root.Add(list);
                var deadline = EditorApplication.timeSinceStartup + 10;
                var stableFrames = 0;
                while (stableFrames < 3 && EditorApplication.timeSinceStartup < deadline)
                {
                    window.Pump();
                    yield return null;
                    stableFrames = list.worldBound.height > 0 && list.worldBound.width > 0
                        ? stableFrames + 1 : 0;
                }
                Assert.That(stableFrames, Is.EqualTo(3), "The empty list must be displayed and laid out");
                var audit = UiAudit.Run(root, new UiApplyReport(), Array.Empty<UiCollectionMetadata>());
                Assert.That(audit["errorCount"], Is.EqualTo(0));
                Assert.That(audit["warningCount"], Is.EqualTo(0), MiniJson.Serialize(audit));

                // An unnamed authored control must not be exempted with the internals.
                var authored = new VisualElement { focusable = true };
                authored.style.width = 0;
                authored.style.height = 0;
                root.Add(authored);
                for (var frame = 0; frame < 3; frame++)
                {
                    window.Pump();
                    yield return null;
                }
                audit = UiAudit.Run(root, new UiApplyReport(), Array.Empty<UiCollectionMetadata>());
                Assert.That(audit["warningCount"], Is.EqualTo(1));
                Assert.That(MiniJson.Serialize(audit["warnings"]), Does.Contain("focusable_zero_size"));
            }
            finally
            {
                UiHostWindow.CloseAndDestroy(window);
                if (previousFocus != null)
                    previousFocus.Focus();
            }
        }

        [TestCase(":root")]
        [TestCase("#row-label")]
        [TestCase(".card_item-2")]
        public void ValidateSelector_AcceptsRootNameAndClassSelectors(string selector)
        {
            Assert.DoesNotThrow(() => UiScenarioLoader.ValidateSelector(selector, "$test"));
        }

        [TestCase("row-label")]
        [TestCase("#")]
        [TestCase("#a b")]
        [TestCase(".a>b")]
        [TestCase("#a.b")]
        public void ValidateSelector_RejectsAnythingButOneSimpleSelector(string selector)
        {
            var exception = Assert.Throws<UiScenarioException>(
                () => UiScenarioLoader.ValidateSelector(selector, "$test"));
            Assert.That(exception.Code, Is.EqualTo("ui.selector-syntax"));
        }

        [Test]
        public void Apply_SetOperations_CoverEveryAllowlistedProperty()
        {
            var root = new VisualElement();
            var label = new Label("before") { name = "label" };
            var field = new TextField { name = "field" };
            var toggle = new Toggle { name = "toggle" };
            var panel = new VisualElement { name = "panel" };
            root.Add(label);
            root.Add(field);
            root.Add(toggle);
            root.Add(panel);

            var report = UiScenarioApplier.Apply(root, ScenarioWithSets(
                new UiSetOperation("#label", "text", "after", "$test.set[0]"),
                new UiSetOperation("#field", "value", "typed", "$test.set[1]"),
                new UiSetOperation("#toggle", "value", true, "$test.set[2]"),
                new UiSetOperation("#panel", "display", "none", "$test.set[3]"),
                new UiSetOperation("#panel", "class:highlight", true, "$test.set[4]"),
                new UiSetOperation("#label", "enabled", false, "$test.set[5]"),
                new UiSetOperation("#field", "tooltip", "hint", "$test.set[6]"),
                new UiSetOperation("#toggle", "visibility", "hidden", "$test.set[7]")));

            Assert.That(report.SetCount, Is.EqualTo(8));
            Assert.That(label.text, Is.EqualTo("after"));
            Assert.That(field.value, Is.EqualTo("typed"));
            Assert.That(toggle.value, Is.True);
            Assert.That(panel.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(panel.ClassListContains("highlight"), Is.True);
            Assert.That(label.enabledSelf, Is.False);
            Assert.That(field.tooltip, Is.EqualTo("hint"));
            Assert.That(toggle.style.visibility.value, Is.EqualTo(Visibility.Hidden));
        }

        [Test]
        public void Apply_SetValueTypeMismatch_ReportsCodeAndExactLocation()
        {
            var root = new VisualElement();
            root.Add(new Toggle { name = "toggle" });

            var exception = Assert.Throws<UiScenarioException>(() => UiScenarioApplier.Apply(
                root,
                ScenarioWithSets(new UiSetOperation("#toggle", "value", "yes", "$test.set[0]"))));

            Assert.That(exception.Code, Is.EqualTo("ui.set-value-type"));
            Assert.That(exception.Location, Is.EqualTo("$test.set[0].value"));
        }

        [Test]
        public void List_ReportsDocumentsAndFixturesIncludingInvalidOnes()
        {
            var validPath = WriteFixture(
                "Valid.ucp-ui.json",
                "{\"schemaVersion\":0,\"document\":\"./Document.uxml\",\"states\":{\"default\":{}}}");
            var invalidPath = WriteFixture(
                "Invalid.ucp-ui.json",
                "{\"schemaVersion\":0,\"document\":\"./Document.uxml\",\"bogus\":1,\"states\":{\"default\":{}}}");

            var result = (Dictionary<string, object>)UiScenarioLoader.List(
                new Dictionary<string, object> { ["path"] = TestFolder });
            var items = ((List<object>)result["items"]).Cast<Dictionary<string, object>>().ToList();

            Assert.That(Convert.ToInt32(result["count"]), Is.EqualTo(4));
            Assert.That(result["truncated"], Is.False);
            Assert.That(
                items.Select(item => item["target"]),
                Is.EquivalentTo(new object[] { DocumentPath, RowPath, validPath, invalidPath }));
            Assert.That(items.Count(item => Equals(item["type"], "uxml")), Is.EqualTo(2));

            var valid = items.Single(item => Equals(item["target"], validPath));
            Assert.That(valid["valid"], Is.True);
            Assert.That(valid["defaultState"], Is.EqualTo("default"));

            var invalid = items.Single(item => Equals(item["target"], invalidPath));
            Assert.That(invalid["valid"], Is.False);
            var diagnostic = (Dictionary<string, object>)((List<object>)invalid["diagnostics"])[0];
            Assert.That(diagnostic["code"], Is.EqualTo("ui.unknown-field"));
        }

        private static UiResolvedScenario ScenarioWithSets(params UiSetOperation[] sets)
        {
            return new UiResolvedScenario(
                DocumentPath,
                null,
                "default",
                DocumentPath,
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(DocumentPath),
                new Dictionary<string, object>(),
                sets,
                Array.Empty<UiCollectionDefinition>(),
                new UiViewport(),
                new UiSettleOptions());
        }

        private static Dictionary<string, object> Item(string name)
        {
            return new Dictionary<string, object> { ["name"] = name };
        }

        private static string WriteFixture(string filename, string contents)
        {
            var assetPath = TestFolder + "/" + filename;
            File.WriteAllText(AbsolutePath(assetPath), contents);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            return assetPath;
        }

        private static void DeleteTestFolder()
        {
            if (AssetDatabase.IsValidFolder(TestFolder))
                AssetDatabase.DeleteAsset(TestFolder);
            else if (Directory.Exists(AbsolutePath(TestFolder)))
                Directory.Delete(AbsolutePath(TestFolder), true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static string AbsolutePath(string assetPath)
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
#endif
