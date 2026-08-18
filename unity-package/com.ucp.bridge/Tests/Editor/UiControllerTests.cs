#if UNITY_6000_0_OR_NEWER
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace UCP.Bridge.Tests
{
    public sealed class UiControllerTests
    {
        [Test]
        public void Register_ExposesUiCommandFamily()
        {
            var router = new CommandRouter();
            UiController.Register(router);

            Assert.That(router.HasMethod("ui/list"), Is.True);
            Assert.That(router.HasMethod("ui/lint"), Is.True);
            Assert.That(router.HasMethod("ui/inspect"), Is.True);
            Assert.That(router.HasMethod("ui/screenshot"), Is.True);
            Assert.That(router.HasMethod("ui/check"), Is.True);
            Assert.That(router.HasMethod("ui/status"), Is.True);
        }

        [Test]
        public void Inspect_ReturnsAsyncStartEnvelopeBeforeResolvingTarget()
        {
            try
            {
                var router = new CommandRouter();
                UiController.Register(router);

                var response = router.Dispatch(
                    "ui/inspect",
                    1,
                    "{\"target\":\"Assets/DoesNotExist.uxml\"}");

                Assert.That(response.error, Is.Null);
                var result = (Dictionary<string, object>)response.result;
                Assert.That(result["status"], Is.EqualTo("started"));
                Assert.That(result["operation"], Is.EqualTo("inspect"));
                Assert.That(result["operationId"].ToString(), Does.StartWith("ui-"));
            }
            finally
            {
                // Prevent the intentionally unresolved target from running on a later
                // editor update and polluting the test run's console guard.
                UiOperationManager.ResetForTests();
            }
        }

        [Test]
        public void OperationManager_ConstructorFailureCompletesAndAdvancesQueue()
        {
            UiOperationManager.ResetForTests();
            try
            {
                var failedStart = UiOperationManager.Start("inspect", new Dictionary<string, object>
                {
                    ["target"] = "Assets/DoesNotExist.uxml",
                    ["failOnWarnings"] = "not-a-boolean"
                });
                var followingStart = UiOperationManager.Start("inspect", new Dictionary<string, object>
                {
                    ["failOnWarnings"] = "also-not-a-boolean"
                });

                UiOperationManager.TickForTests();

                var failed = UiOperationManager.Status(failedStart["operationId"].ToString());
                Assert.That(failed["status"], Is.EqualTo("failed"));
                var error = (Dictionary<string, object>)failed["error"];
                Assert.That(error["code"], Is.EqualTo("invalid_params"));

                var following = UiOperationManager.Status(followingStart["operationId"].ToString());
                Assert.That(following["status"], Is.EqualTo("failed"));
                Assert.That(
                    ((Dictionary<string, object>)following["error"])["code"],
                    Is.EqualTo("invalid_params"));
            }
            finally
            {
                UiOperationManager.ResetForTests();
            }
        }

        [Test]
        public void Cleanup_RunsEveryActionAndRetainsFirstFailure()
        {
            var visited = new List<int>();
            var expected = new InvalidOperationException("first");

            var result = UiCleanup.RunAll(
                () =>
                {
                    visited.Add(1);
                    throw expected;
                },
                () => visited.Add(2),
                () =>
                {
                    visited.Add(3);
                    throw new InvalidOperationException("later");
                });

            Assert.That(visited, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(result, Is.SameAs(expected));
        }

        [Test]
        public void OperationRecord_PreservesResultWhenCleanupFails()
        {
            var record = new UiOperationRecord(
                "ui-test",
                "inspect",
                new Dictionary<string, object>());
            record.MarkRunning();
            record.Complete(
                new Dictionary<string, object> { ["stateCount"] = 1 },
                UiRenderOperation.BuildError(new UiOperationException(
                    "cleanup_failed",
                    "cleanup failed")));

            var envelope = record.Envelope();
            Assert.That(envelope["status"], Is.EqualTo("failed"));
            Assert.That(envelope.ContainsKey("result"), Is.True);
            Assert.That(envelope.ContainsKey("error"), Is.True);
            Assert.That(
                ((Dictionary<string, object>)envelope["error"])["code"],
                Is.EqualTo("cleanup_failed"));
        }

        [Test]
        public void CaptureArtifactFileName_DisambiguatesLossyStateTokens()
        {
            var first = UiRenderOperation.CaptureArtifactFileName(
                "Assets/Panel.uxml",
                "a/b",
                0,
                "ui-test");
            var second = UiRenderOperation.CaptureArtifactFileName(
                "Assets/Panel.uxml",
                "a-b",
                1,
                "ui-test");

            Assert.That(first, Is.EqualTo("Panel-a-b-s0000-ui-test.png"));
            Assert.That(second, Is.EqualTo("Panel-a-b-s0001-ui-test.png"));
            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(
                UiRenderOperation.CaptureArtifactFileName("Assets/Panel.uxml", "a/b", 0, "ui-test"),
                Is.EqualTo(first));
        }

        [Test]
        public void Inspector_AppliesSimpleQueryAndElementBound()
        {
            var root = new VisualElement { name = "root" };
            var first = new Label("First") { name = "first" };
            first.AddToClassList("match");
            var second = new Label("Second") { name = "second" };
            second.AddToClassList("match");
            root.Add(first);
            root.Add(second);

            var options = UiInspectOptions.Parse(new Dictionary<string, object>
            {
                ["query"] = ".match",
                ["detail"] = "summary",
                ["max"] = 1L
            });
            var result = UiVisualTreeInspector.Inspect(
                root,
                options,
                Array.Empty<UiCollectionMetadata>());

            Assert.That(Convert.ToInt32(result["matchCount"]), Is.EqualTo(2));
            Assert.That(Convert.ToInt32(result["returnedElementCount"]), Is.EqualTo(1));
            Assert.That(Convert.ToBoolean(result["truncated"]), Is.True);
        }

        [Test]
        public void Audit_ReportsDuplicateStaticNames()
        {
            var root = new VisualElement();
            root.Add(new Label { name = "duplicate" });
            root.Add(new Label { name = "duplicate" });

            var result = UiAudit.Run(
                root,
                new UiApplyReport(),
                Array.Empty<UiCollectionMetadata>());

            Assert.That(Convert.ToInt32(result["errorCount"]), Is.EqualTo(0));
            Assert.That(Convert.ToInt32(result["warningCount"]), Is.EqualTo(1));
            var warnings = (List<object>)result["warnings"];
            var warning = (Dictionary<string, object>)warnings[0];
            Assert.That(warning["code"], Is.EqualTo("duplicate_name"));
        }

        [Test]
        public void Audit_UsesOneGlobalDiagnosticBudgetIncludingDuplicateNames()
        {
            var report = new UiApplyReport();
            for (var index = 0; index < 199; index++)
                report.AddDiagnostic("error", "test", "test diagnostic");

            var root = new VisualElement();
            root.Add(new VisualElement { name = "first-duplicate" });
            root.Add(new VisualElement { name = "first-duplicate" });
            root.Add(new VisualElement { name = "second-duplicate" });
            root.Add(new VisualElement { name = "second-duplicate" });

            var result = UiAudit.Run(
                root,
                report,
                Array.Empty<UiCollectionMetadata>());

            Assert.That(Convert.ToInt32(result["errorCount"]), Is.EqualTo(199));
            Assert.That(Convert.ToInt32(result["warningCount"]), Is.EqualTo(1));
            Assert.That(result["diagnosticsTruncated"], Is.True);
        }

        [Test]
        public void Audit_IgnoresHiddenFocusableElementsAndUnityInternalNames()
        {
            var root = new VisualElement();
            var displayNone = new VisualElement();
            displayNone.style.display = DisplayStyle.None;
            displayNone.Add(new VisualElement { name = "hidden-by-display", focusable = true });
            root.Add(displayNone);

            var visibilityHidden = new VisualElement();
            visibilityHidden.style.visibility = Visibility.Hidden;
            visibilityHidden.Add(new VisualElement { name = "hidden-by-visibility", focusable = true });
            root.Add(visibilityHidden);

            root.Add(new VisualElement { name = "unity-generated" });
            root.Add(new VisualElement { name = "unity-generated" });
            root.Add(new VisualElement { name = "authored-name" });
            root.Add(new VisualElement { name = "authored-name" });

            var result = UiAudit.Run(
                root,
                new UiApplyReport(),
                Array.Empty<UiCollectionMetadata>());
            var warnings = (List<object>)result["warnings"];

            Assert.That(warnings, Has.Count.EqualTo(1));
            var warning = (Dictionary<string, object>)warnings[0];
            Assert.That(warning["code"], Is.EqualTo("duplicate_name"));
            Assert.That(
                ((Dictionary<string, object>)warning["element"])["name"],
                Is.EqualTo("authored-name"));
        }

        [Test]
        public void OperationDurationLimit_IsOverallAndReportsCompletedStates()
        {
            Assert.That(
                UiOperationManager.HasExceededOperationDuration(
                    10.0,
                    10.0 + UiOperationManager.MaxOperationDurationSeconds),
                Is.False);
            Assert.That(
                UiOperationManager.HasExceededOperationDuration(
                    10.0,
                    10.001 + UiOperationManager.MaxOperationDurationSeconds),
                Is.True);

            var error = UiRenderOperation.BuildError(
                UiOperationManager.CreateOperationTimeout("check", 3, 301.0));
            Assert.That(error["code"], Is.EqualTo("timeout"));
            var details = (Dictionary<string, object>)error["details"];
            Assert.That(details["scope"], Is.EqualTo("operation"));
            Assert.That(Convert.ToInt32(details["completedStateCount"]), Is.EqualTo(3));
        }

        [Test]
        public void GeometrySampler_IsDeterministicForUnchangedTree()
        {
            var root = new VisualElement { name = "root" };
            root.Add(new Label("Stable") { name = "label" });

            var first = UiGeometrySampler.Measure(root);
            var second = UiGeometrySampler.Measure(root);

            Assert.That(first.Hash, Is.EqualTo(second.Hash));
            Assert.That(first.ElementCount, Is.EqualTo(2));
            Assert.That(first.PanelAttached, Is.False);
            Assert.That(first.IsValid, Is.False);
        }

        [Test]
        public void JsonSanitizer_ReplacesNonFiniteNumbersWithNull()
        {
            var result = (Dictionary<string, object>)UiJsonSanitizer.Sanitize(
                new Dictionary<string, object>
                {
                    ["nan"] = double.NaN,
                    ["infinity"] = float.PositiveInfinity,
                    ["finite"] = 4.25
                });

            Assert.That(result["nan"], Is.Null);
            Assert.That(result["infinity"], Is.Null);
            Assert.That(result["finite"], Is.EqualTo(4.25));
        }

        [Test]
        public void JsonSanitizer_NormalizesScalarsUnsupportedByMiniJson()
        {
            var result = (Dictionary<string, object>)UiJsonSanitizer.Sanitize(
                new Dictionary<string, object>
                {
                    ["character"] = 'x',
                    ["byte"] = (byte)12,
                    ["signedByte"] = (sbyte)-4,
                    ["short"] = (short)-123,
                    ["unsignedShort"] = ushort.MaxValue,
                    ["unsignedInt"] = uint.MaxValue,
                    ["unsignedLong"] = ulong.MaxValue,
                    ["decimal"] = 123.4500m
                });

            Assert.That(result["character"], Is.EqualTo("x"));
            Assert.That(result["byte"], Is.TypeOf<int>().And.EqualTo(12));
            Assert.That(result["signedByte"], Is.TypeOf<int>().And.EqualTo(-4));
            Assert.That(result["short"], Is.TypeOf<int>().And.EqualTo(-123));
            Assert.That(result["unsignedShort"], Is.TypeOf<int>().And.EqualTo(ushort.MaxValue));
            Assert.That(result["unsignedInt"], Is.TypeOf<long>().And.EqualTo((long)uint.MaxValue));
            Assert.That(result["unsignedLong"], Is.EqualTo(ulong.MaxValue.ToString()));
            Assert.That(result["decimal"], Is.EqualTo("123.4500"));

            var serialized = MiniJson.Serialize(result);
            Assert.That(serialized, Does.Not.Contain("{}"));
            Assert.That(MiniJson.Deserialize(serialized), Is.TypeOf<Dictionary<string, object>>());
        }
    }
}
#endif
