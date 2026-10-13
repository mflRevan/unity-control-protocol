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
            catch (ArgumentException ex)
            {
                return JsonRpcResponse.Error(id, ErrorCodes.InvalidParams, ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return JsonRpcResponse.Error(id, ErrorCodes.FileAccessDenied, ex.Message);
            }
            catch (Exception ex)
            {
                // The failure goes back to the caller in the response. Logging it as a console
                // error made every failed request (a missing file, a bad property name) count as
                // a project error in the editor-state line the CLI appends to every command, so
                // an agent's own typos looked like broken scripts. The full exception still goes
                // to the editor log, as a plain entry.
                Debug.Log($"[UCP] '{method}' failed: {ex}");
                return JsonRpcResponse.Error(id, ErrorCodes.InternalError, ex.Message);
            }
        }

        public bool HasMethod(string method) => _handlers.ContainsKey(method);

        public IEnumerable<string> RegisteredMethods => _handlers.Keys;
    }
}
