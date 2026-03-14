using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace UCP.Bridge.Tests
{
    public class ControllerSmokeTests
    {
        private const string TempAssetPath = "Assets/UcpControllerSmoke.asset";
        private const string TempReferenceAssetPath = "Assets/UcpControllerReference.asset";
        private const string TempPrefabPath = "Assets/UcpControllerSmoke.prefab";
        private const string TempMaterialPath = "Assets/UcpControllerSmoke.mat";
        private const string TempTextPath = "Assets/UcpControllerSmoke.txt";

        private CommandRouter _router;

        [SetUp]
        public void SetUp()
        {
            _router = new CommandRouter();
            SnapshotController.Register(_router);
            AssetController.Register(_router);
            LogsController.Register(_router);
            HierarchyController.Register(_router);
            PropertyController.Register(_router);
            FileController.Register(_router);
            MaterialController.Register(_router);
            PrefabController.Register(_router);
            BuildController.Register(_router);
            EditorSettingsController.Register(_router);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            DeleteTempAsset();
            DeleteTempReferenceAsset();
            DeleteTempPrefab();
            DeleteTempMaterial();
            DeleteTempTextFile();
            LogsController.ClearHistoryForTests();
        }

        [TearDown]
        public void TearDown()
        {
            DeleteTempAsset();
            DeleteTempReferenceAsset();
            DeleteTempPrefab();
            DeleteTempMaterial();
            DeleteTempTextFile();
            LogsController.ClearHistoryForTests();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [Test]
        public void Snapshot_DefaultDepth_ReturnsLeanRootMetadata()
        {
            var root = new GameObject("Root");
            var child = new GameObject("Child");
            child.transform.SetParent(root.transform, false);

            var response = _router.Dispatch("snapshot", 1, "{\"depth\":0}");

            Assert.That(response.error, Is.Null);

            var result = (Dictionary<string, object>)response.result;
            var objects = (List<object>)result["objects"];
            Assert.That(objects.Count, Is.EqualTo(1));

            var entry = (Dictionary<string, object>)objects[0];
            Assert.That(entry["name"], Is.EqualTo("Root"));
            Assert.That(Convert.ToInt32(entry["depth"]), Is.EqualTo(0));
            Assert.That(Convert.ToInt32(entry["childCount"]), Is.EqualTo(1));
            Assert.That(entry.ContainsKey("components"), Is.True);
            Assert.That(entry.ContainsKey("layerName"), Is.True);
            Assert.That(entry.ContainsKey("children"), Is.False);
            Assert.That(entry.ContainsKey("position"), Is.False);
            Assert.That(entry.ContainsKey("rotation"), Is.False);
            Assert.That(result.ContainsKey("logs"), Is.False);
        }

        [Test]
        public void AssetSearch_FiltersActualSubassetTypeMatches()
        {
            var root = ScriptableObject.CreateInstance<SearchRootAsset>();
            root.name = "SmokeRoot";
            AssetDatabase.CreateAsset(root, TempAssetPath);

            var nested = ScriptableObject.CreateInstance<SearchNestedAsset>();
            nested.name = "SmokeNested";
            AssetDatabase.AddObjectToAsset(nested, root);
            AssetDatabase.ImportAsset(TempAssetPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets();

            var response = _router.Dispatch(
                "asset/search",
                1,
                "{\"name\":\"SmokeNested\",\"path\":\"Assets\",\"maxResults\":10}"
            );

            Assert.That(response.error, Is.Null);

            var result = (Dictionary<string, object>)response.result;
            Assert.That(Convert.ToInt32(result["total"]), Is.GreaterThanOrEqualTo(1));
            Assert.That(Convert.ToInt32(result["returned"]), Is.GreaterThanOrEqualTo(1));

            var matches = (List<object>)result["results"];
            var match = FindAssetMatch(matches, TempAssetPath, "SmokeNested");
            Assert.That(match, Is.Not.Null);
            Assert.That(match["path"], Is.EqualTo(TempAssetPath));
            Assert.That(match["type"], Is.EqualTo("SearchNestedAsset"));
            Assert.That(match["name"], Is.EqualTo("SmokeNested"));
            Assert.That(Convert.ToBoolean(match["isSubAsset"]), Is.True);
        }

        [Test]
        public void LogsTail_ReturnsRequestedBufferedCount()
        {
            for (var index = 0; index < 12; index++)
                LogsController.RecordTestLog("info", $"log {index}");

            var response = _router.Dispatch("logs/tail", 1, "{\"count\":50}");

            Assert.That(response.error, Is.Null);

            var result = (Dictionary<string, object>)response.result;
            Assert.That(Convert.ToInt32(result["total"]), Is.EqualTo(12));
            Assert.That(Convert.ToInt32(result["returned"]), Is.EqualTo(12));
            Assert.That(Convert.ToBoolean(result["truncated"]), Is.False);

            var logs = (List<object>)result["logs"];
            var first = (Dictionary<string, object>)logs[0];
            Assert.That(Convert.ToInt64(first["id"]), Is.EqualTo(12));
            Assert.That(first.ContainsKey("messagePreview"), Is.True);
        }

        [Test]
        public void LogsSearch_FiltersBeforeApplyingCount()
        {
            LogsController.RecordTestLog("warning", "Target failed once");
            for (var index = 0; index < 8; index++)
                LogsController.RecordTestLog("info", $"Noise {index}");

            var response = _router.Dispatch("logs/search", 1, "{\"pattern\":\"Target\",\"count\":1}");

            Assert.That(response.error, Is.Null);

            var result = (Dictionary<string, object>)response.result;
            Assert.That(Convert.ToInt32(result["total"]), Is.EqualTo(1));
            Assert.That(Convert.ToInt32(result["returned"]), Is.EqualTo(1));
            Assert.That(Convert.ToBoolean(result["truncated"]), Is.False);
        }

        [Test]
        public void LogsSearch_UsesRegexAgainstBufferedHistory()
        {
            LogsController.RecordTestLog("info", "Alpha ready");
            LogsController.RecordTestLog("warning", "Beta failed");
            LogsController.RecordTestLog("error", "Gamma failed hard");

            var response = _router.Dispatch("logs/search", 1, "{\"pattern\":\"failed\",\"count\":20}");

            Assert.That(response.error, Is.Null);

            var result = (Dictionary<string, object>)response.result;
            Assert.That(Convert.ToInt32(result["total"]), Is.EqualTo(2));

            var logs = (List<object>)result["logs"];
            Assert.That(logs.Count, Is.EqualTo(2));
            var first = (Dictionary<string, object>)logs[0];
            Assert.That(first["level"], Is.EqualTo("error"));
        }

        [Test]
        public void LogsTail_RespectsLevelThresholdAndIdWindow()
        {
            var first = LogsController.RecordTestLog("info", "Alpha");
            var second = LogsController.RecordTestLog("warning", "Beta warning");
            var third = LogsController.RecordTestLog("error", "Gamma error");

            var response = _router.Dispatch(
                "logs/tail",
                1,
                "{\"level\":\"warn\",\"afterId\":" + Convert.ToInt64(first["id"]) + ",\"beforeId\":" + Convert.ToInt64(third["id"]) + ",\"count\":20}"
            );

            Assert.That(response.error, Is.Null);

            var result = (Dictionary<string, object>)response.result;
            Assert.That(Convert.ToInt32(result["total"]), Is.EqualTo(1));

            var logs = (List<object>)result["logs"];
            Assert.That(logs.Count, Is.EqualTo(1));
            var only = (Dictionary<string, object>)logs[0];
            Assert.That(only["level"], Is.EqualTo("warning"));
            Assert.That(Convert.ToInt64(only["id"]), Is.EqualTo(Convert.ToInt64(second["id"])));
        }

        [Test]
        public void LogsGet_ReturnsFullMessageAndStackTrace()
        {
            var created = LogsController.RecordTestLog("error", "Exploded", "stack line 1\nstack line 2");
            var id = Convert.ToInt64(created["id"]);

            var response = _router.Dispatch("logs/get", 1, "{\"id\":" + id + "}");

            Assert.That(response.error, Is.Null);

            var result = (Dictionary<string, object>)response.result;
            Assert.That(result["message"], Is.EqualTo("Exploded"));
            Assert.That(result["stackTrace"], Is.EqualTo("stack line 1\nstack line 2"));
        }

        [Test]
        public void ObjectLifecycle_CreateMutateAndDelete_WorksEndToEnd()
        {
            var create = _router.Dispatch("object/create", 1, "{\"name\":\"SmokeObject\"}");
            Assert.That(create.error, Is.Null);

            var createResult = (Dictionary<string, object>)create.result;
            var instanceId = Convert.ToInt32(createResult["instanceId"]);

            var rename = _router.Dispatch("object/set-name", 1, "{\"instanceId\":" + instanceId + ",\"name\":\"RenamedSmoke\"}");
            Assert.That(rename.error, Is.Null);

            var deactivate = _router.Dispatch("object/set-active", 1, "{\"instanceId\":" + instanceId + ",\"active\":false}");
            Assert.That(deactivate.error, Is.Null);
            var deactivateResult = (Dictionary<string, object>)deactivate.result;
            Assert.That(Convert.ToBoolean(deactivateResult["active"]), Is.False);

            var activate = _router.Dispatch("object/set-active", 1, "{\"instanceId\":" + instanceId + ",\"active\":true}");
            Assert.That(activate.error, Is.Null);

            var addComponent = _router.Dispatch("object/add-component", 1, "{\"instanceId\":" + instanceId + ",\"type\":\"BoxCollider\"}");
            Assert.That(addComponent.error, Is.Null);

            var setPosition = _router.Dispatch(
                "object/set-property",
                1,
                "{\"instanceId\":" + instanceId + ",\"component\":\"Transform\",\"property\":\"m_LocalPosition\",\"value\":[1,2,3]}"
            );
            Assert.That(setPosition.error, Is.Null);

            var getPosition = _router.Dispatch(
                "object/get-property",
                1,
                "{\"instanceId\":" + instanceId + ",\"component\":\"Transform\",\"property\":\"m_LocalPosition\"}"
            );
            Assert.That(getPosition.error, Is.Null);

            var updated = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            Assert.That(updated, Is.Not.Null);
            var localPosition = updated.transform.localPosition;
            Assert.That(localPosition.x, Is.EqualTo(1f).Within(0.001f));
            Assert.That(localPosition.y, Is.EqualTo(2f).Within(0.001f));
            Assert.That(localPosition.z, Is.EqualTo(3f).Within(0.001f));

            var removeComponent = _router.Dispatch("object/remove-component", 1, "{\"instanceId\":" + instanceId + ",\"type\":\"BoxCollider\"}");
            Assert.That(removeComponent.error, Is.Null);

            var delete = _router.Dispatch("object/delete", 1, "{\"instanceId\":" + instanceId + "}");
            Assert.That(delete.error, Is.Null);
            Assert.That(EditorUtility.InstanceIDToObject(instanceId), Is.Null);
        }

        [Test]
        public void ObjectSetProperty_AssignsObjectReferenceByAssetPath()
        {
            var referencedAsset = ScriptableObject.CreateInstance<SearchRootAsset>();
            referencedAsset.name = "ReferenceAsset";
            AssetDatabase.CreateAsset(referencedAsset, TempReferenceAssetPath);
            AssetDatabase.SaveAssets();

            var go = new GameObject("ReferenceCarrier");
            var component = go.AddComponent<ReferenceComponent>();

            var response = _router.Dispatch(
                "object/set-property",
                1,
                "{\"instanceId\":" + go.GetInstanceID() + ",\"component\":\"ReferenceComponent\",\"property\":\"referenceAsset\",\"value\":{\"path\":\"" + TempReferenceAssetPath + "\"}}"
            );

            Assert.That(response.error, Is.Null);
            Assert.That(component.referenceAsset, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(component.referenceAsset), Is.EqualTo(TempReferenceAssetPath));
        }

        [Test]
        public void ObjectSetProperty_RejectsUnknownObjectReference()
        {
            var go = new GameObject("ReferenceCarrier");
            go.AddComponent<ReferenceComponent>();

            var response = _router.Dispatch(
                "object/set-property",
                1,
                "{\"instanceId\":" + go.GetInstanceID() + ",\"component\":\"ReferenceComponent\",\"property\":\"referenceAsset\",\"value\":{\"path\":\"Assets/Missing.asset\"}}"
            );

            Assert.That(response.error, Is.Not.Null);
        }

        [Test]
        public void AssetWriteBatch_UpdatesMultipleFieldsIncludingObjectReference()
        {
            var reference = ScriptableObject.CreateInstance<SearchRootAsset>();
            reference.name = "ReferenceAsset";
            AssetDatabase.CreateAsset(reference, TempReferenceAssetPath);

            var asset = ScriptableObject.CreateInstance<BatchWritableAsset>();
            asset.maxPlayers = 2;
            asset.spawnDelay = 5f;
            AssetDatabase.CreateAsset(asset, TempAssetPath);
            AssetDatabase.SaveAssets();

            var response = _router.Dispatch(
                "asset/write-batch",
                1,
                "{\"path\":\"" + TempAssetPath + "\",\"values\":{\"maxPlayers\":8,\"spawnDelay\":1.5,\"referenceAsset\":{\"path\":\"" + TempReferenceAssetPath + "\"}}}"
            );

            Assert.That(response.error, Is.Null);

            var reloaded = AssetDatabase.LoadAssetAtPath<BatchWritableAsset>(TempAssetPath);
            Assert.That(reloaded.maxPlayers, Is.EqualTo(8));
            Assert.That(reloaded.spawnDelay, Is.EqualTo(1.5f).Within(0.001f));
            Assert.That(AssetDatabase.GetAssetPath(reloaded.referenceAsset), Is.EqualTo(TempReferenceAssetPath));
        }

        [Test]
        public void FileController_WritePatchRead_AndRejectsPathTraversal()
        {
            var write = _router.Dispatch("file/write", 1, "{\"path\":\"Assets/UcpControllerSmoke.txt\",\"content\":\"hello smoke\"}");
            Assert.That(write.error, Is.Null);

            var patch = _router.Dispatch(
                "file/patch",
                1,
                "{\"path\":\"Assets/UcpControllerSmoke.txt\",\"patch\":{\"find\":\"smoke\",\"replace\":\"patched\"}}"
            );
            Assert.That(patch.error, Is.Null);

            var read = _router.Dispatch("file/read", 1, "{\"path\":\"Assets/UcpControllerSmoke.txt\"}");
            Assert.That(read.error, Is.Null);
            var readResult = (Dictionary<string, object>)read.result;
            Assert.That(readResult["content"].ToString(), Is.EqualTo("hello patched"));

            LogAssert.Expect(LogType.Error, new Regex("\\[UCP\\] Error handling 'file/read':"));
            var traversal = _router.Dispatch("file/read", 1, "{\"path\":\"../outside.txt\"}");
            Assert.That(traversal.error, Is.Not.Null);
        }

        [Test]
        public void MaterialController_SetAndGetFloatProperty_RoundTrips()
        {
            var shader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);

            var propertyName = FindFirstFloatOrRangeProperty(shader);
            Assert.That(propertyName, Is.Not.Null.And.Not.Empty);

            var material = new Material(shader) { name = "UcpControllerSmokeMat" };
            AssetDatabase.CreateAsset(material, TempMaterialPath);
            AssetDatabase.SaveAssets();

            var set = _router.Dispatch(
                "material/set-property",
                1,
                "{\"path\":\"Assets/UcpControllerSmoke.mat\",\"property\":\"" + propertyName + "\",\"value\":0.42}"
            );
            Assert.That(set.error, Is.Null);

            var get = _router.Dispatch(
                "material/get-property",
                1,
                "{\"path\":\"Assets/UcpControllerSmoke.mat\",\"property\":\"" + propertyName + "\"}"
            );
            Assert.That(get.error, Is.Null);

            var getResult = (Dictionary<string, object>)get.result;
            var value = Convert.ToSingle(getResult["value"]);
            Assert.That(value, Is.EqualTo(0.42f).Within(0.001f));
        }

        [Test]
        public void PrefabController_CreateStatusOverridesAndUnpack_Works()
        {
            var create = _router.Dispatch("object/create", 1, "{\"name\":\"PrefabSource\"}");
            Assert.That(create.error, Is.Null);
            var sourceId = Convert.ToInt32(((Dictionary<string, object>)create.result)["instanceId"]);

            var createPrefab = _router.Dispatch(
                "prefab/create",
                1,
                "{\"instanceId\":" + sourceId + ",\"path\":\"Assets/UcpControllerSmoke.prefab\"}"
            );
            Assert.That(createPrefab.error, Is.Null);

            var instantiate = _router.Dispatch(
                "object/instantiate",
                1,
                "{\"prefab\":\"Assets/UcpControllerSmoke.prefab\",\"name\":\"PrefabInstance\"}"
            );
            Assert.That(instantiate.error, Is.Null);
            var instanceId = Convert.ToInt32(((Dictionary<string, object>)instantiate.result)["instanceId"]);

            var status = _router.Dispatch("prefab/status", 1, "{\"instanceId\":" + instanceId + "}");
            Assert.That(status.error, Is.Null);
            var statusResult = (Dictionary<string, object>)status.result;
            Assert.That(Convert.ToBoolean(statusResult["isInstance"]), Is.True);

            var mutate = _router.Dispatch(
                "object/set-property",
                1,
                "{\"instanceId\":" + instanceId + ",\"component\":\"Transform\",\"property\":\"m_LocalPosition\",\"value\":[2,0,0]}"
            );
            Assert.That(mutate.error, Is.Null);

            var overrides = _router.Dispatch("prefab/overrides", 1, "{\"instanceId\":" + instanceId + "}");
            Assert.That(overrides.error, Is.Null);
            var overridesResult = (Dictionary<string, object>)overrides.result;
            var modifications = (List<object>)overridesResult["propertyModifications"];
            Assert.That(modifications.Count, Is.GreaterThanOrEqualTo(1));

            var unpack = _router.Dispatch("prefab/unpack", 1, "{\"instanceId\":" + instanceId + "}");
            Assert.That(unpack.error, Is.Null);

            var unpackedStatus = _router.Dispatch("prefab/status", 1, "{\"instanceId\":" + instanceId + "}");
            Assert.That(unpackedStatus.error, Is.Null);
            var unpackedStatusResult = (Dictionary<string, object>)unpackedStatus.result;
            Assert.That(Convert.ToBoolean(unpackedStatusResult["isInstance"]), Is.False);
        }

        [Test]
        public void SettingsAndBuildControllers_RoundTripWithoutSideEffects()
        {
            var playerSettings = _router.Dispatch("settings/player", 1, "{}");
            Assert.That(playerSettings.error, Is.Null);
            var settingsResult = (Dictionary<string, object>)playerSettings.result;
            var originalProductName = settingsResult["productName"].ToString();

            var setPlayer = _router.Dispatch(
                "settings/player-set",
                1,
                "{\"key\":\"productName\",\"value\":\"UcpQaProduct\"}"
            );
            Assert.That(setPlayer.error, Is.Null);

            var verifyPlayer = _router.Dispatch("settings/player", 1, "{}");
            Assert.That(verifyPlayer.error, Is.Null);
            var verifyResult = (Dictionary<string, object>)verifyPlayer.result;
            Assert.That(verifyResult["productName"].ToString(), Is.EqualTo("UcpQaProduct"));

            var restorePlayer = _router.Dispatch(
                "settings/player-set",
                1,
                "{\"key\":\"productName\",\"value\":\"" + originalProductName + "\"}"
            );
            Assert.That(restorePlayer.error, Is.Null);

            var scenes = _router.Dispatch("build/scenes", 1, "{}");
            Assert.That(scenes.error, Is.Null);
            var scenesResult = (Dictionary<string, object>)scenes.result;
            var currentScenes = (List<object>)scenesResult["scenes"];
            Assert.That(currentScenes.Count, Is.GreaterThanOrEqualTo(1));

            var firstScene = (Dictionary<string, object>)currentScenes[0];
            var firstScenePath = firstScene["path"].ToString();

            var setScenes = _router.Dispatch(
                "build/set-scenes",
                1,
                "{\"scenes\":[\"" + EscapeForJson(firstScenePath) + "\"]}"
            );
            Assert.That(setScenes.error, Is.Null);

            var defines = _router.Dispatch("build/defines", 1, "{}");
            Assert.That(defines.error, Is.Null);
            var definesResult = (Dictionary<string, object>)defines.result;
            var originalDefines = definesResult["defines"].ToString();

            var setDefines = _router.Dispatch("build/set-defines", 1, "{\"defines\":\"" + EscapeForJson(originalDefines) + "\"}");
            Assert.That(setDefines.error, Is.Null);
        }

        private static void DeleteTempAsset()
        {
            if (AssetDatabase.LoadMainAssetAtPath(TempAssetPath) != null)
            {
                AssetDatabase.DeleteAsset(TempAssetPath);
                AssetDatabase.SaveAssets();
            }
        }

        private static void DeleteTempReferenceAsset()
        {
            if (AssetDatabase.LoadMainAssetAtPath(TempReferenceAssetPath) != null)
            {
                AssetDatabase.DeleteAsset(TempReferenceAssetPath);
                AssetDatabase.SaveAssets();
            }
        }

        private static void DeleteTempPrefab()
        {
            if (AssetDatabase.LoadMainAssetAtPath(TempPrefabPath) != null)
            {
                AssetDatabase.DeleteAsset(TempPrefabPath);
                AssetDatabase.SaveAssets();
            }
        }

        private static void DeleteTempMaterial()
        {
            if (AssetDatabase.LoadMainAssetAtPath(TempMaterialPath) != null)
            {
                AssetDatabase.DeleteAsset(TempMaterialPath);
                AssetDatabase.SaveAssets();
            }
        }

        private static void DeleteTempTextFile()
        {
            if (AssetDatabase.LoadMainAssetAtPath(TempTextPath) != null)
            {
                AssetDatabase.DeleteAsset(TempTextPath);
                AssetDatabase.SaveAssets();
            }
        }

        private static string FindFirstFloatOrRangeProperty(Shader shader)
        {
            for (var index = 0; index < shader.GetPropertyCount(); index++)
            {
                var propertyType = shader.GetPropertyType(index);
                if (propertyType == UnityEngine.Rendering.ShaderPropertyType.Float
                    || propertyType == UnityEngine.Rendering.ShaderPropertyType.Range)
                {
                    return shader.GetPropertyName(index);
                }
            }

            return null;
        }

        private static string EscapeForJson(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }

        private static Dictionary<string, object> FindAssetMatch(List<object> matches, string expectedPath, string expectedName)
        {
            foreach (var entry in matches)
            {
                var match = (Dictionary<string, object>)entry;
                var path = match.ContainsKey("path") ? match["path"].ToString() : string.Empty;
                var name = match.ContainsKey("name") ? match["name"].ToString() : string.Empty;
                if (path == expectedPath && name == expectedName)
                    return match;
            }

            return null;
        }

        private sealed class SearchRootAsset : ScriptableObject
        {
        }

        private sealed class SearchNestedAsset : ScriptableObject
        {
        }

        private sealed class BatchWritableAsset : ScriptableObject
        {
            public int maxPlayers;
            public float spawnDelay;
            public SearchRootAsset referenceAsset;
        }

        private sealed class ReferenceComponent : MonoBehaviour
        {
            public SearchRootAsset referenceAsset;
        }
    }
}