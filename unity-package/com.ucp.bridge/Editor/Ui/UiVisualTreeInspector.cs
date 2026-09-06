#if UNITY_6000_0_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace UCP.Bridge
{
    internal sealed class UiInspectOptions
    {
        private const int DefaultDepth = 12;
        private const int DefaultMaxElements = 500;

        internal string Query { get; private set; }
        internal int MaxDepth { get; private set; }
        internal int MaxElements { get; private set; }
        internal string Detail { get; private set; }

        internal static UiInspectOptions Parse(Dictionary<string, object> parameters)
        {
            parameters ??= new Dictionary<string, object>();
            var options = new UiInspectOptions
            {
                Query = ReadString(parameters, "query"),
                MaxDepth = ReadInt(parameters, "depth", DefaultDepth, 0, 64),
                MaxElements = ReadInt(parameters, "max", DefaultMaxElements, 1, 5000),
                Detail = ReadString(parameters, "detail") ?? "normal"
            };

            options.Detail = options.Detail.ToLowerInvariant();
            if (options.Detail != "summary" && options.Detail != "normal" && options.Detail != "verbose")
                throw new ArgumentException("'detail' must be one of: summary, normal, verbose");

            UiSimpleQuery.Validate(options.Query);
            return options;
        }

        private static string ReadString(Dictionary<string, object> parameters, string key)
        {
            return parameters.TryGetValue(key, out var value) && value != null
                ? value.ToString()
                : null;
        }

        private static int ReadInt(
            Dictionary<string, object> parameters,
            string key,
            int defaultValue,
            int min,
            int max)
        {
            if (!parameters.TryGetValue(key, out var value) || value == null)
                return defaultValue;

            try
            {
                var result = Convert.ToInt32(value);
                if (result < min || result > max)
                    throw new ArgumentException($"'{key}' must be between {min} and {max}");
                return result;
            }
            catch (ArgumentException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new ArgumentException($"'{key}' must be an integer between {min} and {max}");
            }
        }
    }

    internal static class UiVisualTreeInspector
    {
        internal static Dictionary<string, object> Inspect(
            VisualElement root,
            UiInspectOptions options,
            IReadOnlyList<UiCollectionMetadata> collectionMetadata)
        {
            if (root == null)
                throw new ArgumentException("The resolved UXML did not create a visual tree");

            var selectedRoots = Select(root, options.Query);
            var context = new SnapshotContext(options);
            var roots = new List<object>();
            foreach (var selectedRoot in selectedRoots)
            {
                if (context.Truncated)
                    break;
                var node = BuildNode(selectedRoot, 0, context);
                if (node != null)
                    roots.Add(node);
            }

            var collections = new List<object>();
            if (collectionMetadata != null)
            {
                foreach (var metadata in collectionMetadata)
                    collections.Add(metadata.ToDictionary());
            }

            return new Dictionary<string, object>
            {
                ["query"] = options.Query,
                ["matchCount"] = selectedRoots.Count,
                ["returnedElementCount"] = context.ReturnedCount,
                ["truncated"] = context.Truncated,
                ["maxElements"] = options.MaxElements,
                ["maxDepth"] = options.MaxDepth,
                ["detail"] = options.Detail,
                ["roots"] = roots,
                ["collections"] = collections
            };
        }

        private static List<VisualElement> Select(VisualElement root, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<VisualElement> { root };

            var result = new List<VisualElement>();
            Visit(root, element =>
            {
                if (UiSimpleQuery.Matches(element, query))
                    result.Add(element);
            });
            return result;
        }

        private static Dictionary<string, object> BuildNode(
            VisualElement element,
            int depth,
            SnapshotContext context)
        {
            if (context.ReturnedCount >= context.Options.MaxElements)
            {
                context.Truncated = true;
                return null;
            }

            context.ReturnedCount++;
            var node = new Dictionary<string, object>
            {
                ["ref"] = $"e{context.ReturnedCount}",
                ["type"] = element.GetType().Name,
                ["name"] = string.IsNullOrEmpty(element.name) ? null : element.name,
                ["classes"] = element.GetClasses().Cast<object>().ToList()
            };

            if (element is TextElement textElement && !string.IsNullOrEmpty(textElement.text))
                node["text"] = textElement.text;

            var value = ReadValue(element);
            if (value.Found)
                node["value"] = value.Value;

            if (context.Options.Detail != "summary")
            {
                var displayed = IsDisplayed(element);
                node["enabled"] = element.enabledInHierarchy;
                node["focusable"] = element.focusable;
                node["focused"] = element.panel?.focusController?.focusedElement == element;
                node["visible"] = displayed && element.resolvedStyle.visibility == Visibility.Visible;
                node["layout"] = RectDictionary(element.layout);
                node["worldBound"] = RectDictionary(element.worldBound);
                node["resolvedStyle"] = StyleDictionary(element.resolvedStyle);
                node["bindings"] = BindingDictionaries(element);
            }

            if (context.Options.Detail == "verbose")
            {
                node["fullType"] = element.GetType().FullName;
                node["pickingMode"] = element.pickingMode.ToString();
                node["tooltip"] = string.IsNullOrEmpty(element.tooltip) ? null : element.tooltip;
            }

            var children = new List<object>();
            if (depth < context.Options.MaxDepth)
            {
                foreach (var child in element.hierarchy.Children())
                {
                    if (context.ReturnedCount >= context.Options.MaxElements)
                    {
                        context.Truncated = true;
                        break;
                    }

                    var childNode = BuildNode(child, depth + 1, context);
                    if (childNode != null)
                        children.Add(childNode);
                }
            }
            else if (element.hierarchy.childCount > 0)
            {
                context.Truncated = true;
            }

            if (children.Count > 0)
                node["children"] = children;
            return node;
        }

        internal static List<object> BindingDictionaries(VisualElement element)
        {
            var bindings = new List<object>();
            foreach (var info in element.GetBindingInfos())
            {
                var bindingId = info.bindingId;
                var item = new Dictionary<string, object>
                {
                    ["property"] = bindingId.ToString(),
                    ["bindingType"] = info.binding?.GetType().Name
                };

                if (element.TryGetLastBindingToUIResult(bindingId, out var toUi))
                {
                    item["toUi"] = new Dictionary<string, object>
                    {
                        ["status"] = toUi.status.ToString(),
                        ["message"] = string.IsNullOrEmpty(toUi.message) ? null : toUi.message
                    };
                }

                if (element.TryGetLastBindingToSourceResult(bindingId, out var toSource))
                {
                    item["toSource"] = new Dictionary<string, object>
                    {
                        ["status"] = toSource.status.ToString(),
                        ["message"] = string.IsNullOrEmpty(toSource.message) ? null : toSource.message
                    };
                }

                bindings.Add(item);
            }
            return bindings;
        }

        private static Dictionary<string, object> RectDictionary(Rect value)
        {
            var finite = UiValue.IsFinite(value.x) && UiValue.IsFinite(value.y) &&
                         UiValue.IsFinite(value.width) && UiValue.IsFinite(value.height);
            return new Dictionary<string, object>
            {
                ["x"] = UiValue.FiniteOrNull(value.x),
                ["y"] = UiValue.FiniteOrNull(value.y),
                ["width"] = UiValue.FiniteOrNull(value.width),
                ["height"] = UiValue.FiniteOrNull(value.height),
                ["finite"] = finite
            };
        }

        private static Dictionary<string, object> StyleDictionary(IResolvedStyle style)
        {
            return new Dictionary<string, object>
            {
                ["display"] = style.display.ToString(),
                ["visibility"] = style.visibility.ToString(),
                ["position"] = style.position.ToString(),
                ["flexDirection"] = style.flexDirection.ToString(),
                ["justifyContent"] = style.justifyContent.ToString(),
                ["alignItems"] = style.alignItems.ToString(),
                ["alignSelf"] = style.alignSelf.ToString(),
                ["flexGrow"] = UiValue.FiniteOrNull(style.flexGrow),
                ["flexShrink"] = UiValue.FiniteOrNull(style.flexShrink),
                ["width"] = UiValue.FiniteOrNull(style.width),
                ["height"] = UiValue.FiniteOrNull(style.height),
                ["opacity"] = UiValue.FiniteOrNull(style.opacity),
                ["color"] = ColorDictionary(style.color),
                ["backgroundColor"] = ColorDictionary(style.backgroundColor),
                ["fontSize"] = UiValue.FiniteOrNull(style.fontSize),
                ["whiteSpace"] = style.whiteSpace.ToString()
            };
        }

        private static Dictionary<string, object> ColorDictionary(Color value)
        {
            return new Dictionary<string, object>
            {
                ["r"] = UiValue.FiniteOrNull(value.r),
                ["g"] = UiValue.FiniteOrNull(value.g),
                ["b"] = UiValue.FiniteOrNull(value.b),
                ["a"] = UiValue.FiniteOrNull(value.a)
            };
        }

        private static ValueRead ReadValue(VisualElement element)
        {
            try
            {
                var property = element.GetType().GetProperty(
                    "value",
                    BindingFlags.Instance | BindingFlags.Public);
                if (property == null || !property.CanRead || property.GetIndexParameters().Length != 0)
                    return default;

                return new ValueRead(true, NormalizeValue(property.GetValue(element)));
            }
            catch
            {
                return default;
            }
        }

        private static object NormalizeValue(object value)
        {
            if (value == null || value is string || value is bool || value is char ||
                value is byte || value is sbyte || value is short || value is ushort ||
                value is int || value is uint || value is long || value is ulong || value is decimal)
                return value;
            if (value is float floatValue)
                return UiValue.FiniteOrNull(floatValue);
            if (value is double doubleValue)
                return UiValue.FiniteOrNull(doubleValue);
            if (value is Enum enumValue)
                return enumValue.ToString();
            if (value is Color colorValue)
                return ColorDictionary(colorValue);
            if (value is UnityEngine.Object objectValue)
                return objectValue != null ? objectValue.name : null;
            return value.ToString();
        }

        private static bool IsDisplayed(VisualElement element)
        {
            for (var current = element; current != null; current = current.parent)
            {
                if (current.resolvedStyle.display == DisplayStyle.None)
                    return false;
            }
            return true;
        }

        internal static void Visit(VisualElement root, Action<VisualElement> action)
        {
            if (root == null)
                return;
            action(root);
            // Collection controls can have a null contentContainer while their
            // realized rows still exist in the physical visual hierarchy.
            foreach (var child in root.hierarchy.Children())
                Visit(child, action);
        }

        private sealed class SnapshotContext
        {
            internal SnapshotContext(UiInspectOptions options)
            {
                Options = options;
            }

            internal UiInspectOptions Options { get; }
            internal int ReturnedCount { get; set; }
            internal bool Truncated { get; set; }
        }

        private readonly struct ValueRead
        {
            internal ValueRead(bool found, object value)
            {
                Found = found;
                Value = value;
            }

            internal bool Found { get; }
            internal object Value { get; }
        }
    }

    internal static class UiSimpleQuery
    {
        internal static void Validate(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return;
            query = query.Trim();
            if (query == "#" || query == ".")
                throw new ArgumentException("'query' must contain a name, class, or element type");
            if (query.IndexOfAny(new[] { ' ', '>', '+', '~', '[', ']', ':', ',' }) >= 0)
            {
                throw new ArgumentException(
                    "'query' supports one simple selector only: #name, .class, or element type");
            }
        }

        internal static bool Matches(VisualElement element, string query)
        {
            if (element == null || string.IsNullOrWhiteSpace(query))
                return false;
            query = query.Trim();
            if (query[0] == '#')
                return string.Equals(element.name, query.Substring(1), StringComparison.Ordinal);
            if (query[0] == '.')
                return element.ClassListContains(query.Substring(1));
            return string.Equals(element.GetType().Name, query, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(element.GetType().FullName, query, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class UiAudit
    {
        private const int MaxDiagnostics = 200;

        internal static Dictionary<string, object> Run(
            VisualElement root,
            UiApplyReport applyReport,
            IReadOnlyList<UiCollectionMetadata> collectionMetadata)
        {
            var diagnostics = new DiagnosticCollector();
            var collectionRoots = FindCollectionRoots(root, collectionMetadata);
            var namedElements = new Dictionary<string, List<VisualElement>>(StringComparer.Ordinal);

            if (applyReport != null)
            {
                foreach (var diagnostic in applyReport.Diagnostics)
                {
                    var severity = diagnostic.TryGetValue("severity", out var value)
                        ? value?.ToString()
                        : null;
                    diagnostics.Add(severity == "error", diagnostic);
                }
            }

            UiVisualTreeInspector.Visit(root, element =>
            {
                var insideDynamicCollection = HasAncestor(element, collectionRoots);
                if (!insideDynamicCollection && IsUserElementName(element.name))
                {
                    if (!namedElements.TryGetValue(element.name, out var matches))
                    {
                        matches = new List<VisualElement>();
                        namedElements.Add(element.name, matches);
                    }
                    matches.Add(element);
                }

                if (element.focusable && !IsUnityInternalName(element.name) && IsVisibleThroughAncestors(element))
                {
                    var rect = element.worldBound;
                    if (UiValue.IsFinite(rect.width) && UiValue.IsFinite(rect.height) &&
                        (rect.width <= 0f || rect.height <= 0f))
                    {
                        diagnostics.Add(false, Diagnostic(
                            "focusable_zero_size",
                            "Focusable element has zero rendered width or height",
                            element));
                    }
                    else if (!insideDynamicCollection && !HasScrollViewAncestor(element) &&
                             IsFinitePositive(rect) && IsFinitePositive(root.worldBound))
                    {
                        var viewport = root.worldBound;
                        var intersection = Intersect(rect, viewport);
                        if (intersection.width <= 0f || intersection.height <= 0f)
                        {
                            diagnostics.Add(false, Diagnostic(
                                "focusable_outside_viewport",
                                "Focusable element is fully outside the UI viewport",
                                element));
                        }
                        else if (intersection.width + 0.5f < rect.width ||
                                 intersection.height + 0.5f < rect.height)
                        {
                            diagnostics.Add(false, Diagnostic(
                                "focusable_partially_outside_viewport",
                                "Focusable element is partially outside the UI viewport",
                                element));
                        }
                    }
                }

                foreach (var bindingObject in UiVisualTreeInspector.BindingDictionaries(element))
                {
                    if (bindingObject is not Dictionary<string, object> binding)
                        continue;
                    CheckBindingResult(
                        binding,
                        "toUi",
                        element,
                        diagnostics);
                    CheckBindingResult(
                        binding,
                        "toSource",
                        element,
                        diagnostics);
                }
            });

            foreach (var pair in namedElements)
            {
                if (pair.Value.Count < 2)
                    continue;
                diagnostics.Add(false, new Dictionary<string, object>
                {
                    ["code"] = "duplicate_name",
                    ["message"] = $"Element name '{pair.Key}' is used {pair.Value.Count} times outside dynamic collections",
                    ["element"] = new Dictionary<string, object>
                    {
                        ["type"] = pair.Value[0].GetType().Name,
                        ["name"] = pair.Key
                    }
                });
            }

            return new Dictionary<string, object>
            {
                ["passed"] = diagnostics.ErrorCount == 0,
                ["errorCount"] = diagnostics.ErrorCount,
                ["warningCount"] = diagnostics.WarningCount,
                ["diagnosticsTruncated"] = diagnostics.Truncated,
                ["errors"] = diagnostics.Errors,
                ["warnings"] = diagnostics.Warnings
            };
        }

        private static void CheckBindingResult(
            Dictionary<string, object> binding,
            string direction,
            VisualElement element,
            DiagnosticCollector diagnostics)
        {
            if (!binding.TryGetValue(direction, out var resultObject) ||
                resultObject is not Dictionary<string, object> result ||
                !result.TryGetValue("status", out var statusObject))
                return;

            var status = statusObject?.ToString();
            if (string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase))
                return;

            var message = result.TryGetValue("message", out var messageObject)
                ? messageObject?.ToString()
                : null;
            var property = binding.TryGetValue("property", out var propertyObject)
                ? propertyObject?.ToString()
                : null;
            var diagnostic = Diagnostic(
                "binding_" + (status ?? "unknown").ToLowerInvariant(),
                $"Binding {direction} for '{property}' reported {status}: {message}",
                element);
            diagnostics.Add(string.Equals(status, "Failure", StringComparison.OrdinalIgnoreCase), diagnostic);
        }

        private static Dictionary<string, object> Diagnostic(
            string code,
            string message,
            VisualElement element)
        {
            return new Dictionary<string, object>
            {
                ["code"] = code,
                ["message"] = message,
                ["element"] = new Dictionary<string, object>
                {
                    ["type"] = element.GetType().Name,
                    ["name"] = string.IsNullOrEmpty(element.name) ? null : element.name
                }
            };
        }

        private static HashSet<VisualElement> FindCollectionRoots(
            VisualElement root,
            IReadOnlyList<UiCollectionMetadata> metadata)
        {
            var result = new HashSet<VisualElement>();
            if (metadata == null)
                return result;
            foreach (var collection in metadata)
            {
                if (collection.Selector == ":root")
                {
                    result.Add(root);
                    continue;
                }
                UiVisualTreeInspector.Visit(root, element =>
                {
                    if (UiSimpleQuery.Matches(element, collection.Selector))
                        result.Add(element);
                });
            }
            return result;
        }

        private static bool HasAncestor(VisualElement element, HashSet<VisualElement> candidates)
        {
            for (var current = element; current != null; current = current.parent)
            {
                if (candidates.Contains(current))
                    return true;
            }
            return false;
        }

        private static bool HasScrollViewAncestor(VisualElement element)
        {
            for (var current = element.parent; current != null; current = current.parent)
            {
                if (current is ScrollView)
                    return true;
            }
            return false;
        }

        private static bool IsUserElementName(string name)
        {
            return !string.IsNullOrEmpty(name) &&
                   !IsUnityInternalName(name);
        }

        private static bool IsUnityInternalName(string name)
        {
            return name != null && name.StartsWith("unity-", StringComparison.Ordinal);
        }

        private static bool IsVisibleThroughAncestors(VisualElement element)
        {
            for (var current = element; current != null; current = current.parent)
            {
                if (current.resolvedStyle.display == DisplayStyle.None ||
                    current.resolvedStyle.visibility == Visibility.Hidden)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsFinitePositive(Rect rect)
        {
            return UiValue.IsFinite(rect.x) && UiValue.IsFinite(rect.y) &&
                   UiValue.IsFinite(rect.width) && UiValue.IsFinite(rect.height) &&
                   rect.width > 0f && rect.height > 0f;
        }

        private static Rect Intersect(Rect left, Rect right)
        {
            var xMin = Mathf.Max(left.xMin, right.xMin);
            var yMin = Mathf.Max(left.yMin, right.yMin);
            var xMax = Mathf.Min(left.xMax, right.xMax);
            var yMax = Mathf.Min(left.yMax, right.yMax);
            return Rect.MinMaxRect(xMin, yMin, Mathf.Max(xMin, xMax), Mathf.Max(yMin, yMax));
        }

        private sealed class DiagnosticCollector
        {
            internal List<object> Errors { get; } = new List<object>();
            internal List<object> Warnings { get; } = new List<object>();
            internal int ErrorCount { get; private set; }
            internal int WarningCount { get; private set; }
            internal bool Truncated { get; private set; }

            internal void Add(bool isError, object diagnostic)
            {
                // Bound the response, but keep counting every diagnostic so
                // truncation cannot hide a failing audit or warning policy.
                if (isError)
                    ErrorCount++;
                else
                    WarningCount++;

                if (Errors.Count + Warnings.Count >= MaxDiagnostics)
                {
                    Truncated = true;
                    if (!isError || Warnings.Count == 0)
                        return;
                    // Preserve the cause of failure even when warnings arrived first.
                    Warnings.RemoveAt(Warnings.Count - 1);
                }
                (isError ? Errors : Warnings).Add(diagnostic);
            }
        }
    }
}
#endif
