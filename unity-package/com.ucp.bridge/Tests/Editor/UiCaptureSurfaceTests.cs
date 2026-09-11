#if UNITY_6000_0_OR_NEWER
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace UCP.Bridge.Tests
{
    /// <summary>
    /// Color fidelity of <see cref="UiCaptureSurface"/>. The harness window is an editor window,
    /// which Unity renders in gamma space regardless of the project's color space, and the
    /// capture must copy those bytes through unchanged. A surface that applied an sRGB
    /// conversion on the way in or out shows up here as a washed-out or darkened pixel; this
    /// regressed on Linux (GitHub PR #5) while staying invisible on Windows, so the assertion is
    /// exact rather than tolerant.
    /// </summary>
    public sealed class UiCaptureSurfaceTests
    {
        private static readonly Color32 PanelColor = new Color32(28, 30, 38, 255);
        private const int Size = 64;

        [UnityTest]
        public IEnumerator Capture_PreservesAuthoredColorsByteForByte()
        {
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("UI capture needs an interactive editor with a graphics device");

            var window = UiHostWindow.Open(Size, Size);
            UiCaptureSurface surface = null;
            Texture2D decoded = null;
            try
            {
                var panel = new VisualElement { name = "panel" };
                panel.style.position = Position.Absolute;
                panel.style.left = 0f;
                panel.style.top = 0f;
                panel.style.width = Size;
                panel.style.height = Size;
                panel.style.backgroundColor = (Color)PanelColor;
                window.ContentRoot.Add(panel);

                for (var frame = 0; frame < 4; frame++)
                {
                    window.Pump();
                    yield return null;
                }

                surface = new UiCaptureSurface(Size, Size);
                var sample = surface.Capture(window);
                Assert.That(sample.Png, Is.Not.Null.And.Not.Empty);

                decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave };
                Assert.That(ImageConversion.LoadImage(decoded, sample.Png, false), Is.True, "the capture must be a decodable PNG");
                Assert.That(decoded.width, Is.EqualTo(Size));
                Assert.That(decoded.height, Is.EqualTo(Size));

                var center = decoded.GetPixel(Size / 2, Size / 2);
                Assert.That((Color32)center, Is.EqualTo(PanelColor),
                    $"the captured panel color must match the authored USS color exactly; got {(Color32)center}");
            }
            finally
            {
                if (decoded != null)
                    Object.DestroyImmediate(decoded);
                surface?.Dispose();
                UiHostWindow.CloseAndDestroy(window);
            }
        }
    }
}
#endif
