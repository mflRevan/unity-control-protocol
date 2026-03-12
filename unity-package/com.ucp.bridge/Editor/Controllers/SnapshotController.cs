using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UCP.Bridge
{
    public static class SnapshotController
    {
        public static void Register(CommandRouter router)
        {
            router.Register("snapshot", HandleSnapshot);
            router.Register("objects/list", HandleListObjects);
            router.Register("objects/components", HandleGetComponents);
            router.Register("objects/transform", HandleGetTransform);
        }

        private static object HandleSnapshot(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            var filter = p != null && p.TryGetValue("filter", out var f) ? f?.ToString() : null;
            var maxDepth = 0;
            if (p != null && p.TryGetValue("depth", out var d))
                maxDepth = System.Convert.ToInt32(d);

            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var objects = new List<object>();
            int totalObjects = 0;
            int totalComponents = 0;

            foreach (var root in roots)
            {
                SerializeGameObject(root, objects, ref totalObjects, ref totalComponents, filter, 0, maxDepth);
            }

            return new Dictionary<string, object>
            {
                ["scene"] = scene.path,
                ["sceneName"] = scene.name,
                ["playMode"] = Application.isPlaying,
                ["timestamp"] = System.DateTime.UtcNow.ToString("o"),
                ["objects"] = objects,
                ["stats"] = new Dictionary<string, object>
                {
                    ["objectCount"] = totalObjects,
                    ["componentCount"] = totalComponents,
                    ["rootCount"] = roots.Length
                }
            };
        }

        private static bool SerializeGameObject(
            GameObject go, List<object> list, ref int objectCount, ref int componentCount,
            string filter, int depth, int maxDepth)
        {
            bool matchesFilter = string.IsNullOrEmpty(filter)
                || go.name.Contains(filter, System.StringComparison.OrdinalIgnoreCase);

            var children = new List<object>();
            if (depth < maxDepth)
            {
                for (int i = 0; i < go.transform.childCount; i++)
                {
                    SerializeGameObject(
                        go.transform.GetChild(i).gameObject,
                        children,
                        ref objectCount,
                        ref componentCount,
                        filter,
                        depth + 1,
                        maxDepth);
                }
            }

            if (!matchesFilter && children.Count == 0)
            {
                return false;
            }

            objectCount++;
            var compList = GetComponentTypes(go, ref componentCount);

            var entry = new Dictionary<string, object>
            {
                ["instanceId"] = go.GetInstanceID(),
                ["name"] = go.name,
                ["active"] = go.activeSelf,
                ["tag"] = go.tag,
                ["layer"] = go.layer,
                ["layerName"] = LayerMask.LayerToName(go.layer),
                ["depth"] = depth,
                ["childCount"] = go.transform.childCount,
                ["components"] = compList,
            };

            if (children.Count > 0)
                entry["children"] = children;

            list.Add(entry);
            return true;
        }

        private static object HandleListObjects(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            var maxDepth = 0;
            if (p != null && p.TryGetValue("depth", out var d))
                maxDepth = Convert.ToInt32(d);

            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var objects = new List<object>();

            foreach (var root in roots)
            {
                ListObjectsRecursive(root, objects, 0, maxDepth);
            }

            return new Dictionary<string, object> { ["objects"] = objects };
        }

        private static void ListObjectsRecursive(GameObject go, List<object> list, int depth, int maxDepth)
        {
            list.Add(new Dictionary<string, object>
            {
                ["instanceId"] = go.GetInstanceID(),
                ["name"] = go.name,
                ["active"] = go.activeSelf,
                ["tag"] = go.tag,
                ["layer"] = go.layer,
                ["layerName"] = LayerMask.LayerToName(go.layer),
                ["childCount"] = go.transform.childCount,
                ["components"] = GetComponentTypes(go),
                ["depth"] = depth
            });

            if (depth < maxDepth)
            {
                for (int i = 0; i < go.transform.childCount; i++)
                    ListObjectsRecursive(go.transform.GetChild(i).gameObject, list, depth + 1, maxDepth);
            }
        }

        private static object HandleGetComponents(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("instanceId", out var idObj))
                throw new System.ArgumentException("Missing 'instanceId' parameter");

            int instanceId = System.Convert.ToInt32(idObj);
            var go = FindByInstanceId(instanceId);
            if (go == null)
                throw new System.Exception($"GameObject not found: {instanceId}");

            var components = go.GetComponents<Component>();
            var list = new List<object>();
            foreach (var c in components)
            {
                if (c == null) continue;
                var dict = new Dictionary<string, object>
                {
                    ["type"] = c.GetType().Name,
                    ["fullType"] = c.GetType().FullName
                };
                if (c is Behaviour b)
                    dict["enabled"] = b.enabled;
                list.Add(dict);
            }

            return new Dictionary<string, object>
            {
                ["instanceId"] = instanceId,
                ["name"] = go.name,
                ["components"] = list
            };
        }

        private static object HandleGetTransform(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("instanceId", out var idObj))
                throw new System.ArgumentException("Missing 'instanceId' parameter");

            int instanceId = System.Convert.ToInt32(idObj);
            var go = FindByInstanceId(instanceId);
            if (go == null)
                throw new System.Exception($"GameObject not found: {instanceId}");

            var t = go.transform;
            return new Dictionary<string, object>
            {
                ["instanceId"] = instanceId,
                ["name"] = go.name,
                ["position"] = new List<object> { (double)t.position.x, (double)t.position.y, (double)t.position.z },
                ["localPosition"] = new List<object> { (double)t.localPosition.x, (double)t.localPosition.y, (double)t.localPosition.z },
                ["rotation"] = new List<object> { (double)t.rotation.x, (double)t.rotation.y, (double)t.rotation.z, (double)t.rotation.w },
                ["eulerAngles"] = new List<object> { (double)t.eulerAngles.x, (double)t.eulerAngles.y, (double)t.eulerAngles.z },
                ["localScale"] = new List<object> { (double)t.localScale.x, (double)t.localScale.y, (double)t.localScale.z }
            };
        }

        private static GameObject FindByInstanceId(int id)
        {
            var obj = UnityEditor.EditorUtility.InstanceIDToObject(id);
            return obj as GameObject;
        }

        private static List<object> GetComponentTypes(GameObject go)
        {
            int ignored = 0;
            return GetComponentTypes(go, ref ignored);
        }

        private static List<object> GetComponentTypes(GameObject go, ref int componentCount)
        {
            var componentTypes = new List<object>();
            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null) continue;
                componentCount++;
                componentTypes.Add(component.GetType().Name);
            }
            return componentTypes;
        }
    }
}
