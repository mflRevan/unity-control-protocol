using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UCP.Bridge
{
    public static class MaterialController
    {
        public static void Register(CommandRouter router)
        {
            router.Register("material/create", HandleCreate);
            router.Register("material/get-properties", HandleGetProperties);
            router.Register("material/get-property", HandleGetProperty);
            router.Register("material/set-property", HandleSetProperty);
            router.Register("material/get-keywords", HandleGetKeywords);
            router.Register("material/set-keyword", HandleSetKeyword);
            router.Register("material/set-shader", HandleSetShader);
        }

        private static object HandleCreate(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("path", out var pathObj) || pathObj == null)
                throw new ArgumentException("Missing 'path' parameter");

            string path = pathObj.ToString();
            string shaderName = p.TryGetValue("shader", out var shaderObj) && shaderObj != null
                ? shaderObj.ToString()
                : null;

            var shader = ResolveCreateShader(shaderName);
            if (shader == null)
                throw new ArgumentException("Could not resolve a shader for material creation");

            string dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                CreateFoldersRecursive(dir);
            }

            var material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
            AssetDatabase.SaveAssets();

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["path"] = path,
                ["name"] = material.name,
                ["shader"] = shader.name,
                ["instanceId"] = material.GetId()
            };
        }

        private static object HandleGetProperties(string paramsJson)
        {
            var mat = ResolveMaterial(paramsJson);
            var shader = mat.shader;
            int count = shader.GetPropertyCount();

            var properties = new List<object>();
            for (int i = 0; i < count; i++)
            {
                var propName = shader.GetPropertyName(i);
                var propType = shader.GetPropertyType(i);
                var propDesc = shader.GetPropertyDescription(i);

                var propInfo = new Dictionary<string, object>
                {
                    ["name"] = propName,
                    ["type"] = propType.ToString(),
                    ["description"] = propDesc,
                    ["value"] = ReadMaterialValue(mat, propName, propType)
                };

                properties.Add(propInfo);
            }

            return new Dictionary<string, object>
            {
                ["material"] = mat.name,
                ["shader"] = shader.name,
                ["properties"] = properties
            };
        }

        private static object HandleGetProperty(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("property", out var propObj))
                throw new ArgumentException("Missing 'property' parameter");

            var mat = ResolveMaterial(paramsJson);
            string propName = propObj.ToString();
            var shader = mat.shader;

            int propIdx = shader.FindPropertyIndex(propName);
            if (propIdx < 0)
                throw new ArgumentException($"Property '{propName}' not found on shader {shader.name}");

            var propType = shader.GetPropertyType(propIdx);

            return new Dictionary<string, object>
            {
                ["material"] = mat.name,
                ["property"] = propName,
                ["type"] = propType.ToString(),
                ["value"] = ReadMaterialValue(mat, propName, propType)
            };
        }

        private static object HandleSetProperty(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("property", out var propObj))
                throw new ArgumentException("Missing 'property' parameter");
            if (!p.ContainsKey("value"))
                throw new ArgumentException("Missing 'value' parameter");

            var mat = ResolveMaterial(paramsJson);
            string propName = propObj.ToString();
            var shader = mat.shader;

            int propIdx = shader.FindPropertyIndex(propName);
            if (propIdx < 0)
                throw new ArgumentException($"Property '{propName}' not found on shader {shader.name}");

            var propType = shader.GetPropertyType(propIdx);
            Undo.RecordObject(mat, $"UCP Set Material {propName}");
            WriteMaterialValue(mat, propName, propType, p["value"]);
            var finish = FinishMaterialEdit(mat);

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["material"] = mat.name,
                ["property"] = propName,
                ["value"] = ReadMaterialValue(mat, propName, propType),
                ["keywordsReset"] = finish.keywordsReset,
                ["saved"] = finish.saved
            };
        }

        /// <summary>
        /// What the Inspector does after an edit and a bare property write skips: re-derive the
        /// shader keywords that depend on property values (HDRP and URP keep a lot of state there,
        /// so without this a toggled `_LayerCount` or `_VertexColorMode` has no visible effect),
        /// then write the asset to disk so the change survives the session.
        /// </summary>
        private static (string keywordsReset, bool saved) FinishMaterialEdit(Material mat, bool resetKeywords = true)
        {
            EditorUtility.SetDirty(mat);
            string keywordsReset = null;
            if (resetKeywords)
                keywordsReset = ResetPipelineKeywords(mat);
            var saved = false;
            if (AssetDatabase.Contains(mat))
            {
                AssetDatabase.SaveAssetIfDirty(mat);
                saved = true;
            }
            return (keywordsReset, saved);
        }

        // The pipeline editor assemblies are not referenced by the bridge (it must load without
        // them), so the helpers that re-derive keywords from property values are found by name.
        private static readonly (string type, string method)[] KeywordResetters =
        {
            ("UnityEditor.Rendering.HighDefinition.HDShaderUtils", "ResetMaterialKeywords"),
            ("UnityEditor.Rendering.Universal.ShaderUtils", "ResetMaterialKeywords"),
            ("Unity.Rendering.Universal.ShaderUtils", "ResetMaterialKeywords"),
        };

        /// <returns>The helper that ran, or "propertyDrawers" for the generic fallback.</returns>
        private static string ResetPipelineKeywords(Material mat)
        {
            foreach (var (typeName, methodName) in KeywordResetters)
            {
                var type = FindLoadedType(typeName);
                if (type == null)
                    continue;
                var method = type.GetMethod(methodName,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static,
                    null, new[] { typeof(Material) }, null);
                if (method == null)
                    continue;
                try
                {
                    method.Invoke(null, new object[] { mat });
                    return type.Name + "." + methodName;
                }
                catch (Exception)
                {
                    // A shader this pipeline helper does not own throws; fall through to the next.
                }
            }
            var urp = TryUrpUpdateMaterial(mat);
            if (urp == null)
                return "ShaderUtils.UpdateMaterial";
            if (urp != "no URP")
                return "propertyDrawers (URP helper failed: " + urp + ")";
            // Built-in pipeline and custom shaders: property drawers own the keyword rules.
            MaterialEditor.ApplyMaterialPropertyDrawers(mat);
            return "propertyDrawers";
        }

        /// <summary>
        /// URP has no public one-call reset; its internal
        /// <c>ShaderUtils.UpdateMaterial(Material, MaterialUpdateType.ModifiedMaterial, ShaderID)</c>
        /// is what the Lit/Unlit inspectors run after an edit.
        /// </summary>
        private static string TryUrpUpdateMaterial(Material mat)
        {
            // URP 17 moved the class to Unity.Rendering.Universal; older releases use UnityEditor.*.
            var utils = FindLoadedType("Unity.Rendering.Universal.ShaderUtils")
                ?? FindLoadedType("UnityEditor.Rendering.Universal.ShaderUtils");
            if (utils == null)
                return "no URP";
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            var getShaderId = utils.GetMethod("GetShaderID", flags, null, new[] { typeof(Shader) }, null);
            System.Reflection.MethodInfo update = null;
            foreach (var candidate in utils.GetMethods(flags))
            {
                if (candidate.Name != "UpdateMaterial")
                    continue;
                var parameters = candidate.GetParameters();
                if (parameters.Length == 3 && parameters[0].ParameterType == typeof(Material) && parameters[2].ParameterType.Name == "ShaderID")
                {
                    update = candidate;
                    break;
                }
            }
            if (update == null || getShaderId == null)
                return "UpdateMaterial/GetShaderID not found on " + utils.Assembly.GetName().Name;
            try
            {
                var updateType = Enum.Parse(update.GetParameters()[1].ParameterType, "ModifiedMaterial");
                var shaderId = getShaderId.Invoke(null, new object[] { mat.shader });
                update.Invoke(null, new[] { mat, updateType, shaderId });
                return null;
            }
            catch (Exception e)
            {
                return (e.InnerException ?? e).Message;
            }
        }

        private static Type FindLoadedType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = null;
                try { type = assembly.GetType(fullName, false); }
                catch (Exception) { /* dynamic or broken assembly */ }
                if (type != null)
                    return type;
            }
            return null;
        }

        private static object HandleGetKeywords(string paramsJson)
        {
            var mat = ResolveMaterial(paramsJson);
            var keywords = new List<object>();
            foreach (var kw in mat.enabledKeywords)
                keywords.Add(kw.name);

            return new Dictionary<string, object>
            {
                ["material"] = mat.name,
                ["keywords"] = keywords
            };
        }

        private static object HandleSetKeyword(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("keyword", out var kwObj))
                throw new ArgumentException("Missing 'keyword' parameter");
            if (!p.TryGetValue("enabled", out var enObj))
                throw new ArgumentException("Missing 'enabled' parameter");

            var mat = ResolveMaterial(paramsJson);
            string keyword = kwObj.ToString();
            bool enabled = Convert.ToBoolean(enObj);

            Undo.RecordObject(mat, $"UCP Set Keyword {keyword}");
            if (enabled)
                mat.EnableKeyword(keyword);
            else
                mat.DisableKeyword(keyword);
            var finish = FinishMaterialEdit(mat, resetKeywords: false);

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["material"] = mat.name,
                ["keyword"] = keyword,
                ["enabled"] = enabled,
                ["saved"] = finish.saved
            };
        }

        private static object HandleSetShader(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("shader", out var shaderObj))
                throw new ArgumentException("Missing 'shader' parameter");

            var mat = ResolveMaterial(paramsJson);
            string shaderName = shaderObj.ToString();
            var shader = Shader.Find(shaderName);
            if (shader == null)
                throw new ArgumentException($"Shader not found: {shaderName}");

            Undo.RecordObject(mat, "UCP Set Shader");
            mat.shader = shader;
            FinishMaterialEdit(mat);

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["material"] = mat.name,
                ["shader"] = shaderName
            };
        }

        // ---- Helper methods ----

        private static Material ResolveMaterial(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null)
                throw new ArgumentException("Invalid parameters");

            // Resolve by asset path
            if (p.TryGetValue("path", out var pathObj) && pathObj != null)
            {
                string path = pathObj.ToString();
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                    throw new ArgumentException($"Material not found at: {path}");
                return mat;
            }

            // Resolve by instanceId (scene object's renderer material)
            if (p.TryGetValue("instanceId", out var idObj))
            {
                long instanceId = Convert.ToInt64(idObj);
                var obj = UnityObjectCompat.ResolveByInstanceId(instanceId);

                if (obj is Material directMat)
                    return directMat;

                if (obj is GameObject go)
                {
                    var renderer = go.GetComponent<Renderer>();
                    if (renderer != null && renderer.sharedMaterial != null)
                        return renderer.sharedMaterial;
                }

                throw new ArgumentException($"No material found for instance {instanceId}");
            }

            throw new ArgumentException("Provide 'path' or 'instanceId' to identify the material");
        }

        private static Shader ResolveCreateShader(string shaderName)
        {
            if (!string.IsNullOrWhiteSpace(shaderName))
                return Shader.Find(shaderName);

            return Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Sprites/Default");
        }

        private static object ReadMaterialValue(Material mat, string propName, UnityEngine.Rendering.ShaderPropertyType propType)
        {
            switch (propType)
            {
                case ShaderPropertyType.Color:
                    var c = mat.GetColor(propName);
                    return new List<object> { (double)c.r, (double)c.g, (double)c.b, (double)c.a };
                case ShaderPropertyType.Vector:
                    var v = mat.GetVector(propName);
                    return new List<object> { (double)v.x, (double)v.y, (double)v.z, (double)v.w };
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    return (double)mat.GetFloat(propName);
                case ShaderPropertyType.Texture:
                    var tex = mat.GetTexture(propName);
                    if (tex != null)
                    {
                        var texPath = AssetDatabase.GetAssetPath(tex);
                        return new Dictionary<string, object>
                        {
                            ["name"] = tex.name,
                            ["path"] = texPath,
                            ["instanceId"] = tex.GetId()
                        };
                    }
                    return null;
                case ShaderPropertyType.Int:
                    return mat.GetInteger(propName);
                default:
                    return null;
            }
        }

        private static void WriteMaterialValue(Material mat, string propName, ShaderPropertyType propType, object value)
        {
            switch (propType)
            {
                case ShaderPropertyType.Color:
                    if (value is List<object> ca && ca.Count >= 3)
                        mat.SetColor(propName, new Color(
                            Convert.ToSingle(ca[0]),
                            Convert.ToSingle(ca[1]),
                            Convert.ToSingle(ca[2]),
                            ca.Count >= 4 ? Convert.ToSingle(ca[3]) : 1f));
                    break;
                case ShaderPropertyType.Vector:
                    if (value is List<object> va && va.Count >= 4)
                        mat.SetVector(propName, new Vector4(
                            Convert.ToSingle(va[0]),
                            Convert.ToSingle(va[1]),
                            Convert.ToSingle(va[2]),
                            Convert.ToSingle(va[3])));
                    break;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    mat.SetFloat(propName, Convert.ToSingle(value));
                    break;
                case ShaderPropertyType.Int:
                    mat.SetInteger(propName, Convert.ToInt32(value));
                    break;
                case ShaderPropertyType.Texture:
                    if (value == null)
                    {
                        mat.SetTexture(propName, null);
                    }
                    else if (value is Dictionary<string, object> texDict)
                    {
                        if (texDict.TryGetValue("path", out var tp))
                        {
                            var tex = AssetDatabase.LoadAssetAtPath<Texture>(tp.ToString());
                            mat.SetTexture(propName, tex);
                        }
                        else if (texDict.TryGetValue("instanceId", out var tid))
                        {
                            var tex = UnityObjectCompat.ResolveByInstanceId<Texture>(Convert.ToInt64(tid));
                            mat.SetTexture(propName, tex);
                        }
                    }
                    break;
            }
        }

        private static void CreateFoldersRecursive(string path)
        {
            var parts = path.Replace("\\", "/").Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
