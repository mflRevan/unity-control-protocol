using UnityEditor;
using UnityEditor.Compilation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace UCP.Bridge
{
    public static class CompilationController
    {
        public static void Register(CommandRouter router)
        {
            router.Register("compile", HandleCompile);
            router.Register("refresh-assets", HandleRefresh);
            router.Register("script/doctor", HandleScriptDoctor);
        }

        private static object HandleCompile(string paramsJson)
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            CompilationPipeline.RequestScriptCompilation();
            TrySyncSolution();
            return new { status = "ok", message = "Asset database refreshed and compilation requested" };
        }

        private static object HandleRefresh(string paramsJson)
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return new { status = "ok", message = "Asset database refreshed" };
        }

        private static object HandleScriptDoctor(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            var fix = p != null && p.TryGetValue("fix", out var fixObj) && fixObj != null && Convert.ToBoolean(fixObj);
            var projectRoot = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
            var projects = new List<object>();
            var staleProjectCount = 0;
            var missingFileCount = 0;
            var deletedProjectCount = 0;

            foreach (var csproj in Directory.GetFiles(projectRoot, "*.csproj", SearchOption.TopDirectoryOnly))
            {
                var missing = FindMissingCompileItems(projectRoot, csproj);
                if (missing.Count > 0)
                {
                    staleProjectCount++;
                    missingFileCount += missing.Count;
                    if (fix)
                    {
                        File.Delete(csproj);
                        deletedProjectCount++;
                    }
                }

                projects.Add(new Dictionary<string, object>
                {
                    ["path"] = csproj,
                    ["missingCompileItems"] = missing.ConvertAll<object>(item => item),
                    ["stale"] = missing.Count > 0
                });
            }

            if (fix)
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                TrySyncSolution();
            }

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["projectRoot"] = projectRoot,
                ["projectCount"] = projects.Count,
                ["staleProjectCount"] = staleProjectCount,
                ["missingFileCount"] = missingFileCount,
                ["deletedProjectCount"] = deletedProjectCount,
                ["fixed"] = fix,
                ["projects"] = projects
            };
        }

        private static List<string> FindMissingCompileItems(string projectRoot, string csproj)
        {
            var missing = new List<string>();
            var content = File.ReadAllText(csproj);
            foreach (Match match in Regex.Matches(content, "<Compile Include=\"([^\"]+\\.cs)\""))
            {
                var include = match.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar);
                var fullPath = Path.GetFullPath(Path.Combine(projectRoot, include));
                if (!File.Exists(fullPath))
                    missing.Add(include.Replace('\\', '/'));
            }

            return missing;
        }

        private static void TrySyncSolution()
        {
            try
            {
                var syncVs = typeof(Editor).Assembly.GetType("UnityEditor.SyncVS");
                var method = syncVs?.GetMethod("SyncSolution", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                method?.Invoke(null, null);
            }
            catch
            {
                // Best-effort only; Unity may regenerate solution files asynchronously.
            }
        }
    }
}
