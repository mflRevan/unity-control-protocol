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
        // SessionState survives domain reloads within one editor session. That is exactly the
        // window we need: a compile that SUCCEEDS reloads the domain, a compile that FAILS keeps
        // the old domain (no reload), and in both cases the captured diagnostics must still be
        // readable by the CLI after the editor settles. CompileDiagnosticsTracker writes here;
        // HandleDiagnostics reads it back.
        internal const string DiagStateKey = "ucp.compile.diagnostics.state";
        internal const string DiagRequestKey = "ucp.compile.diagnostics.requestId";
        private const int MaxDiagnosticMessages = 200;

        public static void Register(CommandRouter router)
        {
            router.Register("compile", HandleCompile);
            router.Register("compile/diagnostics", HandleDiagnostics);
            router.Register("refresh-assets", HandleRefresh);
            router.Register("script/doctor", HandleScriptDoctor);
        }

        private static object HandleCompile(string paramsJson)
        {
            // Stamp a fresh request id and reset the diagnostics buffer so the CLI can tell THIS
            // compile's result apart from a stale one. CompileDiagnosticsTracker fills in the
            // per-assembly CompilerMessages as compilation progresses and finishes.
            var requestId = SessionState.GetInt(DiagRequestKey, 0) + 1;
            SessionState.SetInt(DiagRequestKey, requestId);
            SessionState.SetString(DiagStateKey, MiniJson.Serialize(new Dictionary<string, object>
            {
                ["status"] = "requested",
                ["requestId"] = requestId,
                ["errorCount"] = 0,
                ["warningCount"] = 0,
                ["messages"] = new List<object>()
            }));

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            CompilationPipeline.RequestScriptCompilation();
            TrySyncSolution();
            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["message"] = "Asset database refreshed and compilation requested",
                ["requestId"] = requestId
            };
        }

        private static object HandleDiagnostics(string paramsJson)
        {
            var raw = SessionState.GetString(DiagStateKey, string.Empty);
            if (string.IsNullOrEmpty(raw)
                || !(MiniJson.Deserialize(raw) is Dictionary<string, object> state))
            {
                return new Dictionary<string, object>
                {
                    ["status"] = "idle",
                    ["requestId"] = SessionState.GetInt(DiagRequestKey, 0),
                    ["errorCount"] = 0,
                    ["warningCount"] = 0,
                    ["messages"] = new List<object>(),
                    ["compiling"] = EditorApplication.isCompiling
                };
            }

            // Surface live compile state too: a "completed" buffer while isCompiling is true means
            // another compilation has already started after the one the CLI asked about.
            state["compiling"] = EditorApplication.isCompiling;
            return state;
        }

        internal static void WriteDiagnosticsState(string status, int errorCount, int warningCount, List<object> messages)
        {
            var truncated = messages.Count > MaxDiagnosticMessages;
            var trimmed = truncated ? messages.GetRange(0, MaxDiagnosticMessages) : messages;
            SessionState.SetString(DiagStateKey, MiniJson.Serialize(new Dictionary<string, object>
            {
                ["status"] = status,
                ["requestId"] = SessionState.GetInt(DiagRequestKey, 0),
                ["errorCount"] = errorCount,
                ["warningCount"] = warningCount,
                ["truncated"] = truncated,
                ["messages"] = new List<object>(trimmed)
            }));
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

    /// <summary>
    /// Captures per-assembly compiler messages so `compile` can report whether the build actually
    /// succeeded instead of always claiming success. Subscribes once per domain load; accumulates
    /// messages for the in-flight compilation in static fields (safe because a domain reload only
    /// happens AFTER compilationFinished) and flushes them to SessionState, which persists across
    /// the reload for the CLI to read once the editor settles.
    /// </summary>
    [InitializeOnLoad]
    internal static class CompileDiagnosticsTracker
    {
        private static readonly List<object> s_messages = new List<object>();
        private static int s_errorCount;
        private static int s_warningCount;

        static CompileDiagnosticsTracker()
        {
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
        }

        private static void OnCompilationStarted(object context)
        {
            s_messages.Clear();
            s_errorCount = 0;
            s_warningCount = 0;
            CompilationController.WriteDiagnosticsState("compiling", 0, 0, s_messages);
        }

        private static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            if (messages == null) return;
            var assembly = Path.GetFileNameWithoutExtension(assemblyPath);
            foreach (var message in messages)
            {
                string type;
                switch (message.type)
                {
                    case CompilerMessageType.Error:
                        type = "error";
                        s_errorCount++;
                        break;
                    case CompilerMessageType.Warning:
                        type = "warning";
                        s_warningCount++;
                        break;
                    default:
                        type = "info";
                        break;
                }

                s_messages.Add(new Dictionary<string, object>
                {
                    ["assembly"] = assembly,
                    ["type"] = type,
                    ["message"] = message.message ?? string.Empty,
                    ["file"] = message.file ?? string.Empty,
                    ["line"] = message.line,
                    ["column"] = message.column
                });
            }
        }

        private static void OnCompilationFinished(object context)
        {
            CompilationController.WriteDiagnosticsState("completed", s_errorCount, s_warningCount, s_messages);
        }
    }
}
