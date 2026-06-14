using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UCP.Bridge
{
    /// <summary>
    /// First-class transform authoring: move / rotate / scale / look-at with world|local
    /// space and absolute|relative semantics, plus a bulk transform read. Rotations are
    /// expressed as Euler angles (degrees) on the wire — quaternions are an internal detail.
    ///
    /// Every mutation registers Undo, marks the scene dirty, and records a change digest,
    /// matching the rest of the bridge. No modal APIs are touched.
    /// </summary>
    public static class TransformController
    {
        public static void Register(CommandRouter router)
        {
            router.Register("transform/move", HandleMove);
            router.Register("transform/rotate", HandleRotate);
            router.Register("transform/scale", HandleScale);
            router.Register("transform/look-at", HandleLookAt);
            router.Register("transform/get", HandleGet);
        }

        private static object HandleMove(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            var go = ObjectLocator.Resolve(p);
            var value = ReadVec3(p, "position", required: true).Value;
            var space = ReadSpace(p);
            var relative = ReadBool(p, "relative", false);

            Undo.RecordObject(go.transform, "UCP Move");

            if (space == Space.World)
            {
                go.transform.position = relative ? go.transform.position + value : value;
            }
            else
            {
                go.transform.localPosition = relative ? go.transform.localPosition + value : value;
            }

            return Commit(go, "Transform", ExtraTransform(go));
        }

        private static object HandleRotate(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            var go = ObjectLocator.Resolve(p);
            var euler = ReadVec3(p, "euler", required: true).Value;
            var space = ReadSpace(p);
            var relative = ReadBool(p, "relative", false);

            Undo.RecordObject(go.transform, "UCP Rotate");

            if (relative)
            {
                go.transform.Rotate(euler, space == Space.World ? Space.World : Space.Self);
            }
            else if (space == Space.World)
            {
                go.transform.eulerAngles = euler;
            }
            else
            {
                go.transform.localEulerAngles = euler;
            }

            return Commit(go, "Transform", ExtraTransform(go));
        }

        private static object HandleScale(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            var go = ObjectLocator.Resolve(p);
            var relative = ReadBool(p, "relative", false);

            Vector3 scale;
            if (p != null && p.TryGetValue("uniform", out var uniObj) && uniObj != null)
            {
                var u = Convert.ToSingle(uniObj);
                scale = new Vector3(u, u, u);
            }
            else
            {
                scale = ReadVec3(p, "scale", required: true).Value;
            }

            Undo.RecordObject(go.transform, "UCP Scale");

            if (relative)
            {
                var cur = go.transform.localScale;
                go.transform.localScale = new Vector3(cur.x * scale.x, cur.y * scale.y, cur.z * scale.z);
            }
            else
            {
                go.transform.localScale = scale;
            }

            return Commit(go, "Transform", ExtraTransform(go));
        }

        private static object HandleLookAt(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            var go = ObjectLocator.Resolve(p);

            // Target is either an explicit world point or another object.
            Vector3 targetPoint;
            var explicitPoint = ReadVec3(p, "target", required: false);
            if (explicitPoint.HasValue)
            {
                targetPoint = explicitPoint.Value;
            }
            else if (p != null && (p.ContainsKey("targetId") || p.ContainsKey("targetPath") || p.ContainsKey("targetName")))
            {
                var targetGo = ObjectLocator.Resolve(RemapTargetKeys(p));
                targetPoint = targetGo.transform.position;
            }
            else
            {
                throw new ArgumentException("look-at requires 'target' ([x,y,z]) or 'targetId'/'targetPath'/'targetName'");
            }

            var up = ReadVec3(p, "up", required: false) ?? Vector3.up;

            Undo.RecordObject(go.transform, "UCP LookAt");
            go.transform.LookAt(targetPoint, up);

            return Commit(go, "Transform", ExtraTransform(go));
        }

        private static object HandleGet(string paramsJson)
        {
            // Reported bounds read collider.bounds, which lag transform edits until synced.
            Physics.SyncTransforms();
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;

            // Bulk: 'ids' array → one entry per object, skipping unresolved ids.
            if (p != null && p.TryGetValue("ids", out var idsObj) && idsObj is List<object> ids)
            {
                var list = new List<object>();
                foreach (var idObj in ids)
                {
                    var go = ObjectLocator.FindByInstanceId(Convert.ToInt32(idObj));
                    if (go == null) continue;
                    list.Add(DescribeTransform(go));
                }
                return new Dictionary<string, object> { ["transforms"] = list, ["count"] = list.Count };
            }

            // Single target.
            var single = ObjectLocator.Resolve(p);
            return DescribeTransform(single);
        }

        private static Dictionary<string, object> DescribeTransform(GameObject go)
        {
            var t = go.transform;
            var entry = new Dictionary<string, object>
            {
                ["instanceId"] = go.GetId(),
                ["name"] = go.name,
                ["position"] = ObjectLocator.Vec3(t.position),
                ["localPosition"] = ObjectLocator.Vec3(t.localPosition),
                ["eulerAngles"] = ObjectLocator.Vec3(t.eulerAngles),
                ["localEulerAngles"] = ObjectLocator.Vec3(t.localEulerAngles),
                ["localScale"] = ObjectLocator.Vec3(t.localScale),
                ["lossyScale"] = ObjectLocator.Vec3(t.lossyScale)
            };
            if (ObjectLocator.TryComputeWorldBounds(go, true, out var bounds))
            {
                entry["boundsCenter"] = ObjectLocator.Vec3(bounds.center);
                entry["boundsExtents"] = ObjectLocator.Vec3(bounds.extents);
            }
            return entry;
        }

        private static Dictionary<string, object> ExtraTransform(GameObject go)
        {
            var t = go.transform;
            return new Dictionary<string, object>
            {
                ["instanceId"] = go.GetId(),
                ["name"] = go.name,
                ["position"] = ObjectLocator.Vec3(t.position),
                ["localPosition"] = ObjectLocator.Vec3(t.localPosition),
                ["eulerAngles"] = ObjectLocator.Vec3(t.eulerAngles),
                ["localEulerAngles"] = ObjectLocator.Vec3(t.localEulerAngles),
                ["localScale"] = ObjectLocator.Vec3(t.localScale)
            };
        }

        private static object Commit(GameObject go, string label, Dictionary<string, object> extra)
        {
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            SceneChangeTracker.RecordGameObjectChange(go, label);
            extra["status"] = "ok";
            return extra;
        }

        // --- param helpers -------------------------------------------------

        private static Vector3? ReadVec3(Dictionary<string, object> p, string key, bool required)
        {
            if (p == null || !p.TryGetValue(key, out var v) || v == null)
            {
                if (required) throw new ArgumentException($"Missing '{key}' ([x,y,z]) parameter");
                return null;
            }
            if (v is not List<object> list || list.Count < 3)
                throw new ArgumentException($"'{key}' must be an array of three numbers");
            return new Vector3(Convert.ToSingle(list[0]), Convert.ToSingle(list[1]), Convert.ToSingle(list[2]));
        }

        private static Space ReadSpace(Dictionary<string, object> p)
        {
            if (p != null && p.TryGetValue("space", out var s) && s != null)
            {
                var str = s.ToString().ToLowerInvariant();
                if (str == "local") return Space.Self;
                if (str == "world") return Space.World;
                throw new ArgumentException("'space' must be 'world' or 'local'");
            }
            return Space.World;
        }

        private static bool ReadBool(Dictionary<string, object> p, string key, bool dflt)
        {
            if (p != null && p.TryGetValue(key, out var v) && v is bool b) return b;
            return dflt;
        }

        private static Dictionary<string, object> RemapTargetKeys(Dictionary<string, object> p)
        {
            var remapped = new Dictionary<string, object>();
            if (p.TryGetValue("targetId", out var id)) remapped["instanceId"] = id;
            if (p.TryGetValue("targetPath", out var path)) remapped["path"] = path;
            if (p.TryGetValue("targetName", out var name)) remapped["name"] = name;
            return remapped;
        }
    }
}
