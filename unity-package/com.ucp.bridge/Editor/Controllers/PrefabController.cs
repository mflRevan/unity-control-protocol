using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UCP.Bridge
{
    public static class PrefabController
    {
        public static void Register(CommandRouter router)
        {
            router.Register("prefab/status", HandleStatus);
            router.Register("prefab/apply", HandleApply);
            router.Register("prefab/revert", HandleRevert);
            router.Register("prefab/unpack", HandleUnpack);
            router.Register("prefab/create", HandleCreate);
            router.Register("prefab/overrides", HandleOverrides);
        }

        private static object HandleStatus(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("instanceId", out var idObj))
                throw new ArgumentException("Missing 'instanceId' parameter");

            var go = EditorUtility.EntityIdToObject(Convert.ToInt32(idObj)) as GameObject;
            if (go == null)
                throw new ArgumentException($"GameObject not found: {idObj}");

            bool isPrefab = PrefabUtility.IsPartOfAnyPrefab(go);
            bool isInstance = PrefabUtility.IsPartOfPrefabInstance(go);
            bool isRoot = PrefabUtility.IsOutermostPrefabInstanceRoot(go);
            bool hasOverrides = PrefabUtility.HasPrefabInstanceAnyOverrides(go, false);

            var result = new Dictionary<string, object>
            {
                ["name"] = go.name,
                ["isPrefab"] = isPrefab,
                ["isInstance"] = isInstance,
                ["isRoot"] = isRoot,
                ["hasOverrides"] = hasOverrides
            };

            if (isInstance)
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
                if (source != null)
                {
                    result["sourcePath"] = AssetDatabase.GetAssetPath(source);
                    result["sourceName"] = source.name;
                }
            }

            return result;
        }

        private static object HandleApply(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("instanceId", out var idObj))
                throw new ArgumentException("Missing 'instanceId' parameter");

            var go = EditorUtility.EntityIdToObject(Convert.ToInt32(idObj)) as GameObject;
            if (go == null)
                throw new ArgumentException($"GameObject not found: {idObj}");

            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                throw new ArgumentException("Object is not a prefab instance");

            string assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            PrefabUtility.ApplyPrefabInstance(go, InteractionMode.AutomatedAction);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            SceneChangeTracker.RecordGameObjectChange(go, "Prefab");

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["assetPath"] = assetPath
            };
        }

        private static object HandleRevert(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("instanceId", out var idObj))
                throw new ArgumentException("Missing 'instanceId' parameter");

            var go = EditorUtility.EntityIdToObject(Convert.ToInt32(idObj)) as GameObject;
            if (go == null)
                throw new ArgumentException($"GameObject not found: {idObj}");

            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                throw new ArgumentException("Object is not a prefab instance");

            PrefabUtility.RevertPrefabInstance(go, InteractionMode.AutomatedAction);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            SceneChangeTracker.RecordGameObjectChange(go, "Prefab");

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["name"] = go.name
            };
        }

        private static object HandleUnpack(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("instanceId", out var idObj))
                throw new ArgumentException("Missing 'instanceId' parameter");

            bool completely = false;
            if (p.TryGetValue("completely", out var compObj))
                completely = Convert.ToBoolean(compObj);

            var go = EditorUtility.EntityIdToObject(Convert.ToInt32(idObj)) as GameObject;
            if (go == null)
                throw new ArgumentException($"GameObject not found: {idObj}");

            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                throw new ArgumentException("Object is not a prefab instance");

            var mode = completely
                ? PrefabUnpackMode.Completely
                : PrefabUnpackMode.OutermostRoot;

            PrefabUtility.UnpackPrefabInstance(go, mode, InteractionMode.AutomatedAction);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            SceneChangeTracker.RecordGameObjectChange(go, "Prefab");

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["name"] = go.name,
                ["mode"] = mode.ToString()
            };
        }

        private static object HandleCreate(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("instanceId", out var idObj))
                throw new ArgumentException("Missing 'instanceId' parameter");
            if (!p.TryGetValue("path", out var pathObj))
                throw new ArgumentException("Missing 'path' parameter");

            var go = EditorUtility.EntityIdToObject(Convert.ToInt32(idObj)) as GameObject;
            if (go == null)
                throw new ArgumentException($"GameObject not found: {idObj}");

            string savePath = pathObj.ToString();

            // Ensure directory exists
            string dir = System.IO.Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                CreateFoldersRecursive(dir);
            }

            bool success;
            var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(
                go,
                savePath,
                InteractionMode.AutomatedAction,
                out success
            );

            if (!success || prefab == null)
                throw new InvalidOperationException($"Failed to create prefab at {savePath}");

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            SceneChangeTracker.RecordGameObjectChange(go, "Prefab");

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["path"] = savePath,
                ["name"] = prefab.name,
                ["instanceId"] = prefab.GetInstanceID(),
                ["sceneInstanceId"] = go.GetInstanceID(),
                ["isPrefabInstance"] = PrefabUtility.IsPartOfPrefabInstance(go)
            };
        }

        private static object HandleOverrides(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("instanceId", out var idObj))
                throw new ArgumentException("Missing 'instanceId' parameter");

            var go = EditorUtility.EntityIdToObject(Convert.ToInt32(idObj)) as GameObject;
            if (go == null)
                throw new ArgumentException($"GameObject not found: {idObj}");

            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                throw new ArgumentException("Object is not a prefab instance");

            var modifications = PrefabUtility.GetPropertyModifications(go);
            var overrides = new List<object>();

            if (modifications != null)
            {
                foreach (var mod in modifications)
                {
                    overrides.Add(new Dictionary<string, object>
                    {
                        ["target"] = mod.target?.GetType().Name ?? "null",
                        ["propertyPath"] = mod.propertyPath,
                        ["value"] = mod.value ?? ""
                    });
                }
            }

            var addedComponents = PrefabUtility.GetAddedComponents(go);
            var added = new List<object>();
            foreach (var ac in addedComponents)
            {
                added.Add(new Dictionary<string, object>
                {
                    ["component"] = ac.instanceComponent.GetType().Name,
                    ["instanceId"] = ac.instanceComponent.GetInstanceID()
                });
            }

            var removedComponents = PrefabUtility.GetRemovedComponents(go);
            var removed = new List<object>();
            foreach (var rc in removedComponents)
            {
                removed.Add(new Dictionary<string, object>
                {
                    ["component"] = rc.assetComponent.GetType().Name
                });
            }

            return new Dictionary<string, object>
            {
                ["name"] = go.name,
                ["propertyModifications"] = overrides,
                ["addedComponents"] = added,
                ["removedComponents"] = removed
            };
        }

        private static void CreateFoldersRecursive(string path)
        {
            var parts = path.Replace("\\", "/").Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
