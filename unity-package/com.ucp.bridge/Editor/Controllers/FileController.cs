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

            var fullPath = ResolveSafePath(pathObj.ToString());

            // Create directory if needed
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(fullPath, contentObj.ToString());
            RefreshProjectAssets(pathObj.ToString());

            return new Dictionary<string, object>
            {
                ["path"] = pathObj.ToString(),
                ["written"] = true,
                ["size"] = contentObj.ToString().Length
            };
        }

        private static object HandlePatch(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("path", out var pathObj))
                throw new ArgumentException("Missing 'path' parameter");
            if (!p.TryGetValue("patch", out var patchObj))
                throw new ArgumentException("Missing 'patch' parameter");

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
                File.WriteAllText(fullPath, patched);
                RefreshProjectAssets(pathObj.ToString());

                return new Dictionary<string, object>
                {
                    ["path"] = pathObj.ToString(),
                    ["patched"] = true
                };
            }

            throw new ArgumentException("Unsupported patch format. Use {\"find\": \"...\", \"replace\": \"...\"}");
        }

        private static void RefreshProjectAssets(string relativePath)
        {
            var normalized = relativePath.Replace('\\', '/');
            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Packages", StringComparison.OrdinalIgnoreCase))
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
        }
    }
}
