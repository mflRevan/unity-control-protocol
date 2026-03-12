using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UCP.Bridge.Tests
{
    public class ControllerSmokeTests
    {
        private const string TempAssetPath = "Assets/UcpControllerSmoke.asset";

        private CommandRouter _router;

        [SetUp]
        public void SetUp()
        {
            _router = new CommandRouter();
            SnapshotController.Register(_router);
            AssetController.Register(_router);
            LogsController.Register(_router);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            DeleteTempAsset();
            LogsController.ClearHistoryForTests();
        }

        [TearDown]
        public void TearDown()
        {
            DeleteTempAsset();
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
                "{\"type\":\"SearchNestedAsset\",\"name\":\"SmokeNested\",\"path\":\"Assets\",\"maxResults\":10}"
            );

            Assert.That(response.error, Is.Null);

            var result = (Dictionary<string, object>)response.result;
            Assert.That(Convert.ToInt32(result["total"]), Is.EqualTo(1));
            Assert.That(Convert.ToInt32(result["returned"]), Is.EqualTo(1));

            var matches = (List<object>)result["results"];
            var match = (Dictionary<string, object>)matches[0];
            Assert.That(match["path"], Is.EqualTo(TempAssetPath));
            Assert.That(match["type"], Is.EqualTo("SearchNestedAsset"));
            Assert.That(match["name"], Is.EqualTo("SmokeNested"));
            Assert.That(Convert.ToBoolean(match["isSubAsset"]), Is.True);
        }

        [Test]
        public void LogsTail_TruncatesBulkResultsToTenEntries()
        {
            for (var index = 0; index < 12; index++)
                LogsController.RecordTestLog("info", $"log {index}");

            var response = _router.Dispatch("logs/tail", 1, "{\"count\":50}");

            Assert.That(response.error, Is.Null);

            var result = (Dictionary<string, object>)response.result;
            Assert.That(Convert.ToInt32(result["total"]), Is.EqualTo(12));
            Assert.That(Convert.ToInt32(result["returned"]), Is.EqualTo(10));
            Assert.That(Convert.ToBoolean(result["truncated"]), Is.True);

            var logs = (List<object>)result["logs"];
            var first = (Dictionary<string, object>)logs[0];
            Assert.That(Convert.ToInt64(first["id"]), Is.EqualTo(12));
            Assert.That(first.ContainsKey("messagePreview"), Is.True);
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

        private static void DeleteTempAsset()
        {
            if (AssetDatabase.LoadMainAssetAtPath(TempAssetPath) != null)
            {
                AssetDatabase.DeleteAsset(TempAssetPath);
                AssetDatabase.SaveAssets();
            }
        }

        private sealed class SearchRootAsset : ScriptableObject
        {
        }

        private sealed class SearchNestedAsset : ScriptableObject
        {
        }
    }
}