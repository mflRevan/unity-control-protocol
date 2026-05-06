using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UCP.Bridge
{
    public static class SnapshotController
    {
        public static void Register(CommandRouter router)
        {
            router.Register("snapshot", HandleSnapshot);
            router.Register("scene/query", HandleSceneQuery);
            router.Register("object/get-children", HandleGetChildren);
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

        private static object HandleSceneQuery(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("query", out var queryObj) || queryObj == null)
                throw new ArgumentException("Missing 'query' parameter");

            var query = ParseSceneQuery(queryObj.ToString());
            var fields = ParseFields(p.TryGetValue("fields", out var fieldsObj) ? fieldsObj?.ToString() : null);
            var maxDepth = 32;
            if (p.TryGetValue("depth", out var depthObj) && depthObj != null)
                maxDepth = Math.Max(0, Convert.ToInt32(depthObj));

            var scene = SceneManager.GetActiveScene();
            var results = new List<object>();
            foreach (var root in scene.GetRootGameObjects())
                QueryHierarchy(root, query, fields, results, 0, maxDepth);

            return new Dictionary<string, object>
            {
                ["scene"] = scene.path,
                ["sceneName"] = scene.name,
                ["query"] = query.Raw,
                ["count"] = results.Count,
                ["objects"] = results
            };
        }

        private static object HandleGetChildren(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("instanceId", out var idObj))
                throw new ArgumentException("Missing 'instanceId' parameter");

            int instanceId = Convert.ToInt32(idObj);
            int maxDepth = 1;
            if (p.TryGetValue("depth", out var depthObj))
                maxDepth = Math.Max(1, Convert.ToInt32(depthObj));

            var go = FindByInstanceId(instanceId);
            if (go == null)
                throw new Exception($"GameObject not found: {instanceId}");

            var children = new List<object>();
            int objectCount = 0;
            int componentCount = 0;

            for (int i = 0; i < go.transform.childCount; i++)
            {
                children.Add(SerializeGameObjectTree(
                    go.transform.GetChild(i).gameObject,
                    1,
                    maxDepth,
                    ref objectCount,
                    ref componentCount));
            }

            return new Dictionary<string, object>
            {
                ["instanceId"] = instanceId,
                ["name"] = go.name,
                ["active"] = go.activeSelf,
                ["tag"] = go.tag,
                ["layer"] = go.layer,
                ["layerName"] = LayerMask.LayerToName(go.layer),
                ["childCount"] = go.transform.childCount,
                ["requestedDepth"] = maxDepth,
                ["children"] = children,
                ["stats"] = new Dictionary<string, object>
                {
                    ["objectCount"] = objectCount,
                    ["componentCount"] = componentCount
                }
            };
        }

        private static void ListObjectsRecursive(GameObject go, List<object> list, int depth, int maxDepth)
        {
            list.Add(CreateGameObjectEntry(go, depth));

            if (depth < maxDepth)
            {
                for (int i = 0; i < go.transform.childCount; i++)
                    ListObjectsRecursive(go.transform.GetChild(i).gameObject, list, depth + 1, maxDepth);
            }
        }

        private static void QueryHierarchy(
            GameObject go,
            SceneQuery query,
            HashSet<string> fields,
            List<object> results,
            int depth,
            int maxDepth)
        {
            if (MatchesQuery(go, query))
                results.Add(ProjectGameObject(go, fields, depth));

            if (depth >= maxDepth)
                return;

            for (int i = 0; i < go.transform.childCount; i++)
                QueryHierarchy(go.transform.GetChild(i).gameObject, query, fields, results, depth + 1, maxDepth);
        }

        private static Dictionary<string, object> ProjectGameObject(GameObject go, HashSet<string> fields, int depth)
        {
            var entry = new Dictionary<string, object>();
            AddField(entry, fields, "instanceId", go.GetInstanceID());
            AddField(entry, fields, "name", go.name);
            AddField(entry, fields, "active", go.activeSelf);
            AddField(entry, fields, "activeInHierarchy", go.activeInHierarchy);
            AddField(entry, fields, "tag", go.tag);
            AddField(entry, fields, "layer", go.layer);
            AddField(entry, fields, "layerName", LayerMask.LayerToName(go.layer));
            AddField(entry, fields, "depth", depth);
            AddField(entry, fields, "childCount", go.transform.childCount);
            AddField(entry, fields, "components", GetComponentTypes(go));
            return entry;
        }

        private static void AddField(Dictionary<string, object> entry, HashSet<string> fields, string key, object value)
        {
            if (fields.Contains(key))
                entry[key] = value;
        }

        private static HashSet<string> ParseFields(string raw)
        {
            var source = string.IsNullOrEmpty(raw) ? "instanceId,name,active,components" : raw;
            return new HashSet<string>(
                source.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(field => field.Trim()),
                StringComparer.OrdinalIgnoreCase);
        }

        private static SceneQuery ParseSceneQuery(string raw)
        {
            var query = new SceneQuery { Raw = raw ?? string.Empty };
            foreach (var token in SplitQuery(query.Raw))
            {
                var index = token.IndexOf('=');
                if (index <= 0)
                    throw new ArgumentException($"Unsupported query token '{token}'. Use key=value.");

                var key = token.Substring(0, index).Trim().ToLowerInvariant();
                var value = token.Substring(index + 1).Trim().Trim('"');
                switch (key)
                {
                    case "name":
                        query.Name = value;
                        break;
                    case "component":
                    case "components":
                        query.Component = value;
                        break;
                    case "active":
                        query.Active = Convert.ToBoolean(value);
                        break;
                    case "tag":
                        query.Tag = value;
                        break;
                    case "layer":
                        query.Layer = value;
                        break;
                    default:
                        throw new ArgumentException($"Unsupported scene query key '{key}'");
                }
            }

            return query;
        }

        private static IEnumerable<string> SplitQuery(string raw)
        {
            return (raw ?? string.Empty)
                .Replace(" AND ", " and ")
                .Split(new[] { " and ", "," }, StringSplitOptions.RemoveEmptyEntries)
                .Select(token => token.Trim())
                .Where(token => token.Length > 0);
        }

        private static bool MatchesQuery(GameObject go, SceneQuery query)
        {
            if (!string.IsNullOrEmpty(query.Name)
                && !go.name.Contains(query.Name, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(query.Component) && !HasComponent(go, query.Component))
                return false;
            if (query.Active.HasValue && go.activeSelf != query.Active.Value)
                return false;
            if (!string.IsNullOrEmpty(query.Tag) && !string.Equals(go.tag, query.Tag, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(query.Layer) && !LayerMatches(go.layer, query.Layer))
                return false;

            return true;
        }

        private static bool HasComponent(GameObject go, string componentName)
        {
            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null)
                    continue;
                var type = component.GetType();
                if (type.Name.Equals(componentName, StringComparison.OrdinalIgnoreCase)
                    || (type.FullName != null && type.FullName.Equals(componentName, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }

            return false;
        }

        private static bool LayerMatches(int layer, string expected)
        {
            if (int.TryParse(expected, out var parsed))
                return layer == parsed;
            return string.Equals(LayerMask.LayerToName(layer), expected, StringComparison.OrdinalIgnoreCase);
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
            var direct = UnityObjectCompat.ResolveByInstanceId<GameObject>(id);
            if (direct != null)
                return direct;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    var found = FindInHierarchy(root, id);
                    if (found != null)
                        return found;
                }
            }

            return null;
        }

        private static GameObject FindInHierarchy(GameObject go, int instanceId)
        {
            if (go.GetInstanceID() == instanceId)
                return go;

            for (int i = 0; i < go.transform.childCount; i++)
            {
                var found = FindInHierarchy(go.transform.GetChild(i).gameObject, instanceId);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static Dictionary<string, object> SerializeGameObjectTree(
            GameObject go,
            int depth,
            int maxDepth,
            ref int objectCount,
            ref int componentCount)
        {
            objectCount++;

            var entry = CreateGameObjectEntry(go, depth, ref componentCount);
            if (depth < maxDepth)
            {
                var children = new List<object>();
                for (int i = 0; i < go.transform.childCount; i++)
                {
                    children.Add(SerializeGameObjectTree(
                        go.transform.GetChild(i).gameObject,
                        depth + 1,
                        maxDepth,
                        ref objectCount,
                        ref componentCount));
                }

                if (children.Count > 0)
                    entry["children"] = children;
            }

            return entry;
        }

        private static Dictionary<string, object> CreateGameObjectEntry(GameObject go, int depth)
        {
            int ignored = 0;
            return CreateGameObjectEntry(go, depth, ref ignored);
        }

        private static Dictionary<string, object> CreateGameObjectEntry(GameObject go, int depth, ref int componentCount)
        {
            return new Dictionary<string, object>
            {
                ["instanceId"] = go.GetInstanceID(),
                ["name"] = go.name,
                ["active"] = go.activeSelf,
                ["tag"] = go.tag,
                ["layer"] = go.layer,
                ["layerName"] = LayerMask.LayerToName(go.layer),
                ["childCount"] = go.transform.childCount,
                ["components"] = GetComponentTypes(go, ref componentCount),
                ["depth"] = depth
            };
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

        private sealed class SceneQuery
        {
            public string Raw;
            public string Name;
            public string Component;
            public bool? Active;
            public string Tag;
            public string Layer;
        }
    }
}
