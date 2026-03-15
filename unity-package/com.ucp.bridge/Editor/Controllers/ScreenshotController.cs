using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace UCP.Bridge
{
    public static class ScreenshotController
    {
        public static void Register(CommandRouter router)
        {
            router.Register("screenshot", HandleScreenshot);
        }

        private static object HandleScreenshot(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            int width = 1920, height = 1080;
            string view = "game";

            if (p != null)
            {
                if (p.TryGetValue("width", out var w)) width = Convert.ToInt32(w);
                if (p.TryGetValue("height", out var h)) height = Convert.ToInt32(h);
                if (p.TryGetValue("view", out var v)) view = v?.ToString() ?? "game";
            }

            // Clamp dimensions for safety
            width = Mathf.Clamp(width, 64, 7680);
            height = Mathf.Clamp(height, 64, 4320);

            byte[] png;

            if (view == "game")
            {
                png = CaptureGameView(width, height);
            }
            else
            {
                // Scene view capture
                png = CaptureSceneView(width, height);
            }

            if (png == null || png.Length == 0)
            {
                throw new Exception("Screenshot capture failed - no camera available");
            }

            string base64 = Convert.ToBase64String(png);

            return new Dictionary<string, object>
            {
                ["width"] = width,
                ["height"] = height,
                ["format"] = "png",
                ["encoding"] = "base64",
                ["data"] = base64,
                ["size"] = png.Length
            };
        }

        private static byte[] CaptureGameView(int width, int height)
        {
            var camera = Camera.main;
            if (camera == null)
            {
                // Try to find any camera
                camera = UnityEngine.Object.FindAnyObjectByType<Camera>();
            }

            if (camera == null)
                throw new Exception("No camera found in scene");

            var rt = new RenderTexture(width, height, 24);
            var prevTarget = camera.targetTexture;

            camera.targetTexture = rt;
            camera.Render();

            RenderTexture.active = rt;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();

            camera.targetTexture = prevTarget;
            RenderTexture.active = null;

            byte[] png = texture.EncodeToPNG();

            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(texture);

            return png;
        }

        private static byte[] CaptureSceneView(int width, int height)
        {
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null || sceneView.camera == null)
                throw new Exception("No active Scene view");

            var camera = sceneView.camera;
            var rt = new RenderTexture(width, height, 24);
            var prevTarget = camera.targetTexture;

            camera.targetTexture = rt;
            camera.Render();

            RenderTexture.active = rt;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();

            camera.targetTexture = prevTarget;
            RenderTexture.active = null;

            byte[] png = texture.EncodeToPNG();

            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(texture);

            return png;
        }
    }
}
