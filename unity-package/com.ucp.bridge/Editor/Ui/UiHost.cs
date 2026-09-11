#if UNITY_6000_0_OR_NEWER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace UCP.Bridge
{
    /// <summary>
    /// Transient editor-only host for deterministic UI Toolkit inspection and capture.
    /// The compositor compensates for CaptureEditorWindow treating logical window points
    /// as physical pixels on high-DPI displays.
    /// </summary>
    internal sealed class UiHostWindow : EditorWindow
    {
        private int _viewportWidth;
        private int _viewportHeight;
        private float _compositorScale = 1f;

        internal VisualElement ContentRoot { get; private set; }

        internal static UiHostWindow Open(int width, int height)
        {
            var window = CreateInstance<UiHostWindow>();
            try
            {
                window.hideFlags = HideFlags.HideAndDontSave;
                window._viewportWidth = width;
                window._viewportHeight = height;
                window._compositorScale = 1f / Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
                window.titleContent = new GUIContent("UCP UI Harness");
                window.minSize = new Vector2(width, height);
                window.maxSize = new Vector2(width, height);
                window.position = new Rect(80f, 80f, width, height);
                window.ShowUtility();
                window.EnsureContent();
                window.Focus();
                return window;
            }
            catch
            {
                try
                {
                    CloseAndDestroy(window);
                }
                catch
                {
                    // Preserve the exception that prevented the host from opening.
                }
                throw;
            }
        }

        internal static void CloseAndDestroy(UiHostWindow window)
        {
            if (window == null)
                return;

            var error = UiCleanup.RunAll(
                () => window.Close(),
                () =>
                {
                    if (window != null)
                        UnityEngine.Object.DestroyImmediate(window);
                });
            if (error != null)
                throw error;
        }

        public void CreateGUI()
        {
            EnsureContent();
        }

        internal void EnsureContent()
        {
            if (ContentRoot != null)
                return;

            rootVisualElement.Clear();
            rootVisualElement.style.overflow = Overflow.Hidden;

            ContentRoot = new VisualElement
            {
                name = "ucp-ui-content-root",
                pickingMode = PickingMode.Position
            };
            ContentRoot.style.position = Position.Absolute;
            ContentRoot.style.left = 0f;
            ContentRoot.style.bottom = 0f;
            ContentRoot.style.width = _viewportWidth;
            ContentRoot.style.height = _viewportHeight;
            ContentRoot.style.overflow = Overflow.Hidden;
            ContentRoot.style.transformOrigin = new TransformOrigin(Length.Percent(0f), Length.Percent(100f));
            ContentRoot.style.scale = new Scale(new Vector3(_compositorScale, _compositorScale, 1f));
            rootVisualElement.Add(ContentRoot);
        }

        internal void Pump()
        {
            EnsureContent();
            Focus();
            Repaint();
            EditorApplication.QueuePlayerLoopUpdate();
        }
    }

    internal readonly struct UiGeometrySample
    {
        internal UiGeometrySample(
            string hash,
            int elementCount,
            int renderedElementCount,
            int invalidGeometryCount,
            bool panelAttached)
        {
            Hash = hash;
            ElementCount = elementCount;
            RenderedElementCount = renderedElementCount;
            InvalidGeometryCount = invalidGeometryCount;
            PanelAttached = panelAttached;
        }

        internal string Hash { get; }
        internal int ElementCount { get; }
        internal int RenderedElementCount { get; }
        internal int InvalidGeometryCount { get; }
        internal bool PanelAttached { get; }
        internal bool IsValid => PanelAttached && RenderedElementCount > 0 && InvalidGeometryCount == 0;

        internal Dictionary<string, object> ToDictionary(int stableFrames)
        {
            return new Dictionary<string, object>
            {
                ["panelAttached"] = PanelAttached,
                ["elementCount"] = ElementCount,
                ["renderedElementCount"] = RenderedElementCount,
                ["invalidGeometryCount"] = InvalidGeometryCount,
                ["stableFrames"] = stableFrames,
                ["geometryHash"] = Hash
            };
        }
    }

    internal static class UiGeometrySampler
    {
        internal static UiGeometrySample Measure(VisualElement root)
        {
            var hash = 1469598103934665603UL;
            var elementCount = 0;
            var renderedCount = 0;
            var invalidCount = 0;
            Visit(root, true, ref hash, ref elementCount, ref renderedCount, ref invalidCount);
            return new UiGeometrySample(
                hash.ToString("x16", CultureInfo.InvariantCulture),
                elementCount,
                renderedCount,
                invalidCount,
                root?.panel != null);
        }

        private static void Visit(
            VisualElement element,
            bool ancestorsDisplayed,
            ref ulong hash,
            ref int elementCount,
            ref int renderedCount,
            ref int invalidCount)
        {
            if (element == null)
                return;

            elementCount++;
            var displayed = ancestorsDisplayed && element.resolvedStyle.display != DisplayStyle.None;
            Add(ref hash, element.GetType().FullName);
            Add(ref hash, element.name);
            Add(ref hash, displayed ? "displayed" : "not-displayed");
            Add(ref hash, element.hierarchy.childCount.ToString(CultureInfo.InvariantCulture));
            if (element is TextElement textElement)
                Add(ref hash, textElement.text);

            if (displayed)
            {
                renderedCount++;
                AddRect(ref hash, element.layout, ref invalidCount);
                AddRect(ref hash, element.worldBound, ref invalidCount);
                Add(ref hash, element.resolvedStyle.visibility.ToString());
            }

            foreach (var child in element.hierarchy.Children())
                Visit(child, displayed, ref hash, ref elementCount, ref renderedCount, ref invalidCount);
        }

        private static void AddRect(ref ulong hash, Rect rect, ref int invalidCount)
        {
            if (!UiValue.IsFinite(rect.x) || !UiValue.IsFinite(rect.y) ||
                !UiValue.IsFinite(rect.width) || !UiValue.IsFinite(rect.height))
            {
                invalidCount++;
                Add(ref hash, "<non-finite>");
                return;
            }

            Add(ref hash, Math.Round(rect.x, 3).ToString(CultureInfo.InvariantCulture));
            Add(ref hash, Math.Round(rect.y, 3).ToString(CultureInfo.InvariantCulture));
            Add(ref hash, Math.Round(rect.width, 3).ToString(CultureInfo.InvariantCulture));
            Add(ref hash, Math.Round(rect.height, 3).ToString(CultureInfo.InvariantCulture));
        }

        private static void Add(ref ulong hash, string value)
        {
            value ??= "<null>";
            for (var i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 1099511628211UL;
            }

            hash ^= 0xff;
            hash *= 1099511628211UL;
        }
    }

    internal sealed class UiCaptureSample
    {
        internal byte[] Png { get; set; }
        internal string PixelHash { get; set; }
        internal int Width { get; set; }
        internal int Height { get; set; }
        internal int SampledDistinctColors { get; set; }
        internal int NonTransparentPixels { get; set; }
    }

    internal sealed class UiCaptureSurface : IDisposable
    {
        private RenderTexture _texture;

        internal UiCaptureSurface(int width, int height)
        {
            // The harness is an editor window, and editor windows render in gamma space whatever the
            // project's color space is. The capture must copy those bytes through untouched, so the
            // surface is declared Linear (no sRGB conversion on write or readback). Declaring it sRGB
            // double-encoded the pixels on Linux (washed-out captures) while DirectX ignored the flag.
            _texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = "UCP UI Capture",
                antiAliasing = 1,
                hideFlags = HideFlags.HideAndDontSave
            };
            _texture.Create();
        }

        internal UiCaptureSample Capture(UiHostWindow window)
        {
            if (window == null || _texture == null || !_texture.IsCreated())
                throw new InvalidOperationException("The UI capture surface is unavailable");

            _texture.DiscardContents();
            if (!InternalEditorUtility.CaptureEditorWindow(window, _texture))
                throw new InvalidOperationException("Unity could not capture the UI harness window");

            var previous = RenderTexture.active;
            Texture2D texture = null;
            try
            {
                RenderTexture.active = _texture;
                texture = new Texture2D(_texture.width, _texture.height, TextureFormat.RGBA32, false, false)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                texture.ReadPixels(new Rect(0f, 0f, _texture.width, _texture.height), 0, 0, false);
                texture.Apply(false, false);

                var pixels = texture.GetPixels32();
                var distinct = new HashSet<uint>();
                var nonTransparent = 0;
                var stride = Mathf.Max(1, pixels.Length / 8192);
                for (var i = 0; i < pixels.Length; i++)
                {
                    var pixel = pixels[i];
                    if (pixel.a > 0)
                        nonTransparent++;
                    if (i % stride == 0)
                    {
                        distinct.Add((uint)(pixel.r << 24 | pixel.g << 16 | pixel.b << 8 | pixel.a));
                    }
                }

                var png = texture.EncodeToPNG();
                return new UiCaptureSample
                {
                    Png = png,
                    PixelHash = Sha256(png),
                    Width = _texture.width,
                    Height = _texture.height,
                    SampledDistinctColors = distinct.Count,
                    NonTransparentPixels = nonTransparent
                };
            }
            finally
            {
                RenderTexture.active = previous;
                if (texture != null)
                    UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        public void Dispose()
        {
            var texture = _texture;
            _texture = null;
            if (texture == null)
                return;

            var error = UiCleanup.RunAll(
                () =>
                {
                    if (RenderTexture.active == texture)
                        RenderTexture.active = null;
                },
                () => texture.Release(),
                () =>
                {
                    if (texture != null)
                        UnityEngine.Object.DestroyImmediate(texture);
                });
            if (error != null)
                throw error;
        }

        private static string Sha256(byte[] value)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(value);
                var result = new StringBuilder(bytes.Length * 2);
                foreach (var item in bytes)
                    result.Append(item.ToString("x2", CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }
    }

    internal static class UiHostCapabilities
    {
        internal static void EnsureAvailable(string operation)
        {
            if (Application.isBatchMode)
            {
                throw new UiOperationException(
                    "capability_unavailable",
                    $"ui/{operation} requires an interactive Unity Editor; batch mode supports ui/lint only",
                    Describe());
            }

            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                throw new UiOperationException(
                    "capability_unavailable",
                    $"ui/{operation} requires a graphics device; Unity is using the Null graphics device",
                    Describe());
            }
        }

        internal static Dictionary<string, object> Describe()
        {
            return new Dictionary<string, object>
            {
                ["batchMode"] = Application.isBatchMode,
                ["graphicsDeviceType"] = SystemInfo.graphicsDeviceType.ToString(),
                ["graphicsDeviceName"] = SystemInfo.graphicsDeviceName,
                ["pixelsPerPoint"] = UiValue.FiniteOrNull(EditorGUIUtility.pixelsPerPoint),
                ["editorHost"] = !Application.isBatchMode && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null,
                ["lint"] = true,
                ["layout"] = !Application.isBatchMode && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null,
                ["screenshot"] = !Application.isBatchMode && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null
            };
        }
    }

    internal static class UiCleanup
    {
        internal static Exception RunAll(params Action[] actions)
        {
            Exception firstError = null;
            if (actions == null)
                return null;

            foreach (var action in actions)
            {
                if (action == null)
                    continue;
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    firstError ??= exception;
                }
            }
            return firstError;
        }
    }

    internal sealed class UiOperationException : Exception
    {
        internal UiOperationException(string code, string message, object details = null)
            : base(message)
        {
            Code = code;
            Details = details;
        }

        internal string Code { get; }
        internal object Details { get; }
    }

    internal static class UiValue
    {
        internal static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        internal static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        internal static object FiniteOrNull(float value)
        {
            return IsFinite(value) ? (object)value : null;
        }

        internal static object FiniteOrNull(double value)
        {
            return IsFinite(value) ? (object)value : null;
        }

        internal static string NormalizePath(string path)
        {
            return path?.Replace('\\', '/');
        }

        internal static string SafeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "ui";

            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                builder.Append(Array.IndexOf(invalid, character) >= 0 || character == '/' || character == '\\'
                    ? '-'
                    : character);
            }

            var result = builder.ToString().Trim('.', ' ', '-');
            return string.IsNullOrEmpty(result) ? "ui" : result;
        }
    }
}
#endif
