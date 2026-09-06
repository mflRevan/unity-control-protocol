using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UCP.Bridge
{
    /// <summary>
    /// The compact editor state that rides on every main-thread response, so an agent is told
    /// whether the console is red, whether the editor is playing or compiling, and whether the
    /// scene is dirty without spending a request on it.
    ///
    /// Budget: this runs once per response on the main thread, so it may only use O(1) editor
    /// queries and cached reflection. It must never allocate more than a few small dictionaries,
    /// never touch the asset database, and never throw -- a summary failure is swallowed by the
    /// caller so it can never break a response.
    /// </summary>
    public static class EditorStateSummary
    {
        private delegate void GetCountsByType(ref int errors, ref int warnings, ref int logs);

        private static GetCountsByType s_getCountsByType;
        private static bool s_countsLookedUp;

        /// <param name="logsSinceId">
        /// The log history cursor captured before the request was dispatched; entries after it
        /// were produced by this request and are reported as "new".
        /// </param>
        public static Dictionary<string, object> Capture(long logsSinceId)
        {
            var summary = new Dictionary<string, object>(8);

            var playing = EditorApplication.isPlaying;
            summary["mode"] = playing
                ? (EditorApplication.isPaused ? "paused" : "play")
                : "edit";
            if (EditorApplication.isPlayingOrWillChangePlaymode != playing)
                summary["modeChanging"] = true;
            if (EditorApplication.isCompiling)
                summary["compiling"] = true;
            if (EditorApplication.isUpdating)
                summary["importing"] = true;
            if (EditorUtility.scriptCompilationFailed)
                summary["compileErrors"] = true;
            if (BuildPipeline.isBuildingPlayer)
                summary["building"] = true;

            summary["scene"] = DescribeScenes();

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
                summary["prefabStage"] = stage.assetPath;

            summary["console"] = DescribeConsole(logsSinceId);

            var recording = RecordingController.StateForSummary();
            if (recording != null)
                summary["recording"] = recording;

            return summary;
        }

        private static Dictionary<string, object> DescribeScenes()
        {
            var active = SceneManager.GetActiveScene();
            var scene = new Dictionary<string, object>(4)
            {
                ["name"] = active.name,
                ["dirty"] = active.isDirty
            };
            if (string.IsNullOrEmpty(active.path))
                scene["untitled"] = true;

            var count = SceneManager.sceneCount;
            if (count > 1)
            {
                var otherDirty = 0;
                for (var index = 0; index < count; index++)
                {
                    var candidate = SceneManager.GetSceneAt(index);
                    if (candidate != active && candidate.isDirty)
                        otherDirty++;
                }
                scene["loaded"] = count;
                if (otherDirty > 0)
                    scene["otherDirty"] = otherDirty;
            }

            return scene;
        }

        private static Dictionary<string, object> DescribeConsole(long logsSinceId)
        {
            var console = new Dictionary<string, object>(4);
            if (TryReadConsoleCounts(out var errors, out var warnings))
            {
                console["errors"] = errors;
                console["warnings"] = warnings;
            }
            else
            {
                // Reflection into UnityEditor.LogEntries failed on this editor build; fall back to
                // the bridge's own history, which at least covers everything since the bridge loaded.
                LogsController.CountLevels(0, out errors, out warnings);
                console["errors"] = errors;
                console["warnings"] = warnings;
                console["source"] = "bridge";
            }

            LogsController.CountLevels(logsSinceId, out var newErrors, out var newWarnings);
            if (newErrors > 0)
                console["newErrors"] = newErrors;
            if (newWarnings > 0)
                console["newWarnings"] = newWarnings;
            return console;
        }

        /// <summary>
        /// The counts the Console window's toolbar badges show, read through the same internal
        /// API the window uses. Cached after the first lookup; the call itself is native and cheap.
        /// </summary>
        internal static bool TryReadConsoleCounts(out int errors, out int warnings)
        {
            errors = 0;
            warnings = 0;
            if (!s_countsLookedUp)
            {
                s_countsLookedUp = true;
                try
                {
                    var type = typeof(Editor).Assembly.GetType("UnityEditor.LogEntries");
                    var method = type?.GetMethod(
                        "GetCountsByType",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (method != null)
                    {
                        s_getCountsByType = (GetCountsByType)Delegate.CreateDelegate(
                            typeof(GetCountsByType), method, false);
                    }
                }
                catch
                {
                    s_getCountsByType = null;
                }
            }

            if (s_getCountsByType == null)
                return false;

            try
            {
                var logs = 0;
                s_getCountsByType(ref errors, ref warnings, ref logs);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
