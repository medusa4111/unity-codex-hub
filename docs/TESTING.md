# Testing

## Automated Hub suite

```bash
cd /полный/путь/к/unity-codex-hub/hub
npm run typecheck
npm test
```

The suite performs a clean TypeScript build and verifies:

- all protocol commands are present in the Unity parser allowlist;
- all 73 MCP tools, strict input schemas, and read/destructive/idempotent/closed-world annotations;
- real MCP stdio calls against a mock Unity WebSocket client;
- status, command correlation, structured errors, timeouts, disconnect/reconnect, invalid responses;
- compilation → Domain Reload disconnect → reconnect → ready and Play Mode waiting;
- invalid and oversized candidate connections do not replace the active Unity session;
- asset/object exclusivity, path traversal rejection, batch allowlist, dirty-Scene and Terrain/scatter bounds;
- capture traversal/absolute path/symlink/non-PNG rejection and end-to-end MCP image output;
- editor-only assembly structure, Unity meta files, main-thread queue, Undo use, and forbidden APIs.

## Unity API compile check

The C# editor sources are compiled against the installed Unity 6000.5.8f1 managed assemblies with `UNITY_6000_5_OR_NEWER`. This catches obsolete or missing public APIs without launching a second Unity instance.

## Live acceptance test

Live testing changes the open project, so use an empty disposable Scene and commit/stash other work first.

1. Open Unity and wait until `unity_status` reports connected and not compiling/updating.
2. Create `Assets/CodexBridgeTests/BridgeAcceptance.unity` with `unity_new_scene` + `unity_save_scene_as`.
3. Create a primitive and empty parent; inspect hierarchy/component properties; reparent and edit Transform.
4. Create a Material, Prefab, and ScriptableObject test asset under `Assets/CodexBridgeTests/`.
5. Capture Game/Scene View and confirm an image block is visible.
6. Enter Play Mode, wait for `playing`, pause, step, exit, and wait for `stopped`.
7. Confirm Console has no Bridge compile/runtime errors.
8. Press Unity Undo for scene mutations and verify state restoration.
9. Delete the disposable test assets/Scene manually or through version control after review.

Live acceptance cannot be claimed from the Node mock suite alone; record Unity version, project, Scene path, tested tools, Console result, and cleanup status.
