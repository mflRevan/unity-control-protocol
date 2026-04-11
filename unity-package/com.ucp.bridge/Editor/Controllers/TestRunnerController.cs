using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace UCP.Bridge
{
    public static class TestRunnerController
    {
        private static TestRunnerApi s_api;
        private static TestResultCollector s_collector;
        private const string ConsoleGuardName = "UCP.Bridge.Tests.ConsoleLogGuard";

        public static void Register(CommandRouter router)
        {
            router.Register("tests/run", HandleRunTests);
        }

        private static object HandleRunTests(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            var mode = "edit";
            string filter = null;

            if (p != null)
            {
                if (p.TryGetValue("mode", out var m)) mode = m?.ToString() ?? "edit";
                if (p.TryGetValue("filter", out var f)) filter = f?.ToString();
            }

            var testMode = mode.ToLowerInvariant() switch
            {
                "play" => TestMode.PlayMode,
                "playmode" => TestMode.PlayMode,
                _ => TestMode.EditMode
            };

            if (s_api == null)
                s_api = ScriptableObject.CreateInstance<TestRunnerApi>();

            if (s_collector != null)
                s_api.UnregisterCallbacks(s_collector);

            var logCursor = LogsController.GetLatestId();
            s_collector = new TestResultCollector(logCursor);
            s_api.RegisterCallbacks(s_collector);

            var executionSettings = new ExecutionSettings
            {
                filters = new[] { new Filter { testMode = testMode } }
            };

            if (!string.IsNullOrEmpty(filter))
            {
                executionSettings.filters[0].testNames = new[] { filter };
            }

            var shouldWaitForPlayModeExit =
                testMode == TestMode.EditMode &&
                (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode);

            if (shouldWaitForPlayModeExit)
            {
                if (EditorApplication.isPlaying)
                    EditorApplication.isPlaying = false;

                ExecuteWhenReady(executionSettings, testMode, EditorApplication.timeSinceStartup + 30.0);
            }
            else
            {
                s_api.Execute(executionSettings);
            }

            // Tests run asynchronously in Unity. We return immediately with a pending status.
            // Results will be sent as notifications when complete.
            return new Dictionary<string, object>
            {
                ["status"] = "started",
                ["mode"] = mode,
                ["message"] = shouldWaitForPlayModeExit
                    ? "Edit-mode tests queued. Waiting for Unity to exit play mode before starting."
                    : "Tests started. Results will arrive as notifications."
            };
        }

        private static void ExecuteWhenReady(ExecutionSettings settings, TestMode mode, double deadline)
        {
            EditorApplication.delayCall += () =>
            {
                var stillInPlayMode = EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode;
                if (mode == TestMode.EditMode && stillInPlayMode)
                {
                    if (EditorApplication.timeSinceStartup > deadline)
                    {
                        BridgeServer.BroadcastNotification("tests/result", new Dictionary<string, object>
                        {
                            ["summary"] = new Dictionary<string, object>
                            {
                                ["total"] = 1,
                                ["passed"] = 0,
                                ["failed"] = 1,
                                ["skipped"] = 0,
                                ["duration"] = 0.0
                            },
                            ["tests"] = new List<object>
                            {
                                new Dictionary<string, object>
                                {
                                    ["name"] = "UCP.Bridge.Tests.PlayModeExitGuard",
                                    ["status"] = "failed",
                                    ["duration"] = 0.0,
                                    ["message"] = "Timed out waiting for Unity to exit play mode before running edit-mode tests."
                                }
                            }
                        });

                        if (s_api != null && s_collector != null)
                            s_api.UnregisterCallbacks(s_collector);
                        return;
                    }

                    ExecuteWhenReady(settings, mode, deadline);
                    return;
                }

                s_api.Execute(settings);
            };
        }

        private class TestResultCollector : ICallbacks
        {
            private readonly List<object> _results = new();
            private readonly long _logCursor;
            private int _passed, _failed, _skipped;
            private double _startTime;

            public TestResultCollector(long logCursor)
            {
                _logCursor = logCursor;
                _startTime = EditorApplication.timeSinceStartup;
            }

            public void RunStarted(ITestAdaptor testsToRun) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                _results.Clear();
                _passed = 0;
                _failed = 0;
                _skipped = 0;
                CollectLeafResults(result);
                var logSummary = LogsController.BuildStatusSummary(_logCursor);
                var consoleWarnings = GetLevelCount(logSummary, "warning");
                var consoleErrors = GetLevelCount(logSummary, "error") + GetLevelCount(logSummary, "exception");

                if (consoleErrors > 0)
                {
                    _failed++;
                    _results.Add(new Dictionary<string, object>
                    {
                        ["name"] = ConsoleGuardName,
                        ["status"] = "failed",
                        ["duration"] = 0.0,
                        ["message"] = BuildConsoleGuardMessage(consoleErrors, consoleWarnings, logSummary)
                    });
                }

                var duration = EditorApplication.timeSinceStartup - _startTime;

                var summary = new Dictionary<string, object>
                {
                    ["total"] = _passed + _failed + _skipped,
                    ["passed"] = _passed,
                    ["failed"] = _failed,
                    ["skipped"] = _skipped,
                    ["duration"] = (double)duration,
                    ["consoleClean"] = consoleErrors == 0,
                    ["consoleWarnings"] = consoleWarnings,
                    ["consoleErrors"] = consoleErrors
                };

                BridgeServer.BroadcastNotification("tests/result", new Dictionary<string, object>
                {
                    ["summary"] = summary,
                    ["tests"] = _results,
                    ["logs"] = logSummary
                });

                if (s_api != null)
                    s_api.UnregisterCallbacks(this);
            }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result) { }

            private void CollectLeafResults(ITestResultAdaptor result)
            {
                if (result == null)
                    return;

                if (result.HasChildren)
                {
                    foreach (var child in result.Children)
                        CollectLeafResults(child);
                    return;
                }

                string status;
                switch (result.TestStatus)
                {
                    case TestStatus.Passed:
                        status = "passed";
                        _passed++;
                        break;
                    case TestStatus.Failed:
                        status = "failed";
                        _failed++;
                        break;
                    case TestStatus.Skipped:
                        status = "skipped";
                        _skipped++;
                        break;
                    default:
                        status = "unknown";
                        break;
                }

                var entry = new Dictionary<string, object>
                {
                    ["name"] = ResolveTestName(result),
                    ["status"] = status,
                    ["duration"] = result.Duration
                };

                if (!string.IsNullOrEmpty(result.Message))
                    entry["message"] = result.Message;
                if (!string.IsNullOrEmpty(result.StackTrace))
                    entry["stackTrace"] = result.StackTrace;

                _results.Add(entry);
            }

            private static string ResolveTestName(ITestResultAdaptor result)
            {
                if (!string.IsNullOrEmpty(result.FullName))
                    return result.FullName;

                if (result.Test != null)
                {
                    if (!string.IsNullOrEmpty(result.Test.FullName))
                        return result.Test.FullName;
                    if (!string.IsNullOrEmpty(result.Test.Name))
                        return result.Test.Name;
                }

                return result.Name;
            }

            private static int GetLevelCount(Dictionary<string, object> logSummary, string level)
            {
                if (!logSummary.TryGetValue("byLevel", out var byLevelObj)
                    || byLevelObj is not Dictionary<string, object> byLevel
                    || !byLevel.TryGetValue(level, out var value))
                {
                    return 0;
                }

                return Convert.ToInt32(value);
            }

            private static string BuildConsoleGuardMessage(
                int consoleErrors,
                int consoleWarnings,
                Dictionary<string, object> logSummary)
            {
                var message = $"Unity emitted {consoleErrors} error/exception log(s) during the test run";
                if (consoleWarnings > 0)
                    message += $" and {consoleWarnings} warning log(s)";

                if (!logSummary.TryGetValue("topCategories", out var categoriesObj)
                    || categoriesObj is not List<object> categories
                    || categories.Count == 0)
                {
                    return message + ".";
                }

                var parts = new List<string>();
                foreach (var categoryObj in categories)
                {
                    if (categoryObj is not Dictionary<string, object> category)
                        continue;

                    var count = category.TryGetValue("count", out var countObj)
                        ? Convert.ToInt32(countObj)
                        : 0;
                    var sample = category.TryGetValue("sampleMessage", out var sampleObj)
                        ? sampleObj?.ToString() ?? string.Empty
                        : string.Empty;
                    if (count <= 0 || string.IsNullOrEmpty(sample))
                        continue;

                    parts.Add($"{count}x {sample}");
                    if (parts.Count >= 3)
                        break;
                }

                return parts.Count == 0
                    ? message + "."
                    : $"{message}. Top categories: {string.Join(" | ", parts)}";
            }
        }
    }
}
