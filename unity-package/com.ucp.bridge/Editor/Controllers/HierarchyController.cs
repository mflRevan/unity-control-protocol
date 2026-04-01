using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UCP.Bridge
{
    public static class HierarchyController
    {
        public static void Register(CommandRouter router)
        {
            router.Register("object/create", HandleCreate);
            router.Register("object/delete", HandleDelete);
            router.Register("object/reparent", HandleReparent);
            router.Register("object/instantiate", HandleInstantiate);
            router.Register("object/add-component", HandleAddComponent);
            router.Register("object/remove-component", HandleRemoveComponent);
        }

        private static object HandleCreate(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            var name = "GameObject";
            if (p != null && p.TryGetValue("name", out var nameObj) && nameObj != null)
                name = nameObj.ToString();

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "UCP Create GameObject");

            // Optional parent
            if (p != null && p.TryGetValue("parent", out var parentObj))
            {
                int parentId = Convert.ToInt32(parentObj);
                var parent = FindGameObject(parentId);
                go.transform.SetParent(parent.transform, false);
            }

            // Optional position
            if (p != null && p.TryGetValue("position", out var posObj) && posObj is List<object> pos && pos.Count >= 3)
            {
                go.transform.localPosition = new Vector3(
                    Convert.ToSingle(pos[0]),
                    Convert.ToSingle(pos[1]),
                    Convert.ToSingle(pos[2]));
            }

            // Optional rotation (euler angles)
            if (p != null && p.TryGetValue("rotation", out var rotObj) && rotObj is List<object> rot && rot.Count >= 3)
            {
                go.transform.localEulerAngles = new Vector3(
                    Convert.ToSingle(rot[0]),
                    Convert.ToSingle(rot[1]),
                    Convert.ToSingle(rot[2]));
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            SceneChangeTracker.RecordGameObjectChange(go, "GameObject");

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["instanceId"] = go.GetInstanceID(),
                ["name"] = go.name
            };
        }

        private static object HandleDelete(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("instanceId", out var idObj))
                throw new ArgumentException("Missing 'instanceId' parameter");

            int instanceId = Convert.ToInt32(idObj);
            var go = FindGameObject(instanceId);
            string name = go.name;
            var scene = go.scene;

            Undo.DestroyObjectImmediate(go);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            SceneChangeTracker.RecordDeletedObject(scene, instanceId, name, "GameObject");

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["deleted"] = name,
                ["instanceId"] = instanceId
            };
        }

        private static object HandleReparent(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("instanceId", out var idObj))
                throw new ArgumentException("Missing 'instanceId' parameter");

            int instanceId = Convert.ToInt32(idObj);
            var go = FindGameObject(instanceId);

            Undo.SetTransformParent(go.transform, null, "UCP Reparent");

            if (p.TryGetValue("parent", out var parentObj) && parentObj != null)
            {
                int parentId = Convert.ToInt32(parentObj);
                var parent = FindGameObject(parentId);
                Undo.SetTransformParent(go.transform, parent.transform, "UCP Reparent");
            }
            // else: parent = null means move to root

            // Optional sibling index
            if (p.TryGetValue("siblingIndex", out var sibObj))
            {
                go.transform.SetSiblingIndex(Convert.ToInt32(sibObj));
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            SceneChangeTracker.RecordGameObjectChange(go, "Transform");

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["instanceId"] = instanceId,
                ["name"] = go.name,
                ["parent"] = go.transform.parent != null ? go.transform.parent.gameObject.name : null
            };
        }

        private static object HandleInstantiate(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null)
                throw new ArgumentException("Missing parameters");

            GameObject source = null;

            // Instantiate from prefab asset path
            if (p.TryGetValue("prefab", out var prefabObj) && prefabObj != null)
            {
                string prefabPath = prefabObj.ToString();
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                    throw new ArgumentException($"Prefab not found: {prefabPath}");
                source = prefab;
            }
            // Instantiate from existing scene object (clone)
            else if (p.TryGetValue("sourceId", out var srcObj))
            {
                int srcId = Convert.ToInt32(srcObj);
                source = FindGameObject(srcId);
            }
            else
            {
                throw new ArgumentException("Must provide 'prefab' (asset path) or 'sourceId' (instanceId)");
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            if (instance == null)
            {
                // Fallback for non-prefab objects (e.g. cloning scene objects)
                instance = UnityEngine.Object.Instantiate(source);
            }

            Undo.RegisterCreatedObjectUndo(instance, "UCP Instantiate");

            // Optional parent
            if (p.TryGetValue("parent", out var parentObj) && parentObj != null)
            {
                int parentId = Convert.ToInt32(parentObj);
                var parent = FindGameObject(parentId);
                instance.transform.SetParent(parent.transform, false);
            }

            // Optional name override
            if (p.TryGetValue("name", out var nameObj) && nameObj != null)
            {
                instance.name = nameObj.ToString();
            }

            // Optional position
            if (p.TryGetValue("position", out var posObj) && posObj is List<object> pos && pos.Count >= 3)
            {
                instance.transform.localPosition = new Vector3(
                    Convert.ToSingle(pos[0]),
                    Convert.ToSingle(pos[1]),
                    Convert.ToSingle(pos[2]));
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            SceneChangeTracker.RecordGameObjectChange(instance, "GameObject");

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["instanceId"] = instance.GetInstanceID(),
                ["name"] = instance.name
            };
        }

        private static object HandleAddComponent(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("instanceId", out var idObj))
                throw new ArgumentException("Missing 'instanceId' parameter");
            if (!p.TryGetValue("type", out var typeObj) || typeObj == null)
                throw new ArgumentException("Missing 'type' parameter");

            int instanceId = Convert.ToInt32(idObj);
            var go = FindGameObject(instanceId);
            string typeName = typeObj.ToString();

            // Resolve component type
            Type compType = ResolveComponentType(typeName);
            if (compType == null)
                throw new ArgumentException($"Component type not found: {typeName}");

            var comp = Undo.AddComponent(go, compType);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            SceneChangeTracker.RecordGameObjectChange(go, comp.GetType().Name);

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["instanceId"] = instanceId,
                ["component"] = comp.GetType().Name,
                ["componentFullType"] = comp.GetType().FullName
            };
        }

        private static object HandleRemoveComponent(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("instanceId", out var idObj))
                throw new ArgumentException("Missing 'instanceId' parameter");
            if (!p.TryGetValue("type", out var typeObj) || typeObj == null)
                throw new ArgumentException("Missing 'type' parameter");

            int instanceId = Convert.ToInt32(idObj);
            var go = FindGameObject(instanceId);
            string typeName = typeObj.ToString();

            Component target = null;
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                if (c.GetType().Name == typeName || c.GetType().FullName == typeName)
                {
                    target = c;
                    break;
                }
            }

            if (target == null)
                throw new ArgumentException($"Component '{typeName}' not found on '{go.name}'");
            if (target is Transform)
                throw new ArgumentException("Cannot remove Transform component");

            Undo.DestroyObjectImmediate(target);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            SceneChangeTracker.RecordGameObjectChange(go, typeName);

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["instanceId"] = instanceId,
                ["removed"] = typeName
            };
        }

        private static Type ResolveComponentType(string typeName)
        {
            // Try exact match first
            var type = Type.GetType(typeName);
            if (type != null && typeof(Component).IsAssignableFrom(type))
                return type;

            // Search Unity assemblies
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var t in assembly.GetTypes())
                {
                    if (!typeof(Component).IsAssignableFrom(t)) continue;
                    if (t.Name == typeName || t.FullName == typeName)
                        return t;
                }
            }

            // Common Unity types fallback
            var unityType = Type.GetType($"UnityEngine.{typeName}, UnityEngine.CoreModule");
            if (unityType != null && typeof(Component).IsAssignableFrom(unityType))
                return unityType;

            return null;
        }

        private static GameObject FindGameObject(int instanceId)
        {
            var obj = UnityObjectCompat.ResolveByInstanceId<GameObject>(instanceId);
            if (obj != null) return obj;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    var found = FindInHierarchy(root, instanceId);
                    if (found != null) return found;
                }
            }

            throw new ArgumentException($"GameObject not found: {instanceId}");
        }

        private static GameObject FindInHierarchy(GameObject go, int instanceId)
        {
            if (go.GetInstanceID() == instanceId) return go;
            for (int i = 0; i < go.transform.childCount; i++)
            {
                var found = FindInHierarchy(go.transform.GetChild(i).gameObject, instanceId);
                if (found != null) return found;
            }
            return null;
        }
    }
}
