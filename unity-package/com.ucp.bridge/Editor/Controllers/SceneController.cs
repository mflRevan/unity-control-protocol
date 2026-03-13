using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace UCP.Bridge
{
    public static class SceneController
    {
        public static void Register(CommandRouter router)
        {
            router.Register("scene/list", HandleList);
            router.Register("scene/load", HandleLoad);
            router.Register("scene/active", HandleActive);
        }

        private static object HandleList(string paramsJson)
        {
            var scenes = new List<object>();

            for (int i = 0; i < EditorBuildSettings.scenes.Length; i++)
            {
                var s = EditorBuildSettings.scenes[i];
                scenes.Add(new Dictionary<string, object>
                {
                    ["index"] = i,
                    ["path"] = s.path,
                    ["enabled"] = s.enabled,
                    ["guid"] = s.guid.ToString()
                });
            }

            return new Dictionary<string, object> { ["scenes"] = scenes };
        }

        private static object HandleLoad(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("path", out var pathObj))
                throw new System.ArgumentException("Missing 'path' parameter");

            var path = pathObj.ToString();
            var saveDirtyScenes = GetBoolParam(p, "saveDirtyScenes", true);
            var discardUntitled = GetBoolParam(p, "discardUntitled", true);

            if (EditorApplication.isPlaying)
            {
                SceneManager.LoadScene(path);
            }
            else
            {
                SaveDirtyScenesIfRequested(saveDirtyScenes, discardUntitled);
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            }

            return new { status = "ok", loaded = path };
        }

        private static bool GetBoolParam(Dictionary<string, object> parameters, string key, bool defaultValue)
        {
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

        private static object HandleActive(string paramsJson)
        {
            var scene = SceneManager.GetActiveScene();
            return new Dictionary<string, object>
            {
                ["name"] = scene.name,
                ["path"] = scene.path,
                ["buildIndex"] = scene.buildIndex,
                ["isDirty"] = scene.isDirty,
                ["isLoaded"] = scene.isLoaded,
                ["rootCount"] = scene.rootCount
            };
        }
    }
}
