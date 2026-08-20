using System;
using System.Collections.Generic;
using Codex.UnityBridge.Protocol;
using UnityEditor;
using UnityEngine;

namespace Codex.UnityBridge.Commands
{
    internal static class ComponentResolver
    {
        private static List<Type> cachedComponentTypes;

        public static Type ResolveType(string requestedName)
        {
            List<Type> fullNameMatches = new List<Type>();
            List<Type> shortNameMatches = new List<Type>();
            foreach (Type type in ComponentTypes)
            {
                if (type.IsAbstract || type.IsGenericTypeDefinition)
                {
                    continue;
                }

                if (string.Equals(type.FullName, requestedName, StringComparison.Ordinal)
                    || string.Equals(type.AssemblyQualifiedName, requestedName, StringComparison.Ordinal))
                {
                    fullNameMatches.Add(type);
                }
                else if (string.Equals(type.Name, requestedName, StringComparison.Ordinal))
                {
                    shortNameMatches.Add(type);
                }
            }

            List<Type> matches = fullNameMatches.Count > 0 ? fullNameMatches : shortNameMatches;
            if (matches.Count == 0)
            {
                throw new ProtocolException(
                    "COMPONENT_NOT_FOUND", "Component type '" + requestedName + "' was not found.");
            }
            if (matches.Count > 1)
            {
                List<string> names = new List<string>();
                foreach (Type match in matches) names.Add(match.FullName);
                throw new ProtocolException(
                    "INVALID_ARGUMENT",
                    "Component type name '" + requestedName + "' is ambiguous. Use a full type name.",
                    new Dictionary<string, object> { { "matches", names } });
            }
            return matches[0];
        }

        private static IList<Type> ComponentTypes
        {
            get
            {
                if (cachedComponentTypes == null)
                {
                    cachedComponentTypes = new List<Type>();
                    foreach (Type type in TypeCache.GetTypesDerivedFrom<Component>())
                    {
                        if (!type.IsAbstract && !type.IsGenericTypeDefinition) cachedComponentTypes.Add(type);
                    }
                }
                return cachedComponentTypes;
            }
        }

        public static Component ResolveComponent(
            GameObject gameObject,
            IDictionary<string, object> parameters)
        {
            string componentInstanceId;
            if (CommandArguments.TryObjectId(parameters, "componentInstanceId", out componentInstanceId))
            {
                Component byId = UnityObjectId.Resolve(componentInstanceId) as Component;
                if (byId == null || byId.gameObject != gameObject)
                {
                    throw new ProtocolException(
                        "COMPONENT_NOT_FOUND",
                        "Component instanceId " + componentInstanceId + " was not found on the target GameObject.");
                }
                return byId;
            }

            string componentTypeName = CommandArguments.RequiredString(parameters, "componentType");
            Type componentType = ResolveType(componentTypeName);
            Component[] components = gameObject.GetComponents(componentType);
            if (components.Length == 0)
            {
                throw new ProtocolException(
                    "COMPONENT_NOT_FOUND",
                    "GameObject '" + GameObjectResolver.HierarchyPath(gameObject)
                    + "' does not have component " + componentType.FullName + ".");
            }
            if (components.Length > 1)
            {
                List<object> instanceIds = new List<object>();
                foreach (Component component in components) instanceIds.Add(UnityObjectId.Get(component));
                throw new ProtocolException(
                    "INVALID_ARGUMENT",
                    "The GameObject has multiple components of type " + componentType.FullName
                    + ". Use componentInstanceId.",
                    new Dictionary<string, object> { { "componentInstanceIds", instanceIds } });
            }
            return components[0];
        }
    }
}
