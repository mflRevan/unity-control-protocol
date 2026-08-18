#if UNITY_6000_0_OR_NEWER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
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
