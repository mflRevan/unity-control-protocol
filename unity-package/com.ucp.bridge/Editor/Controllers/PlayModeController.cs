using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

namespace UCP.Bridge
{
    public static class PlayModeController
    {
        public static void Register(CommandRouter router)
        {
            router.Register("play", HandlePlay);
            router.Register("stop", HandleStop);
            router.Register("pause", HandlePause);
        }

        private static object HandlePlay(string paramsJson)
        {
            if (EditorApplication.isPlaying)
                return new { status = "already_playing" };

            var saveDirtyScenes = GetBoolParam(paramsJson, "saveDirtyScenes", true);
            var discardUntitled = GetBoolParam(paramsJson, "discardUntitled", true);
            SaveDirtyScenesIfRequested(saveDirtyScenes, discardUntitled);

            EditorApplication.isPlaying = true;
            return new { status = "ok" };
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
            }

            if (requiresUntitledDiscard)
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        private static object HandleStop(string paramsJson)
        {
            if (!EditorApplication.isPlaying)
                return new { status = "already_stopped" };

            EditorApplication.isPlaying = false;
            return new { status = "ok" };
        }

        private static object HandlePause(string paramsJson)
        {
            EditorApplication.isPaused = !EditorApplication.isPaused;
            return new { status = "ok", paused = EditorApplication.isPaused };
        }
    }
}
