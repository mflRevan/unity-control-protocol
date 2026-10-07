using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UCP.Bridge
{
    /// <summary>
    /// Replaces method bodies in the running editor without a domain reload.
    ///
    /// The edited files are compiled on their own, against the assemblies already loaded in the
    /// editor, with Unity's bundled Roslyn (`dotnet csc.dll`; Unity's build has no compiler server). The result is a small patch assembly whose types mirror the
    /// originals. For every method whose declaring type still has the same instance field layout,
    /// a Harmony prefix is installed on the original that forwards to the new body and skips the
    /// original. The forwarders are emitted with Reflection.Emit and pass the original `this`
    /// through untyped: Mono's JIT does not re-check the receiver type, and because the layouts
    /// match, field access inside the new body lands on the same memory.
    ///
    /// What survives: method bodies (instance and static, properties, lambdas, async, local
    /// functions), private methods added to a patched type. What needs a real compile: new or
    /// removed fields, new types, signature changes, generics, constructors, anything touching
    /// internals of other types in the same assembly.
    ///
    /// Patches live in the managed domain, so a domain reload drops them. Reloads that are not a
    /// real compile (entering play mode with domain reload on, a manual Ctrl+R) re-apply them
    /// from the file list kept in SessionState; a reload that rebuilt the script assemblies
    /// (`ucp compile`, or any refresh that touched Library/ScriptAssemblies) makes the edits
    /// permanent and the patch list is dropped instead.
    /// </summary>
    public static class HotReloadController
    {
        private const string HarmonyId = "ucp.hot-reload";
        private static Harmony s_harmony;
        private static int s_batch;
        private static bool s_autoRefreshHeld;
        private static readonly List<PatchRecord> s_patches = new List<PatchRecord>();
        private static readonly HashSet<string> s_files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static ModuleBuilder s_module;
        private const string SessionFilesKey = "ucp.hot-reload.files";
        private const string SessionHeldKey = "ucp.hot-reload.held";
        private const string SessionStampKey = "ucp.hot-reload.stamp";

        private sealed class PatchRecord
        {
            public MethodBase Original;
            public string Type;
            public string Method;
            public string File;
            public int Batch;
        }

        public static void Register(CommandRouter router)
        {
            router.Register("hot-reload/apply", HandleApply);
            router.Register("hot-reload/status", _ => BuildStatus());
            router.Register("hot-reload/revert", _ => HandleRevert());
        }

        /// <summary>Let an explicit compile reload the domain; the patches are superseded by it.</summary>
        public static void ReleaseHold()
        {
            SessionState.EraseString(SessionFilesKey);
            SessionState.EraseString(SessionStampKey);
            if (!s_autoRefreshHeld && !SessionState.GetBool(SessionHeldKey, false))
                return;
            s_autoRefreshHeld = false;
            SessionState.EraseBool(SessionHeldKey);
            try { AssetDatabase.AllowAutoRefresh(); }
            catch (Exception) { /* editor shutting down */ }
        }

        private static void HoldAutoRefresh()
        {
            if (s_autoRefreshHeld)
                return;
            // The native hold counter survives domain reloads while this static does not, so a
            // hold that is already in place (recorded in SessionState) is adopted, not doubled.
            if (!SessionState.GetBool(SessionHeldKey, false))
                AssetDatabase.DisallowAutoRefresh();
            SessionState.SetBool(SessionHeldKey, true);
            s_autoRefreshHeld = true;
        }

        /// <summary>
        /// After a domain reload, re-apply the patches that were live before it, unless the
        /// script assemblies were rebuilt in the meantime (then the source edits are compiled in).
        /// </summary>
        [InitializeOnLoadMethod]
        private static void RestoreAfterDomainReload()
        {
            var files = SessionState.GetString(SessionFilesKey, "");
            if (string.IsNullOrEmpty(files))
            {
                if (SessionState.GetBool(SessionHeldKey, false))
                    ReleaseHold(); // stale hold without patches; never leave auto refresh off
                return;
            }
            var stamp = SessionState.GetString(SessionStampKey, "");
            if (stamp != AssemblyStamp())
            {
                Debug.Log("[UCP] hot-reload: script assemblies were rebuilt; the hot-patched edits are now compiled in");
                ReleaseHold();
                return;
            }
            var list = files.Split('\n').Where(f => f.Length > 0).Select(f => (object)f).ToList();
            try
            {
                var result = Apply(list) as Dictionary<string, object>;
                var patched = result != null && result.TryGetValue("patched", out var p) ? (p as List<object>)?.Count ?? 0 : 0;
                Debug.Log($"[UCP] hot-reload: re-applied {patched} patch(es) after the domain reload (edit {string.Join(", ", list)} or run `ucp compile` to make them permanent)");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UCP] hot-reload: could not re-apply patches after the domain reload: {e.Message}");
                ReleaseHold();
            }
        }

        /// <summary>Last-write ticks of every editor script assembly, to detect a real rebuild.</summary>
        private static string AssemblyStamp()
        {
            var sb = new StringBuilder();
            foreach (var a in CompilationPipeline.GetAssemblies(AssembliesType.Editor).OrderBy(a => a.name))
            {
                sb.Append(a.name).Append('=');
                try { sb.Append(File.GetLastWriteTimeUtc(a.outputPath).Ticks); } catch (Exception) { sb.Append('?'); }
                sb.Append(';');
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ apply

        private static object HandleApply(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("files", out var filesObj) || !(filesObj is List<object> fileList) || fileList.Count == 0)
                throw new ArgumentException("Missing 'files': one or more .cs paths relative to the project");
            return Apply(fileList);
        }

        private static object Apply(List<object> fileList)
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var files = fileList.Select(f => NormalizeScriptPath(projectRoot, f.ToString())).ToList();
            var byAssembly = new Dictionary<string, List<string>>();
            foreach (var file in files)
            {
                if (!File.Exists(Path.Combine(projectRoot, file)))
                    throw new ArgumentException($"File not found: {file}");
                var assemblyName = CompilationPipeline.GetAssemblyNameFromScriptPath(file);
                if (string.IsNullOrEmpty(assemblyName))
                    throw new ArgumentException($"{file} does not belong to any script assembly (is it under Assets/ or a package with an asmdef?)");
                if (!byAssembly.TryGetValue(assemblyName, out var list))
                    byAssembly[assemblyName] = list = new List<string>();
                list.Add(file);
            }

            var stopwatch = Stopwatch.StartNew();
            var patched = new List<object>();
            var skipped = new List<object>();
            var needsCompile = new List<object>();
            var errors = new List<object>();
            var compileMs = 0L;

            foreach (var entry in byAssembly)
            {
                var assemblyFileName = entry.Key;
                var editorAssemblies = CompilationPipeline.GetAssemblies(AssembliesType.Editor);
                var target = editorAssemblies.FirstOrDefault(a => string.Equals(Path.GetFileName(a.outputPath), assemblyFileName, StringComparison.OrdinalIgnoreCase));
                if (target == null)
                    throw new InvalidOperationException($"Script assembly {assemblyFileName} is not part of the editor compilation");
                var live = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => !a.IsDynamic && string.Equals(a.GetName().Name + ".dll", assemblyFileName, StringComparison.OrdinalIgnoreCase));
                if (live == null)
                    throw new InvalidOperationException($"{assemblyFileName} is not loaded; run `ucp compile` first");

                var compile = Compile(projectRoot, target, entry.Value);
                compileMs += compile.elapsedMs;
                if (compile.errors.Count > 0)
                {
                    foreach (var error in compile.errors)
                        errors.Add(error);
                    continue;
                }

                var patchAssembly = System.Reflection.Assembly.Load(File.ReadAllBytes(compile.outputPath));
                s_batch++;
                foreach (var newType in patchAssembly.GetTypes())
                {
                    if (newType.Name.Contains("<") || newType.IsNested && newType.DeclaringType != null && newType.DeclaringType.Name.Contains("<"))
                        continue; // compiler-generated (display classes, state machines) live inside the patch assembly and need no detour
                    var originalType = live.GetType(newType.FullName, false);
                    if (originalType == null)
                    {
                        needsCompile.Add(Describe(newType.FullName, null, "new type"));
                        continue;
                    }
                    if (newType.IsGenericTypeDefinition)
                    {
                        skipped.Add(Describe(newType.FullName, null, "generic type"));
                        continue;
                    }
                    var layoutIssue = CompareLayout(originalType, newType);
                    if (layoutIssue != null)
                    {
                        needsCompile.Add(Describe(newType.FullName, null, layoutIssue));
                        continue;
                    }
                    PatchType(originalType, newType, entry.Value, patched, skipped, needsCompile);
                }
                foreach (var file in entry.Value)
                    s_files.Add(file);
            }

            if (patched.Count > 0)
            {
                // Unity would otherwise notice the edited files on the next focus, recompile,
                // and reload the domain, wiping the patches mid-iteration.
                HoldAutoRefresh();
                SessionState.SetString(SessionFilesKey, string.Join("\n", s_files));
                SessionState.SetString(SessionStampKey, AssemblyStamp());
            }

            return new Dictionary<string, object>
            {
                ["status"] = errors.Count == 0 ? "ok" : "compile-failed",
                ["patched"] = patched,
                ["skipped"] = skipped,
                ["needsFullCompile"] = needsCompile,
                ["errors"] = errors,
                ["compileMs"] = compileMs,
                ["totalMs"] = stopwatch.ElapsedMilliseconds,
                ["activePatches"] = s_patches.Count,
                ["autoRefreshHeld"] = s_autoRefreshHeld,
                ["playMode"] = EditorApplication.isPlaying
            };
        }

        private static string NormalizeScriptPath(string projectRoot, string input)
        {
            var path = input.Replace('\\', '/');
            if (Path.IsPathRooted(path))
            {
                var root = projectRoot.Replace('\\', '/').TrimEnd('/') + "/";
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"{input} is outside the project");
                path = path.Substring(root.Length);
            }
            return path;
        }

        private static Dictionary<string, object> Describe(string type, string method, string reason)
        {
            var d = new Dictionary<string, object> { ["type"] = type, ["reason"] = reason };
            if (method != null) d["method"] = method;
            return d;
        }

        // ------------------------------------------------------------------ compile

        private struct CompileResult
        {
            public string outputPath;
            public List<object> errors;
            public long elapsedMs;
        }

        /// <summary>
        /// Unity ships Roslyn in two layouts: `Data/DotNetSdkRoslyn/csc.dll` run by
        /// `Data/NetCoreRuntime/dotnet` (6000.0 through 6000.5), and from 6000.6 a full SDK under
        /// `Data/DotNetSdk` with `sdk/<version>/Roslyn/bincore/csc.dll` and its own `dotnet`.
        /// On macOS the same folders live under `Unity.app/Contents`.
        /// </summary>
        private static bool TryLocateCompiler(out string dotnet, out string csc, out string data)
        {
            var editorRoot = Path.GetDirectoryName(EditorApplication.applicationPath);
            data = Application.platform == RuntimePlatform.OSXEditor
                ? Path.Combine(EditorApplication.applicationPath, "Contents")
                : Path.Combine(editorRoot ?? "", "Data");
            var exe = Application.platform == RuntimePlatform.WindowsEditor ? "dotnet.exe" : "dotnet";

            dotnet = Path.Combine(data, "NetCoreRuntime", exe);
            csc = Path.Combine(data, "DotNetSdkRoslyn", "csc.dll");
            if (File.Exists(dotnet) && File.Exists(csc))
                return true;

            var sdkRoot = Path.Combine(data, "DotNetSdk");
            var sdkDotnet = Path.Combine(sdkRoot, exe);
            var sdks = Path.Combine(sdkRoot, "sdk");
            if (File.Exists(sdkDotnet) && Directory.Exists(sdks))
            {
                foreach (var versionDir in Directory.GetDirectories(sdks).OrderByDescending(d => d, StringComparer.Ordinal))
                {
                    var candidate = Path.Combine(versionDir, "Roslyn", "bincore", "csc.dll");
                    if (File.Exists(candidate))
                    {
                        dotnet = sdkDotnet;
                        csc = candidate;
                        return true;
                    }
                }
            }
            return false;
        }

        private static CompileResult Compile(string projectRoot, UnityEditor.Compilation.Assembly target, List<string> files)
        {
            if (!TryLocateCompiler(out var dotnet, out var csc, out var data))
                throw new InvalidOperationException($"Unity's bundled C# compiler was not found under {data}");

            var outDir = Path.Combine(projectRoot, "Temp", "UcpHotReload");
            Directory.CreateDirectory(outDir);
            var outputPath = Path.Combine(outDir, $"{Path.GetFileNameWithoutExtension(target.outputPath)}.Patch{DateTime.UtcNow.Ticks % 1000000}.dll");

            var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var reference in target.compiledAssemblyReferences)
                references.Add(Path.GetFullPath(reference));
            foreach (var reference in target.assemblyReferences)
                references.Add(Path.GetFullPath(Path.Combine(projectRoot, reference.outputPath)));
            // The assembly being patched itself: every type the edited files mention but do not
            // define resolves to the live original.
            references.Add(Path.GetFullPath(Path.Combine(projectRoot, target.outputPath)));

            var rsp = new StringBuilder();
            rsp.AppendLine("/nologo");
            rsp.AppendLine("/noconfig");
            rsp.AppendLine("/nostdlib+");
            rsp.AppendLine("/target:library");
            rsp.AppendLine("/langversion:9.0");
            rsp.AppendLine("/unsafe+");
            rsp.AppendLine("/optimize-");
            rsp.AppendLine("/debug:portable");
            rsp.AppendLine("/deterministic-");
            // CS0436: a source type shadows the identical type in the referenced original; wanted.
            rsp.AppendLine("/nowarn:0436,1701,1702,0169,0414,0649");
            rsp.AppendLine($"/out:\"{outputPath}\"");
            if (target.defines.Length > 0)
                rsp.AppendLine($"/define:{string.Join(";", target.defines)}");
            foreach (var reference in references)
                if (File.Exists(reference))
                    rsp.AppendLine($"/reference:\"{reference}\"");
            foreach (var file in files)
                rsp.AppendLine($"\"{Path.GetFullPath(Path.Combine(projectRoot, file))}\"");
            var rspPath = outputPath + ".rsp";
            File.WriteAllText(rspPath, rsp.ToString());

            var watch = Stopwatch.StartNew();
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = dotnet,
                    Arguments = $"\"{csc}\" @\"{rspPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = projectRoot,
                }
            };
            var output = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit(120000))
            {
                try { process.Kill(); } catch (Exception) { }
                throw new InvalidOperationException("The C# compiler did not finish within 120s");
            }
            process.WaitForExit();
            watch.Stop();

            var errors = new List<object>();
            foreach (var line in output.ToString().Split('\n'))
            {
                var text = line.Trim();
                if (text.Contains("error CS"))
                    errors.Add(text);
            }
            if (process.ExitCode != 0 && errors.Count == 0)
                errors.Add($"csc exited with {process.ExitCode}: {output.ToString().Trim()}");
            return new CompileResult { outputPath = outputPath, errors = errors, elapsedMs = watch.ElapsedMilliseconds };
        }

        // ------------------------------------------------------------------ patch

        private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        /// <returns>null when the instance field layout matches, otherwise why the type must be recompiled.</returns>
        private static string CompareLayout(Type original, Type patched)
        {
            var a = original.GetFields(Declared).Where(f => !f.IsStatic).Select(f => f.Name + ":" + f.FieldType.FullName).ToList();
            var b = patched.GetFields(Declared).Where(f => !f.IsStatic).Select(f => f.Name + ":" + f.FieldType.FullName).ToList();
            if (a.Count != b.Count)
                return $"instance fields changed ({a.Count} -> {b.Count})";
            for (var i = 0; i < a.Count; i++)
                if (a[i] != b[i])
                    return $"instance field changed: {a[i]} -> {b[i]}";
            var sa = original.GetFields(Declared).Where(f => f.IsStatic).Select(f => f.Name).ToList();
            var sb = patched.GetFields(Declared).Where(f => f.IsStatic).Select(f => f.Name).ToList();
            if (sb.Except(sa).Any())
                return "static field added: " + string.Join(", ", sb.Except(sa));
            return null;
        }

        private static void PatchType(Type original, Type patched, List<string> files,
            List<object> patchedOut, List<object> skipped, List<object> needsCompile)
        {
            s_harmony ??= new Harmony(HarmonyId);
            var file = files.Count == 1 ? files[0] : string.Join(";", files);
            foreach (var newMethod in patched.GetMethods(Declared))
            {
                var label = original.FullName + "." + newMethod.Name;
                if (newMethod.IsAbstract || newMethod.IsGenericMethodDefinition || newMethod.GetMethodBody() == null)
                {
                    skipped.Add(Describe(original.FullName, newMethod.Name, newMethod.IsGenericMethodDefinition ? "generic method" : "no body"));
                    continue;
                }
                if (IsCompilerGenerated(newMethod))
                    continue;
                var originalMethod = FindCounterpart(original, newMethod);
                if (originalMethod == null)
                {
                    // Reachable only from other patched bodies, which bind to the patch assembly.
                    skipped.Add(Describe(original.FullName, newMethod.Name, "added method (callable from patched code only)"));
                    continue;
                }
                if (originalMethod.IsGenericMethodDefinition)
                {
                    skipped.Add(Describe(original.FullName, newMethod.Name, "generic method"));
                    continue;
                }
                var existing = s_patches.FirstOrDefault(r => r.Original == originalMethod);
                if (existing != null)
                {
                    s_harmony.Unpatch(originalMethod, HarmonyPatchType.Prefix, HarmonyId);
                    s_patches.Remove(existing);
                }
                try
                {
                    var prefix = EmitForwarder(originalMethod, newMethod);
                    s_harmony.Patch(originalMethod, prefix: new HarmonyMethod(prefix));
                    s_patches.Add(new PatchRecord { Original = originalMethod, Type = original.FullName, Method = newMethod.Name, File = file, Batch = s_batch });
                    patchedOut.Add(new Dictionary<string, object> { ["type"] = original.FullName, ["method"] = newMethod.Name });
                }
                catch (Exception e)
                {
                    skipped.Add(Describe(original.FullName, newMethod.Name, "patch failed: " + e.Message));
                }
            }
            // Properties and operators are methods too (get_/set_/op_), handled above. Constructors
            // are deliberately not patched: they run once per object and interplay with field
            // initializers, which the layout check already pins down.
            foreach (var ctor in patched.GetConstructors(Declared))
                if (ctor.GetMethodBody() != null && ctor.GetMethodBody().GetILAsByteArray().Length > 8)
                    needsCompile.Add(Describe(original.FullName, ".ctor", "constructor bodies are not hot-patched"));
        }

        private static bool IsCompilerGenerated(MethodInfo method)
        {
            return method.Name.Contains("<") || method.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() != null;
        }

        private static MethodInfo FindCounterpart(Type original, MethodInfo patched)
        {
            var parameters = patched.GetParameters();
            foreach (var candidate in original.GetMethods(Declared))
            {
                if (candidate.Name != patched.Name || candidate.IsStatic != patched.IsStatic)
                    continue;
                var cp = candidate.GetParameters();
                if (cp.Length != parameters.Length)
                    continue;
                var match = true;
                for (var i = 0; i < cp.Length; i++)
                {
                    if (!SameType(cp[i].ParameterType, parameters[i].ParameterType))
                    {
                        match = false;
                        break;
                    }
                }
                if (match && SameType(candidate.ReturnType, patched.ReturnType))
                    return candidate;
            }
            return null;
        }

        /// <summary>Types that live in the patch assembly stand in for their originals by name.</summary>
        private static bool SameType(Type a, Type b)
        {
            if (a == b) return true;
            if (a.IsByRef != b.IsByRef || a.IsArray != b.IsArray || a.IsPointer != b.IsPointer) return false;
            return a.FullName == b.FullName;
        }

        /// <summary>
        /// Emits `static bool Prefix(object __instance, ref R __result, params...)` (or without the
        /// first two for static / void methods) that calls the new body and returns false so Harmony
        /// skips the original.
        /// </summary>
        private static MethodInfo EmitForwarder(MethodInfo original, MethodInfo replacement)
        {
            if (s_module == null)
            {
                var builder = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("UCP.HotReload.Forwarders"), AssemblyBuilderAccess.Run);
                s_module = builder.DefineDynamicModule("UCP.HotReload.Forwarders");
            }
            var typeBuilder = s_module.DefineType($"Forwarder_{s_batch}_{Guid.NewGuid():N}", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
            var originalParams = original.GetParameters();
            var returnsValue = original.ReturnType != typeof(void);
            var paramTypes = new List<Type>();
            var paramNames = new List<string>();
            if (!original.IsStatic) { paramTypes.Add(typeof(object)); paramNames.Add("__instance"); }
            if (returnsValue) { paramTypes.Add(original.ReturnType.MakeByRefType()); paramNames.Add("__result"); }
            foreach (var parameter in originalParams)
            {
                paramTypes.Add(parameter.ParameterType);
                paramNames.Add(parameter.Name);
            }

            var method = typeBuilder.DefineMethod("Prefix", MethodAttributes.Public | MethodAttributes.Static, typeof(bool), paramTypes.ToArray());
            for (var i = 0; i < paramNames.Count; i++)
                method.DefineParameter(i + 1, ParameterAttributes.None, paramNames[i]);

            var il = method.GetILGenerator();
            var argIndex = 0;
            if (!original.IsStatic)
            {
                il.Emit(OpCodes.Ldarg, argIndex); // untyped receiver, same layout as the patched type
                argIndex++;
            }
            var resultArg = -1;
            if (returnsValue)
            {
                resultArg = argIndex;
                argIndex++;
            }
            for (var i = 0; i < originalParams.Length; i++)
                il.Emit(OpCodes.Ldarg, argIndex + i);
            il.Emit(OpCodes.Call, replacement);
            if (returnsValue)
            {
                var local = il.DeclareLocal(original.ReturnType);
                il.Emit(OpCodes.Stloc, local);
                il.Emit(OpCodes.Ldarg, resultArg);
                il.Emit(OpCodes.Ldloc, local);
                il.Emit(OpCodes.Stobj, original.ReturnType);
            }
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);

            var built = typeBuilder.CreateType();
            return built.GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static);
        }

        // ------------------------------------------------------------------ status / revert

        private static object BuildStatus()
        {
            return new Dictionary<string, object>
            {
                ["activePatches"] = s_patches.Count,
                ["patches"] = s_patches.Select(r => (object)new Dictionary<string, object>
                {
                    ["type"] = r.Type, ["method"] = r.Method, ["file"] = r.File, ["batch"] = r.Batch
                }).ToList(),
                ["files"] = s_files.ToList(),
                ["autoRefreshHeld"] = s_autoRefreshHeld,
                ["playMode"] = EditorApplication.isPlaying
            };
        }

        private static object HandleRevert()
        {
            var count = s_patches.Count;
            if (s_harmony != null)
            {
                foreach (var record in s_patches)
                {
                    try { s_harmony.Unpatch(record.Original, HarmonyPatchType.Prefix, HarmonyId); }
                    catch (Exception e) { Debug.LogWarning($"[UCP] hot-reload: could not unpatch {record.Type}.{record.Method}: {e.Message}"); }
                }
            }
            s_patches.Clear();
            s_files.Clear();
            ReleaseHold();
            return new Dictionary<string, object> { ["status"] = "ok", ["reverted"] = count };
        }
    }
}
