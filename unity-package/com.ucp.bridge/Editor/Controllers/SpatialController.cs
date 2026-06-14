using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UCP.Bridge
{
    /// <summary>
    /// Spatial reasoning primitives so an agent can answer geometric questions about a scene
    /// instead of inferring them from raw transforms: raycast, overlap, world bounds, drop-to-
    /// surface (ground), and nearest-object search.
    ///
    /// Physics queries hit colliders only — objects without a Collider are invisible to
    /// raycast/overlap/ground. 'bounds' and 'nearest' fall back to renderer bounds and so also
    /// see render-only objects.
    /// </summary>
    public static class SpatialController
    {
        public static void Register(CommandRouter router)
        {
            router.Register("physics/raycast", HandleRaycast);
            router.Register("physics/overlap", HandleOverlap);
            router.Register("object/bounds", HandleBounds);
            router.Register("spatial/ground", HandleGround);
            router.Register("spatial/nearest", HandleNearest);
        }

        private static object HandleRaycast(string paramsJson)
        {
            // Collider positions in the edit-mode physics scene can lag transform edits made via
            // earlier RPCs; sync so queries see the current state.
            Physics.SyncTransforms();
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            var origin = RequireVec3(p, "origin");
            var direction = RequireVec3(p, "direction");
            if (direction.sqrMagnitude < 1e-8f)
                throw new ArgumentException("'direction' must not be the zero vector");

            var maxDistance = ReadFloat(p, "maxDistance", Mathf.Infinity);
            var layerMask = ReadLayerMask(p);
            var queryTriggers = ReadBool(p, "queryTriggers", false)
                ? QueryTriggerInteraction.Collide
                : QueryTriggerInteraction.Ignore;

            if (Physics.Raycast(new Ray(origin, direction.normalized), out var hit, maxDistance, layerMask, queryTriggers))
            {
                return new Dictionary<string, object>
                {
                    ["status"] = "ok",
                    ["hit"] = true,
                    ["point"] = ObjectLocator.Vec3(hit.point),
                    ["normal"] = ObjectLocator.Vec3(hit.normal),
                    ["distance"] = hit.distance,
                    ["instanceId"] = hit.collider.gameObject.GetId(),
                    ["gameObject"] = hit.collider.gameObject.name,
                    ["collider"] = hit.collider.GetType().Name
                };
            }

            return new Dictionary<string, object> { ["status"] = "ok", ["hit"] = false };
        }

        private static object HandleOverlap(string paramsJson)
        {
            Physics.SyncTransforms();
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            var shape = (ReadString(p, "shape") ?? "sphere").ToLowerInvariant();
            var center = RequireVec3(p, "center");
            var layerMask = ReadLayerMask(p);
            var queryTriggers = ReadBool(p, "queryTriggers", false)
                ? QueryTriggerInteraction.Collide
                : QueryTriggerInteraction.Ignore;

            Collider[] hits;
            switch (shape)
            {
                case "sphere":
                    hits = Physics.OverlapSphere(center, ReadFloat(p, "radius", 1f), layerMask, queryTriggers);
                    break;
                case "box":
                    var half = ReadVec3Optional(p, "halfExtents") ?? Vector3.one * 0.5f;
                    hits = Physics.OverlapBox(center, half, Quaternion.identity, layerMask, queryTriggers);
                    break;
                case "capsule":
                    var end = ReadVec3Optional(p, "end") ?? center;
                    hits = Physics.OverlapCapsule(center, end, ReadFloat(p, "radius", 1f), layerMask, queryTriggers);
                    break;
                default:
                    throw new ArgumentException("'shape' must be 'sphere', 'box', or 'capsule'");
            }

            var list = new List<object>();
            foreach (var c in hits)
            {
                if (c == null) continue;
                list.Add(new Dictionary<string, object>
                {
                    ["instanceId"] = c.gameObject.GetId(),
                    ["gameObject"] = c.gameObject.name,
                    ["collider"] = c.GetType().Name,
                    ["distance"] = Vector3.Distance(center, c.bounds.center)
                });
            }

            return new Dictionary<string, object> { ["status"] = "ok", ["count"] = list.Count, ["hits"] = list };
        }

        private static object HandleBounds(string paramsJson)
        {
            // Collider bounds lag transform edits in edit mode; sync before reading them.
            Physics.SyncTransforms();
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            var go = ObjectLocator.Resolve(p);
            var includeChildren = ReadBool(p, "includeChildren", true);

            if (!ObjectLocator.TryComputeWorldBounds(go, includeChildren, out var bounds))
            {
                // No renderers/colliders: fall back to a zero-size box at the transform.
                bounds = new Bounds(go.transform.position, Vector3.zero);
                return new Dictionary<string, object>
                {
                    ["status"] = "ok",
                    ["instanceId"] = go.GetId(),
                    ["name"] = go.name,
                    ["empty"] = true,
                    ["center"] = ObjectLocator.Vec3(bounds.center),
                    ["extents"] = ObjectLocator.Vec3(bounds.extents),
                    ["size"] = ObjectLocator.Vec3(bounds.size),
                    ["min"] = ObjectLocator.Vec3(bounds.min),
                    ["max"] = ObjectLocator.Vec3(bounds.max)
                };
            }

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["instanceId"] = go.GetId(),
                ["name"] = go.name,
                ["empty"] = false,
                ["center"] = ObjectLocator.Vec3(bounds.center),
                ["extents"] = ObjectLocator.Vec3(bounds.extents),
                ["size"] = ObjectLocator.Vec3(bounds.size),
                ["min"] = ObjectLocator.Vec3(bounds.min),
                ["max"] = ObjectLocator.Vec3(bounds.max)
            };
        }

        private static object HandleGround(string paramsJson)
        {
            Physics.SyncTransforms();
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            var direction = (ReadVec3Optional(p, "direction") ?? Vector3.down).normalized;
            var maxDistance = ReadFloat(p, "maxDistance", 1000f);
            var layerMask = ReadLayerMask(p);
            var apply = ReadBool(p, "apply", true);

            // Two modes: drop an object onto the surface, or just probe a point.
            GameObject go = null;
            Vector3 origin;
            if (p != null && (p.ContainsKey("instanceId") || p.ContainsKey("id") || p.ContainsKey("path") || p.ContainsKey("name")))
            {
                go = ObjectLocator.Resolve(p);
                origin = go.transform.position;
            }
            else
            {
                origin = RequireVec3(p, "point");
            }

            // Offset the ray start slightly against the cast direction so an object already
            // resting on / overlapping the surface still registers a hit.
            var start = origin - direction * 0.01f;
            if (!Physics.Raycast(start, direction, out var hit, maxDistance, layerMask, QueryTriggerInteraction.Ignore))
                return new Dictionary<string, object> { ["status"] = "ok", ["hit"] = false };

            var result = new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["hit"] = true,
                ["point"] = ObjectLocator.Vec3(hit.point),
                ["normal"] = ObjectLocator.Vec3(hit.normal),
                ["distance"] = hit.distance,
                ["surface"] = hit.collider.gameObject.name,
                ["surfaceId"] = hit.collider.gameObject.GetId()
            };

            if (go != null && apply)
            {
                // Rest the object's pivot on the surface, raised by the half-height of its
                // bounds along the up axis so it sits on rather than through the surface.
                var lift = 0f;
                if (ObjectLocator.TryComputeWorldBounds(go, true, out var bounds))
                {
                    var pivotToBottom = go.transform.position.y - bounds.min.y;
                    lift = Mathf.Max(pivotToBottom, 0f);
                }
                Undo.RecordObject(go.transform, "UCP Ground");
                go.transform.position = hit.point + Vector3.up * lift;
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                SceneChangeTracker.RecordGameObjectChange(go, "Transform");
                result["movedId"] = go.GetId();
                result["restPosition"] = ObjectLocator.Vec3(go.transform.position);
            }

            return result;
        }

        private static object HandleNearest(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;

            Vector3 from;
            GameObject self = null;
            if (p != null && (p.ContainsKey("instanceId") || p.ContainsKey("id") || p.ContainsKey("path") || p.ContainsKey("name")))
            {
                self = ObjectLocator.Resolve(p);
                from = self.transform.position;
            }
            else
            {
                from = RequireVec3(p, "point");
            }

            var max = (int)ReadFloat(p, "max", 5f);
            var componentFilter = ReadString(p, "component");
            var tagFilter = ReadString(p, "tag");

            var candidates = new List<(GameObject go, float dist)>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    CollectNearest(root, from, self, componentFilter, tagFilter, candidates);
            }

            candidates.Sort((a, b) => a.dist.CompareTo(b.dist));
            var list = new List<object>();
            for (var i = 0; i < candidates.Count && i < max; i++)
            {
                var (go, dist) = candidates[i];
                list.Add(new Dictionary<string, object>
                {
                    ["instanceId"] = go.GetId(),
                    ["name"] = go.name,
                    ["distance"] = dist,
                    ["position"] = ObjectLocator.Vec3(go.transform.position)
                });
            }

            return new Dictionary<string, object> { ["status"] = "ok", ["count"] = list.Count, ["objects"] = list };
        }

        private static void CollectNearest(GameObject go, Vector3 from, GameObject self,
            string componentFilter, string tagFilter, List<(GameObject, float)> outList)
        {
            var include = go != self;
            if (include && !string.IsNullOrEmpty(componentFilter) && go.GetComponent(componentFilter) == null)
                include = false;
            if (include && !string.IsNullOrEmpty(tagFilter) && !go.CompareTag(tagFilter))
                include = false;
            if (include)
                outList.Add((go, Vector3.Distance(from, go.transform.position)));

            foreach (Transform child in go.transform)
                CollectNearest(child.gameObject, from, self, componentFilter, tagFilter, outList);
        }

        // --- param helpers -------------------------------------------------

        private static Vector3 RequireVec3(Dictionary<string, object> p, string key)
        {
            var v = ReadVec3Optional(p, key);
            if (!v.HasValue) throw new ArgumentException($"Missing '{key}' ([x,y,z]) parameter");
            return v.Value;
        }

        private static Vector3? ReadVec3Optional(Dictionary<string, object> p, string key)
        {
            if (p == null || !p.TryGetValue(key, out var v) || v == null) return null;
            if (v is not List<object> list || list.Count < 3)
                throw new ArgumentException($"'{key}' must be an array of three numbers");
            return new Vector3(Convert.ToSingle(list[0]), Convert.ToSingle(list[1]), Convert.ToSingle(list[2]));
        }

        private static float ReadFloat(Dictionary<string, object> p, string key, float dflt)
        {
            if (p != null && p.TryGetValue(key, out var v) && v != null) return Convert.ToSingle(v);
            return dflt;
        }

        private static bool ReadBool(Dictionary<string, object> p, string key, bool dflt)
        {
            if (p != null && p.TryGetValue(key, out var v) && v is bool b) return b;
            return dflt;
        }

        private static string ReadString(Dictionary<string, object> p, string key)
        {
            if (p != null && p.TryGetValue(key, out var v) && v != null) return v.ToString();
            return null;
        }

        private static int ReadLayerMask(Dictionary<string, object> p)
        {
            // Accept an explicit int mask, or a single layer name, else everything.
            if (p != null && p.TryGetValue("layerMask", out var v) && v != null)
            {
                if (v is string s)
                {
                    var layer = LayerMask.NameToLayer(s);
                    if (layer < 0) throw new ArgumentException($"Unknown layer name '{s}'");
                    return 1 << layer;
                }
                return Convert.ToInt32(v);
            }
            return ~0;
        }
    }
}
