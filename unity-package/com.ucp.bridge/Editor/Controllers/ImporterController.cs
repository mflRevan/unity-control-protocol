using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace UCP.Bridge
{
    public static class ImporterController
    {
        public static void Register(CommandRouter router)
        {
            router.Register("asset/reimport", HandleReimport);
            router.Register("asset/import-settings/read", HandleRead);
            router.Register("asset/import-settings/write", HandleWrite);
            router.Register("asset/import-settings/write-batch", HandleWriteBatch);
        }

        private static object HandleReimport(string paramsJson)
        {
            var parameters = ParseParameters(paramsJson);
            var requestedPath = RequirePath(parameters);
            var recursive = TryGetOptionalBool(parameters, "recursive");
            return recursive
                ? AssetImportSupport.ReimportRecursive(requestedPath)
                : AssetImportSupport.Reimport(requestedPath);
        }

        private static object HandleRead(string paramsJson)
        {
            var parameters = ParseParameters(paramsJson);
            var requestedPath = RequirePath(parameters);
            var importer = AssetImportSupport.ResolveImporter(requestedPath);
            var serializedObject = new SerializedObject(importer);
            var fields = new List<object>();

            try
            {
                if (TryGetOptionalString(parameters, "field", out var fieldName))
                {
                    var property = serializedObject.FindProperty(fieldName);
                    if (property == null)
                        throw new ArgumentException($"Field '{fieldName}' not found on {importer.GetType().Name}");

                    fields.Add(SerializedPropertyControllerSupport.Describe(property));
                }
                else
                {
                    var iterator = serializedObject.GetIterator();
                    if (iterator.NextVisible(true))
                    {
                        do
                        {
                            fields.Add(SerializedPropertyControllerSupport.Describe(iterator));
                        }
                        while (iterator.NextVisible(false));
                    }
                }

                return CreateImporterPayload(requestedPath, importer, fields);
            }
            finally
            {
                serializedObject.Dispose();
            }
        }

        private static object HandleWrite(string paramsJson)
        {
            var parameters = ParseParameters(paramsJson);
            var requestedPath = RequirePath(parameters);
            if (!parameters.TryGetValue("field", out var fieldObject) || fieldObject == null)
                throw new ArgumentException("Missing 'field' parameter");
            if (!parameters.ContainsKey("value"))
                throw new ArgumentException("Missing 'value' parameter");

            var importer = AssetImportSupport.ResolveImporter(requestedPath);
            var fieldName = fieldObject.ToString();
            var noReimport = TryGetOptionalBool(parameters, "noReimport");
            var serializedObject = new SerializedObject(importer);

            try
            {
                Undo.RecordObject(importer, $"UCP Importer Write {fieldName}");
                SerializedPropertyControllerSupport.WriteFieldValue(
                    serializedObject,
                    importer.GetType().Name,
                    fieldName,
                    parameters["value"]);
                serializedObject.ApplyModifiedProperties();

                var result = new Dictionary<string, object>
                {
                    ["status"] = "ok",
                    ["path"] = requestedPath,
                    ["assetPath"] = AssetImportSupport.GetPrimaryAssetPath(requestedPath),
                    ["importerType"] = importer.GetType().Name,
                    ["field"] = fieldName,
                    ["reimport"] = AssetImportSupport.SaveImporterSettings(requestedPath, importer, noReimport)
                };
                AddPlatformOverrideNote(result, importer, new[] { fieldName });
                return result;
            }
            finally
            {
                serializedObject.Dispose();
            }
        }

        private static object HandleWriteBatch(string paramsJson)
        {
            var parameters = ParseParameters(paramsJson);
            var requestedPath = RequirePath(parameters);
            if (!parameters.TryGetValue("values", out var valuesObject) || !(valuesObject is Dictionary<string, object> values))
                throw new ArgumentException("Missing 'values' parameter");

            var importer = AssetImportSupport.ResolveImporter(requestedPath);
            var noReimport = TryGetOptionalBool(parameters, "noReimport");
            var serializedObject = new SerializedObject(importer);
            var fields = new List<object>();

            try
            {
                Undo.RecordObject(importer, $"UCP Importer Batch Write {importer.name}");

                foreach (var entry in values)
                {
                    SerializedPropertyControllerSupport.WriteFieldValue(
                        serializedObject,
                        importer.GetType().Name,
                        entry.Key,
                        entry.Value);
                    fields.Add(entry.Key);
                }

                serializedObject.ApplyModifiedProperties();

                var result = new Dictionary<string, object>
                {
                    ["status"] = "ok",
                    ["path"] = requestedPath,
                    ["assetPath"] = AssetImportSupport.GetPrimaryAssetPath(requestedPath),
                    ["importerType"] = importer.GetType().Name,
                    ["fields"] = fields,
                    ["reimport"] = AssetImportSupport.SaveImporterSettings(requestedPath, importer, noReimport)
                };
                AddPlatformOverrideNote(result, importer, values.Keys);
                return result;
            }
            finally
            {
                serializedObject.Dispose();
            }
        }

        /// <summary>
        /// Texture fields that a per-platform override block shadows. A write to the default
        /// settings leaves such an override in place, which the caller cannot see from the
        /// response alone, so the affected platforms are reported.
        /// </summary>
        private static readonly string[] PlatformOverridableTextureFields =
        {
            "m_MaxTextureSize", "m_TextureFormat", "m_CompressionQuality", "m_TextureCompression",
            "m_CrunchedCompression", "m_ResizeAlgorithm", "m_AlphaSplitEnabled"
        };

        private static void AddPlatformOverrideNote(Dictionary<string, object> result, AssetImporter importer, IEnumerable<string> fields)
        {
            if (!(importer is TextureImporter))
                return;
            var affected = fields.Where(f => Array.IndexOf(PlatformOverridableTextureFields, f) >= 0).ToList();
            if (affected.Count == 0)
                return;
            var overrides = new List<object>();
            var paths = new List<string>();
            using (var serialized = new SerializedObject(importer))
            {
                var platforms = serialized.FindProperty("m_PlatformSettings");
                if (platforms == null || !platforms.isArray)
                    return;
                for (var i = 0; i < platforms.arraySize; i++)
                {
                    var entry = platforms.GetArrayElementAtIndex(i);
                    var overridden = entry.FindPropertyRelative("m_Overridden");
                    var target = entry.FindPropertyRelative("m_BuildTarget");
                    if (overridden == null || !overridden.boolValue || target == null)
                        continue;
                    var path = $"m_PlatformSettings.Array.data[{i}]";
                    overrides.Add(new Dictionary<string, object> { ["platform"] = target.stringValue, ["path"] = path });
                    paths.Add($"{target.stringValue} ({path})");
                }
            }
            if (overrides.Count == 0)
                return;
            result["platformOverrides"] = overrides;
            var plural = overrides.Count == 1 ? "keeps its own override" : "keep their own overrides";
            result["warning"] = $"{string.Join(", ", affected)} changed the default platform settings only; "
                + $"{string.Join(", ", paths)} {plural}. Write the per-platform field, e.g. "
                + $"`--field {((Dictionary<string, object>)overrides[0])["path"]}.{affected[0]}`, "
                + "or clear it with `.m_Overridden` = false.";
        }

        private static Dictionary<string, object> CreateImporterPayload(
            string requestedPath,
            AssetImporter importer,
            List<object> fields)
        {
            var assetPath = AssetImportSupport.GetPrimaryAssetPath(requestedPath);
            var mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);

            var payload = new Dictionary<string, object>
            {
                ["path"] = requestedPath,
                ["assetPath"] = assetPath,
                ["metaPath"] = assetPath + ".meta",
                ["importerType"] = importer.GetType().Name,
                ["fields"] = fields
            };

            if (mainAsset != null)
            {
                payload["assetName"] = mainAsset.name;
                payload["assetType"] = mainAsset.GetType().Name;
            }

            return payload;
        }

        private static Dictionary<string, object> ParseParameters(string paramsJson)
        {
            return MiniJson.Deserialize(paramsJson) as Dictionary<string, object>
                ?? throw new ArgumentException("Invalid parameters");
        }

        private static string RequirePath(Dictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue("path", out var pathObject) || pathObject == null)
                throw new ArgumentException("Missing 'path' parameter");

            return pathObject.ToString();
        }

        private static bool TryGetOptionalString(
            Dictionary<string, object> parameters,
            string key,
            out string value)
        {
            if (parameters.TryGetValue(key, out var valueObject) && valueObject != null)
            {
                value = valueObject.ToString();
                return true;
            }

            value = null;
            return false;
        }

        private static bool TryGetOptionalBool(Dictionary<string, object> parameters, string key)
        {
            return parameters.TryGetValue(key, out var valueObject)
                && valueObject != null
                && Convert.ToBoolean(valueObject);
        }
    }
}
