using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UCP.Bridge
{
    public static class ShaderController
    {
        public static void Register(CommandRouter router)
        {
            router.Register("shader/errors", HandleErrors);
        }

        private static object HandleErrors(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            var errorsOnly = p != null && p.TryGetValue("errorsOnly", out var errorsOnlyObj) && errorsOnlyObj != null && Convert.ToBoolean(errorsOnlyObj);
            var filter = p != null && p.TryGetValue("filter", out var filterObj) && filterObj != null ? filterObj.ToString() : null;

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            var diagnostics = new List<object>();
            var scanned = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Shader"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader == null)
                    continue;
                if (!MatchesFilter(shader.name, path, filter))
                    continue;

                scanned++;
                foreach (var diagnostic in ReadShaderDiagnostics(shader, path, errorsOnly))
                    diagnostics.Add(diagnostic);
            }

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["capability"] = FindShaderMessageMethod() != null ? "shader-messages" : "fallback",
                ["scanned"] = scanned,
                ["count"] = diagnostics.Count,
                ["diagnostics"] = diagnostics
            };
        }

        private static IEnumerable<object> ReadShaderDiagnostics(Shader shader, string path, bool errorsOnly)
        {
            var method = FindShaderMessageMethod();
            if (method == null)
                yield break;

            var messages = method.Invoke(null, new object[] { shader }) as Array;
            if (messages == null)
                yield break;

            foreach (var message in messages)
            {
                var isWarning = ReadBoolMember(message, "warning", "isWarning");
                if (errorsOnly && isWarning)
                    continue;

                yield return new Dictionary<string, object>
                {
                    ["shader"] = shader.name,
                    ["path"] = path,
                    ["severity"] = isWarning ? "warning" : "error",
                    ["message"] = ReadStringMember(message, "message", "messageDetails"),
                    ["line"] = ReadIntMember(message, "line"),
                    ["platform"] = ReadStringMember(message, "platform"),
                    ["file"] = ReadStringMember(message, "file")
                };
            }
        }

        private static MethodInfo FindShaderMessageMethod()
        {
            var shaderUtil = typeof(Editor).Assembly.GetType("UnityEditor.ShaderUtil");
            return FindShaderMethod(shaderUtil, "GetShaderMessages")
                ?? FindShaderMethod(shaderUtil, "GetShaderErrors");
        }

        private static MethodInfo FindShaderMethod(Type shaderUtil, string name)
        {
            if (shaderUtil == null)
                return null;

            foreach (var method in shaderUtil.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.Name != name)
                    continue;
                var parameters = method.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(Shader))
                    return method;
            }

            return null;
        }

        private static bool MatchesFilter(string shaderName, string path, string filter)
        {
            if (string.IsNullOrEmpty(filter))
                return true;
            return shaderName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ReadStringMember(object target, params string[] names)
        {
            foreach (var name in names)
            {
                var value = ReadMember(target, name);
                if (value != null)
                    return value.ToString();
            }

            return string.Empty;
        }

        private static int ReadIntMember(object target, string name)
        {
            var value = ReadMember(target, name);
            return value != null ? Convert.ToInt32(value) : 0;
        }

        private static bool ReadBoolMember(object target, params string[] names)
        {
            foreach (var name in names)
            {
                var value = ReadMember(target, name);
                if (value != null)
                    return Convert.ToBoolean(value);
            }

            return false;
        }

        private static object ReadMember(object target, string name)
        {
            var type = target.GetType();
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
                return field.GetValue(target);
            var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property?.GetValue(target);
        }
    }
}
