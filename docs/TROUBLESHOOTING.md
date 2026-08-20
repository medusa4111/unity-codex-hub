# Troubleshooting

## `unity_status` says `connected: false`

Keep both Codex and the target Unity project open. Wait for Unity compilation, then call `unity_wait_for_ready`. Check:

```bash
lsof -nP -iTCP:17891 -sTCP:LISTEN
tail -n 100 "/полный/путь/к/unity-codex-hub/logs/hub.log"
```

If no process listens, restart the Codex task/app so it starts the configured stdio Hub. Do not also run `node dist/src/index.js` manually.

## Safe Mode or compilation errors

Choose Safe Mode if the current package fails to compile, copy the first Console error, and fix/rebuild before normal use. The package 0.2.0 compiles against Unity 6000.5.8f1; older supported Editors use conditional InstanceID compatibility. A stale copied package can be refreshed by removing/re-adding the local package or reopening the project.

## `EADDRINUSE`

Another Hub owns port 17891, usually a diagnostic Terminal process. Stop only that known process with `Ctrl+C`, then restart Codex. Do not kill unrelated Node processes.

## `UNITY_COMPILING`, `UNITY_BUSY`, or disconnect during an operation

This is normal around Asset Database refresh and Domain Reload. Call `unity_wait_for_ready`; it waits for reconnect and authoritative status. Inspect state before retrying non-idempotent create/delete/add operations after a timeout.

## Dirty Scene error

Scene switching/closing never silently discards edits. Save first, pass `saveModified=true`, or intentionally pass `discardModified=true`. Untitled dirty Scenes must be saved with `unity_save_scene_as` before automatic save is possible.

## Capture fails

Game capture needs an active Camera; Camera capture needs a referenced GameObject with `Camera`; Scene capture needs an active Scene View. Dimensions must be 64–4096 with at most 16 megapixels. Generated files are temporary and are removed after Hub converts them to MCP image content.

## Object or property not found

Refresh hierarchy/inspection and reuse the returned `instanceId`; IDs change after reload and duplicate names can make paths ambiguous. For serialized writes, call `unity_get_component_properties` and pass the exact `propertyPath` it returned.

## Protocol mismatch

Hub 0.2.x and Unity package 0.2.x both use protocol v3. Rebuild Hub, confirm the local package path in Package Manager, restart Unity after compilation, then restart the Codex task.
