using System.Globalization;
using Codex.UnityBridge.Protocol;
using UnityEditor;
using UnityEngine;

namespace Codex.UnityBridge.Commands
{
    /// <summary>
    /// Keeps the Bridge protocol's integer object IDs compatible with both the
    /// legacy InstanceID API and Unity 6000.5's replacement EntityId API.
    /// </summary>
    internal static class UnityObjectId
    {
        public static object Get(UnityEngine.Object unityObject)
        {
#if UNITY_6000_5_OR_NEWER
            EntityId entityId = unityObject.GetEntityId();
            return EntityId.ToULong(entityId).ToString(CultureInfo.InvariantCulture);
#else
            return unityObject.GetInstanceID();
#endif
        }

        public static UnityEngine.Object Resolve(string objectId)
        {
#if UNITY_6000_5_OR_NEWER
            ulong rawEntityId;
            if (!ulong.TryParse(
                objectId,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out rawEntityId))
            {
                throw InvalidObjectId(objectId, "EntityId");
            }
            EntityId entityId = EntityId.FromULong(rawEntityId);
            return EditorUtility.EntityIdToObject(entityId);
#else
            int instanceId;
            if (!int.TryParse(objectId, NumberStyles.Integer, CultureInfo.InvariantCulture, out instanceId))
            {
                throw InvalidObjectId(objectId, "32-bit instance ID");
            }
            return EditorUtility.InstanceIDToObject(instanceId);
#endif
        }

        private static ProtocolException InvalidObjectId(string value, string expectedType)
        {
            return new ProtocolException(
                "INVALID_ARGUMENT",
                "Object ID '" + value + "' is not a valid Unity " + expectedType + ".");
        }
    }
}
