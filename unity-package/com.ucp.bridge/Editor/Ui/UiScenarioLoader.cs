#if UNITY_6000_0_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UCP.Bridge
{
    internal static class UiScenarioLoader
    {
        private const int MaxFixtureStates = 64;
        private const long MaxViewportPixelArea = 8_388_608L;

        private static readonly HashSet<string> FixtureFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "schemaVersion", "document", "defaultState", "viewport", "settle", "data", "set", "collections", "states"
        };

        private static readonly HashSet<string> StateFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "viewport", "settle", "data", "set", "collections"
        };

        private static readonly HashSet<string> ViewportFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "width", "height"
        };

        private static readonly HashSet<string> SettleFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "stableFrames", "pixelStableFrames", "timeoutSeconds"
        };

        private static readonly HashSet<string> SetFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "target", "property", "value"
        };

        private static readonly HashSet<string> CollectionFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "target", "mode", "source", "template", "itemHeight"
        };

        public static List<UiResolvedScenario> Resolve(
            Dictionary<string, object> parameters,
            bool allStates = false)
        {
            parameters = parameters ?? new Dictionary<string, object>();
            var target = RequireString(parameters, "target", "$request");
            var targetPath = NormalizeAssetPath(target, null, "$request.target");
            var requestedState = OptionalString(parameters, "state", "$request");

            if (allStates && requestedState != null)
                throw Error("ui.state-conflict", "'state' and 'allStates' cannot be used together.", "$request");

            var requestData = OptionalObject(parameters, "data", "$request.data");
            var hasRequestViewport = parameters.TryGetValue("viewport", out var requestViewportValue);
            var hasRequestSettle = parameters.TryGetValue("settle", out var requestSettleValue);

            if (targetPath.EndsWith(".uxml", StringComparison.OrdinalIgnoreCase))
            {
                if (requestedState != null && !string.Equals(requestedState, "default", StringComparison.Ordinal))
                    throw Error("ui.state-not-found", $"Direct UXML targets only expose the 'default' state, not '{requestedState}'.", "$request.state");

                var document = LoadVisualTree(targetPath, "$request.target");
                return new List<UiResolvedScenario>
                {
                    new UiResolvedScenario(
                        targetPath,
                        null,
                        "default",
                        targetPath,
                        document,
                        MergeData(null, null, requestData),
                        Array.Empty<UiSetOperation>(),
                        Array.Empty<UiCollectionDefinition>(),
                        hasRequestViewport
                            ? ParseViewport(requestViewportValue, new UiViewport(), "$request.viewport")
                            : new UiViewport(),
                        hasRequestSettle
                            ? ParseSettle(requestSettleValue, new UiSettleOptions(), "$request.settle")
                            : new UiSettleOptions())
                };
            }

            if (!targetPath.EndsWith(".ucp-ui.json", StringComparison.OrdinalIgnoreCase))
                throw Error("ui.target-type", "UI targets must end in '.uxml' or '.ucp-ui.json'.", "$request.target");

            var fixture = ParseFixture(targetPath);
            var stateNames = SelectStates(fixture, requestedState, allStates);
            var scenarios = new List<UiResolvedScenario>(stateNames.Count);
            foreach (var stateName in stateNames)
            {
                var state = fixture.States[stateName];
                var stateLocation = $"{targetPath}#states.{stateName}";
                var viewport = state.Viewport ?? fixture.Viewport;
                var settle = state.Settle ?? fixture.Settle;
                var set = state.SetOperations ?? fixture.SetOperations;
                var collections = state.Collections ?? fixture.Collections;

                scenarios.Add(new UiResolvedScenario(
                    targetPath,
                    targetPath,
                    stateName,
                    fixture.DocumentPath,
                    fixture.Document,
                    MergeData(fixture.Data, state.Data, requestData),
                    set ?? Array.Empty<UiSetOperation>(),
                    collections ?? Array.Empty<UiCollectionDefinition>(),
                    hasRequestViewport
                        ? ParseViewport(requestViewportValue, viewport ?? new UiViewport(), "$request.viewport")
                        : viewport ?? new UiViewport(),
                    hasRequestSettle
                        ? ParseSettle(requestSettleValue, settle ?? new UiSettleOptions(), "$request.settle")
                        : settle ?? new UiSettleOptions()));
            }

            return scenarios;
        }

        public static object List(Dictionary<string, object> parameters)
        {
            parameters = parameters ?? new Dictionary<string, object>();
            var limit = OptionalInt(parameters, "limit", 200, "$request.limit", 1, 5000);
            var includePackages = OptionalBool(parameters, "includePackages", false, "$request.includePackages");
            var roots = new List<string>();

            var requestedPath = OptionalString(parameters, "path", "$request");
            if (requestedPath != null)
            {
                var path = NormalizeAssetPath(requestedPath, null, "$request.path");
                if (!AssetDatabase.IsValidFolder(path))
                    throw Error("ui.list-path", $"UI list path is not an asset folder: {path}", "$request.path");
                roots.Add(path);
            }
            else
            {
                roots.Add("Assets");
            }

            if (includePackages && !roots.Contains("Packages"))
                roots.Add("Packages");

            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var guid in AssetDatabase.FindAssets("t:VisualTreeAsset", roots.ToArray()))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".uxml", StringComparison.OrdinalIgnoreCase))
                    paths.Add(path);
            }

            foreach (var guid in AssetDatabase.FindAssets("t:TextAsset", roots.ToArray()))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".ucp-ui.json", StringComparison.OrdinalIgnoreCase))
                    paths.Add(path);
            }

            var ordered = paths.OrderBy(path => path, StringComparer.Ordinal).ToList();
            var items = new List<object>(Math.Min(ordered.Count, limit));
            foreach (var path in ordered.Take(limit))
            {
                if (path.EndsWith(".uxml", StringComparison.OrdinalIgnoreCase))
                {
                    items.Add(new Dictionary<string, object>
                    {
                        ["target"] = path,
                        ["type"] = "uxml",
                        ["valid"] = true,
                        ["states"] = new List<object> { "default" },
                        ["defaultState"] = "default"
                    });
                    continue;
                }

                try
                {
                    var fixture = ParseFixture(path);
                    items.Add(FixtureSummary(fixture));
                }
                catch (UiScenarioException ex)
                {
                    items.Add(new Dictionary<string, object>
                    {
                        ["target"] = path,
                        ["type"] = "fixture",
                        ["valid"] = false,
                        ["diagnostics"] = new List<object> { ex.ToDictionary() }
                    });
                }
            }

            return new Dictionary<string, object>
            {
                ["items"] = items,
                ["count"] = ordered.Count,
                ["returned"] = items.Count,
                ["truncated"] = ordered.Count > items.Count,
                ["searchRoots"] = roots.Select(root => (object)root).ToList()
            };
        }

        public static Dictionary<string, object> ValidateFixture(string fixtureAssetPath)
        {
            var targetPath = NormalizeAssetPath(fixtureAssetPath, null, "$request.target");
            if (!targetPath.EndsWith(".ucp-ui.json", StringComparison.OrdinalIgnoreCase))
                throw Error("ui.target-type", "Fixture paths must end in '.ucp-ui.json'.", "$request.target");

            var fixture = ParseFixture(targetPath);
            var dependencies = new List<object>
            {
                new Dictionary<string, object>
                {
                    ["kind"] = "document",
                    ["path"] = fixture.DocumentPath
                }
            };

            var seen = new HashSet<string>(StringComparer.Ordinal) { "document\0" + fixture.DocumentPath };
            AddCollectionDependencies(fixture.Collections, null, dependencies, seen);
            foreach (var pair in fixture.States.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                AddCollectionDependencies(pair.Value.Collections, pair.Key, dependencies, seen);

            var result = FixtureSummary(fixture);
            result["dependencies"] = dependencies;
            return result;
        }

        private static Dictionary<string, object> FixtureSummary(FixtureDefinition fixture)
        {
            var states = fixture.States.Keys.OrderBy(name => name, StringComparer.Ordinal)
                .Select(name => (object)name).ToList();
            return new Dictionary<string, object>
            {
                ["target"] = fixture.Path,
                ["type"] = "fixture",
                ["valid"] = true,
                ["schemaVersion"] = 0,
                ["document"] = fixture.DocumentPath,
                ["states"] = states,
                ["defaultState"] = fixture.DefaultState
            };
        }

        private static void AddCollectionDependencies(
            IReadOnlyList<UiCollectionDefinition> collections,
            string state,
            List<object> output,
            HashSet<string> seen)
        {
            if (collections == null)
                return;

            foreach (var collection in collections)
            {
                var key = (state ?? string.Empty) + "\0" + collection.TemplatePath;
                if (!seen.Add(key))
                    continue;

                output.Add(new Dictionary<string, object>
                {
                    ["kind"] = "collection-template",
                    ["path"] = collection.TemplatePath,
                    ["state"] = state,
                    ["target"] = collection.Selector
                });
            }
        }

        private static FixtureDefinition ParseFixture(string path)
        {
            var textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (textAsset == null)
                throw Error("ui.fixture-not-found", $"Could not load fixture as a TextAsset: {path}", path);

            Dictionary<string, object> root;
            try
            {
                root = MiniJson.Deserialize(textAsset.text) as Dictionary<string, object>;
            }
            catch (Exception ex)
            {
                throw Error("ui.fixture-json", $"Fixture JSON is invalid: {ex.Message}", path);
            }

            if (root == null)
                throw Error("ui.fixture-root", "Fixture root must be a JSON object.", path);

            RejectUnknownFields(root, FixtureFields, path);
            var schemaVersion = RequireInt(root, "schemaVersion", path, 0, 0);
            if (schemaVersion != 0)
                throw Error("ui.schema-version", $"Unsupported fixture schemaVersion {schemaVersion}; expected 0.", path + "#schemaVersion");

            var documentReference = RequireString(root, "document", path);
            var documentPath = NormalizeAssetPath(documentReference, path, path + "#document");
            if (!documentPath.EndsWith(".uxml", StringComparison.OrdinalIgnoreCase))
                throw Error("ui.document-type", "Fixture 'document' must reference a .uxml asset.", path + "#document");

            var document = LoadVisualTree(documentPath, path + "#document");
            var statesObject = RequireObject(root, "states", path + "#states");
            if (statesObject.Count == 0)
                throw Error("ui.states-empty", "Fixture 'states' must contain at least one state.", path + "#states");
            if (statesObject.Count > MaxFixtureStates)
            {
                throw Error(
                    "ui.states-limit",
                    $"Fixture 'states' cannot contain more than {MaxFixtureStates} states.",
                    path + "#states");
            }

            var fixture = new FixtureDefinition
            {
                Path = path,
                DocumentPath = documentPath,
                Document = document,
                Viewport = root.TryGetValue("viewport", out var viewportValue)
                    ? ParseViewport(viewportValue, new UiViewport(), path + "#viewport")
                    : new UiViewport(),
                Settle = root.TryGetValue("settle", out var settleValue)
                    ? ParseSettle(settleValue, new UiSettleOptions(), path + "#settle")
                    : new UiSettleOptions(),
                Data = OptionalObject(root, "data", path + "#data"),
                SetOperations = root.TryGetValue("set", out var setValue)
                    ? ParseSetOperations(setValue, path + "#set")
                    : Array.Empty<UiSetOperation>(),
                Collections = root.TryGetValue("collections", out var collectionsValue)
                    ? ParseCollections(collectionsValue, path, path + "#collections")
                    : Array.Empty<UiCollectionDefinition>()
            };

            foreach (var pair in statesObject)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                    throw Error("ui.state-name", "Fixture state names cannot be empty.", path + "#states");
                if (!(pair.Value is Dictionary<string, object> stateObject))
                    throw Error("ui.state-type", $"State '{pair.Key}' must be a JSON object.", path + "#states." + pair.Key);

                var location = path + "#states." + pair.Key;
                RejectUnknownFields(stateObject, StateFields, location);
                fixture.States.Add(pair.Key, new StateDefinition
                {
                    Viewport = stateObject.TryGetValue("viewport", out var stateViewport)
                        ? ParseViewport(stateViewport, fixture.Viewport, location + ".viewport")
                        : null,
                    Settle = stateObject.TryGetValue("settle", out var stateSettle)
                        ? ParseSettle(stateSettle, fixture.Settle, location + ".settle")
                        : null,
                    Data = OptionalObject(stateObject, "data", location + ".data"),
                    SetOperations = stateObject.TryGetValue("set", out var stateSet)
                        ? ParseSetOperations(stateSet, location + ".set")
                        : null,
                    Collections = stateObject.TryGetValue("collections", out var stateCollections)
                        ? ParseCollections(stateCollections, path, location + ".collections")
                        : null
                });
            }

            fixture.DefaultState = OptionalString(root, "defaultState", path);
            if (fixture.DefaultState == null)
            {
                if (fixture.States.ContainsKey("default"))
                    fixture.DefaultState = "default";
                else if (fixture.States.Count == 1)
                    fixture.DefaultState = fixture.States.Keys.First();
                else
                    throw Error("ui.default-state", "Fixtures with multiple states must declare 'defaultState'.", path + "#defaultState");
            }

            if (!fixture.States.ContainsKey(fixture.DefaultState))
                throw Error("ui.default-state", $"defaultState '{fixture.DefaultState}' does not exist in 'states'.", path + "#defaultState");

            return fixture;
        }

        private static List<string> SelectStates(FixtureDefinition fixture, string requestedState, bool allStates)
        {
            if (allStates)
                return fixture.States.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList();

            var stateName = requestedState ?? fixture.DefaultState;
            if (!fixture.States.ContainsKey(stateName))
                throw Error("ui.state-not-found", $"Fixture state '{stateName}' does not exist.", fixture.Path + "#states");
            return new List<string> { stateName };
        }

        private static IReadOnlyList<UiSetOperation> ParseSetOperations(object value, string location)
        {
            if (!(value is IList values))
                throw Error("ui.set-type", "'set' must be a JSON array.", location);

            var result = new List<UiSetOperation>(values.Count);
            for (var i = 0; i < values.Count; i++)
            {
                var itemLocation = $"{location}[{i}]";
                if (!(values[i] is Dictionary<string, object> item))
                    throw Error("ui.set-item-type", "Each 'set' entry must be an object.", itemLocation);

                RejectUnknownFields(item, SetFields, itemLocation);
                var selector = RequireString(item, "target", itemLocation);
                ValidateSelector(selector, itemLocation + ".target");
                var property = RequireString(item, "property", itemLocation);
                ValidateSetProperty(property, itemLocation + ".property");
                if (!item.TryGetValue("value", out var setValue))
                    throw Error("ui.missing-field", "Missing required field 'value'.", itemLocation);

                result.Add(new UiSetOperation(selector, property, NormalizeJsonValue(setValue), itemLocation));
            }

            return result;
        }

        private static IReadOnlyList<UiCollectionDefinition> ParseCollections(
            object value,
            string fixturePath,
            string location)
        {
            if (!(value is IList values))
                throw Error("ui.collections-type", "'collections' must be a JSON array.", location);

            var result = new List<UiCollectionDefinition>(values.Count);
            var targets = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < values.Count; i++)
            {
                var itemLocation = $"{location}[{i}]";
                if (!(values[i] is Dictionary<string, object> item))
                    throw Error("ui.collection-item-type", "Each collection entry must be an object.", itemLocation);

                RejectUnknownFields(item, CollectionFields, itemLocation);
                var selector = RequireString(item, "target", itemLocation);
                ValidateSelector(selector, itemLocation + ".target");
                if (!targets.Add(selector))
                    throw Error("ui.collection-duplicate", $"Collection target '{selector}' is configured more than once in one scope.", itemLocation + ".target");

                var modeText = RequireString(item, "mode", itemLocation);
                UiCollectionMode mode;
                if (string.Equals(modeText, "repeat", StringComparison.Ordinal))
                    mode = UiCollectionMode.Repeat;
                else if (string.Equals(modeText, "list-view", StringComparison.Ordinal))
                    mode = UiCollectionMode.ListView;
                else
                    throw Error("ui.collection-mode", "Collection mode must be 'repeat' or 'list-view'.", itemLocation + ".mode");

                var source = RequireString(item, "source", itemLocation);
                ValidateJsonPointer(source, itemLocation + ".source");
                var templateReference = RequireString(item, "template", itemLocation);
                var templatePath = NormalizeAssetPath(templateReference, fixturePath, itemLocation + ".template");
                if (!templatePath.EndsWith(".uxml", StringComparison.OrdinalIgnoreCase))
                    throw Error("ui.template-type", "Collection templates must reference a .uxml asset.", itemLocation + ".template");

                float? itemHeight = null;
                if (item.TryGetValue("itemHeight", out var heightValue))
                {
                    var height = NumberAsDouble(heightValue, itemLocation + ".itemHeight");
                    if (!IsFinite(height) || height <= 0 || height > 10000)
                        throw Error("ui.item-height", "itemHeight must be finite and between 0 and 10000.", itemLocation + ".itemHeight");
                    itemHeight = (float)height;
                }

                result.Add(new UiCollectionDefinition(
                    selector,
                    mode,
                    source,
                    templatePath,
                    LoadVisualTree(templatePath, itemLocation + ".template"),
                    itemHeight,
                    itemLocation));
            }

            return result;
        }

        private static UiViewport ParseViewport(object value, UiViewport fallback, string location)
        {
            if (!(value is Dictionary<string, object> viewport))
                throw Error("ui.viewport-type", "viewport must be an object.", location);
            RejectUnknownFields(viewport, ViewportFields, location);

            var width = OptionalInt(viewport, "width", fallback.Width, location + ".width", 1, 8192);
            var height = OptionalInt(viewport, "height", fallback.Height, location + ".height", 1, 8192);
            if ((long)width * height > MaxViewportPixelArea)
            {
                throw Error(
                    "ui.viewport-area",
                    $"Viewport area cannot exceed {MaxViewportPixelArea} pixels.",
                    location);
            }
            return new UiViewport(width, height);
        }

        private static UiSettleOptions ParseSettle(object value, UiSettleOptions fallback, string location)
        {
            if (!(value is Dictionary<string, object> settle))
                throw Error("ui.settle-type", "settle must be an object.", location);
            RejectUnknownFields(settle, SettleFields, location);

            var stableFrames = OptionalInt(settle, "stableFrames", fallback.StableFrames, location + ".stableFrames", 1, 60);
            var pixelFrames = OptionalInt(settle, "pixelStableFrames", fallback.PixelStableFrames, location + ".pixelStableFrames", 1, 60);
            var timeout = settle.TryGetValue("timeoutSeconds", out var timeoutValue)
                ? NumberAsDouble(timeoutValue, location + ".timeoutSeconds")
                : fallback.TimeoutSeconds;
            if (!IsFinite(timeout) || timeout < 0.1 || timeout > 300)
                throw Error("ui.settle-timeout", "timeoutSeconds must be finite and between 0.1 and 300.", location + ".timeoutSeconds");

            return new UiSettleOptions(stableFrames, pixelFrames, timeout);
        }

        private static Dictionary<string, object> MergeData(
            Dictionary<string, object> fixtureData,
            Dictionary<string, object> stateData,
            Dictionary<string, object> requestData)
        {
            var merged = CloneObject(fixtureData) ?? new Dictionary<string, object>();
            MergeInto(merged, stateData);
            MergeInto(merged, requestData);
            return merged;
        }

        private static void MergeInto(Dictionary<string, object> target, Dictionary<string, object> overlay)
        {
            if (overlay == null)
                return;

            foreach (var pair in overlay)
            {
                if (pair.Value is Dictionary<string, object> overlayObject &&
                    target.TryGetValue(pair.Key, out var current) && current is Dictionary<string, object> currentObject)
                {
                    MergeInto(currentObject, overlayObject);
                }
                else
                {
                    target[pair.Key] = NormalizeJsonValue(pair.Value);
                }
            }
        }

        private static Dictionary<string, object> CloneObject(Dictionary<string, object> value)
        {
            if (value == null)
                return null;

            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var pair in value)
                result[pair.Key] = NormalizeJsonValue(pair.Value);
            return result;
        }

        internal static object NormalizeJsonValue(object value)
        {
            if (value is long integer && integer >= int.MinValue && integer <= int.MaxValue)
                return (int)integer;
            if (value is Dictionary<string, object> dictionary)
                return CloneObject(dictionary);
            if (value is IList list)
            {
                var normalized = new List<object>(list.Count);
                foreach (var item in list)
                    normalized.Add(NormalizeJsonValue(item));
                return normalized;
            }
            return value;
        }

        private static VisualTreeAsset LoadVisualTree(string path, string location)
        {
            var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            if (asset == null)
                throw Error("ui.uxml-not-found", $"Could not load UXML asset: {path}", location);
            return asset;
        }

        internal static string NormalizeAssetPath(string input, string relativeToFixture, string location)
        {
            if (string.IsNullOrWhiteSpace(input))
                throw Error("ui.path-empty", "Asset path cannot be empty.", location);

            var normalizedInput = input.Trim().Replace('\\', '/');
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string absolute;

            if (Path.IsPathRooted(normalizedInput))
            {
                absolute = NormalizePathSegments(normalizedInput);
            }
            else
            {
                string projectRelative;
                if (normalizedInput.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                    normalizedInput.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(normalizedInput, "Assets", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(normalizedInput, "Packages", StringComparison.OrdinalIgnoreCase))
                {
                    projectRelative = normalizedInput;
                }
                else if (relativeToFixture != null)
                {
                    var baseDirectory = Path.GetDirectoryName(relativeToFixture.Replace('/', Path.DirectorySeparatorChar))
                                        ?? string.Empty;
                    projectRelative = Path.Combine(baseDirectory, normalizedInput.Replace('/', Path.DirectorySeparatorChar));
                }
                else
                {
                    projectRelative = normalizedInput;
                }

                absolute = NormalizePathSegments(Path.Combine(projectRoot, projectRelative));
            }

            var rootWithSeparator = projectRoot.Replace('\\', '/').TrimEnd('/') + "/";
            if (!absolute.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                throw Error("ui.path-outside-project", "UI asset paths must stay inside the Unity project.", location);

            var relative = absolute.Substring(rootWithSeparator.Length).Replace('\\', '/');
            if (!(relative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                  relative.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(relative, "Assets", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(relative, "Packages", StringComparison.OrdinalIgnoreCase)))
            {
                throw Error("ui.path-root", "UI asset paths must be under Assets or Packages.", location);
            }

            return relative;
        }

        private static string NormalizePathSegments(string absolutePath)
        {
            // Unity's Path.GetFullPath remaps virtual Packages paths into
            // Library/PackageCache. Collapse dot segments without resolving that
            // alias so validation and AssetDatabase receive the virtual path.
            var path = absolutePath.Replace('\\', '/');
            var root = Path.GetPathRoot(path).Replace('\\', '/');
            var segments = new List<string>();
            foreach (var segment in path.Substring(root.Length).Split('/'))
            {
                if (segment.Length == 0 || segment == ".")
                    continue;
                if (segment == "..")
                {
                    if (segments.Count > 0)
                        segments.RemoveAt(segments.Count - 1);
                    continue;
                }
                segments.Add(segment);
            }
            return root + string.Join("/", segments);
        }

        internal static void ValidateSelector(string selector, string location)
        {
            if (string.Equals(selector, ":root", StringComparison.Ordinal))
                return;
            if (selector.Length < 2 || (selector[0] != '#' && selector[0] != '.'))
                throw Error("ui.selector-syntax", "Selectors must be ':root', '#name', or '.class'.", location);

            for (var i = 1; i < selector.Length; i++)
            {
                var c = selector[i];
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-'))
                    throw Error("ui.selector-syntax", "Selector names may contain only letters, digits, '_' and '-'.", location);
            }
        }

        private static void ValidateSetProperty(string property, string location)
        {
            if (property == "text" || property == "value" || property == "enabled" ||
                property == "display" || property == "visibility" || property == "tooltip")
                return;

            if (property.StartsWith("class:", StringComparison.Ordinal) && property.Length > "class:".Length)
            {
                ValidateSelector("." + property.Substring("class:".Length), location);
                return;
            }

            throw Error("ui.set-property", $"Property '{property}' is not allowed in UI fixtures.", location);
        }

        private static void ValidateJsonPointer(string pointer, string location)
        {
            if (pointer.Length == 0)
                return;
            if (pointer[0] != '/')
                throw Error("ui.source-path", "Collection source must be an RFC 6901-style JSON pointer such as '/items'.", location);

            for (var i = 0; i < pointer.Length; i++)
            {
                if (pointer[i] != '~')
                    continue;
                if (i + 1 >= pointer.Length || (pointer[i + 1] != '0' && pointer[i + 1] != '1'))
                    throw Error("ui.source-path", "JSON pointer '~' escapes must be '~0' or '~1'.", location);
                i++;
            }
        }

        private static void RejectUnknownFields(
            Dictionary<string, object> value,
            HashSet<string> allowed,
            string location)
        {
            foreach (var key in value.Keys)
            {
                if (!allowed.Contains(key))
                    throw Error("ui.unknown-field", $"Unknown field '{key}'.", location + "." + key);
            }
        }

        private static Dictionary<string, object> RequireObject(
            Dictionary<string, object> value,
            string key,
            string location)
        {
            if (!value.TryGetValue(key, out var field))
                throw Error("ui.missing-field", $"Missing required field '{key}'.", location);
            if (!(field is Dictionary<string, object> result))
                throw Error("ui.field-type", $"Field '{key}' must be an object.", location);
            return result;
        }

        private static Dictionary<string, object> OptionalObject(
            Dictionary<string, object> value,
            string key,
            string location)
        {
            if (!value.TryGetValue(key, out var field))
                return null;
            if (!(field is Dictionary<string, object> result))
                throw Error("ui.field-type", $"Field '{key}' must be an object.", location);
            return CloneObject(result);
        }

        private static string RequireString(Dictionary<string, object> value, string key, string location)
        {
            var result = OptionalString(value, key, location);
            if (result == null)
                throw Error("ui.missing-field", $"Missing required string field '{key}'.", location);
            return result;
        }

        private static string OptionalString(Dictionary<string, object> value, string key, string location)
        {
            if (!value.TryGetValue(key, out var field))
                return null;
            if (!(field is string result) || string.IsNullOrWhiteSpace(result))
                throw Error("ui.field-type", $"Field '{key}' must be a non-empty string.", location + "." + key);
            return result;
        }

        private static int RequireInt(
            Dictionary<string, object> value,
            string key,
            string location,
            int minimum,
            int maximum)
        {
            if (!value.TryGetValue(key, out var field))
                throw Error("ui.missing-field", $"Missing required integer field '{key}'.", location);
            return CheckedInt(field, location + "." + key, minimum, maximum);
        }

        private static int OptionalInt(
            Dictionary<string, object> value,
            string key,
            int fallback,
            string location,
            int minimum,
            int maximum)
        {
            return value.TryGetValue(key, out var field)
                ? CheckedInt(field, location, minimum, maximum)
                : fallback;
        }

        private static int CheckedInt(object value, string location, int minimum, int maximum)
        {
            long integer;
            if (value is int intValue)
                integer = intValue;
            else if (value is long longValue)
                integer = longValue;
            else
                throw Error("ui.integer-type", "Expected an integer.", location);

            if (integer < minimum || integer > maximum)
                throw Error("ui.integer-range", $"Integer must be between {minimum} and {maximum}.", location);
            return (int)integer;
        }

        private static bool OptionalBool(
            Dictionary<string, object> value,
            string key,
            bool fallback,
            string location)
        {
            if (!value.TryGetValue(key, out var field))
                return fallback;
            if (!(field is bool result))
                throw Error("ui.boolean-type", "Expected a boolean.", location);
            return result;
        }

        private static double NumberAsDouble(object value, string location)
        {
            if (value is int integer)
                return integer;
            if (value is long longInteger)
                return longInteger;
            if (value is float single)
                return single;
            if (value is double number)
                return number;
            throw Error("ui.number-type", "Expected a number.", location);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static UiScenarioException Error(string code, string message, string location)
        {
            return new UiScenarioException(code, message, location);
        }

        private sealed class FixtureDefinition
        {
            public string Path;
            public string DocumentPath;
            public VisualTreeAsset Document;
            public string DefaultState;
            public UiViewport Viewport;
            public UiSettleOptions Settle;
            public Dictionary<string, object> Data;
            public IReadOnlyList<UiSetOperation> SetOperations;
            public IReadOnlyList<UiCollectionDefinition> Collections;
            public readonly Dictionary<string, StateDefinition> States =
                new Dictionary<string, StateDefinition>(StringComparer.Ordinal);
        }

        private sealed class StateDefinition
        {
            public UiViewport Viewport;
            public UiSettleOptions Settle;
            public Dictionary<string, object> Data;
            public IReadOnlyList<UiSetOperation> SetOperations;
            public IReadOnlyList<UiCollectionDefinition> Collections;
        }
    }
}
#endif
