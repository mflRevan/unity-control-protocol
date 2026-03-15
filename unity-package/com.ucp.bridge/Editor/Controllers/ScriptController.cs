using System;
using System.Collections.Generic;
using UnityEngine;

namespace UCP.Bridge
{
    public static class ScriptController
    {
        public static void Register(CommandRouter router)
        {
            router.Register("exec/list", HandleList);
            router.Register("exec/run", HandleRun);
        }

        private static List<IUCPScript> DiscoverScripts()
        {
            var scripts = new List<IUCPScript>();
            var interfaceType = typeof(IUCPScript);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (interfaceType.IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface)
                        {
                            try
                            {
                                var instance = (IUCPScript)Activator.CreateInstance(type);
                                scripts.Add(instance);
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[UCP] Failed to instantiate script {type.Name}: {ex.Message}");
                            }
                        }
                    }
                }
                catch (System.Reflection.ReflectionTypeLoadException)
                {
                    // Some assemblies can't be scanned - skip silently
                }
            }

            return scripts;
        }

        private static object HandleList(string paramsJson)
        {
            var scripts = DiscoverScripts();
            var result = new List<object>();

            foreach (var s in scripts)
            {
                result.Add(new Dictionary<string, object>
                {
                    ["name"] = s.Name,
                    ["description"] = s.Description
                });
            }

            return new Dictionary<string, object>
            {
                ["scripts"] = result,
                ["count"] = result.Count
            };
        }

        private static object HandleRun(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("name", out var nameObj))
                throw new ArgumentException("Missing 'name' parameter");

            var name = nameObj.ToString();
            var scriptParams = "{}";
            if (p.TryGetValue("params", out var paramsObj) && paramsObj != null)
                scriptParams = MiniJson.Serialize(paramsObj);

            var scripts = DiscoverScripts();
            IUCPScript target = null;
            foreach (var s in scripts)
            {
                if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    target = s;
                    break;
                }
            }

            if (target == null)
                throw new ArgumentException($"Script not found: {name}. Use exec/list to see available scripts.");

            var result = target.Execute(scriptParams);

            return new Dictionary<string, object>
            {
                ["script"] = name,
                ["result"] = result
            };
        }
    }
}
