using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using Unity.Profiling;
using UnityEditor.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace UCP.Bridge
{
    public static class ProfilerController
    {
        private const int DefaultFrameListLimit = 20;
        private const int DefaultSummaryFrameWindow = 120;
        private const int DefaultJsonExportFrameWindow = 120;
        private const int MaxThreadProbeCount = 128;
        private const long MinimumProfilerMemoryBytes = 16L * 1024L * 1024L;
        private const long DefaultProfilerMemoryBytes = 128L * 1024L * 1024L;
        private const long HeavyProfilerMemoryBytes = 64L * 1024L * 1024L;
        private const long AbsoluteProfilerMemoryBytes = 256L * 1024L * 1024L;
        private const long AbsoluteHeavyProfilerMemoryBytes = 128L * 1024L * 1024L;

        private static readonly Type ProfilerDriverType = Type.GetType("UnityEditorInternal.ProfilerDriver, UnityEditor");
        private static readonly MethodInfo GetRawFrameDataViewMethod = FindDriverMethod(
            "GetRawFrameDataView",
            typeof(int),
            typeof(int));
        private static readonly MethodInfo GetHierarchyFrameDataViewMethod = FindDriverMethod(
            "GetHierarchyFrameDataView",
            typeof(int),
            typeof(int),
            typeof(HierarchyFrameDataView.ViewModes),
            typeof(int),
            typeof(bool));
        private static readonly MethodInfo ClearAllFramesMethod = FindDriverMethod("ClearAllFrames");
        private static readonly PropertyInfo FirstFrameIndexProperty = FindDriverProperty("firstFrameIndex");
        private static readonly PropertyInfo LastFrameIndexProperty = FindDriverProperty("lastFrameIndex");
        private static readonly PropertyInfo DriverEnabledProperty = FindDriverProperty("enabled");
        private static readonly PropertyInfo ProfileEditorProperty = FindDriverProperty("profileEditor");
        private static readonly PropertyInfo DeepProfilingProperty = FindDriverProperty("deepProfiling");
        private static readonly MethodInfo GetCategoriesCountMethod = typeof(Profiler).GetMethod(
            "GetCategoriesCount",
            BindingFlags.Public | BindingFlags.Static,
            null,
            Type.EmptyTypes,
            null);
        private static readonly MethodInfo GetAllCategoriesMethod = typeof(Profiler).GetMethod(
            "GetAllCategories",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(ProfilerCategory[]) },
            null);
        private static readonly MethodInfo IsCategoryEnabledMethod = typeof(Profiler).GetMethod(
            "IsCategoryEnabled",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(ProfilerCategory) },
            null);
        private static readonly MethodInfo SetCategoryEnabledMethod = typeof(Profiler).GetMethod(
            "SetCategoryEnabled",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(ProfilerCategory), typeof(bool) },
            null);

        private static readonly ProfilerSessionState SessionState = new();

        public static void Register(CommandRouter router)
        {
            router.Register("profiler/status", HandleStatus);
            router.Register("profiler/config/get", HandleConfigGet);
            router.Register("profiler/config/set", HandleConfigSet);
            router.Register("profiler/session/start", HandleSessionStart);
            router.Register("profiler/session/stop", HandleSessionStop);
            router.Register("profiler/session/clear", HandleSessionClear);
            router.Register("profiler/capture/save", HandleCaptureSave);
            router.Register("profiler/capture/load", HandleCaptureLoad);
            router.Register("profiler/frames/list", HandleFramesList);
            router.Register("profiler/frames/show", HandleFrameShow);
            router.Register("profiler/hierarchy", HandleHierarchy);
            router.Register("profiler/timeline", HandleTimeline);
            router.Register("profiler/callstacks", HandleCallstacks);
            router.Register("profiler/summary", HandleSummary);
        }

        private static object HandleStatus(string paramsJson)
        {
            return BuildStatusResponse(new List<string>());
        }

        private static object HandleConfigGet(string paramsJson)
        {
            return BuildStatusResponse(new List<string>());
        }

        private static object HandleConfigSet(string paramsJson)
        {
            var parameters = ParseParams(paramsJson);
            var warnings = ApplyConfiguration(parameters, Profiler.enableBinaryLog, false);
            return BuildStatusResponse(warnings);
        }

        private static object HandleSessionStart(string paramsJson)
        {
            var parameters = ParseParams(paramsJson);
            var warnings = new List<string>();

            if (!SessionState.Active)
                CaptureSessionDefaults();

            var clearFirst = GetNullableBool(parameters, "clearFirst");
            if (clearFirst.GetValueOrDefault() || (!clearFirst.HasValue && HasBufferedFrames()))
            {
                warnings.AddRange(ClearBufferedFrames());
                if (!clearFirst.HasValue)
                    warnings.Add("Cleared existing buffered profiler frames before starting the new session to keep memory usage bounded.");
            }

            warnings.AddRange(ApplyConfiguration(parameters, false, true));

            var requestedMode = GetString(parameters, "mode");
            if (!string.IsNullOrEmpty(requestedMode))
            {
                SessionState.RequestedMode = NormalizeMode(requestedMode);
                warnings.AddRange(ApplyRequestedMode(SessionState.RequestedMode));
            }
            else if (string.IsNullOrEmpty(SessionState.RequestedMode))
            {
                SessionState.RequestedMode = GetProfileEditor() ? "edit" : "play";
            }

            SessionState.Active = true;
            SessionState.SessionId = Guid.NewGuid().ToString("N");
            SessionState.StartedAtUtc = DateTime.UtcNow.ToString("o");
            SessionState.StoppedAtUtc = null;
            SessionState.LastCapturePath = NormalizePath(Profiler.logFile);
            SessionState.Warnings = warnings.ToList();

            SetDriverEnabled(true);
            Profiler.enabled = true;

            return BuildStatusResponse(warnings, "started");
        }

        private static object HandleSessionStop(string paramsJson)
        {
            var warnings = new List<string>();
            SetDriverEnabled(false);
            Profiler.enabled = false;
            warnings.AddRange(RestoreSessionDefaults());

            SessionState.Active = false;
            SessionState.SessionId = null;
            SessionState.StoppedAtUtc = DateTime.UtcNow.ToString("o");
            SessionState.LastCapturePath = NormalizePath(Profiler.logFile);
            if (SessionState.RequestedBinaryLog && !File.Exists(SessionState.LastCapturePath ?? string.Empty))
            {
                warnings.Add(
                    "Unity Editor does not emit live binary profiler logs at runtime. Use `ucp profiler capture save --output <file>.json` for a structured snapshot, or export raw/data captures manually from the Profiler window.");
            }
            SessionState.RequestedDeepProfile = false;
            SessionState.RequestedAllocationCallstacks = false;
            SessionState.RequestedBinaryLog = false;
            SessionState.Warnings = warnings.ToList();

            var response = BuildStatusResponse(warnings, "stopped");
            var result = response as Dictionary<string, object>;
            result["capture"] = BuildCaptureData(SessionState.LastCapturePath, "current");
            return result;
        }

        private static object HandleSessionClear(string paramsJson)
        {
            var warnings = ClearBufferedFrames();
            var result = new Dictionary<string, object>
            {
                ["status"] = "cleared",
                ["frames"] = BuildFrameRangeData(),
                ["warnings"] = warnings
            };
            return result;
        }

        private static object HandleCaptureSave(string paramsJson)
        {
            var parameters = ParseParams(paramsJson);
            var output = RequireString(parameters, "output");
            var normalizedOutput = NormalizeAbsolutePath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(normalizedOutput));

            var source = GetExistingCaptureSource();
            if (string.IsNullOrEmpty(source))
            {
                if (string.Equals(Path.GetExtension(normalizedOutput), ".json", StringComparison.OrdinalIgnoreCase))
                    return ExportJsonCapture(normalizedOutput);

                throw new ArgumentException(
                    "No raw/data profiler capture file is available. In the Unity Editor, use a .json output path for a structured snapshot export.");
            }

            if (!PathsEqual(source, normalizedOutput))
                File.Copy(source, normalizedOutput, true);

            return new Dictionary<string, object>
            {
                ["capture"] = BuildCaptureData(normalizedOutput, "saved"),
                ["warnings"] = new List<object>()
            };
        }

        private static object HandleCaptureLoad(string paramsJson)
        {
            var parameters = ParseParams(paramsJson);
            var input = NormalizeAbsolutePath(RequireString(parameters, "input"));
            if (!File.Exists(input))
                throw new ArgumentException($"Profiler capture not found: {input}");

            Profiler.AddFramesFromFile(input);
            SessionState.LoadedCapturePath = input;
            SessionState.LastCapturePath = input;

            return new Dictionary<string, object>
            {
                ["capture"] = BuildCaptureData(input, "loaded"),
                ["frames"] = BuildFrameRangeData(),
                ["warnings"] = new List<object>()
            };
        }

        private static object HandleFramesList(string paramsJson)
        {
            var parameters = ParseParams(paramsJson);
            var limit = Math.Max(1, GetInt(parameters, "limit", DefaultFrameListLimit));
            if (!TryGetFrameRange(out var firstFrame, out var lastFrame))
            {
                return new Dictionary<string, object>
                {
                    ["frameRange"] = BuildFrameRangeData(),
                    ["frames"] = new List<object>(),
                    ["warnings"] = new List<object>()
                };
            }

            var requestedFirst = GetNullableInt(parameters, "firstFrame") ?? firstFrame;
            var requestedLast = GetNullableInt(parameters, "lastFrame") ?? lastFrame;
            requestedFirst = Math.Max(firstFrame, requestedFirst);
            requestedLast = Math.Min(lastFrame, requestedLast);

            if (requestedFirst > requestedLast)
                throw new ArgumentException("Requested frame range is empty");

            var frameIndexes = Enumerable.Range(requestedFirst, requestedLast - requestedFirst + 1)
                .Select(index => requestedLast - (index - requestedFirst))
                .Take(limit)
                .OrderBy(index => index)
                .ToList();

            var frames = new List<object>();
            foreach (var frameIndex in frameIndexes)
                frames.Add(BuildFrameSummary(frameIndex, false));

            return new Dictionary<string, object>
            {
                ["frameRange"] = new Dictionary<string, object>
                {
                    ["firstFrame"] = requestedFirst,
                    ["lastFrame"] = requestedLast,
                    ["returned"] = frames.Count
                },
                ["frames"] = frames,
                ["warnings"] = new List<object>()
            };
        }

        private static object HandleFrameShow(string paramsJson)
        {
            var parameters = ParseParams(paramsJson);
            var frameIndex = ResolveFrame(parameters);
            var includeThreads = GetBool(parameters, "includeThreads", false);

            return new Dictionary<string, object>
            {
                ["frame"] = BuildFrameSummary(frameIndex, includeThreads),
                ["warnings"] = new List<object>()
            };
        }

        private static object HandleHierarchy(string paramsJson)
        {
            var parameters = ParseParams(paramsJson);
            var frameIndex = ResolveFrame(parameters);
            var threadIndex = GetInt(parameters, "thread", 0);
            var limit = Math.Max(1, GetInt(parameters, "limit", 50));
            var sort = GetString(parameters, "sort") ?? "total-time";
            var maxDepth = GetNullableInt(parameters, "maxDepth");

            using (var view = GetHierarchyFrameDataView(frameIndex, threadIndex))
            {
                if (view == null || !view.valid)
                    throw new ArgumentException($"Hierarchy profiler data is unavailable for frame {frameIndex}, thread {threadIndex}");

                var items = CollectHierarchyItems(view, maxDepth);
                items = SortHierarchyItems(items, sort);

                var truncated = items.Count > limit;
                if (truncated)
                    items = items.Take(limit).ToList();

                return new Dictionary<string, object>
                {
                    ["frame"] = frameIndex,
                    ["thread"] = threadIndex,
                    ["sort"] = sort,
                    ["count"] = items.Count,
                    ["truncated"] = truncated,
                    ["items"] = items.Select(item => (object)item.ToDictionary()).ToList(),
                    ["warnings"] = new List<object>()
                };
            }
        }

        private static object HandleTimeline(string paramsJson)
        {
            var parameters = ParseParams(paramsJson);
            var frameIndex = ResolveFrame(parameters);
            var threadIndex = GetInt(parameters, "thread", 0);
            var limit = Math.Max(1, GetInt(parameters, "limit", 200));
            var maxDepth = GetNullableInt(parameters, "maxDepth");
            var includeMetadata = GetBool(parameters, "includeMetadata", false);

            using (var view = GetRawFrameDataView(frameIndex, threadIndex))
            {
                if (view == null || !view.valid)
                    throw new ArgumentException($"Raw profiler data is unavailable for frame {frameIndex}, thread {threadIndex}");

                var samples = CollectTimelineSamples(view, maxDepth, includeMetadata);
                var truncated = samples.Count > limit;
                if (truncated)
                    samples = samples.Take(limit).ToList();

                return new Dictionary<string, object>
                {
                    ["frame"] = frameIndex,
                    ["thread"] = threadIndex,
                    ["count"] = samples.Count,
                    ["truncated"] = truncated,
                    ["samples"] = samples.Cast<object>().ToList(),
                    ["warnings"] = new List<object>()
                };
            }
        }

        private static object HandleCallstacks(string paramsJson)
        {
            var parameters = ParseParams(paramsJson);
            var frameIndex = ResolveFrame(parameters);
            var threadIndex = GetInt(parameters, "thread", 0);
            var kind = (GetString(parameters, "kind") ?? "raw").ToLowerInvariant();
            var resolveMethods = GetBool(parameters, "resolveMethods", false);

            if (kind == "raw")
            {
                var sampleIndex = GetNullableInt(parameters, "sample");
                if (!sampleIndex.HasValue)
                    throw new ArgumentException("Missing 'sample' for raw callstack lookup");

                using (var view = GetRawFrameDataView(frameIndex, threadIndex))
                {
                    if (view == null || !view.valid)
                        throw new ArgumentException($"Raw profiler data is unavailable for frame {frameIndex}, thread {threadIndex}");

                    var callstack = new List<ulong>();
                    view.GetSampleCallstack(sampleIndex.Value, callstack);
                    return BuildCallstackResponse(kind, frameIndex, threadIndex, sampleIndex.Value, null, callstack, resolveMethods, view);
                }
            }

            if (kind == "hierarchy")
            {
                var itemId = GetNullableInt(parameters, "item");
                if (!itemId.HasValue)
                    throw new ArgumentException("Missing 'item' for hierarchy callstack lookup");

                using (var view = GetHierarchyFrameDataView(frameIndex, threadIndex))
                {
                    if (view == null || !view.valid)
                        throw new ArgumentException($"Hierarchy profiler data is unavailable for frame {frameIndex}, thread {threadIndex}");

                    var callstack = new List<ulong>();
                    view.GetItemCallstack(itemId.Value, callstack);
                    return BuildCallstackResponse(kind, frameIndex, threadIndex, null, itemId.Value, callstack, resolveMethods, view);
                }
            }

            throw new ArgumentException($"Unsupported callstack kind: {kind}");
        }

        private static object HandleSummary(string paramsJson)
        {
            var parameters = ParseParams(paramsJson);
            var limit = Math.Max(1, GetInt(parameters, "limit", 10));
            var threadIndex = GetInt(parameters, "thread", 0);

            if (!TryGetFrameRange(out var availableFirst, out var availableLast))
            {
                return new Dictionary<string, object>
                {
                    ["summary"] = new Dictionary<string, object>
                    {
                        ["frameRange"] = BuildFrameRangeData(),
                        ["stats"] = new Dictionary<string, object>(),
                        ["topMarkers"] = new List<object>()
                    },
                    ["warnings"] = new List<object>()
                };
            }

            var requestedLastFrame = GetNullableInt(parameters, "lastFrame");
            var lastFrame = Math.Min(availableLast, requestedLastFrame ?? availableLast);
            var requestedFirstFrame = GetNullableInt(parameters, "firstFrame");
            var firstFrame = Math.Max(
                availableFirst,
                requestedFirstFrame ?? Math.Max(availableFirst, lastFrame - DefaultSummaryFrameWindow + 1));
            if (firstFrame > lastFrame)
                throw new ArgumentException("Requested frame range is empty");

            return new Dictionary<string, object>
            {
                ["summary"] = BuildSummaryData(limit, threadIndex, firstFrame, lastFrame),
                ["warnings"] = new List<object>()
            };
        }

        private static object BuildStatusResponse(List<string> warnings, string status = null)
        {
            var response = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(status))
                response["status"] = status;

            response["session"] = BuildSessionData();
            response["config"] = BuildConfigData();
            response["capabilities"] = BuildCapabilitiesData();
            response["frames"] = BuildFrameRangeData();
            response["editorState"] = BuildEditorStateData();
            response["warnings"] = warnings.Cast<object>().ToList();
            return response;
        }

        private static Dictionary<string, object> BuildSessionData()
        {
            return new Dictionary<string, object>
            {
                ["active"] = SessionState.Active || Profiler.enabled,
                ["driverEnabled"] = GetDriverEnabled(),
                ["sessionId"] = SessionState.SessionId ?? string.Empty,
                ["requestedMode"] = string.IsNullOrEmpty(SessionState.RequestedMode)
                    ? (GetProfileEditor() ? "edit" : "play")
                    : SessionState.RequestedMode,
                ["effectiveMode"] = GetProfileEditor() ? "edit" : "play",
                ["deepProfileRequested"] = SessionState.RequestedDeepProfile,
                ["deepProfileEffective"] = GetDeepProfiling(),
                ["allocationCallstacksRequested"] = SessionState.RequestedAllocationCallstacks,
                ["allocationCallstacksEffective"] = Profiler.enableAllocationCallstacks,
                ["binaryLog"] = Profiler.enableBinaryLog,
                ["outputPath"] = NormalizePath(Profiler.logFile) ?? string.Empty,
                ["loadedCapturePath"] = SessionState.LoadedCapturePath ?? string.Empty,
                ["lastCapturePath"] = SessionState.LastCapturePath ?? string.Empty,
                ["startedAtUtc"] = SessionState.StartedAtUtc ?? string.Empty,
                ["stoppedAtUtc"] = SessionState.StoppedAtUtc ?? string.Empty
            };
        }

        private static Dictionary<string, object> BuildConfigData()
        {
            var availableCategories = GetAvailableCategories();
            var enabledCategories = availableCategories
                .Where(IsCategoryEnabled)
                .Select(category => category.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Cast<object>()
                .ToList();

            return new Dictionary<string, object>
            {
                ["mode"] = GetProfileEditor() ? "edit" : "play",
                ["profileEditor"] = GetProfileEditor(),
                ["driverEnabled"] = GetDriverEnabled(),
                ["deepProfile"] = GetDeepProfiling(),
                ["allocationCallstacks"] = Profiler.enableAllocationCallstacks,
                ["binaryLog"] = Profiler.enableBinaryLog,
                ["outputPath"] = NormalizePath(Profiler.logFile) ?? string.Empty,
                ["maxUsedMemory"] = Convert.ToInt64(Profiler.maxUsedMemory),
                ["availableCategories"] = availableCategories
                    .Select(category => category.Name)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .Cast<object>()
                    .ToList(),
                ["enabledCategories"] = enabledCategories
            };
        }

        private static Dictionary<string, object> BuildCapabilitiesData()
        {
            return new Dictionary<string, object>
            {
                ["status"] = true,
                ["config"] = true,
                ["sessionControl"] = true,
                ["driverRecordingControl"] = DriverEnabledProperty != null,
                ["binaryCapture"] = false,
                ["captureSaveFromEditor"] = false,
                ["captureLoad"] = true,
                ["categoryControl"] = true,
                ["rawFrameAccess"] = GetRawFrameDataViewMethod != null,
                ["hierarchyFrameAccess"] = GetHierarchyFrameDataViewMethod != null,
                ["clearFrames"] = ClearAllFramesMethod != null,
                ["editModeTargeting"] = ProfileEditorProperty != null,
                ["deepProfilingAutomation"] = DeepProfilingProperty != null,
                ["callstackResolution"] = GetRawFrameDataViewMethod != null,
                ["moduleCategorySelection"] = true,
                ["moduleLayoutAutomation"] = false,
                ["structuredSnapshotExport"] = true
            };
        }

        private static Dictionary<string, object> BuildFrameRangeData()
        {
            var hasFrames = TryGetFrameRange(out var firstFrame, out var lastFrame);
            return new Dictionary<string, object>
            {
                ["count"] = hasFrames ? (lastFrame - firstFrame + 1) : 0,
                ["firstFrame"] = hasFrames ? firstFrame : -1,
                ["lastFrame"] = hasFrames ? lastFrame : -1
            };
        }

        private static Dictionary<string, object> BuildEditorStateData()
        {
            return new Dictionary<string, object>
            {
                ["playing"] = EditorApplication.isPlaying,
                ["paused"] = EditorApplication.isPaused,
                ["willChange"] = EditorApplication.isPlayingOrWillChangePlaymode,
                ["compiling"] = EditorApplication.isCompiling
            };
        }

        private static Dictionary<string, object> BuildCaptureData(string path, string status)
        {
            var normalizedPath = NormalizePath(path) ?? string.Empty;
            var exists = !string.IsNullOrEmpty(path) && File.Exists(path);
            var extension = Path.GetExtension(normalizedPath)?.ToLowerInvariant() ?? string.Empty;

            return new Dictionary<string, object>
            {
                ["status"] = status,
                ["path"] = normalizedPath,
                ["exists"] = exists,
                ["sizeBytes"] = exists ? new FileInfo(path).Length : 0L,
                ["kind"] = extension.TrimStart('.'),
                ["frames"] = BuildFrameRangeData()
            };
        }

        private static Dictionary<string, object> BuildFrameSummary(int frameIndex, bool includeThreads)
        {
            using (var mainThread = GetRawFrameDataView(frameIndex, 0))
            {
                if (mainThread == null || !mainThread.valid)
                    throw new ArgumentException($"Profiler frame {frameIndex} is unavailable");

                var result = new Dictionary<string, object>
                {
                    ["frame"] = frameIndex,
                    ["cpuMs"] = mainThread.frameTimeMs,
                    ["gpuMs"] = mainThread.frameGpuTimeMs,
                    ["fps"] = mainThread.frameFps,
                    ["threadCount"] = CountThreads(frameIndex),
                    ["gcAllocBytes"] = GetGcAllocBytes(frameIndex)
                };

                if (includeThreads)
                {
                    var threads = new List<object>();
                    for (var threadIndex = 0; threadIndex < MaxThreadProbeCount; threadIndex++)
                    {
                        using (var threadView = GetRawFrameDataView(frameIndex, threadIndex))
                        {
                            if (threadView == null || !threadView.valid)
                                break;

                            threads.Add(new Dictionary<string, object>
                            {
                                ["thread"] = threadIndex,
                                ["threadId"] = threadView.threadId,
                                ["threadName"] = threadView.threadName ?? string.Empty,
                                ["threadGroup"] = threadView.threadGroupName ?? string.Empty,
                                ["sampleCount"] = threadView.sampleCount,
                                ["maxDepth"] = threadView.maxDepth
                            });
                        }
                    }

                    result["threads"] = threads;
                }

                return result;
            }
        }

        private static List<HierarchyItemRecord> CollectHierarchyItems(HierarchyFrameDataView view, int? maxDepth)
        {
            var items = new List<HierarchyItemRecord>();
            var buffer = new List<int>();
            CollectHierarchyChildren(view, view.GetRootItemID(), buffer, items, maxDepth);
            return items;
        }

        private static void CollectHierarchyChildren(
            HierarchyFrameDataView view,
            int parentId,
            List<int> buffer,
            List<HierarchyItemRecord> results,
            int? maxDepth)
        {
            buffer.Clear();
            view.GetItemChildren(parentId, buffer);
            foreach (var childId in buffer)
            {
                var depth = view.GetItemDepth(childId);
                if (maxDepth.HasValue && depth > maxDepth.Value)
                    continue;

                var childBuffer = new List<int>();
                view.GetItemChildren(childId, childBuffer);

                results.Add(new HierarchyItemRecord
                {
                    ItemId = childId,
                    Name = view.GetItemName(childId),
                    Path = view.GetItemPath(childId),
                    Depth = depth,
                    TotalMs = view.GetItemColumnDataAsDouble(childId, HierarchyFrameDataView.columnTotalTime),
                    SelfMs = view.GetItemColumnDataAsDouble(childId, HierarchyFrameDataView.columnSelfTime),
                    Calls = Convert.ToInt64(view.GetItemColumnDataAsDouble(childId, HierarchyFrameDataView.columnCalls)),
                    GcMemory = view.GetItemColumnDataAsDouble(childId, HierarchyFrameDataView.columnGcMemory),
                    ChildCount = childBuffer.Count
                });

                CollectHierarchyChildren(view, childId, childBuffer, results, maxDepth);
            }
        }

        private static List<HierarchyItemRecord> SortHierarchyItems(List<HierarchyItemRecord> items, string sort)
        {
            switch ((sort ?? string.Empty).ToLowerInvariant())
            {
                case "self-time":
                    return items.OrderByDescending(item => item.SelfMs).ToList();
                case "calls":
                    return items.OrderByDescending(item => item.Calls).ToList();
                case "gc-memory":
                    return items.OrderByDescending(item => item.GcMemory).ToList();
                case "name":
                    return items.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
                default:
                    return items.OrderByDescending(item => item.TotalMs).ToList();
            }
        }

        private static List<Dictionary<string, object>> CollectTimelineSamples(
            RawFrameDataView view,
            int? maxDepth,
            bool includeMetadata)
        {
            var samples = new List<Dictionary<string, object>>();
            var stack = new Stack<double>();

            for (var sampleIndex = 0; sampleIndex < view.sampleCount; sampleIndex++)
            {
                var startMs = view.GetSampleStartTimeMs(sampleIndex);
                var durationMs = view.GetSampleTimeMs(sampleIndex);
                while (stack.Count > 0 && startMs >= stack.Peek())
                    stack.Pop();

                var depth = stack.Count;
                var endMs = startMs + durationMs;
                stack.Push(endMs);

                if (maxDepth.HasValue && depth > maxDepth.Value)
                    continue;

                var categoryIndex = view.GetSampleCategoryIndex(sampleIndex);
                var entry = new Dictionary<string, object>
                {
                    ["sample"] = sampleIndex,
                    ["name"] = view.GetSampleName(sampleIndex),
                    ["category"] = ResolveCategoryName(view, categoryIndex),
                    ["startMs"] = startMs,
                    ["durationMs"] = durationMs,
                    ["depth"] = depth,
                    ["childCount"] = view.GetSampleChildrenCount(sampleIndex),
                    ["metadataCount"] = view.GetSampleMetadataCount(sampleIndex)
                };

                if (includeMetadata)
                    entry["metadata"] = ReadSampleMetadata(view, sampleIndex);

                samples.Add(entry);
            }

            return samples;
        }

        private static object BuildCallstackResponse(
            string kind,
            int frameIndex,
            int threadIndex,
            int? sampleIndex,
            int? itemId,
            List<ulong> callstack,
            bool resolveMethods,
            FrameDataView view)
        {
            var frames = new List<object>();
            foreach (var address in callstack)
            {
                var entry = new Dictionary<string, object>
                {
                    ["address"] = $"0x{address:X}"
                };

                if (resolveMethods)
                    entry["method"] = ResolveMethodInfo(view, address);

                frames.Add(entry);
            }

            return new Dictionary<string, object>
            {
                ["callstack"] = new Dictionary<string, object>
                {
                    ["kind"] = kind,
                    ["frame"] = frameIndex,
                    ["thread"] = threadIndex,
                    ["sample"] = sampleIndex ?? -1,
                    ["item"] = itemId ?? -1,
                    ["count"] = frames.Count,
                    ["frames"] = frames
                },
                ["warnings"] = new List<object>()
            };
        }

        private static Dictionary<string, object> ResolveMethodInfo(FrameDataView view, ulong address)
        {
            try
            {
                object method = view.ResolveMethodInfo(address);
                if (method == null)
                    return new Dictionary<string, object>();

                var methodType = method.GetType();
                return new Dictionary<string, object>
                {
                    ["name"] = ReadMember(methodType, method, "methodName"),
                    ["sourceFile"] = ReadMember(methodType, method, "sourceFileName"),
                    ["sourceLine"] = ReadMember(methodType, method, "sourceFileLine")
                };
            }
            catch
            {
                return new Dictionary<string, object>();
            }
        }

        private static object ReadMember(Type type, object instance, string name)
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property != null)
                return property.GetValue(instance) ?? string.Empty;

            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
            return field != null ? field.GetValue(instance) ?? string.Empty : string.Empty;
        }

        private static Dictionary<string, object> ReadSampleMetadata(RawFrameDataView view, int sampleIndex)
        {
            var metadata = new Dictionary<string, object>();
            var count = view.GetSampleMetadataCount(sampleIndex);
            for (var metadataIndex = 0; metadataIndex < count; metadataIndex++)
            {
                try
                {
                    metadata[metadataIndex.ToString()] = view.GetSampleMetadataAsString(sampleIndex, metadataIndex);
                }
                catch
                {
                    try
                    {
                        metadata[metadataIndex.ToString()] = view.GetSampleMetadataAsLong(sampleIndex, metadataIndex);
                    }
                    catch
                    {
                        metadata[metadataIndex.ToString()] = string.Empty;
                    }
                }
            }

            return metadata;
        }

        private static string ResolveCategoryName(FrameDataView view, int categoryIndex)
        {
            try
            {
                object categoryInfo = view.GetCategoryInfo((ushort)categoryIndex);
                if (categoryInfo == null)
                    return categoryIndex.ToString();

                var type = categoryInfo.GetType();
                var property = type.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)
                    ?? type.GetProperty("name", BindingFlags.Public | BindingFlags.Instance);
                if (property != null)
                    return property.GetValue(categoryInfo)?.ToString() ?? categoryIndex.ToString();
            }
            catch
            {
            }

            return categoryIndex.ToString();
        }

        private static int CountThreads(int frameIndex)
        {
            var count = 0;
            for (var threadIndex = 0; threadIndex < MaxThreadProbeCount; threadIndex++)
            {
                using (var view = GetRawFrameDataView(frameIndex, threadIndex))
                {
                    if (view == null || !view.valid)
                        break;
                    count++;
                }
            }

            return count;
        }

        private static long GetGcAllocBytes(int frameIndex)
        {
            var total = 0L;
            var gcMarkerId = FrameDataView.invalidMarkerId;

            for (var threadIndex = 0; threadIndex < MaxThreadProbeCount; threadIndex++)
            {
                using (var view = GetRawFrameDataView(frameIndex, threadIndex))
                {
                    if (view == null || !view.valid)
                        break;

                    if (gcMarkerId == FrameDataView.invalidMarkerId)
                    {
                        gcMarkerId = view.GetMarkerId("GC.Alloc");
                        if (gcMarkerId == FrameDataView.invalidMarkerId)
                            continue;
                    }

                    for (var sampleIndex = 0; sampleIndex < view.sampleCount; sampleIndex++)
                    {
                        if (view.GetSampleMarkerId(sampleIndex) != gcMarkerId)
                            continue;

                        try
                        {
                            total += view.GetSampleMetadataAsLong(sampleIndex, 0);
                        }
                        catch
                        {
                        }
                    }
                }
            }

            return total;
        }

        private static List<string> ApplyConfiguration(
            Dictionary<string, object> parameters,
            bool defaultBinaryLog,
            bool defaultOutputIfNeeded)
        {
            var warnings = new List<string>();

            var requestedMode = GetString(parameters, "mode");
            if (!string.IsNullOrEmpty(requestedMode))
            {
                requestedMode = NormalizeMode(requestedMode);
                SessionState.RequestedMode = requestedMode;
                warnings.AddRange(ApplyRequestedMode(requestedMode));
            }

            var requestedDeepProfile = GetNullableBool(parameters, "deepProfile");
            if (requestedDeepProfile.HasValue)
            {
                SessionState.RequestedDeepProfile = requestedDeepProfile.Value;
                if (!SetDeepProfiling(requestedDeepProfile.Value))
                    warnings.Add("Deep profiling automation is unavailable on this Unity version; the requested value was recorded but not applied.");
            }

            var allocationCallstacks = GetNullableBool(parameters, "allocationCallstacks");
            if (allocationCallstacks.HasValue)
            {
                Profiler.enableAllocationCallstacks = allocationCallstacks.Value;
                SessionState.RequestedAllocationCallstacks = allocationCallstacks.Value;
                if (allocationCallstacks.Value)
                    warnings.Add("Allocation callstacks add noticeable profiler overhead and can trigger frame drops or heavy editor memory pressure during longer captures.");
            }

            var binaryLog = GetNullableBool(parameters, "binaryLog") ?? defaultBinaryLog;
            SessionState.RequestedBinaryLog = binaryLog;
            Profiler.enableBinaryLog = binaryLog;
            if (binaryLog && !Profiler.enableBinaryLog)
            {
                warnings.Add(
                    "Unity Editor keeps Profiler.enableBinaryLog disabled at runtime. Raw file capture requires a player build or manual Profiler export.");
            }

            var output = GetString(parameters, "output");
            if (string.IsNullOrEmpty(output) && binaryLog && defaultOutputIfNeeded)
                output = BuildDefaultCapturePath();

            if (!string.IsNullOrEmpty(output))
            {
                var normalizedOutput = NormalizeAbsolutePath(output);
                Directory.CreateDirectory(Path.GetDirectoryName(normalizedOutput));
                Profiler.logFile = normalizedOutput;
                SessionState.LastCapturePath = normalizedOutput;
            }

            warnings.AddRange(ApplySafeMemoryBudget(parameters, requestedDeepProfile, allocationCallstacks));

            foreach (var categoryName in GetStringList(parameters, "enableCategories"))
                warnings.AddRange(SetCategoryEnabled(categoryName, true));

            foreach (var categoryName in GetStringList(parameters, "disableCategories"))
                warnings.AddRange(SetCategoryEnabled(categoryName, false));

            return warnings;
        }

        private static List<string> ApplyRequestedMode(string mode)
        {
            var warnings = new List<string>();
            var editMode = mode == "edit";

            if (!SetProfileEditor(editMode))
                warnings.Add("Edit/play target selection is unavailable on this Unity version; profiler mode remained unchanged.");

            if (mode == "play" && !EditorApplication.isPlaying)
                EditorApplication.isPlaying = true;
            else if (mode == "edit" && EditorApplication.isPlaying)
                EditorApplication.isPlaying = false;

            return warnings;
        }

        private static List<string> SetCategoryEnabled(string categoryName, bool enabled)
        {
            var warnings = new List<string>();
            var category = ResolveCategory(categoryName);
            if (!category.HasValue)
            {
                warnings.Add($"Unknown profiler category: {categoryName}");
                return warnings;
            }

            if (SetCategoryEnabledMethod == null)
            {
                warnings.Add("Profiler category toggling is unavailable on this Unity version.");
                return warnings;
            }

            SetCategoryEnabledMethod.Invoke(null, new object[] { category.Value, enabled });
            warnings.Add("Unity's open Profiler window can override category settings to match active charts.");
            return warnings;
        }

        private static List<string> ClearBufferedFrames()
        {
            var warnings = new List<string>();
            if (ClearAllFramesMethod == null)
            {
                warnings.Add("Clearing buffered profiler frames is unavailable on this Unity version.");
                return warnings;
            }

            ClearAllFramesMethod.Invoke(null, null);
            return warnings;
        }

        private static RawFrameDataView GetRawFrameDataView(int frameIndex, int threadIndex)
        {
            if (GetRawFrameDataViewMethod == null)
                return null;

            try
            {
                return GetRawFrameDataViewMethod.Invoke(null, new object[] { frameIndex, threadIndex }) as RawFrameDataView;
            }
            catch
            {
                return null;
            }
        }

        private static HierarchyFrameDataView GetHierarchyFrameDataView(int frameIndex, int threadIndex)
        {
            if (GetHierarchyFrameDataViewMethod == null)
                return null;

            try
            {
                return GetHierarchyFrameDataViewMethod.Invoke(
                    null,
                    new object[]
                    {
                        frameIndex,
                        threadIndex,
                        HierarchyFrameDataView.ViewModes.Default,
                        HierarchyFrameDataView.columnDontSort,
                        false
                    }) as HierarchyFrameDataView;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryGetFrameRange(out int firstFrame, out int lastFrame)
        {
            firstFrame = -1;
            lastFrame = -1;
            if (FirstFrameIndexProperty == null || LastFrameIndexProperty == null)
                return false;

            try
            {
                firstFrame = Convert.ToInt32(FirstFrameIndexProperty.GetValue(null));
                lastFrame = Convert.ToInt32(LastFrameIndexProperty.GetValue(null));
                return lastFrame >= firstFrame && firstFrame >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static int ResolveFrame(Dictionary<string, object> parameters)
        {
            if (GetNullableInt(parameters, "frame").HasValue)
                return GetNullableInt(parameters, "frame").Value;

            if (!TryGetFrameRange(out _, out var lastFrame))
                throw new ArgumentException("No profiler frames are available. Start a session or load a capture first.");

            return lastFrame;
        }

        private static bool GetProfileEditor()
        {
            if (ProfileEditorProperty == null)
                return false;

            try
            {
                return Convert.ToBoolean(ProfileEditorProperty.GetValue(null));
            }
            catch
            {
                return false;
            }
        }

        private static bool GetDriverEnabled()
        {
            if (DriverEnabledProperty == null)
                return false;

            try
            {
                return Convert.ToBoolean(DriverEnabledProperty.GetValue(null));
            }
            catch
            {
                return false;
            }
        }

        private static bool SetDriverEnabled(bool value)
        {
            if (DriverEnabledProperty == null)
                return false;

            try
            {
                DriverEnabledProperty.SetValue(null, value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool SetProfileEditor(bool value)
        {
            if (ProfileEditorProperty == null)
                return false;

            try
            {
                ProfileEditorProperty.SetValue(null, value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool GetDeepProfiling()
        {
            if (DeepProfilingProperty == null)
                return false;

            try
            {
                return Convert.ToBoolean(DeepProfilingProperty.GetValue(null));
            }
            catch
            {
                return false;
            }
        }

        private static bool SetDeepProfiling(bool value)
        {
            if (DeepProfilingProperty == null)
                return false;

            try
            {
                DeepProfilingProperty.SetValue(null, value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string GetExistingCaptureSource()
        {
            var current = NormalizePath(Profiler.logFile);
            if (!string.IsNullOrEmpty(current) && File.Exists(current))
                return current;

            if (!string.IsNullOrEmpty(SessionState.LastCapturePath) && File.Exists(SessionState.LastCapturePath))
                return SessionState.LastCapturePath;

            if (!string.IsNullOrEmpty(SessionState.LoadedCapturePath) && File.Exists(SessionState.LoadedCapturePath))
                return SessionState.LoadedCapturePath;

            return null;
        }

        private static string BuildDefaultCapturePath()
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            var capturesDirectory = Path.Combine(projectRoot, "ProfilerCaptures");
            Directory.CreateDirectory(capturesDirectory);
            var fileName = $"ucp-profile-{DateTime.UtcNow:yyyyMMdd-HHmmss}.raw";
            return Path.Combine(capturesDirectory, fileName);
        }

        private static List<string> ApplySafeMemoryBudget(
            Dictionary<string, object> parameters,
            bool? requestedDeepProfile,
            bool? requestedAllocationCallstacks)
        {
            var warnings = new List<string>();
            var heavyCapture =
                (requestedDeepProfile ?? GetDeepProfiling()) ||
                (requestedAllocationCallstacks ?? Profiler.enableAllocationCallstacks);

            var recommendedBudget = heavyCapture ? HeavyProfilerMemoryBytes : DefaultProfilerMemoryBytes;
            var hardCap = heavyCapture ? AbsoluteHeavyProfilerMemoryBytes : AbsoluteProfilerMemoryBytes;
            var currentBudget = Convert.ToInt64(Profiler.maxUsedMemory);
            var requestedBudget = GetNullableLong(parameters, "maxUsedMemory");

            long effectiveBudget;
            if (requestedBudget.HasValue)
            {
                effectiveBudget = Math.Min(
                    hardCap,
                    Math.Max(MinimumProfilerMemoryBytes, requestedBudget.Value));

                if (requestedBudget.Value != effectiveBudget)
                {
                    warnings.Add(
                        $"Profiler buffer memory was clamped to {effectiveBudget / (1024L * 1024L)} MiB to prevent editor memory bloat.");
                }
            }
            else
            {
                effectiveBudget = Math.Min(currentBudget, recommendedBudget);
                if (effectiveBudget < MinimumProfilerMemoryBytes)
                    effectiveBudget = MinimumProfilerMemoryBytes;

                if (currentBudget != effectiveBudget)
                {
                    warnings.Add(
                        $"Profiler buffer memory was reduced to {effectiveBudget / (1024L * 1024L)} MiB for a safer live-editor session.");
                }
            }

            Profiler.maxUsedMemory = Convert.ToInt32(
                Math.Min(int.MaxValue, Math.Max(MinimumProfilerMemoryBytes, effectiveBudget)));

            return warnings;
        }

        private static bool HasBufferedFrames()
        {
            return TryGetFrameRange(out var firstFrame, out var lastFrame) && lastFrame >= firstFrame;
        }

        private static void CaptureSessionDefaults()
        {
            SessionState.HadCapturedDefaults = true;
            SessionState.PreviousProfileEditor = GetProfileEditor();
            SessionState.PreviousDeepProfiling = GetDeepProfiling();
            SessionState.PreviousAllocationCallstacks = Profiler.enableAllocationCallstacks;
            SessionState.PreviousBinaryLog = Profiler.enableBinaryLog;
            SessionState.PreviousMaxUsedMemory = Convert.ToInt64(Profiler.maxUsedMemory);
            SessionState.PreviousOutputPath = NormalizePath(Profiler.logFile);
        }

        private static List<string> RestoreSessionDefaults()
        {
            if (!SessionState.HadCapturedDefaults)
                return new List<string>();

            var warnings = new List<string>();

            Profiler.enableAllocationCallstacks = SessionState.PreviousAllocationCallstacks;
            Profiler.enableBinaryLog = SessionState.PreviousBinaryLog;
            Profiler.maxUsedMemory = Convert.ToInt32(
                Math.Min(int.MaxValue, Math.Max(0L, SessionState.PreviousMaxUsedMemory)));
            Profiler.logFile = SessionState.PreviousOutputPath ?? string.Empty;

            if (!SetDeepProfiling(SessionState.PreviousDeepProfiling))
                warnings.Add("Deep profiling state could not be restored automatically on this Unity version.");

            if (!SetProfileEditor(SessionState.PreviousProfileEditor))
                warnings.Add("Profiler edit/play targeting could not be restored automatically on this Unity version.");

            SessionState.HadCapturedDefaults = false;
            return warnings;
        }

        private static Dictionary<string, object> ParseParams(string paramsJson)
        {
            if (string.IsNullOrWhiteSpace(paramsJson))
                return new Dictionary<string, object>();

            return MiniJson.Deserialize(paramsJson) as Dictionary<string, object>
                ?? new Dictionary<string, object>();
        }

        private static string RequireString(Dictionary<string, object> parameters, string key)
        {
            var value = GetString(parameters, key);
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"Missing '{key}' parameter");
            return value;
        }

        private static string GetString(Dictionary<string, object> parameters, string key)
        {
            return parameters.TryGetValue(key, out var value) ? value?.ToString() : null;
        }

        private static int GetInt(Dictionary<string, object> parameters, string key, int defaultValue)
        {
            return GetNullableInt(parameters, key) ?? defaultValue;
        }

        private static int? GetNullableInt(Dictionary<string, object> parameters, string key)
        {
            if (!parameters.TryGetValue(key, out var value) || value == null)
                return null;

            return Convert.ToInt32(value);
        }

        private static long? GetNullableLong(Dictionary<string, object> parameters, string key)
        {
            if (!parameters.TryGetValue(key, out var value) || value == null)
                return null;

            return Convert.ToInt64(value);
        }

        private static bool GetBool(Dictionary<string, object> parameters, string key, bool defaultValue)
        {
            return GetNullableBool(parameters, key) ?? defaultValue;
        }

        private static bool? GetNullableBool(Dictionary<string, object> parameters, string key)
        {
            if (!parameters.TryGetValue(key, out var value) || value == null)
                return null;

            return Convert.ToBoolean(value);
        }

        private static List<string> GetStringList(Dictionary<string, object> parameters, string key)
        {
            if (!parameters.TryGetValue(key, out var value) || value == null)
                return new List<string>();

            if (value is List<object> values)
                return values.Where(item => item != null).Select(item => item.ToString()).ToList();

            return new List<string> { value.ToString() };
        }

        private static string NormalizeMode(string mode)
        {
            return string.Equals(mode, "edit", StringComparison.OrdinalIgnoreCase) ? "edit" : "play";
        }

        private static string NormalizeAbsolutePath(string path)
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(projectRoot, path));
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                NormalizePath(left),
                NormalizePath(right),
                StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<string, object> BuildSummaryData(
            int limit,
            int threadIndex,
            int? requestedFirstFrame = null,
            int? requestedLastFrame = null)
        {
            if (!TryGetFrameRange(out var availableFirst, out var availableLast))
            {
                return new Dictionary<string, object>
                {
                    ["frameRange"] = BuildFrameRangeData(),
                    ["stats"] = new Dictionary<string, object>(),
                    ["topMarkers"] = new List<object>()
                };
            }

            var firstFrame = requestedFirstFrame ?? availableFirst;
            var lastFrame = requestedLastFrame ?? availableLast;

            var frameCount = 0;
            var totalCpuMs = 0.0;
            var totalGpuMs = 0.0;
            var totalFps = 0.0;
            var totalGcAllocBytes = 0L;
            var minCpuMs = double.MaxValue;
            var maxCpuMs = 0.0;
            var minGpuMs = double.MaxValue;
            var maxGpuMs = 0.0;
            var markerTotals = new Dictionary<string, MarkerAggregate>(StringComparer.Ordinal);

            for (var frameIndex = firstFrame; frameIndex <= lastFrame; frameIndex++)
            {
                using (var raw = GetRawFrameDataView(frameIndex, threadIndex))
                {
                    if (raw == null || !raw.valid)
                        continue;

                    frameCount++;
                    totalCpuMs += raw.frameTimeMs;
                    totalGpuMs += raw.frameGpuTimeMs;
                    totalFps += raw.frameFps;
                    minCpuMs = Math.Min(minCpuMs, raw.frameTimeMs);
                    maxCpuMs = Math.Max(maxCpuMs, raw.frameTimeMs);
                    minGpuMs = Math.Min(minGpuMs, raw.frameGpuTimeMs);
                    maxGpuMs = Math.Max(maxGpuMs, raw.frameGpuTimeMs);
                    totalGcAllocBytes += GetGcAllocBytes(frameIndex);
                }

                using (var hierarchy = GetHierarchyFrameDataView(frameIndex, threadIndex))
                {
                    if (hierarchy == null || !hierarchy.valid)
                        continue;

                    foreach (var item in CollectHierarchyItems(hierarchy, null))
                    {
                        if (!markerTotals.TryGetValue(item.Name, out var aggregate))
                            aggregate = new MarkerAggregate();

                        aggregate.SelfMs += item.SelfMs;
                        aggregate.TotalMs += item.TotalMs;
                        aggregate.Calls += item.Calls;
                        markerTotals[item.Name] = aggregate;
                    }
                }
            }

            if (frameCount == 0)
            {
                return new Dictionary<string, object>
                {
                    ["frameRange"] = new Dictionary<string, object>
                    {
                        ["firstFrame"] = firstFrame,
                        ["lastFrame"] = lastFrame
                    },
                    ["stats"] = new Dictionary<string, object>(),
                    ["topMarkers"] = new List<object>()
                };
            }

            var topMarkers = markerTotals
                .OrderByDescending(entry => entry.Value.SelfMs)
                .Take(limit)
                .Select(entry => new Dictionary<string, object>
                {
                    ["name"] = entry.Key,
                    ["selfMs"] = entry.Value.SelfMs,
                    ["totalMs"] = entry.Value.TotalMs,
                    ["calls"] = entry.Value.Calls
                })
                .Cast<object>()
                .ToList();

            return new Dictionary<string, object>
            {
                ["frameRange"] = new Dictionary<string, object>
                {
                    ["firstFrame"] = firstFrame,
                    ["lastFrame"] = lastFrame
                },
                ["stats"] = new Dictionary<string, object>
                {
                    ["frameCount"] = frameCount,
                    ["avgCpuMs"] = totalCpuMs / frameCount,
                    ["minCpuMs"] = minCpuMs,
                    ["maxCpuMs"] = maxCpuMs,
                    ["avgGpuMs"] = totalGpuMs / frameCount,
                    ["minGpuMs"] = minGpuMs == double.MaxValue ? 0.0 : minGpuMs,
                    ["maxGpuMs"] = maxGpuMs,
                    ["avgFps"] = totalFps / frameCount,
                    ["gcAllocBytes"] = totalGcAllocBytes
                },
                ["topMarkers"] = topMarkers
            };
        }

        private static object ExportJsonCapture(string outputPath)
        {
            List<object> frames;
            Dictionary<string, object> exportedFrameRange;
            var warnings = BuildJsonExportWarnings(out frames, out exportedFrameRange);
            var export = new Dictionary<string, object>
            {
                ["generatedAtUtc"] = DateTime.UtcNow.ToString("o"),
                ["bufferedFrameRange"] = BuildFrameRangeData(),
                ["exportedFrameRange"] = exportedFrameRange,
                ["session"] = BuildSessionData(),
                ["config"] = BuildConfigData(),
                ["frames"] = frames,
                ["summary"] = BuildSummaryData(
                    10,
                    0,
                    Convert.ToInt32(exportedFrameRange["firstFrame"]),
                    Convert.ToInt32(exportedFrameRange["lastFrame"]))
            };

            File.WriteAllText(outputPath, MiniJson.Serialize(export));
            return new Dictionary<string, object>
            {
                ["capture"] = BuildCaptureData(outputPath, "saved"),
                ["warnings"] = warnings
            };
        }

        private static List<object> BuildAllFrameSummaries(int firstFrame, int lastFrame)
        {
            if (firstFrame > lastFrame)
                return new List<object>();

            var frames = new List<object>();
            for (var frameIndex = firstFrame; frameIndex <= lastFrame; frameIndex++)
                frames.Add(BuildFrameSummary(frameIndex, false));

            return frames;
        }

        private static List<object> BuildJsonExportWarnings(
            out List<object> frames,
            out Dictionary<string, object> exportedFrameRange)
        {
            frames = new List<object>();
            exportedFrameRange = new Dictionary<string, object>
            {
                ["firstFrame"] = -1,
                ["lastFrame"] = -1,
                ["count"] = 0
            };

            if (!TryGetFrameRange(out var bufferedFirstFrame, out var bufferedLastFrame))
                return new List<object>();

            var exportFirstFrame = Math.Max(
                bufferedFirstFrame,
                bufferedLastFrame - DefaultJsonExportFrameWindow + 1);
            frames = BuildAllFrameSummaries(exportFirstFrame, bufferedLastFrame);
            exportedFrameRange = new Dictionary<string, object>
            {
                ["firstFrame"] = exportFirstFrame,
                ["lastFrame"] = bufferedLastFrame,
                ["count"] = frames.Count
            };

            if (exportFirstFrame == bufferedFirstFrame)
                return new List<object>();

            return new List<object>
            {
                $"Structured snapshot export included the most recent {frames.Count} frames out of {bufferedLastFrame - bufferedFirstFrame + 1} buffered frames."
            };
        }

        private static ProfilerCategory? ResolveCategory(string categoryName)
        {
            foreach (var category in GetAvailableCategories())
            {
                if (string.Equals(category.Name, categoryName, StringComparison.OrdinalIgnoreCase))
                    return category;
            }

            return null;
        }

        private static ProfilerCategory[] GetAvailableCategories()
        {
            if (GetCategoriesCountMethod == null || GetAllCategoriesMethod == null)
                return Array.Empty<ProfilerCategory>();

            var count = Convert.ToInt32(GetCategoriesCountMethod.Invoke(null, null));
            if (count <= 0)
                return Array.Empty<ProfilerCategory>();

            var categories = new ProfilerCategory[count];
            GetAllCategoriesMethod.Invoke(null, new object[] { categories });
            return categories;
        }

        private static bool IsCategoryEnabled(ProfilerCategory category)
        {
            if (IsCategoryEnabledMethod == null)
                return false;

            try
            {
                return Convert.ToBoolean(IsCategoryEnabledMethod.Invoke(null, new object[] { category }));
            }
            catch
            {
                return false;
            }
        }

        private static PropertyInfo FindDriverProperty(string name)
        {
            return ProfilerDriverType?.GetProperty(
                name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        }

        private static MethodInfo FindDriverMethod(string name, params Type[] parameterTypes)
        {
            return ProfilerDriverType?.GetMethod(
                name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                parameterTypes,
                null);
        }

        private sealed class ProfilerSessionState
        {
            public bool Active;
            public string SessionId;
            public string RequestedMode;
            public bool RequestedDeepProfile;
            public bool RequestedAllocationCallstacks;
            public bool RequestedBinaryLog;
            public bool HadCapturedDefaults;
            public bool PreviousProfileEditor;
            public bool PreviousDeepProfiling;
            public bool PreviousAllocationCallstacks;
            public bool PreviousBinaryLog;
            public long PreviousMaxUsedMemory;
            public string PreviousOutputPath;
            public string StartedAtUtc;
            public string StoppedAtUtc;
            public string LastCapturePath;
            public string LoadedCapturePath;
            public List<string> Warnings = new();
        }

        private sealed class MarkerAggregate
        {
            public double SelfMs;
            public double TotalMs;
            public long Calls;
        }

        private sealed class HierarchyItemRecord
        {
            public int ItemId;
            public string Name;
            public string Path;
            public int Depth;
            public double TotalMs;
            public double SelfMs;
            public long Calls;
            public double GcMemory;
            public int ChildCount;

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["item"] = ItemId,
                    ["name"] = Name ?? string.Empty,
                    ["path"] = Path ?? string.Empty,
                    ["depth"] = Depth,
                    ["totalMs"] = TotalMs,
                    ["selfMs"] = SelfMs,
                    ["calls"] = Calls,
                    ["gcMemory"] = GcMemory,
                    ["childCount"] = ChildCount
                };
            }

            public static implicit operator Dictionary<string, object>(HierarchyItemRecord item)
            {
                return item.ToDictionary();
            }
        }
    }
}
