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

            if (EditorApplication.isPlaying)
            {
                SceneManager.LoadScene(path);
            }
            else
            {
                EditorSceneManager.OpenScene(path);
            }

            return new { status = "ok", loaded = path };
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
