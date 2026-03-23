using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System;

namespace UCP.Bridge
{
    public static class PlayModeController
    {
        public sealed class SessionSnapshot
        {
            public bool Playing;
            public bool Paused;
            public bool WillChange;
            public bool Compiling;
            public DateTime? LastPlayRequestedAtUtc;
            public DateTime? LastEnteredPlayAtUtc;
            public DateTime? LastStopRequestedAtUtc;
            public DateTime? LastExitedPlayAtUtc;
        }

        private static readonly object s_sessionLock = new object();
        private static DateTime? s_lastPlayRequestedAtUtc;
        private static DateTime? s_lastEnteredPlayAtUtc;
        private static DateTime? s_lastStopRequestedAtUtc;
        private static DateTime? s_lastExitedPlayAtUtc;

        static PlayModeController()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static void Register(CommandRouter router)
        {
            router.Register("play", HandlePlay);
            router.Register("play/status", HandleStatus);
            router.Register("stop", HandleStop);
            router.Register("pause", HandlePause);
        }

        public static SessionSnapshot GetSessionSnapshot()
        {
            lock (s_sessionLock)
            {
                return new SessionSnapshot
                {
                    Playing = EditorApplication.isPlaying,
                    Paused = EditorApplication.isPaused,
                    WillChange = EditorApplication.isPlayingOrWillChangePlaymode,
                    Compiling = EditorApplication.isCompiling,
                    LastPlayRequestedAtUtc = s_lastPlayRequestedAtUtc,
                    LastEnteredPlayAtUtc = s_lastEnteredPlayAtUtc,
                    LastStopRequestedAtUtc = s_lastStopRequestedAtUtc,
                    LastExitedPlayAtUtc = s_lastExitedPlayAtUtc
                };
            }
        }

        private static object HandlePlay(string paramsJson)
        {
            if (EditorApplication.isPlaying)
                return new { status = "already_playing" };

            var saveDirtyScenes = GetBoolParam(paramsJson, "saveDirtyScenes", true);
            var discardUntitled = GetBoolParam(paramsJson, "discardUntitled", true);
            SaveDirtyScenesIfRequested(saveDirtyScenes, discardUntitled);

            lock (s_sessionLock)
            {
                s_lastPlayRequestedAtUtc = DateTime.UtcNow;
            }
            EditorApplication.isPlaying = true;
            return new { status = "ok" };
        }

        private static object HandleStatus(string paramsJson)
        {
            return SerializeSessionSnapshot(GetSessionSnapshot());
        }

        private static bool GetBoolParam(string paramsJson, string key, bool defaultValue)
        {
            var parameters = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (parameters != null && parameters.TryGetValue(key, out var valueObj) && valueObj is bool value)
                return value;

            return defaultValue;
        }

        private static void SaveDirtyScenesIfRequested(bool saveDirtyScenes, bool discardUntitled)
        {
            if (!saveDirtyScenes)
                return;

            var requiresUntitledDiscard = false;

            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (!scene.isLoaded || !scene.isDirty)
                    continue;

                if (string.IsNullOrEmpty(scene.path))
                {
                    if (!discardUntitled)
                        throw new System.InvalidOperationException("Dirty untitled scene cannot be auto-saved. Retry with discardUntitled=true.");

                    requiresUntitledDiscard = true;
                    continue;
                }

                if (!EditorSceneManager.SaveScene(scene))
                    throw new System.InvalidOperationException($"Failed to auto-save dirty scene: {scene.path}");

                SceneChangeTracker.ClearScene(scene);
            }

            if (requiresUntitledDiscard)
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        private static object HandleStop(string paramsJson)
        {
            if (!EditorApplication.isPlaying)
                return new { status = "already_stopped" };

            lock (s_sessionLock)
            {
                s_lastStopRequestedAtUtc = DateTime.UtcNow;
            }
            EditorApplication.isPlaying = false;
            return new { status = "ok" };
        }

        private static object HandlePause(string paramsJson)
        {
            EditorApplication.isPaused = !EditorApplication.isPaused;
            return new { status = "ok", paused = EditorApplication.isPaused };
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            lock (s_sessionLock)
            {
                switch (state)
                {
                    case PlayModeStateChange.EnteredPlayMode:
                        s_lastEnteredPlayAtUtc = DateTime.UtcNow;
                        s_lastExitedPlayAtUtc = null;
                        break;
                    case PlayModeStateChange.EnteredEditMode:
                        s_lastExitedPlayAtUtc = DateTime.UtcNow;
                        break;
                }
            }
        }

        private static object SerializeSessionSnapshot(SessionSnapshot snapshot)
        {
            var now = DateTime.UtcNow;
            var result = new Dictionary<string, object>
            {
                ["playing"] = snapshot.Playing,
                ["paused"] = snapshot.Paused,
                ["willChange"] = snapshot.WillChange,
                ["compiling"] = snapshot.Compiling
            };

            if (snapshot.LastPlayRequestedAtUtc.HasValue)
                result["lastPlayRequestedAt"] = snapshot.LastPlayRequestedAtUtc.Value.ToString("o");
            if (snapshot.LastEnteredPlayAtUtc.HasValue)
                result["lastEnteredPlayAt"] = snapshot.LastEnteredPlayAtUtc.Value.ToString("o");
            if (snapshot.LastStopRequestedAtUtc.HasValue)
                result["lastStopRequestedAt"] = snapshot.LastStopRequestedAtUtc.Value.ToString("o");
            if (snapshot.LastExitedPlayAtUtc.HasValue)
                result["lastExitedPlayAt"] = snapshot.LastExitedPlayAtUtc.Value.ToString("o");

            if (snapshot.Playing && snapshot.LastEnteredPlayAtUtc.HasValue)
                result["currentPlayDurationSeconds"] = Math.Max(0d, (now - snapshot.LastEnteredPlayAtUtc.Value).TotalSeconds);

            if (snapshot.LastEnteredPlayAtUtc.HasValue && snapshot.LastExitedPlayAtUtc.HasValue)
                result["lastPlayDurationSeconds"] = Math.Max(0d, (snapshot.LastExitedPlayAtUtc.Value - snapshot.LastEnteredPlayAtUtc.Value).TotalSeconds);

            return result;
        }
    }
}
