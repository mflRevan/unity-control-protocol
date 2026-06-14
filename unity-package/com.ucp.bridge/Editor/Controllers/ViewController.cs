using System;
using System.Collections.Generic;
using UnityEngine;

namespace UCP.Bridge
{
    /// <summary>
    /// Composed visual perception for agents. Beyond the flat `screenshot` command this adds:
    ///   view/capture  — render from a chosen camera (by id) or a temp camera framed on a
    ///                   target object, with an optional longest-edge cap to keep payloads small.
    ///   view/isolate  — render a single object in isolation, auto-framed from its bounds, from
    ///                   one or more orthographic-style directions, optionally as a composite grid.
    ///   view/orbit    — render a ring of angles around an object as a composite grid so an LLM
    ///                   can perceive 3D shape from one image.
    ///
    /// Isolation uses a temporary camera + RenderTexture and a culling layer; it works headless
    /// (no Scene view required). Inactive child objects are NOT force-activated, so a fully
    /// disabled target renders empty.
    /// </summary>
    public static class ViewController
    {
        // A layer reserved for isolation rendering. 31 is the conventional "free" user layer.
        private const int IsolationLayer = 31;

        public static void Register(CommandRouter router)
        {
            router.Register("view/capture", HandleCapture);
            router.Register("view/isolate", HandleIsolate);
            router.Register("view/orbit", HandleOrbit);
        }

        private static object HandleCapture(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            var maxEdge = (int)ReadFloat(p, "maxEdge", 0f);

            // Frame a temp camera on a target object if one was supplied.
            if (p != null && (p.ContainsKey("targetId") || p.ContainsKey("targetPath") || p.ContainsKey("targetName")))
            {
                var target = ObjectLocator.Resolve(RemapTargetKeys(p));
                ObjectLocator.TryComputeWorldBounds(target, true, out var b);
                var (size, _) = Resolve(maxEdge, 1280, 720);
                var fov = 50f;
                var dir = (ReadVec3Optional(p, "direction") ?? new Vector3(0.4f, 0.3f, -1f)).normalized;
                var (pos, rot, distance) = FitToBounds(b, fov, dir, (float)size.w / size.h);
                var png = Render(pos, rot, fov, size.w, size.h, ~0, Background(p, out var transparent), transparent, distance);
                return PngResult(png, size.w, size.h, new Dictionary<string, object> { ["framedOn"] = target.name });
            }

            // Otherwise render from an explicit camera id, or the main camera.
            Camera cam = null;
            if (p != null && (p.TryGetValue("camera", out var camObj)) && camObj != null)
            {
                var go = ObjectLocator.FindByInstanceId(Convert.ToInt32(camObj));
                if (go != null) cam = go.GetComponent<Camera>();
                if (cam == null) throw new ArgumentException($"No Camera component on object {camObj}");
            }
            cam = cam != null ? cam : (Camera.main != null ? Camera.main : UnityEngine.Object.FindAnyObjectByType<Camera>());
            if (cam == null) throw new Exception("No camera available to capture");

            var aspect = cam.pixelHeight > 0 ? (float)cam.pixelWidth / cam.pixelHeight : 16f / 9f;
            var (csize, _) = Resolve(maxEdge, 1280, Mathf.RoundToInt(1280 / Mathf.Max(aspect, 0.01f)));
            var capturePng = RenderCamera(cam, csize.w, csize.h);
            return PngResult(capturePng, csize.w, csize.h, new Dictionary<string, object> { ["camera"] = cam.gameObject.name });
        }

        private static object HandleIsolate(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            var target = ObjectLocator.Resolve(p);
            var maxEdge = (int)ReadFloat(p, "maxEdge", 512f);
            var edge = Mathf.Clamp(maxEdge, 64, 2048);
            var bg = Background(p, out var transparent);

            var views = ReadViews(p); // e.g. ["front","right","top"] or ["composite"]
            var composite = views.Count == 1 && views[0] == "composite";
            if (composite)
                views = new List<string> { "front", "right", "back", "top" };

            using (var iso = new IsolationScope(target, IsolationLayer))
            {
                ObjectLocator.TryComputeWorldBounds(target, true, out var bounds);
                if (bounds.size == Vector3.zero)
                    bounds = new Bounds(target.transform.position, Vector3.one);

                var rendered = new List<(string view, Texture2D tex)>();
                try
                {
                    foreach (var view in views)
                    {
                        var dir = DirectionFor(view);
                        var fov = 35f;
                        var (pos, rot, distance) = FitToBounds(bounds, fov, dir, 1f);
                        var tex = RenderToTexture(pos, rot, fov, edge, edge, 1 << IsolationLayer, bg, transparent, distance);
                        rendered.Add((view, tex));
                    }

                    if (composite)
                    {
                        var grid = ComposeGrid(rendered, edge, transparent);
                        var png = grid.EncodeToPNG();
                        var gw = grid.width;
                        var gh = grid.height;
                        UnityEngine.Object.DestroyImmediate(grid);
                        return PngResult(png, gw, gh,
                            new Dictionary<string, object> { ["target"] = target.name, ["layout"] = "composite", ["views"] = AsObjects(views) });
                    }

                    if (rendered.Count == 1)
                    {
                        var png = rendered[0].tex.EncodeToPNG();
                        return PngResult(png, edge, edge,
                            new Dictionary<string, object> { ["target"] = target.name, ["view"] = rendered[0].view });
                    }

                    var images = new List<object>();
                    foreach (var (view, tex) in rendered)
                    {
                        images.Add(new Dictionary<string, object>
                        {
                            ["view"] = view,
                            ["encoding"] = "base64",
                            ["format"] = "png",
                            ["data"] = Convert.ToBase64String(tex.EncodeToPNG())
                        });
                    }
                    return new Dictionary<string, object>
                    {
                        ["status"] = "ok", ["target"] = target.name, ["width"] = edge, ["height"] = edge, ["images"] = images
                    };
                }
                finally
                {
                    foreach (var (_, tex) in rendered)
                        if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                }
            }
        }

        private static object HandleOrbit(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            var target = ObjectLocator.Resolve(p);
            var count = Mathf.Clamp((int)ReadFloat(p, "count", 4f), 1, 12);
            var elevation = ReadFloat(p, "elevation", 20f);
            var maxEdge = Mathf.Clamp((int)ReadFloat(p, "maxEdge", 384f), 64, 1024);
            var bg = Background(p, out var transparent);

            using (var iso = new IsolationScope(target, IsolationLayer))
            {
                ObjectLocator.TryComputeWorldBounds(target, true, out var bounds);
                if (bounds.size == Vector3.zero)
                    bounds = new Bounds(target.transform.position, Vector3.one);

                var rendered = new List<(string, Texture2D)>();
                try
                {
                    for (var i = 0; i < count; i++)
                    {
                        var yaw = 360f * i / count;
                        var rot = Quaternion.Euler(elevation, yaw, 0f);
                        var dir = rot * Vector3.forward; // camera looks along +dir toward center
                        var fov = 35f;
                        var (pos, lookRot, distance) = FitToBounds(bounds, fov, -dir, 1f);
                        var tex = RenderToTexture(pos, lookRot, fov, maxEdge, maxEdge, 1 << IsolationLayer, bg, transparent, distance);
                        rendered.Add(($"{Mathf.RoundToInt(yaw)}deg", tex));
                    }

                    var grid = ComposeGrid(rendered, maxEdge, transparent);
                    var png = grid.EncodeToPNG();
                    var w = grid.width; var h = grid.height;
                    UnityEngine.Object.DestroyImmediate(grid);
                    return PngResult(png, w, h,
                        new Dictionary<string, object> { ["target"] = target.name, ["layout"] = "orbit", ["angles"] = count });
                }
                finally
                {
                    foreach (var (_, tex) in rendered)
                        if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                }
            }
        }

        // --- rendering core ------------------------------------------------

        /// <summary>Place a camera so the bounds fill the frame, looking from -dir toward center.</summary>
        private static (Vector3 pos, Quaternion rot, float distance) FitToBounds(Bounds bounds, float fov, Vector3 viewDir, float aspect)
        {
            var radius = Mathf.Max(bounds.extents.magnitude, 0.01f);
            var padding = 1.25f;
            var vFov = fov * Mathf.Deg2Rad;
            // Account for aspect so wide/tall frames still contain the object.
            var effectiveFov = aspect < 1f ? 2f * Mathf.Atan(Mathf.Tan(vFov / 2f) * aspect) : vFov;
            var distance = radius * padding / Mathf.Sin(Mathf.Max(effectiveFov, 0.1f) / 2f);
            var dir = viewDir.sqrMagnitude < 1e-6f ? new Vector3(0.4f, 0.3f, -1f).normalized : viewDir.normalized;
            var pos = bounds.center + dir * distance;
            var rot = Quaternion.LookRotation(bounds.center - pos, Vector3.up);
            return (pos, rot, distance);
        }

        private static Texture2D RenderToTexture(Vector3 pos, Quaternion rot, float fov, int w, int h,
            int cullingMask, Color bg, bool transparent, float distance)
        {
            var camGo = new GameObject("__ucp_view_cam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var prevActive = RenderTexture.active;
            try
            {
                cam.transform.position = pos;
                cam.transform.rotation = rot;
                cam.fieldOfView = fov;
                cam.nearClipPlane = Mathf.Max(0.01f, distance * 0.01f);
                cam.farClipPlane = distance * 4f + 1000f;
                cam.cullingMask = cullingMask;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = transparent ? new Color(bg.r, bg.g, bg.b, 0f) : bg;
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, transparent ? TextureFormat.RGBA32 : TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                return tex;
            }
            finally
            {
                RenderTexture.active = prevActive;
                cam.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(camGo);
            }
        }

        private static byte[] Render(Vector3 pos, Quaternion rot, float fov, int w, int h,
            int cullingMask, Color bg, bool transparent, float distance)
        {
            var tex = RenderToTexture(pos, rot, fov, w, h, cullingMask, bg, transparent, distance);
            try { return tex.EncodeToPNG(); }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
        }

        private static byte[] RenderCamera(Camera cam, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                var png = tex.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(tex);
                return png;
            }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        private static Texture2D ComposeGrid(List<(string view, Texture2D tex)> tiles, int tileEdge, bool transparent)
        {
            var cols = Mathf.CeilToInt(Mathf.Sqrt(tiles.Count));
            var rows = Mathf.CeilToInt((float)tiles.Count / cols);
            var grid = new Texture2D(cols * tileEdge, rows * tileEdge,
                transparent ? TextureFormat.RGBA32 : TextureFormat.RGB24, false);

            // Clear to transparent/black.
            var clear = new Color(0, 0, 0, transparent ? 0f : 1f);
            var fill = new Color[grid.width * grid.height];
            for (var i = 0; i < fill.Length; i++) fill[i] = clear;
            grid.SetPixels(fill);

            for (var i = 0; i < tiles.Count; i++)
            {
                var col = i % cols;
                var row = rows - 1 - (i / cols); // top-left first
                grid.SetPixels(col * tileEdge, row * tileEdge, tileEdge, tileEdge, tiles[i].tex.GetPixels());
            }
            grid.Apply();
            return grid;
        }

        // --- isolation scope ----------------------------------------------

        /// <summary>Temporarily moves a hierarchy onto an isolation layer, restoring on dispose.</summary>
        private sealed class IsolationScope : IDisposable
        {
            private readonly List<(GameObject go, int layer)> _saved = new();

            public IsolationScope(GameObject root, int isolationLayer)
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    _saved.Add((t.gameObject, t.gameObject.layer));
                    t.gameObject.layer = isolationLayer;
                }
            }

            public void Dispose()
            {
                foreach (var (go, layer) in _saved)
                    if (go != null) go.layer = layer;
            }
        }

        // --- param helpers -------------------------------------------------

        private static Vector3 DirectionFor(string view)
        {
            switch (view)
            {
                case "front": return new Vector3(0, 0, -1);
                case "back": return new Vector3(0, 0, 1);
                case "left": return new Vector3(-1, 0, 0);
                case "right": return new Vector3(1, 0, 0);
                case "top": return new Vector3(0, 1, -0.0001f);
                case "bottom": return new Vector3(0, -1, -0.0001f);
                default: return new Vector3(0.4f, 0.3f, -1f);
            }
        }

        private static List<string> ReadViews(Dictionary<string, object> p)
        {
            if (p != null && p.TryGetValue("views", out var v) && v is List<object> list && list.Count > 0)
            {
                var views = new List<string>();
                foreach (var item in list) views.Add(item.ToString().ToLowerInvariant());
                return views;
            }
            if (p != null && p.TryGetValue("view", out var single) && single != null)
                return new List<string> { single.ToString().ToLowerInvariant() };
            return new List<string> { "composite" };
        }

        private static Color Background(Dictionary<string, object> p, out bool transparent)
        {
            transparent = false;
            if (p == null) return new Color(0.18f, 0.18f, 0.2f, 1f);
            if (p.TryGetValue("background", out var b) && b != null && b.ToString().ToLowerInvariant() == "transparent")
            {
                transparent = true;
                return new Color(0, 0, 0, 0);
            }
            if (p.TryGetValue("bgColor", out var c) && c is List<object> col && col.Count >= 3)
            {
                return new Color(Convert.ToSingle(col[0]), Convert.ToSingle(col[1]), Convert.ToSingle(col[2]),
                    col.Count >= 4 ? Convert.ToSingle(col[3]) : 1f);
            }
            return new Color(0.18f, 0.18f, 0.2f, 1f);
        }

        private static ((int w, int h) size, bool capped) Resolve(int maxEdge, int defW, int defH)
        {
            if (maxEdge <= 0) return ((Mathf.Clamp(defW, 64, 4096), Mathf.Clamp(defH, 64, 4096)), false);
            var longest = Mathf.Max(defW, defH);
            var scale = (float)Mathf.Clamp(maxEdge, 64, 4096) / longest;
            return ((Mathf.Max(64, Mathf.RoundToInt(defW * scale)), Mathf.Max(64, Mathf.RoundToInt(defH * scale))), true);
        }

        private static Dictionary<string, object> PngResult(byte[] png, int w, int h, Dictionary<string, object> extra)
        {
            var result = new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["width"] = w,
                ["height"] = h,
                ["format"] = "png",
                ["encoding"] = "base64",
                ["data"] = Convert.ToBase64String(png),
                ["size"] = png.Length
            };
            if (extra != null)
                foreach (var kv in extra) result[kv.Key] = kv.Value;
            return result;
        }

        private static List<object> AsObjects(List<string> items)
        {
            var list = new List<object>();
            foreach (var s in items) list.Add(s);
            return list;
        }

        private static Dictionary<string, object> RemapTargetKeys(Dictionary<string, object> p)
        {
            var remapped = new Dictionary<string, object>();
            if (p.TryGetValue("targetId", out var id)) remapped["instanceId"] = id;
            if (p.TryGetValue("targetPath", out var path)) remapped["path"] = path;
            if (p.TryGetValue("targetName", out var name)) remapped["name"] = name;
            return remapped;
        }

        private static Vector3? ReadVec3Optional(Dictionary<string, object> p, string key)
        {
            if (p == null || !p.TryGetValue(key, out var v) || v == null) return null;
            if (v is not List<object> list || list.Count < 3) return null;
            return new Vector3(Convert.ToSingle(list[0]), Convert.ToSingle(list[1]), Convert.ToSingle(list[2]));
        }

        private static float ReadFloat(Dictionary<string, object> p, string key, float dflt)
        {
            if (p != null && p.TryGetValue(key, out var v) && v != null) return Convert.ToSingle(v);
            return dflt;
        }
    }
}
