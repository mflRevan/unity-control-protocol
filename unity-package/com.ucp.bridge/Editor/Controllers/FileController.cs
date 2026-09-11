using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UCP.Bridge
{
    public static class FileController
    {
        public static void Register(CommandRouter router)
        {
            router.Register("file/read", HandleRead);
            router.Register("file/write", HandleWrite);
            router.Register("file/patch", HandlePatch);
        }

        private static string ProjectRoot =>
            Path.GetDirectoryName(Application.dataPath);

        private static string ResolveSafePath(string relativePath)
        {
            var projectRoot = ProjectRoot;
            var fullPath = Path.GetFullPath(Path.Combine(projectRoot, relativePath));

            // Security: ensure path is within project root
            if (!fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException(
                    $"Path escapes project root: {relativePath}");

            return fullPath;
        }

        private static object HandleRead(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("path", out var pathObj))
                throw new ArgumentException("Missing 'path' parameter");

            var fullPath = ResolveSafePath(pathObj.ToString());

            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"File not found: {pathObj}");

            var content = File.ReadAllText(fullPath);

            return new Dictionary<string, object>
            {
                ["path"] = pathObj.ToString(),
                ["content"] = content,
                ["size"] = content.Length
            };
        }

        private static object HandleWrite(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("path", out var pathObj))
                throw new ArgumentException("Missing 'path' parameter");
            if (!p.TryGetValue("content", out var contentObj))
                throw new ArgumentException("Missing 'content' parameter");
            var noReimport = p.TryGetValue("noReimport", out var noReimportObj)
                && noReimportObj != null
                && Convert.ToBoolean(noReimportObj);

            var fullPath = ResolveSafePath(pathObj.ToString());

            // Create directory if needed
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            WriteText(fullPath, contentObj.ToString());
            var reimport = AssetImportSupport.ReimportOrDescribe(pathObj.ToString(), noReimport);

            return new Dictionary<string, object>
            {
                ["path"] = pathObj.ToString(),
                ["written"] = true,
                ["size"] = contentObj.ToString().Length,
                ["reimport"] = reimport
            };
        }

        /// <summary>
        /// Overwrites a file in place. <see cref="File.WriteAllText(string, string)"/> recreates the
        /// file, which Windows refuses for hidden files ("access denied"), and a project in the
        /// "Hidden Meta Files" version-control mode keeps every .meta hidden. Truncating the
        /// existing file keeps its attributes and works in both modes.
        /// </summary>
        internal static void WriteText(string fullPath, string content)
        {
            if (!File.Exists(fullPath))
            {
                File.WriteAllText(fullPath, content);
                return;
            }

            using (var stream = new FileStream(fullPath, FileMode.Truncate, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
            {
                writer.Write(content);
            }
        }

        private static object HandlePatch(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("path", out var pathObj))
                throw new ArgumentException("Missing 'path' parameter");
            if (!p.TryGetValue("patch", out var patchObj))
                throw new ArgumentException("Missing 'patch' parameter");
            var noReimport = p.TryGetValue("noReimport", out var noReimportObj)
                && noReimportObj != null
                && Convert.ToBoolean(noReimportObj);

            var fullPath = ResolveSafePath(pathObj.ToString());

            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"File not found: {pathObj}");

            var original = File.ReadAllText(fullPath);

            // Support patch as either a dict with find/replace keys, or a JSON string
            Dictionary<string, object> patchData = null;

            if (patchObj is Dictionary<string, object> dict)
            {
                patchData = dict;
            }
            else
            {
                var patchContent = patchObj.ToString();
                if (patchContent.TrimStart().StartsWith("{"))
                    patchData = MiniJson.Deserialize(patchContent) as Dictionary<string, object>;
            }

            if (patchData != null &&
                patchData.TryGetValue("find", out var findObj) &&
                patchData.TryGetValue("replace", out var replaceObj))
            {
                var find = findObj.ToString();
                var replace = replaceObj.ToString();
                if (!original.Contains(find))
                    throw new Exception("Patch target not found in file");

                var patched = original.Replace(find, replace);
                WriteText(fullPath, patched);
                var reimport = AssetImportSupport.ReimportOrDescribe(pathObj.ToString(), noReimport);

                return new Dictionary<string, object>
                {
                    ["path"] = pathObj.ToString(),
                    ["patched"] = true,
                    ["reimport"] = reimport
                };
            }

            throw new ArgumentException("Unsupported patch format. Use {\"find\": \"...\", \"replace\": \"...\"}");
        }
    }
}
