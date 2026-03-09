using UnityEditor;

namespace UCP.Bridge
{
    public static class PlayModeController
    {
        public static void Register(CommandRouter router)
        {
            router.Register("play", HandlePlay);
            router.Register("stop", HandleStop);
            router.Register("pause", HandlePause);
        }

        private static object HandlePlay(string paramsJson)
        {
            if (EditorApplication.isPlaying)
                return new { status = "already_playing" };

            EditorApplication.isPlaying = true;
            return new { status = "ok" };
        }

        private static object HandleStop(string paramsJson)
        {
            if (!EditorApplication.isPlaying)
                return new { status = "already_stopped" };

            EditorApplication.isPlaying = false;
            return new { status = "ok" };
        }

        private static object HandlePause(string paramsJson)
        {
            EditorApplication.isPaused = !EditorApplication.isPaused;
            return new { status = "ok", paused = EditorApplication.isPaused };
        }
    }
}
