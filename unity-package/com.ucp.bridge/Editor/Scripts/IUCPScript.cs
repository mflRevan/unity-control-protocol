namespace UCP.Bridge
{
    /// <summary>
    /// Interface for UCP scripts - editor automation sequences auto-discovered by the bridge.
    /// Implement this interface in any Editor script to make it callable via `ucp exec`.
    ///
    /// Example:
    /// <code>
    /// public class ValidateScene : IUCPScript
    /// {
    ///     public string Name => "validate-scene";
    ///     public string Description => "Check that the active scene meets quality requirements";
    ///     public object Execute(string paramsJson)
    ///     {
    ///         var errors = new List&lt;string&gt;();
    ///         // ... validation logic ...
    ///         return new { valid = errors.Count == 0, errors };
    ///     }
    /// }
    /// </code>
    /// </summary>
    public interface IUCPScript
    {
        /// <summary>Unique name used to invoke this script via CLI (e.g., "validate-scene").</summary>
        string Name { get; }

        /// <summary>Short human-readable description of what this script does.</summary>
        string Description { get; }

        /// <summary>
        /// Execute the script. Runs on Unity's main thread.
        /// </summary>
        /// <param name="paramsJson">Optional JSON string with parameters. Defaults to "{}".</param>
        /// <returns>Any serializable object - will be returned as JSON to the CLI.</returns>
        object Execute(string paramsJson);
    }
}
