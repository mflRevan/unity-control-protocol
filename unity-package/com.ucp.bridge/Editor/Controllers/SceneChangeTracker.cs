using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UCP.Bridge
{
    [InitializeOnLoad]
    public static class SceneChangeTracker
    {
        private sealed class TrackedSceneChange
        {
            public int? InstanceId;
            public string Name;
            public HashSet<string> Components = new();
        }

        private static readonly Dictionary<int, Dictionary<string, TrackedSceneChange>> s_changesByScene = new();

        static SceneChangeTracker()
        {
            Undo.postprocessModifications += OnPostprocessModifications;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            EditorSceneManager.sceneSaved += ClearScene;
            EditorSceneManager.sceneOpened += (scene, mode) => ClearScene(scene);
            EditorSceneManager.newSceneCreated += (scene, setup, mode) => ClearScene(scene);
        }

        public static void RecordGameObjectChange(GameObject gameObject, string componentName)
        {
            if (gameObject == null)
                return;

            RecordSceneChange(gameObject.scene, gameObject.GetInstanceID(), gameObject.name, componentName);
        }

        public static void RecordDeletedObject(Scene scene, int instanceId, string name, string componentName)
        {
            RecordSceneChange(scene, instanceId, name, componentName);
        }

        public static void RecordSceneSettingChange(Scene scene, string changeName)
        {
            RecordSceneChange(scene, null, scene.name, changeName);
        }

        public static Dictionary<string, object> DescribeActiveSceneChanges(int maxEntries)
        {
            return DescribeSceneChanges(SceneManager.GetActiveScene(), maxEntries);
        }

        public static Dictionary<string, object> DescribeSceneChanges(Scene scene, int maxEntries)
        {
            var modifications = new List<object>();
            var omittedCount = 0;

            if (scene.IsValid() && s_changesByScene.TryGetValue(scene.handle, out var trackedChanges))
            {
                var ordered = trackedChanges.Values
                    .OrderBy(change => change.InstanceId.HasValue ? 0 : 1)
                    .ThenBy(change => change.Name)
                    .ToList();

                omittedCount = Mathf.Max(ordered.Count - maxEntries, 0);
                foreach (var change in ordered.Take(maxEntries))
                {
                    modifications.Add(new Dictionary<string, object>
                    {
                        ["instanceId"] = change.InstanceId.HasValue ? change.InstanceId.Value : null,
                        ["name"] = change.Name,
                        ["components"] = change.Components.OrderBy(component => component).Cast<object>().ToList()
                    });
                }
            }

            if (scene.isDirty && modifications.Count == 0)
            {
                modifications.Add(new Dictionary<string, object>
                {
                    ["instanceId"] = null,
                    ["name"] = scene.name,
                    ["components"] = new List<object> { "UnknownChange" }
                });
            }

            return new Dictionary<string, object>
            {
                ["name"] = scene.name,
                ["path"] = scene.path,
                ["isDirty"] = scene.isDirty,
                ["isLoaded"] = scene.isLoaded,
                ["modifications"] = modifications,
                ["omittedCount"] = omittedCount
            };
        }

        public static void ClearScene(Scene scene)
        {
            if (!scene.IsValid())
                return;

            s_changesByScene.Remove(scene.handle);
        }

        private static UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
        {
            foreach (var modification in modifications)
            {
                RecordTarget(modification.currentValue.target);
            }

            return modifications;
        }

        private static void OnUndoRedoPerformed()
        {
            var activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.isDirty)
            {
                RecordSceneSettingChange(activeScene, "Undo/Redo");
            }
        }

        private static void RecordTarget(Object target)
        {
            if (target is Component component && component.gameObject != null)
            {
                RecordGameObjectChange(component.gameObject, component.GetType().Name);
            }
            else if (target is GameObject gameObject)
            {
                RecordGameObjectChange(gameObject, "GameObject");
            }
        }

        private static void RecordSceneChange(Scene scene, int? instanceId, string name, string componentName)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            if (!s_changesByScene.TryGetValue(scene.handle, out var sceneChanges))
            {
                sceneChanges = new Dictionary<string, TrackedSceneChange>();
                s_changesByScene[scene.handle] = sceneChanges;
            }

            var key = instanceId.HasValue ? instanceId.Value.ToString() : $"scene::{name}";
            if (!sceneChanges.TryGetValue(key, out var trackedChange))
            {
                trackedChange = new TrackedSceneChange
                {
                    InstanceId = instanceId,
                    Name = string.IsNullOrWhiteSpace(name) ? "Unnamed" : name
                };
                sceneChanges[key] = trackedChange;
            }

            if (!string.IsNullOrWhiteSpace(componentName))
            {
                trackedChange.Components.Add(componentName);
            }
        }
    }
}
