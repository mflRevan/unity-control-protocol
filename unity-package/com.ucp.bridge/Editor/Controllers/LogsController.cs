using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace UCP.Bridge
{
    public static class LogsController
    {
        private const int MaxHistoryEntries = 2000;
        private const int MaxBulkResults = 10;
        private const int DefaultSearchWindow = 200;
        private const int MaxPreviewLength = 200;

        private static readonly object s_historyLock = new object();
        private static readonly List<LogRecord> s_history = new List<LogRecord>();
        private static long s_nextId = 1;

        public static void Register(CommandRouter router)
        {
            router.Register("logs/subscribe", _ => new Dictionary<string, object> { ["subscribed"] = true });
            router.Register("logs/unsubscribe", _ => new Dictionary<string, object> { ["unsubscribed"] = true });
            router.Register("logs/tail", HandleTail);
            router.Register("logs/search", HandleSearch);
            router.Register("logs/get", HandleGet);
        }

        public static Dictionary<string, object> RecordLog(string message, string stackTrace, LogType type)
        {
            return RecordLog(NormalizeLevel(type), message, stackTrace);
        }

        public static void ClearHistoryForTests()
        {
            lock (s_historyLock)
            {
                s_history.Clear();
                s_nextId = 1;
            }
        }

        public static Dictionary<string, object> RecordTestLog(string level, string message, string stackTrace = "")
        {
            return RecordLog(level, message, stackTrace);
        }

        private static object HandleTail(string paramsJson)
        {
            var query = ParseQuery(paramsJson, includePattern: false);
            return BuildListResult(QueryHistory(query));
        }

        private static object HandleSearch(string paramsJson)
        {
            var query = ParseQuery(paramsJson, includePattern: true);
            return BuildListResult(QueryHistory(query));
        }

        private static object HandleGet(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("id", out var idObj))
                throw new ArgumentException("Missing 'id' parameter");

            long id = Convert.ToInt64(idObj);

            lock (s_historyLock)
            {
                var entry = s_history.FirstOrDefault(record => record.Id == id);
                if (entry == null)
                    throw new ArgumentException($"Log entry not found: {id}");

                return SerializeFull(entry);
            }
        }

        private static Dictionary<string, object> RecordLog(string level, string message, string stackTrace)
        {
            lock (s_historyLock)
            {
                var entry = new LogRecord
                {
                    Id = s_nextId++,
                    Level = NormalizeLevel(level),
                    Message = message ?? string.Empty,
                    StackTrace = stackTrace ?? string.Empty,
                    Timestamp = DateTime.UtcNow.ToString("o")
                };

                s_history.Add(entry);
                if (s_history.Count > MaxHistoryEntries)
                    s_history.RemoveAt(0);

                return SerializeFull(entry);
            }
        }

        private static LogQuery ParseQuery(string paramsJson, bool includePattern)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            var query = new LogQuery();

            if (p != null)
            {
                if (p.TryGetValue("level", out var levelObj) && levelObj != null)
                    query.Level = NormalizeLevel(levelObj.ToString());
                if (p.TryGetValue("count", out var countObj) && countObj != null)
                    query.Count = Math.Max(1, Convert.ToInt32(countObj));
                if (p.TryGetValue("beforeId", out var beforeObj) && beforeObj != null)
                    query.BeforeId = Convert.ToInt64(beforeObj);
                if (p.TryGetValue("afterId", out var afterObj) && afterObj != null)
                    query.AfterId = Convert.ToInt64(afterObj);

                if (includePattern && p.TryGetValue("pattern", out var patternObj) && patternObj != null)
                {
                    query.Pattern = patternObj.ToString();
                    try
                    {
                        query.Regex = new Regex(query.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    }
                    catch (Exception ex)
                    {
                        throw new ArgumentException($"Invalid regex pattern: {ex.Message}");
                    }
                }
            }

            if (query.Count <= 0)
                query.Count = DefaultSearchWindow;

            return query;
        }

        private static LogQueryResult QueryHistory(LogQuery query)
        {
            lock (s_historyLock)
            {
                IEnumerable<LogRecord> candidates = s_history;

                if (query.BeforeId.HasValue)
                    candidates = candidates.Where(entry => entry.Id < query.BeforeId.Value);
                if (query.AfterId.HasValue)
                    candidates = candidates.Where(entry => entry.Id > query.AfterId.Value);
                if (!string.IsNullOrEmpty(query.Level))
                    candidates = candidates.Where(entry => PassesLevel(entry.Level, query.Level));

                candidates = candidates.OrderByDescending(entry => entry.Id).Take(query.Count);

                if (query.Regex != null)
                {
                    candidates = candidates.Where(entry =>
                        query.Regex.IsMatch(entry.Message)
                        || (!string.IsNullOrEmpty(entry.StackTrace) && query.Regex.IsMatch(entry.StackTrace))
                    );
                }

                var allMatches = candidates.ToList();
                var returned = allMatches.Take(MaxBulkResults).Select(SerializeSummary).ToList();

                return new LogQueryResult
                {
                    Total = allMatches.Count,
                    Returned = returned,
                    Truncated = allMatches.Count > MaxBulkResults
                };
            }
        }

        private static Dictionary<string, object> BuildListResult(LogQueryResult queryResult)
        {
            return new Dictionary<string, object>
            {
                ["logs"] = queryResult.Returned.Cast<object>().ToList(),
                ["total"] = queryResult.Total,
                ["returned"] = queryResult.Returned.Count,
                ["truncated"] = queryResult.Truncated
            };
        }

        private static Dictionary<string, object> SerializeSummary(LogRecord entry)
        {
            return new Dictionary<string, object>
            {
                ["id"] = entry.Id,
                ["level"] = entry.Level,
                ["timestamp"] = entry.Timestamp,
                ["messagePreview"] = Preview(entry.Message, MaxPreviewLength),
                ["hasStackTrace"] = !string.IsNullOrEmpty(entry.StackTrace)
            };
        }

        private static Dictionary<string, object> SerializeFull(LogRecord entry)
        {
            return new Dictionary<string, object>
            {
                ["id"] = entry.Id,
                ["level"] = entry.Level,
                ["timestamp"] = entry.Timestamp,
                ["message"] = entry.Message,
                ["stackTrace"] = entry.StackTrace
            };
        }

        private static bool PassesLevel(string value, string threshold)
        {
            return Severity(value) >= Severity(threshold);
        }

        private static int Severity(string level)
        {
            switch (NormalizeLevel(level))
            {
                case "error":
                case "exception":
                    return 2;
                case "warning":
                    return 1;
                default:
                    return 0;
            }
        }

        private static string NormalizeLevel(LogType type)
        {
            switch (type)
            {
                case LogType.Error:
                case LogType.Assert:
                    return "error";
                case LogType.Exception:
                    return "exception";
                case LogType.Warning:
                    return "warning";
                default:
                    return "info";
            }
        }

        private static string NormalizeLevel(string level)
        {
            if (string.IsNullOrEmpty(level))
                return "info";

            var normalized = level.Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "warn":
                    return "warning";
                case "err":
                    return "error";
                default:
                    return normalized;
            }
        }

        private static string Preview(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
                return value ?? string.Empty;

            return value.Substring(0, maxChars) + "...";
        }

        private sealed class LogRecord
        {
            public long Id;
            public string Level;
            public string Message;
            public string StackTrace;
            public string Timestamp;
        }

        private sealed class LogQuery
        {
            public string Level;
            public string Pattern;
            public Regex Regex;
            public int Count;
            public long? BeforeId;
            public long? AfterId;
        }

        private sealed class LogQueryResult
        {
            public int Total;
            public List<Dictionary<string, object>> Returned;
            public bool Truncated;
        }
    }
}