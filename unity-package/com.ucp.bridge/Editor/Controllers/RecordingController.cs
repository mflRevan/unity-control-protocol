using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;

namespace UCP.Bridge
{
    /// <summary>
    /// Lightweight editor video capture for agent workflows. Frames are rendered without scene
    /// objects, read back into a reusable texture, and written directly through Unity's native encoder.
    /// </summary>
    public static class RecordingController
    {
        private const string ArmedSessionKey = "UCP.Recording.Armed";
        private static MediaEncoder s_encoder;
        private static RenderTexture s_target;
        private static Texture2D s_readback;
        private static RecordingSettings s_settings;
        private static Dictionary<string, object> s_lastResult;
        private static string s_state = "idle";
        private static string s_path;
        private static string s_tempPath;
        private static string s_error;
        private static double s_startedAt;
        private static double s_stopAt;
        private static double s_hardStopAt;
        private static double s_nextCaptureAt;
        private static int s_frameCount;
        private static int s_droppedFrames;
        private static bool s_stopRequested;
        private static ArmedRecording s_armed;

        static RecordingController()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            EditorApplication.quitting += Shutdown;
            RestoreArmedRecording();
        }

        public static void Register(CommandRouter router)
        {
            router.Register("record/start", HandleStart);
            router.Register("record/stop", HandleStop);
            router.Register("record/status", _ => BuildStatus());
            router.Register("record/arm", HandleArm);
            router.Register("record/signal", HandleSignal);
        }

        public static void Signal(string name)
        {
            if (s_armed == null || !s_armed.MatchesSignal(name)) return;
            StartArmedRecording();
        }

        public static void NotifyLog(string message)
        {
            if (s_armed == null || s_armed.LogRegex == null) return;
            try
            {
                if (s_armed.LogRegex.IsMatch(message ?? string.Empty)) StartArmedRecording();
            }
            catch (RegexMatchTimeoutException)
            {
                FailArm("Log trigger regex timed out");
            }
        }

        public static void Shutdown()
        {
            if (s_encoder == null) return;
            try { FinalizeRecording(); }
            catch { DisposeResources(); }
        }

        internal static void ResetForTests()
        {
            Shutdown();
            ClearArm();
            s_lastResult = null;
            s_state = "idle";
            s_error = null;
        }

        internal static (int width, int height) ResolveDimensionsForTests(
            float aspect, int maxEdge, int? width, int? height)
        {
            return ResolveDimensions(aspect, maxEdge, width, height);
        }

        private static object HandleStart(string paramsJson)
        {
            if (s_encoder != null || s_state == "finalizing")
                throw new InvalidOperationException("A recording is already active. Use `ucp record status` or `ucp record stop`.");

            ClearArm();
            var settings = ParseSettings(paramsJson);
            StartRecording(settings);
            return BuildStatus();
        }

        private static object HandleStop(string paramsJson)
        {
            if (s_armed != null)
            {
                ClearArm();
                s_state = "idle";
                s_error = null;
                s_settings = null;
                s_path = null;
                s_tempPath = null;
                return BuildStatus();
            }
            if (s_encoder == null)
                return BuildStatus();

            s_stopRequested = true;
            s_state = "finalizing";
            FinalizeRecording();
            return BuildStatus();
        }

        private static object HandleArm(string paramsJson)
        {
            if (s_encoder != null || s_state == "finalizing")
                throw new InvalidOperationException("Cannot arm while a recording is active");

            var parameters = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (parameters == null || !TryString(parameters, "trigger", out var trigger))
                throw new ArgumentException("Missing 'trigger' parameter");

            var settings = ParseSettings(paramsJson);
            var timeout = ReadDouble(parameters, "timeout", 60d);
            s_settings = settings;
            s_lastResult = null;
            s_path = null;
            s_tempPath = null;
            s_frameCount = 0;
            s_droppedFrames = 0;
            s_armed = ArmedRecording.Create(trigger, settings,
                timeout > 0d ? EditorApplication.timeSinceStartup + timeout : 0d);
            PersistArm();
            s_state = "armed";
            s_error = null;
            SubscribeTick();
            return BuildStatus();
        }

        private static object HandleSignal(string paramsJson)
        {
            var parameters = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (parameters == null || !TryString(parameters, "name", out var name))
                throw new ArgumentException("Missing 'name' parameter");
            var matched = s_armed != null && s_armed.MatchesSignal(name);
            Signal(name);
            var result = BuildStatus();
            result["matched"] = matched;
            result["signal"] = name;
            return result;
        }

        private static void StartRecording(RecordingSettings settings)
        {
            var camera = ResolveCamera(settings.View);
            var sourceAspect = ResolveSourceAspect(camera);
            var dimensions = ResolveDimensions(sourceAspect, settings.MaxEdge, settings.Width, settings.Height);
            settings.Width = dimensions.width;
            settings.Height = dimensions.height;
            settings.SourceAspect = sourceAspect;

            ResolveOutputPaths(settings, out s_path, out s_tempPath);
            var parent = Path.GetDirectoryName(s_path);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            if (File.Exists(s_path) && !settings.Overwrite)
                throw new IOException($"Recording output already exists: {s_path}. Pass --overwrite to replace it.");
            if (File.Exists(s_tempPath)) File.Delete(s_tempPath);

            try
            {
                s_encoder = CreateEncoder(s_tempPath, settings);
                s_target = new RenderTexture(settings.Width.Value, settings.Height.Value, 24,
                    RenderTextureFormat.ARGB32)
                {
                    antiAliasing = 1,
                    name = "__ucp_recording_target",
                    hideFlags = HideFlags.HideAndDontSave
                };
                s_target.Create();
                s_readback = new Texture2D(settings.Width.Value, settings.Height.Value,
                    TextureFormat.RGBA32, false)
                {
                    name = "__ucp_recording_readback",
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
            catch
            {
                try { s_encoder?.Dispose(); } catch { }
                s_encoder = null;
                DisposeResources();
                TryDeleteTempFile();
                throw;
            }

            s_settings = settings;
            s_startedAt = EditorApplication.timeSinceStartup;
            s_stopAt = settings.Duration > 0d ? s_startedAt + settings.Duration : 0d;
            s_hardStopAt = settings.MaxDuration > 0d ? s_startedAt + settings.MaxDuration : 0d;
            s_nextCaptureAt = s_startedAt;
            s_frameCount = 0;
            s_droppedFrames = 0;
            s_stopRequested = false;
            s_error = null;
            s_state = "recording";
            s_lastResult = null;
            SubscribeTick();
        }

        private static MediaEncoder CreateEncoder(string path, RecordingSettings settings)
        {
            // Capture cadence stays real time; only the declared playback rate changes. No frames
            // are duplicated and nothing is re-encoded -- the container simply spaces the captured
            // frames further apart, which is what raises effective temporal resolution for a
            // consumer that samples the file at a fixed rate.
            var frameRate = new MediaRational(settings.Fps);
            if (settings.Slowdown > 1.0000001d)
            {
                frameRate.numerator = Mathf.RoundToInt(settings.Fps * 1000f);
                frameRate.denominator = Mathf.RoundToInt((float)settings.Slowdown * 1000f);
            }
            var bitrate = (uint)(settings.BitrateKbps * 1000);
            VideoTrackEncoderAttributes attributes;
            if (settings.Format == "webm")
            {
                attributes = new VideoTrackEncoderAttributes(new VP8EncoderAttributes
                {
                    keyframeDistance = (uint)(settings.Fps * 2)
                });
            }
            else
            {
                attributes = new VideoTrackEncoderAttributes(new H264EncoderAttributes
                {
                    gopSize = (uint)(settings.Fps * 2),
                    numConsecutiveBFrames = 0,
                    profile = VideoEncodingProfile.H264Baseline
                });
            }
            attributes.frameRate = frameRate;
            attributes.width = (uint)settings.Width.Value;
            attributes.height = (uint)settings.Height.Value;
            attributes.includeAlpha = false;
            attributes.targetBitRate = bitrate;
            attributes.bitRateMode = VideoBitrateMode.Medium;
            return new MediaEncoder(path, attributes);
        }

        private static void Tick()
        {
            var now = EditorApplication.timeSinceStartup;
            if (s_armed != null)
            {
                if (s_armed.Deadline > 0d && now >= s_armed.Deadline)
                    FailArm("Recording trigger timed out");
                return;
            }
            if (s_encoder == null) { UnsubscribeTick(); return; }

            if ((s_stopAt > 0d && now >= s_stopAt) || (s_hardStopAt > 0d && now >= s_hardStopAt))
            {
                s_stopRequested = true;
                s_state = "finalizing";
                FinalizeRecording();
                return;
            }
            if (s_stopRequested || now < s_nextCaptureAt) return;

            try
            {
                var frameInterval = 1d / s_settings.Fps;
                if (now - s_nextCaptureAt >= frameInterval)
                {
                    var missed = (int)((now - s_nextCaptureAt) / frameInterval);
                    s_droppedFrames += missed;
                    s_nextCaptureAt += missed * frameInterval;
                }
                RenderFrame();
                s_nextCaptureAt += frameInterval;
                EncodeSynchronous();
            }
            catch (Exception ex)
            {
                FailRecording(ex.Message);
            }
        }

        private static void RenderFrame()
        {
            var camera = ResolveCamera(s_settings.View);
            var previousTarget = camera.targetTexture;
            var previousRect = camera.rect;
            var previousAspect = camera.aspect;
            var previousActive = RenderTexture.active;
            try
            {
                RenderTexture.active = s_target;
                GL.Clear(true, true, Color.black);
                camera.targetTexture = s_target;
                camera.aspect = s_settings.SourceAspect;
                camera.rect = ContainRect(s_settings.SourceAspect,
                    (float)s_settings.Width.Value / s_settings.Height.Value);
                camera.Render();
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.rect = previousRect;
                camera.aspect = previousAspect;
                RenderTexture.active = previousActive;
            }
        }

        private static void EncodeSynchronous()
        {
            var previousActive = RenderTexture.active;
            try
            {
                RenderTexture.active = s_target;
                s_readback.ReadPixels(new Rect(0, 0, s_readback.width, s_readback.height), 0, 0);
                s_readback.Apply(false, false);
                if (!s_encoder.AddFrame(s_readback)) throw new IOException("Native video encoder rejected a frame");
                s_frameCount++;
            }
            finally
            {
                RenderTexture.active = previousActive;
            }
        }

        private static void FinalizeRecording()
        {
            if (s_encoder == null) return;
            s_state = "finalizing";
            var elapsed = Math.Max(0d, EditorApplication.timeSinceStartup - s_startedAt);
            try
            {
                var encoder = s_encoder;
                s_encoder = null;
                encoder.Dispose();
                if (File.Exists(s_path))
                {
                    if (!s_settings.Overwrite) throw new IOException($"Recording output already exists: {s_path}");
                    File.Delete(s_path);
                }
                File.Move(s_tempPath, s_path);
                var size = new FileInfo(s_path).Length;
                s_state = "completed";
                s_lastResult = Result("completed", elapsed, size);
            }
            catch (Exception ex)
            {
                s_error = ex.Message;
                s_state = "failed";
                s_lastResult = Result("failed", elapsed, 0);
                TryDeleteTempFile();
            }
            finally
            {
                DisposeResources();
                UnsubscribeTick();
            }
        }

        private static void FailRecording(string message)
        {
            s_error = message;
            s_state = "failed";
            try { s_encoder?.Dispose(); } catch { }
            s_encoder = null;
            s_lastResult = Result("failed", Math.Max(0d, EditorApplication.timeSinceStartup - s_startedAt), 0);
            DisposeResources();
            TryDeleteTempFile();
            UnsubscribeTick();
        }

        private static void DisposeResources()
        {
            if (s_target != null)
            {
                s_target.Release();
                UnityEngine.Object.DestroyImmediate(s_target);
            }
            s_target = null;
            if (s_readback != null) UnityEngine.Object.DestroyImmediate(s_readback);
            s_readback = null;
            s_stopRequested = false;
        }

        private static void TryDeleteTempFile()
        {
            try
            {
                if (!string.IsNullOrEmpty(s_tempPath) && File.Exists(s_tempPath)) File.Delete(s_tempPath);
            }
            catch { }
        }

        private static Dictionary<string, object> BuildStatus()
        {
            if (s_lastResult != null && s_encoder == null && s_armed == null)
                return new Dictionary<string, object>(s_lastResult);
            var result = Result(s_state,
                s_encoder != null ? Math.Max(0d, EditorApplication.timeSinceStartup - s_startedAt) : 0d,
                0);
            if (s_armed != null) result["trigger"] = s_armed.Trigger;
            return result;
        }

        private static Dictionary<string, object> Result(string state, double elapsed, long size)
        {
            var result = new Dictionary<string, object>
            {
                ["state"] = state,
                ["path"] = s_path ?? string.Empty,
                ["durationSeconds"] = elapsed,
                ["frames"] = s_frameCount,
                ["droppedFrames"] = s_droppedFrames,
                ["size"] = size
            };
            if (s_settings != null)
            {
                result["view"] = s_settings.View;
                result["width"] = s_settings.Width ?? 0;
                result["height"] = s_settings.Height ?? 0;
                result["fps"] = s_settings.Fps;
                // Capture cadence and playback rate diverge whenever slowdown is in play, and a
                // caller needs both: `fps` is what was sampled, `playbackFps` is what the file
                // declares.
                result["slowdown"] = s_settings.Slowdown;
                result["playbackFps"] = s_settings.Slowdown > 0d
                    ? s_settings.Fps / s_settings.Slowdown
                    : (double)s_settings.Fps;
                result["format"] = s_settings.Format;
                if (s_settings.Format != "auto")
                    result["codec"] = s_settings.Format == "webm" ? "vp8" : "h264";
            }
            if (!string.IsNullOrEmpty(s_error)) result["error"] = s_error;
            return result;
        }

        private static RecordingSettings ParseSettings(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>
                ?? new Dictionary<string, object>();
            var settings = new RecordingSettings
            {
                View = ReadString(p, "view", "game").ToLowerInvariant(),
                MaxEdge = Mathf.Clamp(ReadInt(p, "maxEdge", 960), 64, 4096),
                Width = ReadNullableInt(p, "width"),
                Height = ReadNullableInt(p, "height"),
                Fps = Mathf.Clamp(ReadInt(p, "fps", 15), 1, 60),
                Format = ReadString(p, "format", "auto").ToLowerInvariant(),
                BitrateKbps = Mathf.Clamp(ReadInt(p, "bitrateKbps", 2000), 128, 50000),
                Overwrite = ReadBool(p, "overwrite", false),
                Path = ReadString(p, "path", null),
                Duration = ReadDouble(p, "duration", 0d),
                MaxDuration = ReadDouble(p, "maxDuration", 60d),
                Slowdown = ReadDouble(p, "slowdown", 1d)
            };
            if (settings.View != "game" && settings.View != "scene")
                throw new ArgumentException("View must be 'game' or 'scene'");
            if (settings.Format != "auto" && settings.Format != "mp4" && settings.Format != "webm")
                throw new ArgumentException("Format must be 'auto', 'mp4', or 'webm'");
            if (double.IsNaN(settings.Duration) || double.IsInfinity(settings.Duration) || settings.Duration < 0d)
                throw new ArgumentException("Duration must be zero or greater");
            if (double.IsNaN(settings.MaxDuration) || double.IsInfinity(settings.MaxDuration) || settings.MaxDuration < 0d)
                throw new ArgumentException("Max duration must be zero or greater");
            if (double.IsNaN(settings.Slowdown) || double.IsInfinity(settings.Slowdown)
                || settings.Slowdown < 1d || settings.Slowdown > 20d)
                throw new ArgumentException("Slowdown must be between 1 and 20");
            if (settings.Width.HasValue) settings.Width = MakeEven(Mathf.Clamp(settings.Width.Value, 64, 4096));
            if (settings.Height.HasValue) settings.Height = MakeEven(Mathf.Clamp(settings.Height.Value, 64, 4096));
            return settings;
        }

        private static void ResolveOutputPaths(RecordingSettings settings, out string path, out string tempPath)
        {
            var format = settings.Format;
            var requestedExtension = string.IsNullOrEmpty(settings.Path) ? string.Empty
                : Path.GetExtension(settings.Path).ToLowerInvariant();
            if (format == "auto")
            {
                if (requestedExtension == ".webm") format = "webm";
                else if (requestedExtension == ".mp4") format = "mp4";
                else format = Application.platform == RuntimePlatform.LinuxEditor ? "webm" : "mp4";
            }
            var extension = format == "webm" ? ".webm" : ".mp4";
            if (!string.IsNullOrEmpty(requestedExtension) && requestedExtension != extension)
                throw new ArgumentException($"Output extension '{requestedExtension}' does not match --format {format}");
            settings.Format = format;
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            var rawPath = string.IsNullOrEmpty(settings.Path)
                ? Path.Combine(projectRoot, ".ucp", "recordings", $"recording-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}{extension}")
                : settings.Path;
            if (string.IsNullOrEmpty(Path.GetExtension(rawPath))) rawPath += extension;
            path = Path.GetFullPath(Path.IsPathRooted(rawPath) ? rawPath : Path.Combine(projectRoot, rawPath));
            tempPath = Path.Combine(Path.GetDirectoryName(path),
                Path.GetFileNameWithoutExtension(path) + ".partial" + extension);
        }

        private static Camera ResolveCamera(string view)
        {
            if (view == "scene")
            {
                var sceneView = SceneView.lastActiveSceneView;
                if (sceneView == null || sceneView.camera == null)
                    throw new InvalidOperationException("No active Scene view is available to record");
                return sceneView.camera;
            }
            var camera = Camera.main;
#if UNITY_2023_1_OR_NEWER
            if (camera == null) camera = UnityEngine.Object.FindAnyObjectByType<Camera>();
#else
            if (camera == null) camera = UnityEngine.Object.FindObjectOfType<Camera>();
#endif
            if (camera == null) throw new InvalidOperationException("No camera is available for Game view recording");
            return camera;
        }

        private static float ResolveSourceAspect(Camera camera)
        {
            if (camera.pixelWidth > 0 && camera.pixelHeight > 0)
                return (float)camera.pixelWidth / camera.pixelHeight;
            return camera.aspect > 0f ? camera.aspect : 16f / 9f;
        }

        private static (int width, int height) ResolveDimensions(float aspect, int maxEdge, int? width, int? height)
        {
            aspect = Mathf.Clamp(aspect, 0.1f, 10f);
            if (width.HasValue && height.HasValue) return (MakeEven(width.Value), MakeEven(height.Value));
            if (width.HasValue) return (MakeEven(width.Value), MakeEven(Mathf.RoundToInt(width.Value / aspect)));
            if (height.HasValue) return (MakeEven(Mathf.RoundToInt(height.Value * aspect)), MakeEven(height.Value));
            maxEdge = Mathf.Clamp(maxEdge, 64, 4096);
            return aspect >= 1f
                ? (MakeEven(maxEdge), MakeEven(Mathf.RoundToInt(maxEdge / aspect)))
                : (MakeEven(Mathf.RoundToInt(maxEdge * aspect)), MakeEven(maxEdge));
        }

        private static Rect ContainRect(float sourceAspect, float outputAspect)
        {
            if (Mathf.Approximately(sourceAspect, outputAspect)) return new Rect(0f, 0f, 1f, 1f);
            if (sourceAspect > outputAspect)
            {
                var height = outputAspect / sourceAspect;
                return new Rect(0f, (1f - height) * 0.5f, 1f, height);
            }
            var width = sourceAspect / outputAspect;
            return new Rect((1f - width) * 0.5f, 0f, width, 1f);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (s_armed == null) return;
            if (s_armed.Trigger == "play-exit" && state == PlayModeStateChange.EnteredPlayMode)
            {
                s_armed.ObservedPlayMode = true;
                PersistArm();
            }
            if ((s_armed.Trigger == "play-enter" && state == PlayModeStateChange.EnteredPlayMode)
                || (s_armed.Trigger == "play-exit" && s_armed.ObservedPlayMode
                    && state == PlayModeStateChange.EnteredEditMode))
                StartArmedRecording();
        }

        private static void StartArmedRecording()
        {
            if (s_armed == null) return;
            var armed = s_armed;
            ClearArm();
            try { StartRecording(armed.Settings); }
            catch (Exception ex) { s_state = "failed"; s_error = ex.Message; }
        }

        private static void RestoreArmedRecording()
        {
            var json = SessionState.GetString(ArmedSessionKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var data = MiniJson.Deserialize(json) as Dictionary<string, object>;
                s_armed = ArmedRecording.Deserialize(data);
                s_state = "armed";
                SubscribeTick();
                if (s_armed.Trigger == "play-enter" && EditorApplication.isPlaying)
                    EditorApplication.delayCall += StartArmedRecording;
                else if (s_armed.Trigger == "play-exit" && EditorApplication.isPlaying)
                {
                    s_armed.ObservedPlayMode = true;
                    PersistArm();
                }
                else if (s_armed.Trigger == "play-exit" && s_armed.ObservedPlayMode
                    && !EditorApplication.isPlayingOrWillChangePlaymode)
                    EditorApplication.delayCall += StartArmedRecording;
            }
            catch { SessionState.EraseString(ArmedSessionKey); }
        }

        private static void PersistArm()
        {
            if (s_armed != null)
                SessionState.SetString(ArmedSessionKey, MiniJson.Serialize(s_armed.Serialize()));
        }

        private static void FailArm(string message)
        {
            ClearArm();
            s_state = "failed";
            s_error = message;
        }

        private static void ClearArm()
        {
            s_armed = null;
            SessionState.EraseString(ArmedSessionKey);
            if (s_encoder == null) UnsubscribeTick();
        }

        private static void SubscribeTick()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void UnsubscribeTick() => EditorApplication.update -= Tick;
        private static int MakeEven(int value) => Mathf.Max(64, value & ~1);
        private static int ReadInt(Dictionary<string, object> p, string key, int fallback) =>
            p.TryGetValue(key, out var value) && value != null ? Convert.ToInt32(value) : fallback;
        private static int? ReadNullableInt(Dictionary<string, object> p, string key) =>
            p.TryGetValue(key, out var value) && value != null ? Convert.ToInt32(value) : (int?)null;
        private static double ReadDouble(Dictionary<string, object> p, string key, double fallback) =>
            p.TryGetValue(key, out var value) && value != null ? Convert.ToDouble(value) : fallback;
        private static bool ReadBool(Dictionary<string, object> p, string key, bool fallback) =>
            p.TryGetValue(key, out var value) && value is bool boolean ? boolean : fallback;
        private static string ReadString(Dictionary<string, object> p, string key, string fallback) =>
            p.TryGetValue(key, out var value) && value != null ? value.ToString() : fallback;
        private static bool TryString(Dictionary<string, object> p, string key, out string value)
        {
            value = ReadString(p, key, null);
            return !string.IsNullOrWhiteSpace(value);
        }

        private sealed class RecordingSettings
        {
            public string View;
            public int MaxEdge;
            public int? Width;
            public int? Height;
            public int Fps;
            public string Format;
            public int BitrateKbps;
            public bool Overwrite;
            public string Path;
            public double Duration;
            public double MaxDuration;
            public double Slowdown;
            public float SourceAspect;

            public Dictionary<string, object> Serialize() => new Dictionary<string, object>
            {
                ["view"] = View, ["maxEdge"] = MaxEdge, ["width"] = Width, ["height"] = Height,
                ["fps"] = Fps, ["format"] = Format, ["bitrateKbps"] = BitrateKbps,
                ["overwrite"] = Overwrite, ["path"] = Path, ["duration"] = Duration,
                ["maxDuration"] = MaxDuration, ["slowdown"] = Slowdown,
                ["playbackFps"] = Slowdown > 0d ? Fps / Slowdown : (double)Fps
            };
        }

        private sealed class ArmedRecording
        {
            public string Trigger;
            public RecordingSettings Settings;
            public double Deadline;
            public Regex LogRegex;
            public bool ObservedPlayMode;

            public static ArmedRecording Create(string trigger, RecordingSettings settings, double deadline)
            {
                var armed = new ArmedRecording
                {
                    Trigger = trigger,
                    Settings = settings,
                    Deadline = deadline,
                    ObservedPlayMode = trigger == "play-exit" && EditorApplication.isPlaying
                };
                if (trigger.StartsWith("log:", StringComparison.Ordinal))
                    armed.LogRegex = new Regex(trigger.Substring(4), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                        TimeSpan.FromMilliseconds(100));
                else if (trigger != "play-enter" && trigger != "play-exit"
                    && !trigger.StartsWith("signal:", StringComparison.Ordinal))
                    throw new ArgumentException("Trigger must be play-enter, play-exit, log:<regex>, or signal:<name>");
                return armed;
            }

            public bool MatchesSignal(string name) => Trigger == "signal:" + name;
            public Dictionary<string, object> Serialize() => new Dictionary<string, object>
            {
                ["trigger"] = Trigger, ["deadline"] = Deadline, ["settings"] = Settings.Serialize(),
                ["observedPlayMode"] = ObservedPlayMode
            };
            public static ArmedRecording Deserialize(Dictionary<string, object> data)
            {
                var settingsData = data["settings"] as Dictionary<string, object>;
                var settings = ParseSettings(MiniJson.Serialize(settingsData));
                var armed = Create(data["trigger"].ToString(), settings, Convert.ToDouble(data["deadline"]));
                armed.ObservedPlayMode = data.TryGetValue("observedPlayMode", out var observed)
                    && observed is bool value && value;
                return armed;
            }
        }
    }
}
