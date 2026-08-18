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

        /// <summary>
        /// Implementing types, cached for the lifetime of the app domain. The scan itself is the
        /// expensive part -- `GetTypes()` over every loaded assembly -- and its result cannot go
        /// stale without a domain reload, which resets this static anyway.
        /// </summary>
        private static Type[] s_scriptTypes;

        private static Type[] DiscoverScriptTypes()
        {
            if (s_scriptTypes != null) return s_scriptTypes;

            var interfaceType = typeof(IUCPScript);
            var bridgeAssemblyName = interfaceType.Assembly.GetName().Name;
            var types = new List<Type>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // A type can only implement IUCPScript if its assembly references the one that
                // declares it. Checking cheap reference metadata first avoids materialising the
                // full type list of every framework and Unity assembly in the domain.
                if (assembly != interfaceType.Assembly && !ReferencesAssembly(assembly, bridgeAssemblyName))
                    continue;

                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (interfaceType.IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface)
                            types.Add(type);
                    }
                }
                catch (System.Reflection.ReflectionTypeLoadException)
                {
                    // Some assemblies can't be scanned - skip silently
                }
            }

            s_scriptTypes = types.ToArray();
            return s_scriptTypes;
        }

        private static bool ReferencesAssembly(System.Reflection.Assembly assembly, string name)
        {
            try
            {
                foreach (var reference in assembly.GetReferencedAssemblies())
                {
                    if (string.Equals(reference.Name, name, StringComparison.Ordinal)) return true;
                }
            }
            catch
            {
                // Dynamic assemblies can refuse to report references; treat them as non-matching.
            }

            return false;
        }

        private static IUCPScript Instantiate(Type type)
        {
            try
            {
                return (IUCPScript)Activator.CreateInstance(type);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UCP] Failed to instantiate script {type.Name}: {ex.Message}");
                return null;
            }
        }

        private static List<IUCPScript> DiscoverScripts()
        {
            var scripts = new List<IUCPScript>();
            foreach (var type in DiscoverScriptTypes())
            {
                var instance = Instantiate(type);
                if (instance != null) scripts.Add(instance);
            }

            return scripts;
        }

        /// <summary>
        /// Resolve one script by name, stopping at the first match.
        /// `Name` is an instance member, so candidates must be constructed to be identified --
        /// but running *every* script's constructor to invoke one of them is a side effect nobody
        /// asked for, so stop as soon as the target is found.
        /// </summary>
        private static IUCPScript FindScript(string name)
        {
            foreach (var type in DiscoverScriptTypes())
            {
                var instance = Instantiate(type);
                if (instance == null) continue;
                if (string.Equals(instance.Name, name, StringComparison.OrdinalIgnoreCase))
                    return instance;
            }

            return null;
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

            var target = FindScript(name);

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
