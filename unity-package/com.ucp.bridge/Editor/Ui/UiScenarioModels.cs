#if UNITY_6000_0_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace UCP.Bridge
{
    internal sealed class UiScenarioException : ArgumentException
    {
        public UiScenarioException(string code, string message, string location = null)
            : base(location == null ? message : $"{message} ({location})")
        {
            Code = code;
            Location = location;
        }

        public string Code { get; }
        public string Location { get; }

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                ["severity"] = "error",
                ["code"] = Code,
                ["message"] = Message,
                ["location"] = Location
            };
        }
    }

    internal sealed class UiViewport
    {
        public const int DefaultWidth = 960;
        public const int DefaultHeight = 640;

        public UiViewport(int width = DefaultWidth, int height = DefaultHeight)
        {
            Width = width;
            Height = height;
        }

        public int Width { get; }
        public int Height { get; }

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                ["width"] = Width,
                ["height"] = Height
            };
        }
    }

    internal sealed class UiSettleOptions
    {
        public const int DefaultStableFrames = 3;
        public const int DefaultPixelStableFrames = 2;
        public const double DefaultTimeoutSeconds = 15.0;

        public UiSettleOptions(
            int stableFrames = DefaultStableFrames,
            int pixelStableFrames = DefaultPixelStableFrames,
            double timeoutSeconds = DefaultTimeoutSeconds)
        {
            StableFrames = stableFrames;
            PixelStableFrames = pixelStableFrames;
            TimeoutSeconds = timeoutSeconds;
        }

        public int StableFrames { get; }
        public int PixelStableFrames { get; }
        public double TimeoutSeconds { get; }

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                ["stableFrames"] = StableFrames,
                ["pixelStableFrames"] = PixelStableFrames,
                ["timeoutSeconds"] = TimeoutSeconds
            };
        }
    }

    internal sealed class UiSetOperation
    {
        public UiSetOperation(string selector, string property, object value, string location)
        {
            Selector = selector;
            Property = property;
            Value = value;
            Location = location;
        }

        public string Selector { get; }
        public string Property { get; }
        public object Value { get; }
        public string Location { get; }
    }

    internal enum UiCollectionMode
    {
        Repeat,
        ListView
    }

    internal sealed class UiCollectionDefinition
    {
        public UiCollectionDefinition(
            string selector,
            UiCollectionMode mode,
            string source,
            string templatePath,
            VisualTreeAsset template,
            float? itemHeight,
            string location)
        {
            Selector = selector;
            Mode = mode;
            Source = source;
            TemplatePath = templatePath;
            Template = template;
            ItemHeight = itemHeight;
            Location = location;
        }

        public string Selector { get; }
        public UiCollectionMode Mode { get; }
        public string Source { get; }
        public string TemplatePath { get; }
        public VisualTreeAsset Template { get; }
        public float? ItemHeight { get; }
        public string Location { get; }
    }

    internal sealed class UiResolvedScenario
    {
        public UiResolvedScenario(
            string targetPath,
            string fixturePath,
            string stateName,
            string documentPath,
            VisualTreeAsset document,
            Dictionary<string, object> data,
            IReadOnlyList<UiSetOperation> setOperations,
            IReadOnlyList<UiCollectionDefinition> collections,
            UiViewport viewport,
            UiSettleOptions settle)
        {
            TargetPath = targetPath;
            FixturePath = fixturePath;
            StateName = stateName;
            DocumentPath = documentPath;
            Document = document;
            Data = data;
            SetOperations = setOperations;
            Collections = collections;
            Viewport = viewport;
            Settle = settle;
        }

        public string TargetPath { get; }
        public string FixturePath { get; }
        public string StateName { get; }
        public string DocumentPath { get; }
        public VisualTreeAsset Document { get; }
        public Dictionary<string, object> Data { get; }
        public IReadOnlyList<UiSetOperation> SetOperations { get; }
        public IReadOnlyList<UiCollectionDefinition> Collections { get; }
        public UiViewport Viewport { get; }
        public UiSettleOptions Settle { get; }

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                ["target"] = TargetPath,
                ["fixture"] = FixturePath,
                ["state"] = StateName,
                ["document"] = DocumentPath,
                ["viewport"] = Viewport.ToDictionary(),
                ["settle"] = Settle.ToDictionary(),
                ["setCount"] = SetOperations.Count,
                ["collectionCount"] = Collections.Count
            };
        }
    }

    internal sealed class UiApplyReport
    {
        private readonly List<Dictionary<string, object>> _diagnostics = new List<Dictionary<string, object>>();
        private readonly List<UiCollectionMetadata> _collections = new List<UiCollectionMetadata>();

        public int BindingRewriteCount { get; internal set; }
        public int SetCount { get; internal set; }
        public int RepeatItemCount { get; internal set; }
        public IReadOnlyList<Dictionary<string, object>> Diagnostics => _diagnostics;
        public IReadOnlyList<UiCollectionMetadata> Collections => _collections;

        internal void AddDiagnostic(string severity, string code, string message, string location = null)
        {
            _diagnostics.Add(new Dictionary<string, object>
            {
                ["severity"] = severity,
                ["code"] = code,
                ["message"] = message,
                ["location"] = location
            });
        }

        internal void AddCollection(UiCollectionMetadata metadata)
        {
            _collections.Add(metadata);
        }

        public Dictionary<string, object> ToDictionary()
        {
            var collections = new List<object>(_collections.Count);
            foreach (var collection in _collections)
                collections.Add(collection.ToDictionary());

            return new Dictionary<string, object>
            {
                ["bindingRewriteCount"] = BindingRewriteCount,
                ["setCount"] = SetCount,
                ["repeatItemCount"] = RepeatItemCount,
                ["collections"] = collections,
                ["diagnostics"] = new List<object>(_diagnostics)
            };
        }
    }

    internal sealed class UiCollectionMetadata
    {
        private readonly HashSet<VisualElement> _realizedRows = new HashSet<VisualElement>();
        private readonly Dictionary<VisualElement, int> _boundRows = new Dictionary<VisualElement, int>();
        private readonly VisualElement _target;
        private readonly VisualElement _inspectionRoot;

        public UiCollectionMetadata(
            string selector,
            UiCollectionMode mode,
            string source,
            int logicalItemCount,
            VisualElement target,
            VisualElement inspectionRoot)
        {
            Selector = selector;
            Mode = mode;
            Source = source;
            LogicalItemCount = logicalItemCount;
            _target = target;
            _inspectionRoot = inspectionRoot;
        }

        public string Selector { get; }
        public UiCollectionMode Mode { get; }
        public string Source { get; }
        public int LogicalItemCount { get; }
        public int RealizedRowCount => _realizedRows.Count;
        public int BoundRowCount => _boundRows.Count;

        internal void Realize(VisualElement row)
        {
            if (row != null)
                _realizedRows.Add(row);
        }

        internal void Bind(VisualElement row, int index)
        {
            Realize(row);
            _boundRows[row] = index;
        }

        internal void Unbind(VisualElement row)
        {
            if (row != null)
                _boundRows.Remove(row);
        }

        internal void Destroy(VisualElement row)
        {
            if (row == null)
                return;

            _boundRows.Remove(row);
            _realizedRows.Remove(row);
        }

        public Dictionary<string, object> ToDictionary()
        {
            var rows = new List<object>(_boundRows.Count);
            var visibleRowCount = 0;
            foreach (var pair in _boundRows)
            {
                var row = pair.Key;
                var visible = row != null && row.panel != null && row.resolvedStyle.display != DisplayStyle.None;
                var intersectsViewport = visible && IntersectsViewport(row);
                if (intersectsViewport)
                    visibleRowCount++;
                rows.Add(new Dictionary<string, object>
                {
                    ["index"] = pair.Value,
                    ["name"] = row != null ? row.name : null,
                    ["attached"] = row != null && row.panel != null,
                    ["displayed"] = visible,
                    ["visible"] = intersectsViewport
                });
            }

            rows.Sort((left, right) =>
            {
                var leftIndex = Convert.ToInt32(((Dictionary<string, object>)left)["index"]);
                var rightIndex = Convert.ToInt32(((Dictionary<string, object>)right)["index"]);
                return leftIndex.CompareTo(rightIndex);
            });

            return new Dictionary<string, object>
            {
                ["target"] = Selector,
                ["mode"] = Mode == UiCollectionMode.Repeat ? "repeat" : "list-view",
                ["source"] = Source,
                ["logicalItemCount"] = LogicalItemCount,
                ["realizedRowCount"] = RealizedRowCount,
                ["boundRowCount"] = BoundRowCount,
                ["visibleRowCount"] = visibleRowCount,
                ["boundRows"] = rows
            };
        }

        private bool IntersectsViewport(VisualElement row)
        {
            if (_target == null || _inspectionRoot == null)
                return false;

            for (var current = row; current != null; current = current.parent)
            {
                if (current.resolvedStyle.display == DisplayStyle.None ||
                    current.resolvedStyle.visibility == Visibility.Hidden)
                    return false;
                if (current == _inspectionRoot)
                    break;
            }

            var rowBounds = row.worldBound;
            var viewport = Intersect(_target.worldBound, _inspectionRoot.worldBound);
            return IsFinite(rowBounds) && IsFinite(viewport) && rowBounds.width > 0f && rowBounds.height > 0f &&
                   viewport.width > 0f && viewport.height > 0f && rowBounds.Overlaps(viewport);
        }

        private static Rect Intersect(Rect left, Rect right)
        {
            var xMin = Math.Max(left.xMin, right.xMin);
            var yMin = Math.Max(left.yMin, right.yMin);
            var xMax = Math.Min(left.xMax, right.xMax);
            var yMax = Math.Min(left.yMax, right.yMax);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static bool IsFinite(Rect value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.width) && IsFinite(value.height);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
#endif
