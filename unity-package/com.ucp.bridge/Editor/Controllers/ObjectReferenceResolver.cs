using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UCP.Bridge
{
    internal static class ObjectReferenceResolver
    {
        public static Dictionary<string, object> Serialize(UnityEngine.Object obj)
        {
            if (obj == null)
                return null;

            var result = new Dictionary<string, object>
            {
                ["instanceId"] = obj.GetInstanceID(),
                ["name"] = obj.name,
                ["type"] = obj.GetType().Name
            };

            var assetPath = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(assetPath))
            {
                result["path"] = assetPath;
                var guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (!string.IsNullOrEmpty(guid))
                    result["guid"] = guid;
            }

            return result;
        }

        public static UnityEngine.Object Resolve(object value, string propertyName)
        {
            if (value == null)
                return null;

            if (value is Dictionary<string, object> reference)
            {
                if (reference.TryGetValue("instanceId", out var instanceId) && instanceId != null)
                    return ResolveByInstanceId(Convert.ToInt32(instanceId), propertyName);

                if (reference.TryGetValue("path", out var path) && path != null)
                    return ResolveByPath(path.ToString(), propertyName);

                if (reference.TryGetValue("guid", out var guid) && guid != null)
                    return ResolveByGuid(guid.ToString(), propertyName);

                throw new ArgumentException($"Object reference for '{propertyName}' must include instanceId, path, or guid");
            }

            if (value is string stringValue)
            {
                return stringValue.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                    ? ResolveByPath(stringValue, propertyName)
                    : ResolveByGuid(stringValue, propertyName);
            }

            if (value is sbyte || value is byte || value is short || value is ushort || value is int || value is uint || value is long || value is ulong)
                return ResolveByInstanceId(Convert.ToInt32(value), propertyName);

            throw new ArgumentException($"Unsupported object reference value for '{propertyName}'");
        }

        private static UnityEngine.Object ResolveByInstanceId(int instanceId, string propertyName)
        {
            var resolved = EditorUtility.InstanceIDToObject(instanceId);
            if (resolved == null)
                throw new ArgumentException($"Object reference for '{propertyName}' could not resolve instance id {instanceId}");

            return resolved;
        }

        private static UnityEngine.Object ResolveByPath(string assetPath, string propertyName)
        {
            var resolved = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (resolved == null)
                throw new ArgumentException($"Object reference for '{propertyName}' could not load asset at {assetPath}");

            return resolved;
        }

        private static UnityEngine.Object ResolveByGuid(string guid, string propertyName)
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException($"Object reference for '{propertyName}' could not resolve guid {guid}");

            return ResolveByPath(assetPath, propertyName);
        }
    }
}