using System;
using System.Collections.Generic;
using UnityEngine;

namespace UCP.Bridge
{
    /// <summary>
    /// Routes JSON-RPC method names to handler functions.
    /// </summary>
    public class CommandRouter
    {
        public delegate object CommandHandler(string paramsJson);

        private readonly Dictionary<string, CommandHandler> _handlers = new();

        public void Register(string method, CommandHandler handler)
        {
            _handlers[method] = handler;
        }

        public JsonRpcResponse Dispatch(string method, long id, string paramsJson)
        {
            if (!_handlers.TryGetValue(method, out var handler))
            {
                return JsonRpcResponse.Error(id, ErrorCodes.MethodNotFound,
                    $"Method not found: {method}");
            }

            try
            {
                var result = handler(paramsJson);
                return JsonRpcResponse.Success(id, result);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UCP] Error handling '{method}': {ex}");
                return JsonRpcResponse.Error(id, ErrorCodes.InternalError, ex.Message);
            }
        }

        public bool HasMethod(string method) => _handlers.ContainsKey(method);

        public IEnumerable<string> RegisteredMethods => _handlers.Keys;
    }
}
