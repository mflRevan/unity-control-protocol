using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UCP.Bridge
{
    public static class EditorSettingsController
    {
        public static void Register(CommandRouter router)
        {
            router.Register("settings/player", HandlePlayerSettings);
            router.Register("settings/player-set", HandleSetPlayerSetting);
            router.Register("settings/quality", HandleQualitySettings);
            router.Register("settings/quality-set", HandleSetQualitySetting);
            router.Register("settings/physics", HandlePhysicsSettings);
            router.Register("settings/physics-set", HandleSetPhysicsSetting);
            router.Register("settings/lighting", HandleLightingSettings);
            router.Register("settings/lighting-set", HandleSetLightingSetting);
            router.Register("settings/tags-layers", HandleTagsAndLayers);
            router.Register("settings/add-tag", HandleAddTag);
            router.Register("settings/add-layer", HandleAddLayer);
        }

        private static object HandlePlayerSettings(string paramsJson)
        {
            return new Dictionary<string, object>
            {
                ["companyName"] = PlayerSettings.companyName,
                ["productName"] = PlayerSettings.productName,
                ["bundleVersion"] = PlayerSettings.bundleVersion,
                ["defaultIsNativeResolution"] = PlayerSettings.defaultIsNativeResolution,
                ["runInBackground"] = PlayerSettings.runInBackground,
                ["colorSpace"] = PlayerSettings.colorSpace.ToString(),
                ["graphicsApi"] = PlayerSettings.GetGraphicsAPIs(EditorUserBuildSettings.activeBuildTarget)?[0].ToString() ?? "Unknown",
                ["scriptingBackend"] = PlayerSettings.GetScriptingBackend(EditorUserBuildSettings.selectedBuildTargetGroup).ToString(),
                ["apiCompatibilityLevel"] = PlayerSettings.GetApiCompatibilityLevel(EditorUserBuildSettings.selectedBuildTargetGroup).ToString(),
                ["targetFrameRate"] = Application.targetFrameRate,
                ["defaultScreenWidth"] = PlayerSettings.defaultScreenWidth,
                ["defaultScreenHeight"] = PlayerSettings.defaultScreenHeight
            };
        }

        private static object HandleSetPlayerSetting(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("key", out var keyObj))
                throw new ArgumentException("Missing 'key' parameter");
            if (!p.ContainsKey("value"))
                throw new ArgumentException("Missing 'value' parameter");

            string key = keyObj.ToString();
            object value = p["value"];

            switch (key)
            {
                case "companyName":
                    PlayerSettings.companyName = value.ToString();
                    break;
                case "productName":
                    PlayerSettings.productName = value.ToString();
                    break;
                case "bundleVersion":
                    PlayerSettings.bundleVersion = value.ToString();
                    break;
                case "runInBackground":
                    PlayerSettings.runInBackground = Convert.ToBoolean(value);
                    break;
                case "defaultIsNativeResolution":
                    PlayerSettings.defaultIsNativeResolution = Convert.ToBoolean(value);
                    break;
                case "defaultScreenWidth":
                    PlayerSettings.defaultScreenWidth = Convert.ToInt32(value);
                    break;
                case "defaultScreenHeight":
                    PlayerSettings.defaultScreenHeight = Convert.ToInt32(value);
                    break;
                case "colorSpace":
                    if (Enum.TryParse<ColorSpace>(value.ToString(), out var cs))
                        PlayerSettings.colorSpace = cs;
                    break;
                default:
                    throw new ArgumentException($"Unknown player setting: {key}");
            }

            return new Dictionary<string, object> { ["status"] = "ok", ["key"] = key };
        }

        private static object HandleQualitySettings(string paramsJson)
        {
            var names = QualitySettings.names;
            var levels = new List<object>();
            for (int i = 0; i < names.Length; i++)
            {
                levels.Add(new Dictionary<string, object>
                {
                    ["index"] = i,
                    ["name"] = names[i],
                    ["isCurrent"] = i == QualitySettings.GetQualityLevel()
                });
            }

            return new Dictionary<string, object>
            {
                ["currentLevel"] = QualitySettings.GetQualityLevel(),
                ["currentName"] = names[QualitySettings.GetQualityLevel()],
                ["levels"] = levels,
                ["shadowDistance"] = (double)QualitySettings.shadowDistance,
                ["shadowCascades"] = QualitySettings.shadowCascades,
                ["antiAliasing"] = QualitySettings.antiAliasing,
                ["vSyncCount"] = QualitySettings.vSyncCount
            };
        }

        private static object HandleSetQualitySetting(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("key", out var keyObj))
                throw new ArgumentException("Missing 'key' parameter");
            if (!p.ContainsKey("value"))
                throw new ArgumentException("Missing 'value' parameter");

            string key = keyObj.ToString();
            object value = p["value"];

            switch (key)
            {
                case "level":
                    QualitySettings.SetQualityLevel(Convert.ToInt32(value));
                    break;
                case "shadowDistance":
                    QualitySettings.shadowDistance = Convert.ToSingle(value);
                    break;
                case "shadowCascades":
                    QualitySettings.shadowCascades = Convert.ToInt32(value);
                    break;
                case "antiAliasing":
                    QualitySettings.antiAliasing = Convert.ToInt32(value);
                    break;
                case "vSyncCount":
                    QualitySettings.vSyncCount = Convert.ToInt32(value);
                    break;
                default:
                    throw new ArgumentException($"Unknown quality setting: {key}");
            }

            return new Dictionary<string, object> { ["status"] = "ok", ["key"] = key };
        }

        private static object HandlePhysicsSettings(string paramsJson)
        {
            return new Dictionary<string, object>
            {
                ["gravity"] = new List<object>
                {
                    (double)Physics.gravity.x,
                    (double)Physics.gravity.y,
                    (double)Physics.gravity.z
                },
                ["defaultSolverIterations"] = Physics.defaultSolverIterations,
                ["defaultSolverVelocityIterations"] = Physics.defaultSolverVelocityIterations,
                ["bounceThreshold"] = (double)Physics.bounceThreshold,
                ["sleepThreshold"] = (double)Physics.sleepThreshold,
                ["defaultContactOffset"] = (double)Physics.defaultContactOffset,
                ["autoSimulation"] = Physics.simulationMode.ToString()
            };
        }

        private static object HandleSetPhysicsSetting(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("key", out var keyObj))
                throw new ArgumentException("Missing 'key' parameter");
            if (!p.ContainsKey("value"))
                throw new ArgumentException("Missing 'value' parameter");

            string key = keyObj.ToString();
            object value = p["value"];

            switch (key)
            {
                case "gravity":
                    if (value is List<object> g && g.Count >= 3)
                        Physics.gravity = new Vector3(
                            Convert.ToSingle(g[0]),
                            Convert.ToSingle(g[1]),
                            Convert.ToSingle(g[2]));
                    break;
                case "defaultSolverIterations":
                    Physics.defaultSolverIterations = Convert.ToInt32(value);
                    break;
                case "defaultSolverVelocityIterations":
                    Physics.defaultSolverVelocityIterations = Convert.ToInt32(value);
                    break;
                case "bounceThreshold":
                    Physics.bounceThreshold = Convert.ToSingle(value);
                    break;
                case "sleepThreshold":
                    Physics.sleepThreshold = Convert.ToSingle(value);
                    break;
                default:
                    throw new ArgumentException($"Unknown physics setting: {key}");
            }

            return new Dictionary<string, object> { ["status"] = "ok", ["key"] = key };
        }

        private static object HandleLightingSettings(string paramsJson)
        {
            var result = new Dictionary<string, object>
            {
                ["ambientMode"] = RenderSettings.ambientMode.ToString(),
                ["ambientIntensity"] = (double)RenderSettings.ambientIntensity,
                ["fog"] = RenderSettings.fog,
                ["fogMode"] = RenderSettings.fogMode.ToString(),
                ["fogDensity"] = (double)RenderSettings.fogDensity,
                ["fogStartDistance"] = (double)RenderSettings.fogStartDistance,
                ["fogEndDistance"] = (double)RenderSettings.fogEndDistance,
            };

            var c = RenderSettings.ambientLight;
            result["ambientColor"] = new List<object> { (double)c.r, (double)c.g, (double)c.b, (double)c.a };

            var fc = RenderSettings.fogColor;
            result["fogColor"] = new List<object> { (double)fc.r, (double)fc.g, (double)fc.b, (double)fc.a };

            if (RenderSettings.skybox != null)
                result["skybox"] = RenderSettings.skybox.name;

            if (RenderSettings.sun != null)
                result["sunSource"] = RenderSettings.sun.gameObject.name;

            return result;
        }

        private static object HandleSetLightingSetting(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("key", out var keyObj))
                throw new ArgumentException("Missing 'key' parameter");
            if (!p.ContainsKey("value"))
                throw new ArgumentException("Missing 'value' parameter");

            string key = keyObj.ToString();
            object value = p["value"];

            switch (key)
            {
                case "ambientMode":
                    if (Enum.TryParse<AmbientMode>(value.ToString(), out var am))
                        RenderSettings.ambientMode = am;
                    break;
                case "ambientIntensity":
                    RenderSettings.ambientIntensity = Convert.ToSingle(value);
                    break;
                case "ambientColor":
                    if (value is List<object> ac && ac.Count >= 3)
                        RenderSettings.ambientLight = new Color(
                            Convert.ToSingle(ac[0]),
                            Convert.ToSingle(ac[1]),
                            Convert.ToSingle(ac[2]),
                            ac.Count >= 4 ? Convert.ToSingle(ac[3]) : 1f);
                    break;
                case "fog":
                    RenderSettings.fog = Convert.ToBoolean(value);
                    break;
                case "fogMode":
                    if (Enum.TryParse<FogMode>(value.ToString(), out var fm))
                        RenderSettings.fogMode = fm;
                    break;
                case "fogDensity":
                    RenderSettings.fogDensity = Convert.ToSingle(value);
                    break;
                case "fogColor":
                    if (value is List<object> fca && fca.Count >= 3)
                        RenderSettings.fogColor = new Color(
                            Convert.ToSingle(fca[0]),
                            Convert.ToSingle(fca[1]),
                            Convert.ToSingle(fca[2]),
                            fca.Count >= 4 ? Convert.ToSingle(fca[3]) : 1f);
                    break;
                case "fogStartDistance":
                    RenderSettings.fogStartDistance = Convert.ToSingle(value);
                    break;
                case "fogEndDistance":
                    RenderSettings.fogEndDistance = Convert.ToSingle(value);
                    break;
                default:
                    throw new ArgumentException($"Unknown lighting setting: {key}");
            }

            return new Dictionary<string, object> { ["status"] = "ok", ["key"] = key };
        }

        private static object HandleTagsAndLayers(string paramsJson)
        {
            var tags = new List<object>();
            foreach (var tag in UnityEditorInternal.InternalEditorUtility.tags)
                tags.Add(tag);

            var layers = new List<object>();
            for (int i = 0; i < 32; i++)
            {
                string name = LayerMask.LayerToName(i);
                if (!string.IsNullOrEmpty(name))
                {
                    layers.Add(new Dictionary<string, object>
                    {
                        ["index"] = i,
                        ["name"] = name
                    });
                }
            }

            var sortingLayers = new List<object>();
            foreach (var sl in SortingLayer.layers)
            {
                sortingLayers.Add(new Dictionary<string, object>
                {
                    ["id"] = sl.id,
                    ["name"] = sl.name,
                    ["value"] = sl.value
                });
            }

            return new Dictionary<string, object>
            {
                ["tags"] = tags,
                ["layers"] = layers,
                ["sortingLayers"] = sortingLayers
            };
        }

        private static object HandleAddTag(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("tag", out var tagObj))
                throw new ArgumentException("Missing 'tag' parameter");

            string tag = tagObj.ToString();

            // Check if tag already exists
            var so = new SerializedObject(
                AssetDatabase.LoadMainAssetAtPath("ProjectSettings/TagManager.asset"));
            var tagsProp = so.FindProperty("tags");

            for (int i = 0; i < tagsProp.arraySize; i++)
            {
                if (tagsProp.GetArrayElementAtIndex(i).stringValue == tag)
                    return new Dictionary<string, object>
                    {
                        ["status"] = "exists",
                        ["tag"] = tag
                    };
            }

            tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
            tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = tag;
            so.ApplyModifiedProperties();
            so.Dispose();

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["tag"] = tag
            };
        }

        private static object HandleAddLayer(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("name", out var nameObj))
                throw new ArgumentException("Missing 'name' parameter");

            string layerName = nameObj.ToString();
            int targetIndex = -1;
            if (p.TryGetValue("index", out var idxObj))
                targetIndex = Convert.ToInt32(idxObj);

            var so = new SerializedObject(
                AssetDatabase.LoadMainAssetAtPath("ProjectSettings/TagManager.asset"));
            var layersProp = so.FindProperty("layers");

            // Check if layer already exists
            for (int i = 0; i < layersProp.arraySize; i++)
            {
                if (layersProp.GetArrayElementAtIndex(i).stringValue == layerName)
                    return new Dictionary<string, object>
                    {
                        ["status"] = "exists",
                        ["name"] = layerName,
                        ["index"] = i
                    };
            }

            // Find first empty user layer (8-31) or use specified index
            int assignedIndex = -1;
            if (targetIndex >= 8 && targetIndex < 32)
            {
                if (string.IsNullOrEmpty(layersProp.GetArrayElementAtIndex(targetIndex).stringValue))
                {
                    layersProp.GetArrayElementAtIndex(targetIndex).stringValue = layerName;
                    assignedIndex = targetIndex;
                }
                else
                {
                    throw new ArgumentException($"Layer index {targetIndex} is already in use");
                }
            }
            else
            {
                for (int i = 8; i < 32; i++)
                {
                    if (string.IsNullOrEmpty(layersProp.GetArrayElementAtIndex(i).stringValue))
                    {
                        layersProp.GetArrayElementAtIndex(i).stringValue = layerName;
                        assignedIndex = i;
                        break;
                    }
                }
            }

            if (assignedIndex < 0)
                throw new InvalidOperationException("No empty layer slots available");

            so.ApplyModifiedProperties();
            so.Dispose();

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["name"] = layerName,
                ["index"] = assignedIndex
            };
        }
    }
}
