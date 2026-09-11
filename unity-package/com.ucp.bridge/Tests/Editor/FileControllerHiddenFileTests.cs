#if UNITY_6000_0_OR_NEWER
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UCP.Bridge.Tests
{
    /// <summary>
    /// Projects in the "Hidden Meta Files" version-control mode keep every .meta file hidden, and
    /// File.WriteAllText cannot recreate a hidden file on Windows. `file/write` must overwrite
    /// such files in place; the QA matrix caught this on a project that had switched modes.
    /// </summary>
    public sealed class FileControllerHiddenFileTests
    {
        private const string AssetPath = "Assets/UcpHiddenWriteProbe.txt";

        [TearDown]
        public void TearDown()
        {
            var full = Path.Combine(Directory.GetCurrentDirectory(), AssetPath);
            if (File.Exists(full))
                File.SetAttributes(full, FileAttributes.Normal);
            AssetDatabase.DeleteAsset(AssetPath);
        }

        [Test]
        public void Write_OverwritesAHiddenFileInPlace()
        {
            var full = Path.Combine(Directory.GetCurrentDirectory(), AssetPath);
            File.WriteAllText(full, "before");
            File.SetAttributes(full, File.GetAttributes(full) | FileAttributes.Hidden);

            FileController.WriteText(full, "after");

            Assert.That(File.ReadAllText(full), Is.EqualTo("after"));
            Assert.That(File.GetAttributes(full) & FileAttributes.Hidden, Is.EqualTo(FileAttributes.Hidden),
                "the file's attributes must survive the write");
        }
    }
}
#endif
