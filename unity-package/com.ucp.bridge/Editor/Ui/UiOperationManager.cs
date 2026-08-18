#if UNITY_6000_0_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UCP.Bridge
{
    /// <summary>
    /// Runs UI Toolkit work over editor update frames so layout and repaint are never
    /// blocked by the initiating JSON-RPC handler. Operations are serialized because
    /// the capture backend requires a focused editor window.
    /// </summary>
    internal static class UiOperationManager
    {
        private const int MaxRetainedOperations = 16;
        private const double CompletedTtlSeconds = 300.0;
        // Hard ceiling from enqueue through every requested state, independent of
        // each state's settle timeout.
        internal const double MaxOperationDurationSeconds = 300.0;
        private static readonly Dictionary<string, UiOperationRecord> Operations =
            new Dictionary<string, UiOperationRecord>(StringComparer.Ordinal);
        private static readonly Queue<string> Queue = new Queue<string>();
        private static UiRenderOperation _active;

        static UiOperationManager()
        {
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            EditorApplication.quitting += Shutdown;
        }

        internal static Dictionary<string, object> Start(
            string operation,
            Dictionary<string, object> parameters)
        {
            if (Operations.Count >= MaxRetainedOperations)
            {
                PruneCompleted(true);
                if (Operations.Count >= MaxRetainedOperations)
                    throw new ArgumentException("Too many retained UI operations; wait for an active operation to finish");
            }

            var operationId = "ui-" + Guid.NewGuid().ToString("N").Substring(0, 12);
            var record = new UiOperationRecord(operationId, operation, parameters);
            Operations.Add(operationId, record);
            Queue.Enqueue(operationId);
            return new Dictionary<string, object>
            {
                ["status"] = "started",
                ["operationId"] = operationId,
                ["operation"] = operation
            };
        }

        internal static Dictionary<string, object> Status(string operationId)
        {
            if (string.IsNullOrWhiteSpace(operationId))
                throw new ArgumentException("Missing 'operationId' parameter");

            if (!Operations.TryGetValue(operationId, out var record))
            {
                return new Dictionary<string, object>
                {
                    ["found"] = false,
                    ["operationId"] = operationId
                };
            }

            var envelope = record.Envelope();
            envelope["found"] = true;
            return envelope;
        }

        internal static void ResetForTests()
        {
            Shutdown();
        }

        internal static void TickForTests()
        {
            Tick();
        }

        private static void Tick()
        {
            PruneCompleted(false);
            ExpireQueuedOperations();
            if (_active == null)
                ActivateNext();
            if (_active == null)
                return;

            try
            {
                _active.Tick();
            }
            catch (Exception exception)
            {
                _active.Fail(exception);
            }

            if (!_active.IsTerminal)
                return;

            var completed = _active;
            _active = null;
            var completionError = completed.Error;
            try
            {
                completed.Dispose();
            }
            catch (Exception exception)
            {
                var cleanupError = UiRenderOperation.BuildError(exception);
                if (completionError == null)
                {
                    completionError = cleanupError;
                }
                else
                {
                    completionError = new Dictionary<string, object>(completionError, StringComparer.Ordinal)
                    {
                        ["cleanupError"] = cleanupError
                    };
                }
            }
            var record = Operations[completed.OperationId];
            CompleteAndBroadcast(record, completed.Result, completionError);
        }

        private static void ActivateNext()
        {
            while (Queue.Count > 0)
            {
                var id = Queue.Dequeue();
                if (!Operations.TryGetValue(id, out var record) || record.IsTerminal)
                    continue;

                record.MarkRunning();
                try
                {
                    _active = new UiRenderOperation(record);
                    return;
                }
                catch (Exception exception)
                {
                    _active = null;
                    CompleteAndBroadcast(record, null, UiRenderOperation.BuildError(exception));
                }
            }
        }

        private static void ExpireQueuedOperations()
        {
            var count = Queue.Count;
            var now = EditorApplication.timeSinceStartup;
            for (var index = 0; index < count; index++)
            {
                var id = Queue.Dequeue();
                if (!Operations.TryGetValue(id, out var record) || record.IsTerminal)
                    continue;

                var elapsed = now - record.StartedAt;
                if (HasExceededOperationDuration(record.StartedAt, now))
                {
                    CompleteAndBroadcast(
                        record,
                        null,
                        UiRenderOperation.BuildError(CreateOperationTimeout(
                            record.Operation,
                            0,
                            elapsed)));
                    continue;
                }

                Queue.Enqueue(id);
            }
        }

        internal static bool HasExceededOperationDuration(double startedAt, double now)
        {
            return now - startedAt > MaxOperationDurationSeconds;
        }

        internal static UiOperationException CreateOperationTimeout(
            string operation,
            int completedStateCount,
            double elapsed)
        {
            return new UiOperationException(
                "timeout",
                $"UI operation '{operation}' exceeded the {MaxOperationDurationSeconds:F0}s overall duration limit",
                new Dictionary<string, object>
                {
                    ["scope"] = "operation",
                    ["timeoutSeconds"] = MaxOperationDurationSeconds,
                    ["durationSeconds"] = UiValue.FiniteOrNull(elapsed),
                    ["completedStateCount"] = completedStateCount
                });
        }

        private static void CompleteAndBroadcast(
            UiOperationRecord record,
            object result,
            Dictionary<string, object> error)
        {
            record.Complete(result, error);
            BridgeServer.BroadcastNotification("ui/result", record.Envelope());

            if (record.Error != null &&
                record.Error.TryGetValue("code", out var code) &&
                (string.Equals(code?.ToString(), "internal_error", StringComparison.Ordinal) ||
                 string.Equals(code?.ToString(), "cleanup_failed", StringComparison.Ordinal)))
            {
                Debug.LogError($"[UCP] ui/{record.Operation} operation {record.OperationId} failed: " +
                               record.Error["message"]);
            }
        }

        private static void PruneCompleted(bool force)
        {
            var now = EditorApplication.timeSinceStartup;
            var expired = Operations
                .Where(pair => pair.Value.IsTerminal &&
                               (force || now - pair.Value.CompletedAt >= CompletedTtlSeconds))
                .Select(pair => pair.Key)
                .ToList();
            foreach (var id in expired)
                Operations.Remove(id);
        }

        private static void Shutdown()
        {
            var active = _active;
            _active = null;
            try
            {
                active?.Dispose();
            }
            catch
            {
                // Reload and quit must continue after best-effort cleanup.
            }
            finally
            {
                Queue.Clear();
                Operations.Clear();
            }
        }
    }

    internal sealed class UiOperationRecord
    {
        internal UiOperationRecord(
            string operationId,
            string operation,
            Dictionary<string, object> parameters)
        {
            OperationId = operationId;
            Operation = operation;
            Parameters = parameters != null
                ? new Dictionary<string, object>(parameters, StringComparer.Ordinal)
                : new Dictionary<string, object>(StringComparer.Ordinal);
            Status = "queued";
            StartedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            StartedAt = EditorApplication.timeSinceStartup;
        }

        internal string OperationId { get; }
        internal string Operation { get; }
        internal Dictionary<string, object> Parameters { get; }
        internal string Status { get; private set; }
        internal object Result { get; private set; }
        internal Dictionary<string, object> Error { get; private set; }
        internal string StartedAtUtc { get; }
        internal double StartedAt { get; }
        internal double CompletedAt { get; private set; }
        internal bool IsTerminal => Status == "completed" || Status == "failed";

        internal void MarkRunning()
        {
            Status = "running";
        }

        internal void Complete(object result, Dictionary<string, object> error)
        {
            Result = UiJsonSanitizer.Sanitize(result);
            Error = error == null
                ? null
                : (Dictionary<string, object>)UiJsonSanitizer.Sanitize(error);
            Status = Error == null ? "completed" : "failed";
            CompletedAt = EditorApplication.timeSinceStartup;
        }

        internal Dictionary<string, object> Envelope()
        {
            var envelope = new Dictionary<string, object>
            {
                ["operationId"] = OperationId,
                ["operation"] = Operation,
                ["status"] = Status,
                ["startedAtUtc"] = StartedAtUtc
            };
            if (Result != null)
                envelope["result"] = Result;
            if (Error != null)
                envelope["error"] = Error;
            return envelope;
        }
    }

    internal sealed class UiRenderOperation : IDisposable
    {
        private enum Phase
        {
            Initialize,
            OpenScenario,
            Settle,
            Terminal
        }

        private readonly UiOperationRecord _record;
        private readonly bool _capture;
        private readonly bool _inspect;
        private readonly bool _check;
        private readonly bool _failOnWarnings;
        private UiInspectOptions _inspectOptions;
        private readonly List<object> _stateResults = new List<object>();
        private Phase _phase;
        private List<UiResolvedScenario> _scenarios;
        private int _scenarioIndex;
        private UiResolvedScenario _scenario;
        private UiApplyReport _applyReport;
        private UiHostWindow _host;
        private UiCaptureSurface _captureSurface;
        private EditorWindow _previousFocusedWindow;
        private UiGeometrySample _geometry;
        private string _lastGeometryHash;
        private int _geometryStableFrames;
        private string _lastPixelHash;
        private int _pixelStableFrames;
        private UiCaptureSample _latestCapture;
        private double _scenarioStartedAt;
        private int _warmupFrames;
        private Dictionary<string, object> _lintResult;

        internal UiRenderOperation(UiOperationRecord record)
        {
            _record = record;
            _capture = record.Operation == "screenshot" || record.Operation == "check";
            _inspect = record.Operation == "inspect" || record.Operation == "check";
            _check = record.Operation == "check";
            _failOnWarnings = ReadBool(record.Parameters, "failOnWarnings", false);
        }

        internal string OperationId => _record.OperationId;
        internal object Result { get; private set; }
        internal Dictionary<string, object> Error { get; private set; }
        internal bool IsTerminal => _phase == Phase.Terminal;

        internal void Tick()
        {
            var now = EditorApplication.timeSinceStartup;
            if (UiOperationManager.HasExceededOperationDuration(_record.StartedAt, now))
            {
                throw UiOperationManager.CreateOperationTimeout(
                    _record.Operation,
                    _stateResults.Count,
                    now - _record.StartedAt);
            }

            switch (_phase)
            {
                case Phase.Initialize:
                    Initialize();
                    break;
                case Phase.OpenScenario:
                    OpenScenario();
                    break;
                case Phase.Settle:
                    Settle();
                    break;
            }
        }

        internal void Fail(Exception exception)
        {
            if (IsTerminal)
                return;

            Error = BuildError(exception);
            _phase = Phase.Terminal;
        }

        private void Initialize()
        {
            UiHostCapabilities.EnsureAvailable(_record.Operation);
            _inspectOptions = _inspect ? UiInspectOptions.Parse(_record.Parameters) : null;

            if (_check)
            {
                var lintParameters = new Dictionary<string, object>
                {
                    ["paths"] = new List<object> { ReadRequiredString(_record.Parameters, "target") },
                    ["failOnWarnings"] = _failOnWarnings
                };
                if (_record.Parameters.TryGetValue("maxDiagnostics", out var maxDiagnostics))
                    lintParameters["maxDiagnostics"] = maxDiagnostics;
                _lintResult = UiLintService.Run(lintParameters);
            }

            var allStates = ReadBool(_record.Parameters, "allStates", false);
            _scenarios = UiScenarioLoader.Resolve(_record.Parameters, allStates);
            if (_scenarios == null || _scenarios.Count == 0)
                throw new ArgumentException("No UI scenarios matched the request");

            _previousFocusedWindow = EditorWindow.focusedWindow;
            _scenarioIndex = 0;
            _phase = Phase.OpenScenario;
        }

        private void OpenScenario()
        {
            DisposeScenario();
            _scenario = _scenarios[_scenarioIndex];
            _host = UiHostWindow.Open(_scenario.Viewport.Width, _scenario.Viewport.Height);
            _scenario.Document.CloneTree(_host.ContentRoot);
            _applyReport = UiScenarioApplier.Apply(_host.ContentRoot, _scenario);
            if (_capture)
            {
                _captureSurface = new UiCaptureSurface(
                    _scenario.Viewport.Width,
                    _scenario.Viewport.Height);
            }

            _scenarioStartedAt = EditorApplication.timeSinceStartup;
            _lastGeometryHash = null;
            _geometryStableFrames = 0;
            _lastPixelHash = null;
            _pixelStableFrames = 0;
            _latestCapture = null;
            _warmupFrames = 0;
            _phase = Phase.Settle;
        }

        private void Settle()
        {
            if (_host == null)
                throw new InvalidOperationException("The transient UI host window was closed before the operation completed");

            var elapsed = EditorApplication.timeSinceStartup - _scenarioStartedAt;
            if (elapsed > _scenario.Settle.TimeoutSeconds)
            {
                throw new UiOperationException(
                    "timeout",
                    $"UI state '{_scenario.StateName}' did not settle within " +
                    $"{_scenario.Settle.TimeoutSeconds:F1}s",
                    new Dictionary<string, object>
                    {
                        ["scenario"] = _scenario.ToDictionary(),
                        ["geometry"] = _geometry.ToDictionary(_geometryStableFrames),
                        ["pixelStableFrames"] = _pixelStableFrames,
                        ["completedStateCount"] = _stateResults.Count
                    });
            }

            _host.Pump();
            _warmupFrames++;
            _geometry = UiGeometrySampler.Measure(_host.ContentRoot);
            var geometryValid = _warmupFrames >= 2 && _host.hasFocus && _geometry.IsValid;
            if (geometryValid && string.Equals(_lastGeometryHash, _geometry.Hash, StringComparison.Ordinal))
                _geometryStableFrames++;
            else
                _geometryStableFrames = geometryValid ? 1 : 0;
            _lastGeometryHash = _geometry.Hash;

            if (_geometryStableFrames < _scenario.Settle.StableFrames)
            {
                ResetPixelStability();
                return;
            }

            if (_capture)
            {
                _latestCapture = _captureSurface.Capture(_host);
                if (string.Equals(_lastPixelHash, _latestCapture.PixelHash, StringComparison.Ordinal))
                    _pixelStableFrames++;
                else
                    _pixelStableFrames = 1;
                _lastPixelHash = _latestCapture.PixelHash;

                if (_pixelStableFrames < _scenario.Settle.PixelStableFrames)
                    return;
            }

            CompleteScenario(elapsed);
        }

        private void CompleteScenario(double elapsed)
        {
            var metadata = UiScenarioApplier.GetCollectionMetadata(_host.ContentRoot);
            var stateResult = new Dictionary<string, object>
            {
                ["scenario"] = _scenario.ToDictionary(),
                ["apply"] = _applyReport.ToDictionary(),
                ["settle"] = BuildSettleResult(elapsed)
            };

            Dictionary<string, object> audit = null;
            if (_inspect)
            {
                stateResult["snapshot"] = UiVisualTreeInspector.Inspect(
                    _host.ContentRoot,
                    _inspectOptions,
                    metadata);
            }

            if (_check)
            {
                audit = UiAudit.Run(_host.ContentRoot, _applyReport, metadata);
                stateResult["audit"] = audit;
            }

            if (_capture)
            {
                var artifactPath = WriteCaptureArtifact(_latestCapture.Png);
                var capture = new Dictionary<string, object>
                {
                    ["artifactPath"] = artifactPath,
                    ["width"] = _latestCapture.Width,
                    ["height"] = _latestCapture.Height,
                    ["pixelHash"] = _latestCapture.PixelHash,
                    ["pixelStableFrames"] = _pixelStableFrames,
                    ["sampledDistinctColors"] = _latestCapture.SampledDistinctColors,
                    ["nonTransparentPixels"] = _latestCapture.NonTransparentPixels,
                    ["pixelsPerPoint"] = UiValue.FiniteOrNull(EditorGUIUtility.pixelsPerPoint),
                    ["compositorScale"] = UiValue.FiniteOrNull(
                        1f / Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint)),
                    ["graphicsDeviceType"] = SystemInfo.graphicsDeviceType.ToString()
                };
                stateResult["capture"] = capture;
                stateResult["artifactPath"] = artifactPath;
                stateResult["width"] = _latestCapture.Width;
                stateResult["height"] = _latestCapture.Height;
                stateResult["pixelHash"] = _latestCapture.PixelHash;
            }

            if (_check)
            {
                var auditPassed = ReadBool(audit, "passed", false);
                var warnings = audit != null && audit.TryGetValue("warningCount", out var warningValue)
                    ? Convert.ToInt32(warningValue)
                    : 0;
                stateResult["passed"] = auditPassed && (!_failOnWarnings || warnings == 0);
            }

            _stateResults.Add(stateResult);
            _scenarioIndex++;
            Result = BuildResult();
            DisposeScenario();
            if (_scenarioIndex < _scenarios.Count)
            {
                _phase = Phase.OpenScenario;
                return;
            }

            _phase = Phase.Terminal;
        }

        private Dictionary<string, object> BuildSettleResult(double elapsed)
        {
            var settle = _geometry.ToDictionary(_geometryStableFrames);
            settle["pixelStableFrames"] = _pixelStableFrames;
            settle["durationSeconds"] = UiValue.FiniteOrNull(elapsed);
            settle["focused"] = _host != null && _host.hasFocus;
            return settle;
        }

        private Dictionary<string, object> BuildResult()
        {
            var result = new Dictionary<string, object>
            {
                ["operation"] = _record.Operation,
                ["target"] = ReadRequiredString(_record.Parameters, "target"),
                ["stateCount"] = _stateResults.Count,
                ["states"] = _stateResults,
                ["capabilities"] = UiHostCapabilities.Describe()
            };

            if (_lintResult != null)
                result["lint"] = _lintResult;

            if (_stateResults.Count == 1 && _stateResults[0] is Dictionary<string, object> onlyState)
            {
                foreach (var pair in onlyState)
                    result[pair.Key] = pair.Value;
            }

            if (_check)
            {
                var lintPassed = _lintResult != null && ReadBool(_lintResult, "passed", false);
                var statesPassed = _stateResults.All(state =>
                    state is Dictionary<string, object> dictionary && ReadBool(dictionary, "passed", false));
                result["passed"] = lintPassed && statesPassed;
                var errorCount = ReadCount(_lintResult, "errorCount");
                var warningCount = ReadCount(_lintResult, "warningCount");
                foreach (var state in _stateResults.OfType<Dictionary<string, object>>())
                {
                    if (state.TryGetValue("audit", out var auditObject) &&
                        auditObject is Dictionary<string, object> audit)
                    {
                        errorCount += ReadCount(audit, "errorCount");
                        warningCount += ReadCount(audit, "warningCount");
                    }
                }
                result["errorCount"] = errorCount;
                result["warningCount"] = warningCount;
            }

            var elementCount = 0;
            foreach (var state in _stateResults.OfType<Dictionary<string, object>>())
            {
                if (state.TryGetValue("snapshot", out var snapshotObject) &&
                    snapshotObject is Dictionary<string, object> snapshot)
                    elementCount += ReadCount(snapshot, "returnedElementCount");
            }
            if (_inspect)
                result["elementCount"] = elementCount;

            return result;
        }

        private string WriteCaptureArtifact(byte[] png)
        {
            if (png == null || png.Length == 0)
                throw new InvalidOperationException("No PNG data was available after capture settled");

            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var outputDirectory = Path.Combine(projectRoot, "Library", "UCP", "UiCaptures");
            Directory.CreateDirectory(outputDirectory);
            var path = Path.Combine(outputDirectory, CaptureArtifactFileName(
                _scenario.TargetPath,
                _scenario.StateName,
                _scenarioIndex,
                OperationId));
            File.WriteAllBytes(path, png);
            return UiValue.NormalizePath(Path.GetFullPath(path));
        }

        internal static string CaptureArtifactFileName(
            string targetPath,
            string stateName,
            int scenarioIndex,
            string operationId)
        {
            var targetToken = UiValue.SafeFileName(Path.GetFileNameWithoutExtension(targetPath));
            var stateToken = UiValue.SafeFileName(stateName);
            return $"{targetToken}-{stateToken}-s{scenarioIndex:D4}-{operationId}.png";
        }

        private void ResetPixelStability()
        {
            _lastPixelHash = null;
            _pixelStableFrames = 0;
            _latestCapture = null;
        }

        private void DisposeScenario()
        {
            var captureSurface = _captureSurface;
            _captureSurface = null;
            var host = _host;
            _host = null;

            var error = UiCleanup.RunAll(
                () => captureSurface?.Dispose(),
                () => UiHostWindow.CloseAndDestroy(host));
            if (error != null)
            {
                throw new UiOperationException(
                    "cleanup_failed",
                    "The UI harness could not release all transient resources",
                    new Dictionary<string, object>
                    {
                        ["exceptionType"] = error.GetType().FullName,
                        ["message"] = error.Message
                    });
            }
        }

        public void Dispose()
        {
            var previousFocusedWindow = _previousFocusedWindow;
            _previousFocusedWindow = null;
            var error = UiCleanup.RunAll(
                DisposeScenario,
                () =>
                {
                    if (previousFocusedWindow == null)
                        return;
                    try
                    {
                        previousFocusedWindow.Focus();
                    }
                    catch
                    {
                        // The previously focused window may have been closed during the operation.
                    }
                });
            if (error != null)
                throw error;
        }

        internal static Dictionary<string, object> BuildError(Exception exception)
        {
            string code;
            object details = null;
            if (exception is UiOperationException operationException)
            {
                code = operationException.Code;
                details = operationException.Details;
            }
            else if (exception is UiScenarioException scenarioException)
            {
                code = scenarioException.Code;
                details = new Dictionary<string, object>
                {
                    ["location"] = scenarioException.Location
                };
            }
            else if (exception is ArgumentException)
            {
                code = "invalid_params";
            }
            else
            {
                code = "internal_error";
                details = new Dictionary<string, object>
                {
                    ["exceptionType"] = exception.GetType().FullName
                };
            }

            var error = new Dictionary<string, object>
            {
                ["code"] = code,
                ["message"] = exception.Message
            };
            if (details != null)
                error["details"] = details;
            return (Dictionary<string, object>)UiJsonSanitizer.Sanitize(error);
        }

        private static bool ReadBool(
            Dictionary<string, object> parameters,
            string key,
            bool defaultValue)
        {
            if (parameters == null || !parameters.TryGetValue(key, out var value) || value == null)
                return defaultValue;
            if (value is bool boolean)
                return boolean;
            if (bool.TryParse(value.ToString(), out var parsed))
                return parsed;
            throw new ArgumentException($"'{key}' must be a boolean");
        }

        private static string ReadRequiredString(Dictionary<string, object> parameters, string key)
        {
            if (parameters == null || !parameters.TryGetValue(key, out var value) ||
                string.IsNullOrWhiteSpace(value?.ToString()))
                throw new ArgumentException($"Missing '{key}' parameter");
            return value.ToString();
        }

        private static int ReadCount(Dictionary<string, object> values, string key)
        {
            if (values == null || !values.TryGetValue(key, out var value) || value == null)
                return 0;
            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
                return 0;
            }
        }
    }

    internal static class UiJsonSanitizer
    {
        internal static object Sanitize(object value)
        {
            if (value == null || value is string || value is bool || value is int || value is long)
                return value;
            if (value is char character)
                return character.ToString();
            if (value is byte || value is sbyte || value is short || value is ushort)
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            if (value is uint unsignedInteger)
                return (long)unsignedInteger;
            if (value is ulong unsignedLong)
                return unsignedLong <= long.MaxValue
                    ? (object)(long)unsignedLong
                    : unsignedLong.ToString(CultureInfo.InvariantCulture);
            if (value is decimal decimalValue)
                return decimalValue.ToString(CultureInfo.InvariantCulture);
            if (value is IntPtr pointer)
                return pointer.ToInt64();
            if (value is UIntPtr unsignedPointer)
            {
                var numericValue = unsignedPointer.ToUInt64();
                return numericValue <= long.MaxValue
                    ? (object)(long)numericValue
                    : numericValue.ToString(CultureInfo.InvariantCulture);
            }
            if (value is float floatValue)
                return UiValue.FiniteOrNull(floatValue);
            if (value is double doubleValue)
                return UiValue.FiniteOrNull(doubleValue);
            if (value is Enum enumValue)
                return enumValue.ToString();
            if (value is IDictionary dictionary)
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (DictionaryEntry pair in dictionary)
                    result[pair.Key?.ToString() ?? "null"] = Sanitize(pair.Value);
                return result;
            }
            if (value is IEnumerable enumerable)
            {
                var result = new List<object>();
                foreach (var item in enumerable)
                    result.Add(Sanitize(item));
                return result;
            }
            return value.ToString();
        }
    }
}
#endif
