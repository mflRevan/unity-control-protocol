using System;
using System.Collections.Generic;

namespace UCP.Bridge
{
    /// <summary>Registers the UI Toolkit discovery, lint, and asynchronous harness methods.</summary>
    public static class UiController
    {
        public static void Register(CommandRouter router)
        {
#if UNITY_6000_0_OR_NEWER
            router.Register("ui/list", HandleList);
            router.Register("ui/lint", HandleLint);
            router.Register("ui/inspect", parameters => Start("inspect", parameters));
            router.Register("ui/screenshot", parameters => Start("screenshot", parameters));
            router.Register("ui/check", parameters => Start("check", parameters));
            router.Register("ui/status", HandleStatus);
#else
            router.Register("ui/list", Unsupported);
            router.Register("ui/lint", Unsupported);
            router.Register("ui/inspect", Unsupported);
            router.Register("ui/screenshot", Unsupported);
            router.Register("ui/check", Unsupported);
            router.Register("ui/status", Unsupported);
#endif
        }

#if UNITY_6000_0_OR_NEWER
        private static object HandleList(string paramsJson)
        {
            return UiScenarioLoader.List(ParseParameters(paramsJson));
        }

        private static object HandleLint(string paramsJson)
        {
            return UiLintService.Run(ParseParameters(paramsJson));
        }

        private static object Start(string operation, string paramsJson)
        {
            return UiOperationManager.Start(operation, ParseParameters(paramsJson));
        }

        private static object HandleStatus(string paramsJson)
        {
            var parameters = ParseParameters(paramsJson);
            var operationId = parameters.TryGetValue("operationId", out var value)
                ? value?.ToString()
                : null;
            return UiOperationManager.Status(operationId);
        }

        private static Dictionary<string, object> ParseParameters(string paramsJson)
        {
            if (string.IsNullOrWhiteSpace(paramsJson))
                return new Dictionary<string, object>(StringComparer.Ordinal);
            var value = MiniJson.Deserialize(paramsJson);
            if (value is not Dictionary<string, object> parameters)
                throw new ArgumentException("UI command parameters must be a JSON object");
            return parameters;
        }
#else
        private static object Unsupported(string paramsJson)
        {
            throw new ArgumentException(
                "The ucp ui command family requires Unity 6.0 or newer; this project uses an older Unity Editor");
        }
#endif
    }
}
