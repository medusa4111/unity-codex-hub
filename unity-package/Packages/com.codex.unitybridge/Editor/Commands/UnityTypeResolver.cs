using System;
using System.Collections.Generic;
using Codex.UnityBridge.Protocol;
using UnityEditor;
using UnityEngine;

namespace Codex.UnityBridge.Commands
{
    internal static class UnityTypeResolver
    {
        private static List<Type> scriptableObjectTypes;

        public static Type ResolveScriptableObject(string requestedName)
        {
            if (scriptableObjectTypes == null)
            {
                scriptableObjectTypes = new List<Type>();
                foreach (Type type in TypeCache.GetTypesDerivedFrom<ScriptableObject>())
                    if (!type.IsAbstract && !type.IsGenericTypeDefinition) scriptableObjectTypes.Add(type);
            }
            return Resolve(requestedName, scriptableObjectTypes, "ScriptableObject");
        }

        private static Type Resolve(string requestedName, IList<Type> candidates, string kind)
        {
            List<Type> fullMatches = new List<Type>();
            List<Type> shortMatches = new List<Type>();
            foreach (Type type in candidates)
            {
                if (string.Equals(type.FullName, requestedName, StringComparison.Ordinal)
                    || string.Equals(type.AssemblyQualifiedName, requestedName, StringComparison.Ordinal))
                    fullMatches.Add(type);
                else if (string.Equals(type.Name, requestedName, StringComparison.Ordinal)) shortMatches.Add(type);
            }
            List<Type> matches = fullMatches.Count > 0 ? fullMatches : shortMatches;
            if (matches.Count == 0)
                throw new ProtocolException("TYPE_NOT_FOUND", kind + " type '" + requestedName + "' was not found.");
            if (matches.Count > 1)
            {
                List<string> names = new List<string>();
                foreach (Type match in matches) names.Add(match.AssemblyQualifiedName);
                throw new ProtocolException("INVALID_ARGUMENT", kind + " type is ambiguous; use a full name.",
                    new Dictionary<string, object> { { "matches", names } });
            }
            return matches[0];
        }
    }
}
