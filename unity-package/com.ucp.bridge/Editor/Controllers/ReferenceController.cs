using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UCP.Bridge
{
    /// <summary>
    /// Bridge controller for reference search - used as a fallback when native Rust
    /// indexing is not available (non-Force-Text projects) or for correctness verification.
    /// </summary>
    public static class ReferenceController
    {
        public static void Register(CommandRouter router)
        {
            router.Register("references/find", HandleFind);
            router.Register("references/serialization-status", HandleSerializationStatus);
        }

        private static object HandleSerializationStatus(string paramsJson)
        {
            var settings = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/EditorSettings.asset")[0]);
            try
            {
                settings.Update();
                var modeProp = settings.FindProperty("m_SerializationMode");
                int mode = modeProp != null ? modeProp.intValue : -1;

                return new Dictionary<string, object>
                {
                    { "serializationMode", mode },
                    { "forceText", mode == 2 },
                    { "visibleMetaFiles", EditorSettings.externalVersionControl == "Visible Meta Files" }
                };
            }
            finally
            {
                settings.Dispose();
            }
        }

        private static object HandleFind(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null)
                throw new ArgumentException("Invalid parameters");

            string targetGuid = null;
            long targetFileId = 0;
            int maxResults = 100;

            if (p.TryGetValue("guid", out var gObj) && gObj != null)
                targetGuid = gObj.ToString();
            if (p.TryGetValue("fileId", out var fObj) && fObj != null)
                targetFileId = Convert.ToInt64(fObj);
            if (p.TryGetValue("maxResults", out var mObj) && mObj != null)
                maxResults = Convert.ToInt32(mObj);

            if (string.IsNullOrEmpty(targetGuid))
                throw new ArgumentException("Missing 'guid' parameter");

            var targetPath = AssetDatabase.GUIDToAssetPath(targetGuid);
            var references = new List<object>();

            // Phase 1: Find all assets that depend on the target using AssetDatabase
            var allAssets = AssetDatabase.GetAllAssetPaths();
            var dependentPaths = new List<string>();

            foreach (var assetPath in allAssets)
            {
                if (!assetPath.StartsWith("Assets/") && !assetPath.StartsWith("Packages/"))
                    continue;

                var deps = AssetDatabase.GetDependencies(assetPath, false);
                foreach (var dep in deps)
                {
                    if (dep == targetPath)
                    {
                        dependentPaths.Add(assetPath);
                        break;
                    }
                }
            }

            // Phase 2: Walk SerializedObject properties to find exact reference locations
            foreach (var sourcePath in dependentPaths)
            {
                if (references.Count >= maxResults)
                    break;

                var assets = AssetDatabase.LoadAllAssetsAtPath(sourcePath);
                foreach (var asset in assets)
                {
                    if (asset == null) continue;
                    if (references.Count >= maxResults) break;

                    var so = new SerializedObject(asset);
                    try
                    {
                        so.Update();
                        var iterator = so.GetIterator();
                        bool enterChildren = true;

                        while (iterator.Next(enterChildren))
                        {
                            enterChildren = true;

                            if (iterator.propertyType != SerializedPropertyType.ObjectReference)
                                continue;

                            var refValue = iterator.objectReferenceValue;
                            if (refValue == null) continue;

                            string refPath = AssetDatabase.GetAssetPath(refValue);
                            if (string.IsNullOrEmpty(refPath)) continue;

                            string refGuid = AssetDatabase.AssetPathToGUID(refPath);
                            if (refGuid != targetGuid) continue;

                            // If a specific fileId was requested, check it
                            if (targetFileId != 0)
                            {
                                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                                    refValue, out string _, out long localId))
                                    continue;
                                if (localId != targetFileId)
                                    continue;
                            }

                            references.Add(new Dictionary<string, object>
                            {
                                { "sourcePath", sourcePath },
                                { "sourceObjectName", asset.name },
                                { "sourceObjectType", asset.GetType().Name },
                                { "propertyPath", iterator.propertyPath },
                                { "targetGuid", targetGuid },
                                { "targetPath", targetPath },
                                { "referencedObjectName", refValue.name },
                                { "referencedObjectType", refValue.GetType().Name }
                            });

                            if (references.Count >= maxResults)
                                break;
                        }
                    }
                    finally
                    {
                        so.Dispose();
                    }
                }
            }

            return new Dictionary<string, object>
            {
                { "targetGuid", targetGuid },
                { "targetPath", targetPath ?? "" },
                { "count", references.Count },
                { "references", references }
            };
        }
    }
}
