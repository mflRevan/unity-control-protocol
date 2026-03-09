using UnityEditor;
using UnityEditor.Compilation;

namespace UCP.Bridge
{
    public static class CompilationController
    {
        public static void Register(CommandRouter router)
        {
            router.Register("compile", HandleCompile);
            router.Register("refresh-assets", HandleRefresh);
        }

        private static object HandleCompile(string paramsJson)
        {
            CompilationPipeline.RequestScriptCompilation();
            return new { status = "ok", message = "Compilation requested" };
        }

        private static object HandleRefresh(string paramsJson)
        {
            AssetDatabase.Refresh();
            return new { status = "ok", message = "Asset database refreshed" };
        }
    }
}
