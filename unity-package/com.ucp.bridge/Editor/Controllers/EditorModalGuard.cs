using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace UCP.Bridge
{
    /// <summary>
    /// Centralizes editor operations that must stay non-blocking when the bridge drives
    /// the editor headlessly.
    ///
    /// Bridge commands run on the main thread via EditorApplication.update. Any modal
    /// dialog (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo,
    /// EditorUtility.DisplayDialog, file/folder panels, ...) blocks that loop with no user
    /// present to dismiss it, hanging every queued command and timing out the CLI. Bridge
    /// code must therefore never call a modal API. Scene saving is the most common trap, so
    /// it lives here behind a single audited, modal-free implementation shared by every
    /// controller instead of being copy-pasted per controller.
    /// </summary>
    internal static class EditorModalGuard
    {
        /// <summary>
        /// Saves every loaded dirty scene without prompting. Dirty untitled scenes have no
        /// path to save to; they are discarded (replaced with a fresh empty scene) when
        /// <paramref name="discardUntitled"/> is true, otherwise an exception is thrown so
        /// the caller surfaces a clear error instead of silently losing work or blocking on
        /// a save dialog.
        /// </summary>
        public static void SaveOpenDirtyScenes(bool saveDirtyScenes, bool discardUntitled)
        {
            if (!saveDirtyScenes)
                return;

            var requiresUntitledDiscard = false;

            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (!scene.isLoaded || !scene.isDirty)
                    continue;

                if (string.IsNullOrEmpty(scene.path))
                {
                    if (!discardUntitled)
                        throw new System.InvalidOperationException("Dirty untitled scene cannot be auto-saved. Retry with discardUntitled=true.");

                    requiresUntitledDiscard = true;
                    continue;
                }

                if (!EditorSceneManager.SaveScene(scene))
                    throw new System.InvalidOperationException($"Failed to auto-save dirty scene: {scene.path}");

                SceneChangeTracker.ClearScene(scene);
            }

            if (requiresUntitledDiscard)
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }
}
