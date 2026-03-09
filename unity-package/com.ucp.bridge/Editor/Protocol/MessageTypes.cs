using System;
using System.Collections.Generic;
using UnityEngine;

namespace UCP.Bridge
{
    /// <summary>
    /// JSON-RPC 2.0 message types for the UCP protocol.
    /// </summary>

    [Serializable]
    public class JsonRpcRequest
    {
        public string jsonrpc = "2.0";
        public long id;
        public string method;
        public string @params; // raw JSON string — parsed by handlers
    }

    [Serializable]
    public class JsonRpcResponse
    {
        public string jsonrpc = "2.0";
        public long? id;
        public object result;
        public JsonRpcError error;

        public static JsonRpcResponse Success(long id, object data)
        {
            return new JsonRpcResponse { id = id, result = data };
        }

        public static JsonRpcResponse Error(long id, int code, string message, object data = null)
        {
            return new JsonRpcResponse
            {
                id = id,
                error = new JsonRpcError { code = code, message = message, data = data }
            };
        }
    }

    [Serializable]
    public class JsonRpcError
    {
        public int code;
        public string message;
        public object data;
    }

    [Serializable]
    public class JsonRpcNotification
    {
        public string jsonrpc = "2.0";
        public string method;
        public object @params;
    }

    /// <summary>
    /// Standard UCP error codes.
    /// </summary>
    public static class ErrorCodes
    {
        public const int ParseError = -32700;
        public const int InvalidRequest = -32600;
        public const int MethodNotFound = -32601;
        public const int InvalidParams = -32602;
        public const int InternalError = -32603;
        public const int UnityError = -32000;
        public const int SceneNotLoaded = -32001;
        public const int CompilationFailed = -32002;
        public const int PlayModeConflict = -32003;
        public const int FileAccessDenied = -32004;
        public const int TestFailed = -32005;
        public const int ScreenshotFailed = -32006;
        public const int ObjectNotFound = -32007;
        public const int Timeout = -32008;
        public const int AuthFailed = -32009;
    }
}
