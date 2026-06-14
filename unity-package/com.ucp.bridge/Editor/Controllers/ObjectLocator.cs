using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UCP.Bridge
{
    /// <summary>
    /// Shared GameObject resolution for the spatial/visual controllers.
    ///
    /// A target may be addressed three ways, tried in priority order:
    ///   1. instanceId (int)   — canonical, survives nothing but a domain reload; preferred.
    ///   2. path (string)      — hierarchy path "Root/Child/Leaf" (leading '/' optional),
    ///                           resolved across all loaded scenes; survives reloads.
    ///   3. name (string)      — first GameObject whose name matches; ambiguous under
    ///                           duplicates, so it is the last resort.
    ///
    /// instanceId stays the deterministic handle. path/name are convenience fallbacks so an
    /// agent does not have to re-snapshot after every reload just to re-acquire an id.
    /// </summary>
    internal static class ObjectLocator
    {
        /// <summary>
        /// Resolve a GameObject from a params dictionary using whichever of
        /// instanceId / id / path / name is present (in that priority order).
        /// Throws ArgumentException if none are present or nothing resolves.
        /// </summary>
        internal static GameObject Resolve(Dictionary<string, object> p)
        {
            if (p == null)
                throw new ArgumentException("Missing target: provide 'instanceId', 'path', or 'name'");

            if ((p.TryGetValue("instanceId", out var idObj) || p.TryGetValue("id", out idObj)) && idObj != null)
            {
                var id = Convert.ToInt32(idObj);
                var byId = FindByInstanceId(id);
                if (byId != null)
                    return byId;
                throw new ArgumentException($"GameObject not found for instanceId {id}");
            }

            if (p.TryGetValue("path", out var pathObj) && pathObj != null)
            {
                var path = pathObj.ToString();
                var byPath = FindByPath(path);
                if (byPath != null)
                    return byPath;
                throw new ArgumentException($"GameObject not found for path '{path}'");
            }

            if (p.TryGetValue("name", out var nameObj) && nameObj != null)
            {
                var name = nameObj.ToString();
                var byName = FindByName(name);
                if (byName != null)
                    return byName;
                throw new ArgumentException($"GameObject not found for name '{name}'");
            }

            throw new ArgumentException("Missing target: provide 'instanceId', 'path', or 'name'");
        }

        internal static GameObject FindByInstanceId(int instanceId)
        {
            var direct = UnityObjectCompat.ResolveByInstanceId<GameObject>(instanceId);
            if (direct != null)
                return direct;

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    var found = FindInHierarchyById(root, instanceId);
                    if (found != null)
                        return found;
                }
            }

            return null;
        }

        private static GameObject FindInHierarchyById(GameObject go, int instanceId)
        {
            if (go.GetId() == instanceId)
                return go;
            foreach (Transform child in go.transform)
            {
                var found = FindInHierarchyById(child.gameObject, instanceId);
                if (found != null)
                    return found;
            }
            return null;
        }

        internal static GameObject FindByPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var trimmed = path.Trim('/');
            var segments = trimmed.Split('/');
            if (segments.Length == 0)
                return null;

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name != segments[0])
                        continue;
                    var resolved = WalkPath(root.transform, segments, 1);
                    if (resolved != null)
                        return resolved;
                }
            }

            return null;
        }

        private static GameObject WalkPath(Transform current, string[] segments, int index)
        {
            if (index >= segments.Length)
                return current.gameObject;

            foreach (Transform child in current)
            {
                if (child.name == segments[index])
                {
                    var resolved = WalkPath(child, segments, index + 1);
                    if (resolved != null)
                        return resolved;
                }
            }

            return null;
        }

        internal static GameObject FindByName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    var found = FindInHierarchyByName(root, name);
                    if (found != null)
                        return found;
                }
            }

            return null;
        }

        private static GameObject FindInHierarchyByName(GameObject go, string name)
        {
            if (go.name == name)
                return go;
            foreach (Transform child in go.transform)
            {
                var found = FindInHierarchyByName(child.gameObject, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        /// <summary>Compute a world-space AABB encapsulating the object's renderers and colliders.</summary>
        internal static bool TryComputeWorldBounds(GameObject target, bool includeChildren, out Bounds bounds)
        {
            var hasBounds = false;
            bounds = new Bounds(target.transform.position, Vector3.zero);

            var renderers = includeChildren
                ? target.GetComponentsInChildren<Renderer>()
                : target.GetComponents<Renderer>();
            foreach (var renderer in renderers)
            {
                if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                else bounds.Encapsulate(renderer.bounds);
            }

            var colliders = includeChildren
                ? target.GetComponentsInChildren<Collider>()
                : target.GetComponents<Collider>();
            foreach (var collider in colliders)
            {
                if (!hasBounds) { bounds = collider.bounds; hasBounds = true; }
                else bounds.Encapsulate(collider.bounds);
            }

            return hasBounds;
        }

        internal static List<object> Vec3(Vector3 v) => new List<object> { v.x, v.y, v.z };
    }
}
