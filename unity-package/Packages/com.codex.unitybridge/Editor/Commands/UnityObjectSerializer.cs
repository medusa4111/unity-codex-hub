using System;
using System.Collections.Generic;
using UnityEngine;

namespace Codex.UnityBridge.Commands
{
    internal static class UnityObjectSerializer
    {
        public static IDictionary<string, object> HierarchyNode(GameObject gameObject)
        {
            int count = 0;
            bool truncated = false;
            return HierarchyNode(gameObject, 16, 2000, 0, ref count, ref truncated);
        }

        public static IDictionary<string, object> HierarchySnapshot(int maxDepth, int maxItems)
        {
            UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            List<object> roots = new List<object>();
            int count = 0;
            bool truncated = false;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (count >= maxItems) { truncated = true; break; }
                roots.Add(HierarchyNode(root, maxDepth, maxItems, 0, ref count, ref truncated));
            }
            return new Dictionary<string, object>
            {
                { "scene", scene.name }, { "scenePath", scene.path }, { "rootCount", scene.rootCount },
                { "returnedObjectCount", count }, { "objects", roots }, { "truncated", truncated },
                { "maxDepth", maxDepth }, { "maxItems", maxItems }
            };
        }

        private static IDictionary<string, object> HierarchyNode(
            GameObject gameObject, int maxDepth, int maxItems, int depth, ref int count, ref bool truncated)
        {
            count++;
            List<object> children = new List<object>();
            if (depth < maxDepth)
            {
                for (int index = 0; index < gameObject.transform.childCount; index++)
                {
                    if (count >= maxItems) { truncated = true; break; }
                    children.Add(HierarchyNode(gameObject.transform.GetChild(index).gameObject,
                        maxDepth, maxItems, depth + 1, ref count, ref truncated));
                }
            }
            else if (gameObject.transform.childCount > 0) truncated = true;

            Dictionary<string, object> result = BasicGameObject(gameObject);
            result["children"] = children;
            result["childCount"] = gameObject.transform.childCount;
            return result;
        }

        public static IDictionary<string, object> DetailedGameObject(GameObject gameObject)
        {
            Dictionary<string, object> result = BasicGameObject(gameObject);
            Transform transform = gameObject.transform;
            result["transform"] = new Dictionary<string, object>
            {
                { "position", Vector3Value(transform.position) },
                { "rotation", Vector3Value(transform.eulerAngles) },
                { "localPosition", Vector3Value(transform.localPosition) },
                { "localRotation", Vector3Value(transform.localEulerAngles) },
                { "localScale", Vector3Value(transform.localScale) }
            };
            return result;
        }

        public static IDictionary<string, object> ComponentValue(Component component)
        {
            Dictionary<string, object> result = new Dictionary<string, object>
            {
                { "name", component.GetType().Name },
                { "fullName", component.GetType().FullName },
                { "instanceId", UnityObjectId.Get(component) }
            };
            Behaviour behaviour = component as Behaviour;
            if (behaviour != null)
            {
                result["enabled"] = behaviour.enabled;
            }
            return result;
        }

        public static IDictionary<string, object> Vector3Value(Vector3 value)
        {
            return new Dictionary<string, object>
            {
                { "x", value.x },
                { "y", value.y },
                { "z", value.z }
            };
        }

        public static Dictionary<string, object> BasicGameObject(GameObject gameObject)
        {
            return new Dictionary<string, object>
            {
                { "name", gameObject.name },
                { "instanceId", UnityObjectId.Get(gameObject) },
                { "active", gameObject.activeSelf },
                { "activeInHierarchy", gameObject.activeInHierarchy },
                { "tag", gameObject.tag },
                { "layer", gameObject.layer },
                { "hierarchyPath", GameObjectResolver.HierarchyPath(gameObject) },
                { "sceneName", gameObject.scene.name },
                { "scenePath", gameObject.scene.path },
                { "components", Components(gameObject) }
            };
        }

        private static IList<object> Components(GameObject gameObject)
        {
            List<object> result = new List<object>();
            Component[] components = gameObject.GetComponents<Component>();
            foreach (Component component in components)
            {
                if (component == null)
                {
                    result.Add(new Dictionary<string, object>
                    {
                        { "name", "Missing Script" },
                        { "fullName", null },
                        { "instanceId", 0 }
                    });
                }
                else
                {
                    result.Add(ComponentValue(component));
                }
            }
            return result;
        }
    }
}
