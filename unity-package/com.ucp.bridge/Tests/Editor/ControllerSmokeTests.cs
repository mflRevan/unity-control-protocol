using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
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
        private const string TempScriptPath = "Assets/UcpControllerSmokeComponent.cs";
        private const string TempTexturePath = "Assets/UcpImporterSmoke.png";
        private const string TempProfilerExportPath = "ProfilerCaptures\\smoke-export.json";
        private const string TempLocalPackageFolder = "TempUcpLocalPackage";
        private const string TempLocalPackageName = "com.ucp.temp.local";

        private CommandRouter _router;

        [SetUp]
        public void SetUp()
        {
            _router = new CommandRouter();
            SnapshotController.Register(_router);
            AssetController.Register(_router);
            ImporterController.Register(_router);
            LogsController.Register(_router);
            HierarchyController.Register(_router);
            ProfilerController.Register(_router);
            PropertyController.Register(_router);
            FileController.Register(_router);
            MaterialController.Register(_router);
            PrefabController.Register(_router);
            BuildController.Register(_router);
            EditorSettingsController.Register(_router);
            SceneController.Register(_router);
            PackagesController.Register(_router);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            DeleteTempAsset();
            DeleteTempReferenceAsset();
            DeleteTempPrefab();
            DeleteTempMaterial();
            DeleteTempTextFile();
            DeleteTempScriptFile();
            DeleteTempTextureAsset();
            DeleteTempProfilerExport();
            DeleteTempLocalPackage();
            RemoveTempLocalPackageDependencyIfPresent();
            LogsController.ClearHistoryForTests();
            AssetImportSupport.ClearTestState();
            Profiler.enabled = false;
            Profiler.enableBinaryLog = false;
            Profiler.enableAllocationCallstacks = false;
            Profiler.logFile = string.Empty;
        }

        [TearDown]
        public void TearDown()
        {
            DeleteTempAsset();
            DeleteTempReferenceAsset();
            DeleteTempPrefab();
            DeleteTempMaterial();
            DeleteTempTextFile();
            DeleteTempScriptFile();
            DeleteTempTextureAsset();
            DeleteTempProfilerExport();
            DeleteTempLocalPackage();
            RemoveTempLocalPackageDependencyIfPresent();
            LogsController.ClearHistoryForTests();
            AssetImportSupport.ClearTestState();
            Profiler.enabled = false;
            Profiler.enableBinaryLog = false;
            Profiler.enableAllocationCallstacks = false;
            Profiler.logFile = string.Empty;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [Test]
        public void ProfilerStatus_ReturnsCapabilitiesAndConfig()
        {
            var response = _router.Dispatch("profiler/status", 1, "{}");

            Assert.That(response.error, Is.Null);

            var result = (Dictionary<string, object>)response.result;
            Assert.That(result.ContainsKey("session"), Is.True);
            Assert.That(result.ContainsKey("config"), Is.True);
            Assert.That(result.ContainsKey("capabilities"), Is.True);
            Assert.That(result.ContainsKey("editorState"), Is.True);

            var capabilities = (Dictionary<string, object>)result["capabilities"];
            Assert.That(Convert.ToBoolean(capabilities["status"]), Is.True);
            Assert.That(Convert.ToBoolean(capabilities["sessionControl"]), Is.True);
        }

        [Test]
        public void ProfilerSessionStartStop_TogglesProfilerState()
        {
            Profiler.maxUsedMemory = 512 * 1024 * 1024;

            var start = _router.Dispatch(
                "profiler/session/start",
                1,
                "{\"mode\":\"edit\",\"binaryLog\":false,\"allocationCallstacks\":true}");

            Assert.That(start.error, Is.Null);
            Assert.That(Profiler.enabled, Is.True);
            Assert.That(Profiler.maxUsedMemory, Is.EqualTo(64 * 1024 * 1024));

            var startResult = (Dictionary<string, object>)start.result;
            Assert.That(startResult["status"], Is.EqualTo("started"));
            var session = (Dictionary<string, object>)startResult["session"];
            Assert.That(Convert.ToBoolean(session["active"]), Is.True);
            Assert.That(session["effectiveMode"], Is.EqualTo("edit"));

            var stop = _router.Dispatch("profiler/session/stop", 1, "{}");

            Assert.That(stop.error, Is.Null);
            Assert.That(Profiler.enabled, Is.False);
            Assert.That(Profiler.enableAllocationCallstacks, Is.False);
            Assert.That(Profiler.maxUsedMemory, Is.EqualTo(512 * 1024 * 1024));

            var stopResult = (Dictionary<string, object>)stop.result;
            Assert.That(stopResult["status"], Is.EqualTo("stopped"));
        }

        [Test]
        public void ProfilerConfigSet_UpdatesAllocationCallstacksFlag()
        {
            var response = _router.Dispatch(
                "profiler/config/set",
                1,
                "{\"mode\":\"edit\",\"binaryLog\":false,\"allocationCallstacks\":true}");

            Assert.That(response.error, Is.Null);
            Assert.That(Profiler.enableAllocationCallstacks, Is.True);

            var result = (Dictionary<string, object>)response.result;
            var config = (Dictionary<string, object>)result["config"];
            Assert.That(Convert.ToBoolean(config["allocationCallstacks"]), Is.True);
        }

        [Test]
        public void ProfilerCaptureSave_ExportsStructuredJsonSnapshot()
        {
            var start = _router.Dispatch(
                "profiler/session/start",
                1,
                "{\"mode\":\"edit\",\"binaryLog\":false,\"allocationCallstacks\":false,\"clearFirst\":true}");

            Assert.That(start.error, Is.Null);

            var response = _router.Dispatch(
                "profiler/capture/save",
                1,
                "{\"output\":\"ProfilerCaptures/smoke-export.json\"}");

            Assert.That(response.error, Is.Null);

            var result = (Dictionary<string, object>)response.result;
            var capture = (Dictionary<string, object>)result["capture"];
            Assert.That(capture["kind"], Is.EqualTo("json"));
            Assert.That(Convert.ToBoolean(capture["exists"]), Is.True);
            Assert.That(File.Exists(ResolveProjectRelativePath(TempProfilerExportPath)), Is.True);
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

            var updated = EditorUtility.EntityIdToObject(instanceId) as GameObject;
            Assert.That(updated, Is.Not.Null);
            var localPosition = updated.transform.localPosition;
            Assert.That(localPosition.x, Is.EqualTo(1f).Within(0.001f));
            Assert.That(localPosition.y, Is.EqualTo(2f).Within(0.001f));
            Assert.That(localPosition.z, Is.EqualTo(3f).Within(0.001f));

            var removeComponent = _router.Dispatch("object/remove-component", 1, "{\"instanceId\":" + instanceId + ",\"type\":\"BoxCollider\"}");
            Assert.That(removeComponent.error, Is.Null);

            var delete = _router.Dispatch("object/delete", 1, "{\"instanceId\":" + instanceId + "}");
            Assert.That(delete.error, Is.Null);
            Assert.That(EditorUtility.EntityIdToObject(instanceId), Is.Null);
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
        public void ObjectSetProperty_AssignsRendererMaterialArrayByAssetPath()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Assert.That(shader, Is.Not.Null, "Expected a lit shader to exist for the material smoke test");

            var material = new Material(shader) { name = "UcpControllerSmokeMat" };
            AssetDatabase.CreateAsset(material, TempMaterialPath);
            AssetDatabase.SaveAssets();

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var renderer = cube.GetComponent<MeshRenderer>();
            Assert.That(renderer, Is.Not.Null);

            var response = _router.Dispatch(
                "object/set-property",
                1,
                "{\"instanceId\":" + cube.GetInstanceID() + ",\"component\":\"MeshRenderer\",\"property\":\"m_Materials\",\"value\":[{\"path\":\"" + TempMaterialPath + "\"}]}"
            );

            Assert.That(response.error, Is.Null);
            Assert.That(renderer.sharedMaterials, Has.Length.EqualTo(1));
            Assert.That(renderer.sharedMaterials[0], Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(renderer.sharedMaterials[0]), Is.EqualTo(TempMaterialPath));
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
            Assert.That(response.error.code, Is.EqualTo(ErrorCodes.InvalidParams));
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

            var traversal = _router.Dispatch("file/read", 1, "{\"path\":\"../outside.txt\"}");
            Assert.That(traversal.error, Is.Not.Null);
            Assert.That(traversal.error.code, Is.EqualTo(ErrorCodes.FileAccessDenied));
        }

        [Test]
        public void FileController_Write_RefreshesAssetDatabaseForNewAssets()
        {
            var write = _router.Dispatch(
                "file/write",
                1,
                "{\"path\":\"Assets/UcpControllerSmokeComponent.cs\",\"content\":\"using UnityEngine; public class UcpControllerSmokeComponent : MonoBehaviour {}\"}"
            );

            Assert.That(write.error, Is.Null);

            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(TempScriptPath);
            Assert.That(script, Is.Not.Null);
            Assert.That(script.name, Is.EqualTo("UcpControllerSmokeComponent"));
        }

        [Test]
        public void FileController_Write_ReimportsOwningAssetForMetaFiles()
        {
            CreateTempTextureAsset(Color.cyan);
            var metaPath = TempTexturePath + ".meta";
            var metaContent = File.ReadAllText(ResolveProjectRelativePath(metaPath));
            AssetImportSupport.ClearTestState();

            var write = _router.Dispatch(
                "file/write",
                1,
                MiniJson.Serialize(new Dictionary<string, object>
                {
                    ["path"] = metaPath,
                    ["content"] = metaContent
                }));

            Assert.That(write.error, Is.Null);

            var result = (Dictionary<string, object>)write.result;
            var reimport = (Dictionary<string, object>)result["reimport"];
            Assert.That(Convert.ToBoolean(reimport["reimported"]), Is.True);
            Assert.That(reimport["assetPath"], Is.EqualTo(TempTexturePath));
            Assert.That(AssetImportSupport.LastReimportedPathForTests, Is.EqualTo(TempTexturePath));
        }

        [Test]
        public void ImporterController_ReadWriteAndReimport_TextureSettings()
        {
            CreateTempTextureAsset(Color.red);

            var read = _router.Dispatch(
                "asset/import-settings/read",
                1,
                "{\"path\":\"" + TempTexturePath + "\",\"field\":\"m_IsReadable\"}");

            Assert.That(read.error, Is.Null);
            var readResult = (Dictionary<string, object>)read.result;
            Assert.That(readResult["assetPath"], Is.EqualTo(TempTexturePath));
            Assert.That(readResult["importerType"], Is.EqualTo("TextureImporter"));
            var fields = (List<object>)readResult["fields"];
            Assert.That(fields.Count, Is.EqualTo(1));

            AssetImportSupport.ClearTestState();
            var write = _router.Dispatch(
                "asset/import-settings/write",
                1,
                "{\"path\":\"" + TempTexturePath + "\",\"field\":\"m_IsReadable\",\"value\":true}");

            Assert.That(write.error, Is.Null);
            var writeResult = (Dictionary<string, object>)write.result;
            var reimport = (Dictionary<string, object>)writeResult["reimport"];
            Assert.That(Convert.ToBoolean(reimport["reimported"]), Is.True);
            Assert.That(reimport["assetPath"], Is.EqualTo(TempTexturePath));
            Assert.That(AssetImportSupport.LastReimportedPathForTests, Is.EqualTo(TempTexturePath));

            var importer = AssetImporter.GetAtPath(TempTexturePath) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.isReadable, Is.True);
        }

        [Test]
        public void ImporterController_WriteBatch_CanSkipReimportUntilExplicitReimport()
        {
            CreateTempTextureAsset(Color.green);
            AssetImportSupport.ClearTestState();

            var write = _router.Dispatch(
                "asset/import-settings/write-batch",
                1,
                "{\"path\":\"" + TempTexturePath + "\",\"values\":{\"m_IsReadable\":true},\"noReimport\":true}");

            Assert.That(write.error, Is.Null);
            var writeResult = (Dictionary<string, object>)write.result;
            var reimport = (Dictionary<string, object>)writeResult["reimport"];
            Assert.That(Convert.ToBoolean(reimport["reimported"]), Is.False);
            Assert.That(Convert.ToBoolean(reimport["skipped"]), Is.True);
            Assert.That(AssetImportSupport.LastReimportedPathForTests, Is.Null);

            var explicitReimport = _router.Dispatch(
                "asset/reimport",
                1,
                "{\"path\":\"" + TempTexturePath + ".meta\"}");

            Assert.That(explicitReimport.error, Is.Null);
            var explicitResult = (Dictionary<string, object>)explicitReimport.result;
            Assert.That(explicitResult["assetPath"], Is.EqualTo(TempTexturePath));
            Assert.That(AssetImportSupport.LastReimportedPathForTests, Is.EqualTo(TempTexturePath));

            var importer = AssetImporter.GetAtPath(TempTexturePath) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.isReadable, Is.True);
        }

        [Test]
        public void PackagesController_DependencySetInfoAndRemove_LocalFilePackage()
        {
            CreateTempLocalPackage();

            var set = _router.Dispatch(
                "packages/dependency/set",
                1,
                MiniJson.Serialize(new Dictionary<string, object>
                {
                    ["name"] = TempLocalPackageName,
                    ["reference"] = "file:../" + TempLocalPackageFolder
                }));

            Assert.That(set.error, Is.Null);
            var setResult = (Dictionary<string, object>)set.result;
            Assert.That(setResult["name"], Is.EqualTo(TempLocalPackageName));
            Assert.That(setResult["reference"], Is.EqualTo("file:../" + TempLocalPackageFolder));
            Assert.That(Convert.ToBoolean(setResult["changed"]), Is.True);

            var info = _router.Dispatch(
                "packages/info",
                1,
                MiniJson.Serialize(new Dictionary<string, object>
                {
                    ["name"] = TempLocalPackageName
                }));

            Assert.That(info.error, Is.Null);
            var infoResult = (Dictionary<string, object>)info.result;
            Assert.That(infoResult["name"], Is.EqualTo(TempLocalPackageName));
            Assert.That(Convert.ToBoolean(infoResult["installed"]), Is.True);
            Assert.That(Convert.ToBoolean(infoResult["directDependency"]), Is.True);
            Assert.That(infoResult["source"], Is.EqualTo("Local"));

            var dependencies = _router.Dispatch("packages/dependencies", 1, "{}");
            Assert.That(dependencies.error, Is.Null);
            var dependencyResult = (Dictionary<string, object>)dependencies.result;
            var entries = (List<object>)dependencyResult["dependencies"];
            Assert.That(entries.Exists(item =>
            {
                var entry = (Dictionary<string, object>)item;
                return entry["name"].ToString() == TempLocalPackageName
                    && entry["reference"].ToString() == "file:../" + TempLocalPackageFolder;
            }), Is.True);

            var remove = _router.Dispatch(
                "packages/dependency/remove",
                1,
                MiniJson.Serialize(new Dictionary<string, object>
                {
                    ["name"] = TempLocalPackageName
                }));

            Assert.That(remove.error, Is.Null);
            var removeResult = (Dictionary<string, object>)remove.result;
            Assert.That(removeResult["name"], Is.EqualTo(TempLocalPackageName));
            Assert.That(removeResult["previousReference"], Is.EqualTo("file:../" + TempLocalPackageFolder));

            var after = _router.Dispatch("packages/dependencies", 1, "{}");
            Assert.That(after.error, Is.Null);
            var afterEntries = (List<object>)((Dictionary<string, object>)after.result)["dependencies"];
            Assert.That(afterEntries.Exists(item =>
            {
                var entry = (Dictionary<string, object>)item;
                return entry["name"].ToString() == TempLocalPackageName;
            }), Is.False);
        }

        [Test]
        public void SceneFocus_WithAxis_AlignsSceneCameraTowardTarget()
        {
            var sceneView = EditorWindow.GetWindow<SceneView>();
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "FocusTarget";
            cube.transform.position = new Vector3(2f, 1f, 3f);
            cube.transform.localScale = new Vector3(2f, 2f, 2f);

            var response = _router.Dispatch(
                "scene/focus",
                1,
                "{\"instanceId\":" + cube.GetInstanceID() + ",\"axis\":[1,0,1]}"
            );

            Assert.That(response.error, Is.Null);

            var result = (Dictionary<string, object>)response.result;
            Assert.That(result["name"].ToString(), Is.EqualTo("FocusTarget"));
            Assert.That(Selection.activeGameObject, Is.EqualTo(cube));

            var expectedDirection = new Vector3(1f, 0f, 1f).normalized;
            var axisData = (List<object>)result["axis"];
            var returnedAxis = new Vector3(
                System.Convert.ToSingle(axisData[0]),
                System.Convert.ToSingle(axisData[1]),
                System.Convert.ToSingle(axisData[2]));
            var actualForward = sceneView.camera.transform.forward;
            Assert.That(Vector3.Dot(returnedAxis.normalized, expectedDirection), Is.GreaterThan(0.98f));
            Assert.That(Mathf.Abs(Vector3.Dot(actualForward.normalized, expectedDirection)), Is.GreaterThan(0.98f));
            Assert.That(Vector3.Distance(sceneView.pivot, cube.transform.position), Is.LessThan(2f));
        }

        [Test]
        public void SceneFocus_RejectsZeroAxisVector()
        {
            EditorWindow.GetWindow<SceneView>();
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "ZeroAxisTarget";

            var response = _router.Dispatch(
                "scene/focus",
                1,
                "{\"instanceId\":" + cube.GetInstanceID() + ",\"axis\":[0,0,0]}"
            );

            Assert.That(response.error, Is.Not.Null);
            Assert.That(response.error.code, Is.EqualTo(ErrorCodes.InvalidParams));
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

        private static void DeleteTempScriptFile()
        {
            if (AssetDatabase.LoadMainAssetAtPath(TempScriptPath) != null)
            {
                AssetDatabase.DeleteAsset(TempScriptPath);
                AssetDatabase.SaveAssets();
            }
        }

        private static void DeleteTempTextureAsset()
        {
            if (AssetDatabase.LoadMainAssetAtPath(TempTexturePath) != null)
            {
                AssetDatabase.DeleteAsset(TempTexturePath);
                AssetDatabase.SaveAssets();
            }

            var fullPath = ResolveProjectRelativePath(TempTexturePath);
            if (File.Exists(fullPath))
                File.Delete(fullPath);

            var metaPath = fullPath + ".meta";
            if (File.Exists(metaPath))
                File.Delete(metaPath);

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void DeleteTempProfilerExport()
        {
            var fullPath = ResolveProjectRelativePath(TempProfilerExportPath);
            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }

        private void RemoveTempLocalPackageDependencyIfPresent()
        {
            var manifestPath = ResolveProjectRootRelativePath("Packages/manifest.json");
            if (!File.Exists(manifestPath))
                return;

            var manifest = MiniJson.Deserialize(File.ReadAllText(manifestPath)) as Dictionary<string, object>;
            if (manifest == null
                || !manifest.TryGetValue("dependencies", out var dependenciesObj)
                || !(dependenciesObj is Dictionary<string, object> dependencies)
                || !dependencies.ContainsKey(TempLocalPackageName))
            {
                return;
            }

            var remove = _router.Dispatch(
                "packages/dependency/remove",
                1,
                MiniJson.Serialize(new Dictionary<string, object>
                {
                    ["name"] = TempLocalPackageName
                }));

            Assert.That(remove.error, Is.Null, "Temp local package dependency cleanup should succeed");
        }

        private static void CreateTempLocalPackage()
        {
            DeleteTempLocalPackage();
            var packageRoot = ResolveProjectRootRelativePath(TempLocalPackageFolder);
            Directory.CreateDirectory(packageRoot);
            File.WriteAllText(
                Path.Combine(packageRoot, "package.json"),
                "{\n"
                + "  \"name\": \"" + TempLocalPackageName + "\",\n"
                + "  \"version\": \"1.0.0\",\n"
                + "  \"displayName\": \"UCP Temp Local Package\",\n"
                + "  \"unity\": \"2021.3\",\n"
                + "  \"description\": \"Temporary local package for controller smoke tests.\"\n"
                + "}");
        }

        private static void DeleteTempLocalPackage()
        {
            var packageRoot = ResolveProjectRootRelativePath(TempLocalPackageFolder);
            if (Directory.Exists(packageRoot))
                Directory.Delete(packageRoot, true);
        }

        private static void CreateTempTextureAsset(Color color)
        {
            DeleteTempTextureAsset();

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.SetPixels(new[]
            {
                color,
                color,
                color,
                color
            });
            texture.Apply();

            var bytes = texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture);

            File.WriteAllBytes(ResolveProjectRelativePath(TempTexturePath), bytes);
            AssetDatabase.ImportAsset(
                TempTexturePath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        }

        private static string ResolveProjectRelativePath(string assetPath)
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string ResolveProjectRootRelativePath(string relativePath)
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
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
