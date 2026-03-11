using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace UCP.Bridge
{
    public static class BuildController
    {
        public static void Register(CommandRouter router)
        {
            router.Register("build/targets", HandleTargets);
            router.Register("build/active-target", HandleActiveTarget);
            router.Register("build/set-target", HandleSetTarget);
            router.Register("build/scenes", HandleScenes);
            router.Register("build/set-scenes", HandleSetScenes);
            router.Register("build/start", HandleStart);
            router.Register("build/defines", HandleDefines);
            router.Register("build/set-defines", HandleSetDefines);
        }

        private static object HandleTargets(string paramsJson)
        {
            var targets = new List<object>();
            foreach (BuildTarget bt in Enum.GetValues(typeof(BuildTarget)))
            {
                if ((int)bt < 0) continue;
                var group = BuildPipeline.GetBuildTargetGroup(bt);
                if (group == BuildTargetGroup.Unknown) continue;

                bool isInstalled = BuildPipeline.IsBuildTargetSupported(group, bt);
                targets.Add(new Dictionary<string, object>
                {
                    ["name"] = bt.ToString(),
                    ["group"] = group.ToString(),
                    ["installed"] = isInstalled,
                    ["isActive"] = bt == EditorUserBuildSettings.activeBuildTarget
                });
            }

            return new Dictionary<string, object>
            {
                ["targets"] = targets,
                ["active"] = EditorUserBuildSettings.activeBuildTarget.ToString(),
                ["activeGroup"] = EditorUserBuildSettings.selectedBuildTargetGroup.ToString()
            };
        }

        private static object HandleActiveTarget(string paramsJson)
        {
            return new Dictionary<string, object>
            {
                ["target"] = EditorUserBuildSettings.activeBuildTarget.ToString(),
                ["group"] = EditorUserBuildSettings.selectedBuildTargetGroup.ToString()
            };
        }

        private static object HandleSetTarget(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("target", out var targetObj))
                throw new ArgumentException("Missing 'target' parameter");

            string targetStr = targetObj.ToString();
            if (!Enum.TryParse<BuildTarget>(targetStr, true, out var target))
                throw new ArgumentException($"Unknown build target: {targetStr}");

            var group = BuildPipeline.GetBuildTargetGroup(target);
            bool success = EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);

            return new Dictionary<string, object>
            {
                ["status"] = success ? "ok" : "failed",
                ["target"] = target.ToString(),
                ["group"] = group.ToString()
            };
        }

        private static object HandleScenes(string paramsJson)
        {
            var scenes = new List<object>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                scenes.Add(new Dictionary<string, object>
                {
                    ["path"] = scene.path,
                    ["enabled"] = scene.enabled,
                    ["guid"] = scene.guid.ToString()
                });
            }

            return new Dictionary<string, object>
            {
                ["scenes"] = scenes
            };
        }

        private static object HandleSetScenes(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("scenes", out var scenesObj))
                throw new ArgumentException("Missing 'scenes' parameter");

            var sceneList = scenesObj as List<object>;
            if (sceneList == null)
                throw new ArgumentException("'scenes' must be an array");

            var editorScenes = new List<EditorBuildSettingsScene>();
            foreach (var s in sceneList)
            {
                if (s is Dictionary<string, object> sd)
                {
                    string path = sd.TryGetValue("path", out var pv) ? pv.ToString() : "";
                    bool enabled = !sd.TryGetValue("enabled", out var ev) || Convert.ToBoolean(ev);
                    editorScenes.Add(new EditorBuildSettingsScene(path, enabled));
                }
                else if (s is string path)
                {
                    editorScenes.Add(new EditorBuildSettingsScene(path, true));
                }
            }

            EditorBuildSettings.scenes = editorScenes.ToArray();

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["count"] = editorScenes.Count
            };
        }

        private static object HandleStart(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;

            string outputPath = null;
            if (p != null && p.TryGetValue("output", out var outObj) && outObj != null)
                outputPath = outObj.ToString();

            BuildOptions options = BuildOptions.None;
            if (p != null && p.TryGetValue("development", out var devObj) && Convert.ToBoolean(devObj))
                options |= BuildOptions.Development;

            // Use build settings scenes
            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
                throw new ArgumentException("No scenes enabled in build settings");

            if (string.IsNullOrEmpty(outputPath))
                outputPath = "Builds/" + EditorUserBuildSettings.activeBuildTarget.ToString() + "/Build";

            var buildOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = EditorUserBuildSettings.activeBuildTarget,
                options = options
            };

            var report = BuildPipeline.BuildPlayer(buildOptions);

            var steps = new List<object>();
            foreach (var step in report.steps)
            {
                steps.Add(new Dictionary<string, object>
                {
                    ["name"] = step.name,
                    ["duration"] = step.duration.TotalSeconds
                });
            }

            return new Dictionary<string, object>
            {
                ["result"] = report.summary.result.ToString(),
                ["totalTime"] = report.summary.totalTime.TotalSeconds,
                ["totalSize"] = report.summary.totalSize,
                ["totalErrors"] = report.summary.totalErrors,
                ["totalWarnings"] = report.summary.totalWarnings,
                ["outputPath"] = report.summary.outputPath,
                ["steps"] = steps
            };
        }

        private static object HandleDefines(string paramsJson)
        {
            var group = EditorUserBuildSettings.selectedBuildTargetGroup;
            string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);

            return new Dictionary<string, object>
            {
                ["group"] = group.ToString(),
                ["defines"] = defines,
                ["list"] = defines.Split(';')
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => (object)s.Trim())
                    .ToList()
            };
        }

        private static object HandleSetDefines(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("defines", out var defObj))
                throw new ArgumentException("Missing 'defines' parameter");

            var group = EditorUserBuildSettings.selectedBuildTargetGroup;
            string defines;

            if (defObj is List<object> defList)
                defines = string.Join(";", defList.Select(d => d.ToString()));
            else
                defines = defObj.ToString();

            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, defines);

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["group"] = group.ToString(),
                ["defines"] = defines
            };
        }
    }
}
