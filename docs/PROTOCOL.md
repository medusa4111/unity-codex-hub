# Hub ↔ Unity protocol

The transport is UTF-8 JSON in WebSocket text frames. Current `protocolVersion` is `3`.

## Handshake

Unity must send this first after every connection or Domain Reload:

```json
{
  "type": "unity_hello",
  "protocolVersion": 3,
  "unityVersion": "2022.3.62f1",
  "projectName": "MyGame",
  "projectPath": "/path/to/MyGame",
  "currentScene": "SampleScene"
}
```

Hub accepts the session with:

```json
{
  "type": "hub_hello",
  "protocolVersion": 3
}
```

A binary frame, invalid handshake, unsupported version, oversized message, or non-loopback peer is rejected.

## Command request

```json
{
  "requestId": "a9e90edf-0197-4814-82a9-d2a07d032a92",
  "command": "create_game_object",
  "params": {
    "name": "Player"
  }
}
```

The complete v3 allowlist is the `UNITY_COMMANDS` constant in `hub/src/protocol/messages.ts` and is mirrored literally by `Editor/Protocol/ProtocolParser.cs`. Automated tests fail if they diverge. It covers status/synchronization, object/component/asset/Prefab/material/Scene/Play/capture/Console/batch/Terrain/Editor-helper commands. MCP-only waiters poll `get_status` and are not Unity commands.

## Success response

```json
{
  "requestId": "a9e90edf-0197-4814-82a9-d2a07d032a92",
  "success": true,
  "result": {
    "name": "Player",
    "instanceId": "4294967297",
    "hierarchyPath": "Player"
  },
  "error": null
}
```

## Failure response

```json
{
  "requestId": "a9e90edf-0197-4814-82a9-d2a07d032a92",
  "success": false,
  "result": null,
  "error": {
    "code": "OBJECT_NOT_FOUND",
    "message": "GameObject 'Player' was not found in the active scene."
  }
}
```

Supported error codes:

- `UNITY_NOT_CONNECTED`
- `OBJECT_NOT_FOUND`
- `COMPONENT_NOT_FOUND`
- `PROPERTY_NOT_FOUND`
- `ASSET_NOT_FOUND`
- `SCENE_NOT_FOUND`
- `PREFAB_NOT_FOUND`
- `TYPE_NOT_FOUND`
- `CAPABILITY_UNAVAILABLE`
- `INVALID_ASSET_PATH`
- `INVALID_SCENE_STATE`
- `PLAY_MODE_TRANSITION`
- `RESULT_TOO_LARGE`
- `JOB_NOT_FOUND`
- `INVALID_RESPONSE` (Hub-local validation)
- `INVALID_ARGUMENT`
- `UNITY_BUSY`
- `UNITY_COMPILING`
- `TIMEOUT`
- `COMMAND_FAILED`
- `INTERNAL_ERROR`

`details` is optional and contains machine-readable context such as ambiguous type matches. Stack traces and raw exceptions are not used as the error message.

Capture commands return metadata containing only a generated project-relative path. Hub independently validates and reads the file from `Library/UnityCodexBridge/Captures`; that path is never a general file-read capability.

## Addressing objects

Object tools require either `instanceId` or `hierarchyPath`, never only a name. `instanceId` is preferred within the current Editor session. Unity 6000.5 and newer returns its 64-bit `EntityId` representation as a string so JavaScript cannot lose precision; earlier Unity versions return a 32-bit number. Hub inputs accept both forms and callers should pass back the value exactly as returned.

Hierarchy path segments use JSON-Pointer-style escaping so names containing separators remain addressable:

- `~` becomes `~0`
- `/` becomes `~1`

When duplicate siblings produce the same path, the Bridge returns `INVALID_ARGUMENT` and asks for `instanceId`.

## Timeout semantics

The Hub expires a command after `requestTimeout`. A timeout means the Hub did not receive a correlated response; it cannot prove that Unity made no change if the connection failed while Unity was already executing. Inspect current state before blindly retrying a non-idempotent create/add/delete action.
