#if UNITY_6000_0_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace UCP.Bridge
{
    internal static class UiScenarioApplier
    {
        private static readonly ConditionalWeakTable<VisualElement, AppliedState> AppliedStates =
            new ConditionalWeakTable<VisualElement, AppliedState>();

        private static bool _propertyBagsRegistered;

        public static UiApplyReport Apply(VisualElement root, UiResolvedScenario scenario)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));
            if (scenario == null)
                throw new ArgumentNullException(nameof(scenario));

            EnsurePropertyBags();
            AppliedStates.Remove(root);

            var report = new UiApplyReport();
            report.BindingRewriteCount = RewriteBindingsToDictionaryKeys(root);
            root.dataSource = scenario.Data;
            AddLargeIntegerDiagnostics(scenario.Data, report);

            foreach (var operation in scenario.SetOperations)
            {
                var target = ResolveSingle(root, operation.Selector, operation.Location);
                ApplySet(target, operation);
                report.SetCount++;
            }

            var state = new AppliedState();
            foreach (var collection in scenario.Collections)
            {
                var target = ResolveSingle(root, collection.Selector, collection.Location);
                var items = ResolveCollectionSource(scenario.Data, collection.Source, collection.Location);
                var metadata = new UiCollectionMetadata(
                    collection.Selector,
                    collection.Mode,
                    collection.Source,
                    items.Count,
                    target,
                    root);

                if (collection.Mode == UiCollectionMode.Repeat)
                    ApplyRepeat(target, items, collection, metadata, report);
                else
                    ApplyListView(target, items, collection, metadata, report);

                state.Collections.Add(metadata);
                report.AddCollection(metadata);
            }

            AppliedStates.Add(root, state);
            return report;
        }

        public static IReadOnlyList<UiCollectionMetadata> GetCollectionMetadata(VisualElement root)
        {
            if (root != null && AppliedStates.TryGetValue(root, out var state))
                return state.Collections;
            return Array.Empty<UiCollectionMetadata>();
        }

        private static void ApplyRepeat(
            VisualElement target,
            IList items,
            UiCollectionDefinition definition,
            UiCollectionMetadata metadata,
            UiApplyReport report)
        {
            if (target is ListView)
                throw Error("ui.collection-target-type", "repeat collections cannot target a ListView.", definition.Location + ".target");

            target.Clear();
            for (var i = 0; i < items.Count; i++)
            {
                var row = definition.Template.CloneTree();
                report.BindingRewriteCount += RewriteBindingsToDictionaryKeys(row);
                row.dataSource = items[i];
                target.Add(row);
                metadata.Bind(row, i);
                report.RepeatItemCount++;
            }
        }

        private static void ApplyListView(
            VisualElement target,
            IList items,
            UiCollectionDefinition definition,
            UiCollectionMetadata metadata,
            UiApplyReport report)
        {
            if (!(target is ListView listView))
                throw Error("ui.collection-target-type", "list-view collections must target a ListView element.", definition.Location + ".target");

            listView.makeItem = () =>
            {
                var row = definition.Template.CloneTree();
                report.BindingRewriteCount += RewriteBindingsToDictionaryKeys(row);
                metadata.Realize(row);
                return row;
            };
            listView.bindItem = (row, index) =>
            {
                if (index < 0 || index >= items.Count)
                    throw new ArgumentOutOfRangeException(nameof(index));
                row.dataSource = items[index];
                metadata.Bind(row, index);
            };
            listView.unbindItem = (row, _) =>
            {
                row.dataSource = null;
                metadata.Unbind(row);
            };
            listView.destroyItem = row =>
            {
                row.dataSource = null;
                metadata.Destroy(row);
            };

            if (definition.ItemHeight.HasValue)
            {
                listView.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
                listView.fixedItemHeight = definition.ItemHeight.Value;
            }
            else
            {
                listView.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            }

            listView.itemsSource = items;
            listView.Rebuild();
        }

        private static IList ResolveCollectionSource(
            Dictionary<string, object> data,
            string pointer,
            string location)
        {
            object current = data;
            if (pointer.Length > 0)
            {
                var segments = pointer.Substring(1).Split('/');
                foreach (var encodedSegment in segments)
                {
                    var segment = encodedSegment.Replace("~1", "/").Replace("~0", "~");
                    if (current is Dictionary<string, object> dictionary)
                    {
                        if (!dictionary.TryGetValue(segment, out current))
                            throw Error("ui.collection-source-missing", $"Collection source segment '{segment}' was not found.", location + ".source");
                    }
                    else if (current is IList list)
                    {
                        if (!int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                            index < 0 || index >= list.Count)
                        {
                            throw Error("ui.collection-source-index", $"Collection source index '{segment}' is invalid.", location + ".source");
                        }
                        current = list[index];
                    }
                    else
                    {
                        throw Error("ui.collection-source-traversal", $"Cannot traverse collection source through '{segment}'.", location + ".source");
                    }
                }
            }

            if (!(current is IList result))
                throw Error("ui.collection-source-type", "Collection source must resolve to a JSON array.", location + ".source");
            return result;
        }

        private static VisualElement ResolveSingle(VisualElement root, string selector, string location)
        {
            if (selector == ":root")
                return root;

            var matches = new List<VisualElement>();
            CollectMatches(root, selector, matches);
            if (matches.Count == 0)
                throw Error("ui.selector-not-found", $"Selector '{selector}' did not match any element.", location + ".target");
            if (matches.Count > 1)
                throw Error("ui.selector-ambiguous", $"Selector '{selector}' matched {matches.Count} elements; targets must be unique.", location + ".target");
            return matches[0];
        }

        private static void CollectMatches(VisualElement element, string selector, List<VisualElement> output)
        {
            var matches = (selector[0] == '#' && string.Equals(element.name, selector.Substring(1), StringComparison.Ordinal)) ||
                          (selector[0] == '.' && element.ClassListContains(selector.Substring(1)));
            if (matches)
                output.Add(element);

            foreach (var child in element.hierarchy.Children())
                CollectMatches(child, selector, output);
        }

        private static void ApplySet(VisualElement target, UiSetOperation operation)
        {
            switch (operation.Property)
            {
                case "text":
                    if (!(target is TextElement textElement))
                        throw SetTypeError(operation, target, "TextElement");
                    textElement.text = RequireStringOrNull(operation.Value, operation);
                    return;
                case "value":
                    ApplyValue(target, operation);
                    return;
                case "enabled":
                    target.SetEnabled(RequireBool(operation.Value, operation));
                    return;
                case "display":
                    target.style.display = ParseDisplay(operation);
                    return;
                case "visibility":
                    target.style.visibility = ParseVisibility(operation);
                    return;
                case "tooltip":
                    target.tooltip = RequireStringOrNull(operation.Value, operation);
                    return;
            }

            if (operation.Property.StartsWith("class:", StringComparison.Ordinal))
            {
                target.EnableInClassList(
                    operation.Property.Substring("class:".Length),
                    RequireBool(operation.Value, operation));
                return;
            }

            throw Error("ui.set-property", $"Property '{operation.Property}' is not allowed.", operation.Location + ".property");
        }

        private static void ApplyValue(VisualElement target, UiSetOperation operation)
        {
            switch (target)
            {
                case TextField field:
                    field.SetValueWithoutNotify(RequireStringOrNull(operation.Value, operation));
                    return;
                case IntegerField field:
                    field.SetValueWithoutNotify(RequireInt(operation.Value, operation));
                    return;
                case LongField field:
                    field.SetValueWithoutNotify(RequireLong(operation.Value, operation));
                    return;
                case FloatField field:
                    field.SetValueWithoutNotify(RequireFloat(operation.Value, operation));
                    return;
                case DoubleField field:
                    field.SetValueWithoutNotify(RequireDouble(operation.Value, operation));
                    return;
                case Toggle field:
                    field.SetValueWithoutNotify(RequireBool(operation.Value, operation));
                    return;
                case RadioButton field:
                    field.SetValueWithoutNotify(RequireBool(operation.Value, operation));
                    return;
                case Foldout field:
                    field.SetValueWithoutNotify(RequireBool(operation.Value, operation));
                    return;
                case Slider field:
                    field.SetValueWithoutNotify(RequireFloat(operation.Value, operation));
                    return;
                case SliderInt field:
                    field.SetValueWithoutNotify(RequireInt(operation.Value, operation));
                    return;
                case DropdownField field:
                    field.SetValueWithoutNotify(RequireStringOrNull(operation.Value, operation));
                    return;
                case ProgressBar field:
                    field.SetValueWithoutNotify(RequireFloat(operation.Value, operation));
                    return;
                default:
                    throw SetTypeError(operation, target, "a supported UI Toolkit value control");
            }
        }

        private static DisplayStyle ParseDisplay(UiSetOperation operation)
        {
            var value = RequireStringOrNull(operation.Value, operation);
            if (value == "flex")
                return DisplayStyle.Flex;
            if (value == "none")
                return DisplayStyle.None;
            throw Error("ui.set-value", "display must be 'flex' or 'none'.", operation.Location + ".value");
        }

        private static Visibility ParseVisibility(UiSetOperation operation)
        {
            var value = RequireStringOrNull(operation.Value, operation);
            if (value == "visible")
                return Visibility.Visible;
            if (value == "hidden")
                return Visibility.Hidden;
            throw Error("ui.set-value", "visibility must be 'visible' or 'hidden'.", operation.Location + ".value");
        }

        private static string RequireStringOrNull(object value, UiSetOperation operation)
        {
            if (value == null || value is string)
                return (string)value;
            throw Error("ui.set-value-type", $"'{operation.Property}' requires a string or null.", operation.Location + ".value");
        }

        private static bool RequireBool(object value, UiSetOperation operation)
        {
            if (value is bool result)
                return result;
            throw Error("ui.set-value-type", $"'{operation.Property}' requires a boolean.", operation.Location + ".value");
        }

        private static int RequireInt(object value, UiSetOperation operation)
        {
            if (value is int integer)
                return integer;
            if (value is long longInteger && longInteger >= int.MinValue && longInteger <= int.MaxValue)
                return (int)longInteger;
            throw Error("ui.set-value-type", "value requires a 32-bit integer for this control.", operation.Location + ".value");
        }

        private static long RequireLong(object value, UiSetOperation operation)
        {
            if (value is int integer)
                return integer;
            if (value is long longInteger)
                return longInteger;
            throw Error("ui.set-value-type", "value requires an integer for this control.", operation.Location + ".value");
        }

        private static float RequireFloat(object value, UiSetOperation operation)
        {
            var number = RequireDouble(value, operation);
            if (number < -float.MaxValue || number > float.MaxValue)
                throw Error("ui.set-value-range", "value is outside the supported float range.", operation.Location + ".value");
            return (float)number;
        }

        private static double RequireDouble(object value, UiSetOperation operation)
        {
            double number;
            if (value is int integer)
                number = integer;
            else if (value is long longInteger)
                number = longInteger;
            else if (value is float single)
                number = single;
            else if (value is double doubleValue)
                number = doubleValue;
            else
                throw Error("ui.set-value-type", "value requires a number for this control.", operation.Location + ".value");

            if (double.IsNaN(number) || double.IsInfinity(number))
                throw Error("ui.set-value-range", "value must be finite.", operation.Location + ".value");
            return number;
        }

        private static UiScenarioException SetTypeError(
            UiSetOperation operation,
            VisualElement target,
            string expected)
        {
            return Error(
                "ui.set-target-type",
                $"Cannot set '{operation.Property}' on {target.GetType().Name}; expected {expected}.",
                operation.Location + ".target");
        }

        private static int RewriteBindingsToDictionaryKeys(VisualElement root)
        {
            var changed = 0;
            RewriteBindingsRecursive(root, ref changed);
            return changed;
        }

        private static void RewriteBindingsRecursive(VisualElement element, ref int changed)
        {
            var bindings = element.GetBindingInfos().ToList();
            foreach (var bindingInfo in bindings)
            {
                if (!(bindingInfo.binding is DataBinding dataBinding) || dataBinding.dataSourcePath.IsEmpty)
                    continue;

                dataBinding.dataSourcePath = ConvertNamesToDictionaryKeys(dataBinding.dataSourcePath);
                element.ClearBinding(bindingInfo.bindingId);
                element.SetBinding(bindingInfo.bindingId, dataBinding);
                changed++;
            }

            foreach (var child in element.hierarchy.Children())
                RewriteBindingsRecursive(child, ref changed);
        }

        private static PropertyPath ConvertNamesToDictionaryKeys(PropertyPath original)
        {
            var converted = new PropertyPath();
            for (var i = 0; i < original.Length; i++)
            {
                var part = original[i];
                if (part.IsName)
                {
                    if (part.Name == "Value" && i > 0 && original[i - 1].IsKey)
                    {
                        converted = PropertyPath.AppendName(converted, part.Name);
                        continue;
                    }

                    converted = PropertyPath.AppendKey(converted, part.Name);
                    converted = PropertyPath.AppendName(converted, "Value");
                }
                else if (part.IsIndex)
                {
                    converted = PropertyPath.AppendIndex(converted, part.Index);
                }
                else if (part.IsKey)
                {
                    converted = PropertyPath.AppendKey(converted, part.Key);
                }
            }
            return converted;
        }

        private static void EnsurePropertyBags()
        {
            if (_propertyBagsRegistered)
                return;

            PropertyBag.RegisterDictionary<string, object>();
            PropertyBag.RegisterList<object>();
            _propertyBagsRegistered = true;
        }

        private static void AddLargeIntegerDiagnostics(
            Dictionary<string, object> data,
            UiApplyReport report)
        {
            var locations = new List<string>();
            FindLargeIntegers(data, "$data", locations, 20);
            foreach (var location in locations)
            {
                report.AddDiagnostic(
                    "warning",
                    "ui.integer-64-bit",
                    "Integer is outside the 32-bit range and was preserved as a 64-bit value; some UI controls cannot bind it.",
                    location);
            }
        }

        private static void FindLargeIntegers(object value, string location, List<string> output, int limit)
        {
            if (output.Count >= limit)
                return;
            if (value is long integer)
            {
                if (integer < int.MinValue || integer > int.MaxValue)
                    output.Add(location);
                return;
            }
            if (value is Dictionary<string, object> dictionary)
            {
                foreach (var pair in dictionary)
                    FindLargeIntegers(pair.Value, location + "." + pair.Key, output, limit);
                return;
            }
            if (value is IList list)
            {
                for (var i = 0; i < list.Count && output.Count < limit; i++)
                    FindLargeIntegers(list[i], $"{location}[{i}]", output, limit);
            }
        }

        private static UiScenarioException Error(string code, string message, string location)
        {
            return new UiScenarioException(code, message, location);
        }

        private sealed class AppliedState
        {
            public readonly List<UiCollectionMetadata> Collections = new List<UiCollectionMetadata>();
        }
    }
}
#endif
