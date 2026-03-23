using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
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
            router.Register("scene/save-active", HandleSaveActive);
            router.Register("scene/dirty-summary", HandleDirtySummary);
            router.Register("scene/focus", HandleFocus);
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

                SceneChangeTracker.ClearScene(scene);
            }

            if (requiresUntitledDiscard)
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        private static object HandleSaveActive(string paramsJson)
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
                throw new System.InvalidOperationException("No active loaded scene to save");

            if (string.IsNullOrEmpty(scene.path))
                throw new System.InvalidOperationException("Active scene is untitled and cannot be auto-saved");

            if (!scene.isDirty)
            {
                return new Dictionary<string, object>
                {
                    ["status"] = "ok",
                    ["saved"] = false,
                    ["name"] = scene.name,
                    ["path"] = scene.path
                };
            }

            if (!EditorSceneManager.SaveScene(scene))
                throw new System.InvalidOperationException($"Failed to save active scene: {scene.path}");

            SceneChangeTracker.ClearScene(scene);

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["saved"] = true,
                ["name"] = scene.name,
                ["path"] = scene.path
            };
        }

        private static object HandleDirtySummary(string paramsJson)
        {
            var parameters = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            var maxEntries = 8;
            if (parameters != null && parameters.TryGetValue("maxEntries", out var valueObj) && valueObj != null)
                maxEntries = Mathf.Max(1, System.Convert.ToInt32(valueObj));

            return SceneChangeTracker.DescribeActiveSceneChanges(maxEntries);
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

        private static object HandleFocus(string paramsJson)
        {
            var parameters = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (parameters == null || !parameters.TryGetValue("instanceId", out var idObj))
                throw new System.ArgumentException("Missing 'instanceId' parameter");

            var instanceId = System.Convert.ToInt32(idObj);
            var target = FindGameObject(instanceId);
            var bounds = CalculateFocusBounds(target);
            var sceneView = SceneView.lastActiveSceneView ?? EditorWindow.GetWindow<SceneView>();

            if (sceneView == null)
                throw new System.InvalidOperationException("Unable to open Scene view");

            sceneView.Show();
            sceneView.Focus();
            Selection.activeGameObject = target;

            var focusPoint = bounds.center;
            var focusSize = Mathf.Max(bounds.extents.magnitude * 2f, 1f);
            var axis = TryReadAxis(parameters);

            if (axis.HasValue)
            {
                var normalizedAxis = axis.Value.normalized;
                var rotation = Quaternion.LookRotation(-normalizedAxis, SelectUpVector(normalizedAxis));
                sceneView.LookAtDirect(focusPoint, rotation, focusSize);
            }
            else
            {
                sceneView.LookAtDirect(focusPoint, sceneView.rotation, focusSize);
            }

            sceneView.Repaint();
            SceneView.RepaintAll();

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["instanceId"] = instanceId,
                ["name"] = target.name,
                ["pivot"] = VectorToList(sceneView.pivot),
                ["cameraPosition"] = VectorToList(sceneView.camera.transform.position),
                ["cameraRotationEuler"] = VectorToList(sceneView.camera.transform.rotation.eulerAngles),
                ["size"] = sceneView.size,
                ["axis"] = axis.HasValue ? VectorToList(axis.Value.normalized) : null
            };
        }

        private static Vector3? TryReadAxis(Dictionary<string, object> parameters)
        {
            if (parameters == null || !parameters.TryGetValue("axis", out var axisObj) || axisObj == null)
                return null;

            if (axisObj is not List<object> values || values.Count != 3)
                throw new System.ArgumentException("axis must be an array of exactly three numeric values");

            var axis = new Vector3(
                System.Convert.ToSingle(values[0]),
                System.Convert.ToSingle(values[1]),
                System.Convert.ToSingle(values[2]));

            if (axis.sqrMagnitude < 0.0001f)
                throw new System.ArgumentException("axis must not be the zero vector");

            return axis;
        }

        private static Vector3 SelectUpVector(Vector3 axis)
        {
            if (Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.98f)
                return Vector3.forward;

            return Vector3.up;
        }

        private static Bounds CalculateFocusBounds(GameObject target)
        {
            var hasBounds = false;
            var bounds = new Bounds(target.transform.position, Vector3.one);

            foreach (var renderer in target.GetComponentsInChildren<Renderer>())
            {
                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            foreach (var collider in target.GetComponentsInChildren<Collider>())
            {
                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            if (!hasBounds)
                bounds = new Bounds(target.transform.position, Vector3.one);

            return bounds;
        }

        private static GameObject FindGameObject(int instanceId)
        {
            var direct = EditorUtility.EntityIdToObject(instanceId) as GameObject;
            if (direct != null)
                return direct;

            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (!scene.isLoaded)
                    continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    var found = FindInHierarchy(root, instanceId);
                    if (found != null)
                        return found;
                }
            }

            throw new System.ArgumentException($"GameObject not found: {instanceId}");
        }

        private static GameObject FindInHierarchy(GameObject gameObject, int instanceId)
        {
            if (gameObject.GetInstanceID() == instanceId)
                return gameObject;

            foreach (Transform child in gameObject.transform)
            {
                var found = FindInHierarchy(child.gameObject, instanceId);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static List<object> VectorToList(Vector3 value)
        {
            return new List<object> { value.x, value.y, value.z };
        }
    }
}

