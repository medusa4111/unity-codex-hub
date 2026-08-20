# Security model

The trust boundary is the local user account and the currently open Unity Editor project. The bridge is intentionally not a general automation or remote-control endpoint.

## Enforced boundaries

- MCP uses stdio; the Unity transport binds to literal `127.0.0.1` and rejects non-loopback clients.
- A versioned `unity_hello` handshake is mandatory. An invalid or oversized candidate cannot displace the authenticated Unity connection.
- WebSocket compression is disabled, binary frames are rejected, payloads default to 1 MiB, requests expire, and responses require a correlated UUID.
- Every MCP tool has a strict Zod schema and closed-world annotation. Both Hub and Unity have explicit command allowlists; there is no generic execute command.
- Asset writes accept only normalized paths strictly below `Assets/`; `..`, absolute paths, backslashes, empty segments, and implicit overwrite are rejected.
- Object/asset references are exact and exclusive. Unity 6000.5 `EntityId` values travel as strings to preserve 64-bit precision.
- Hierarchies, searches, serialized properties, arrays, batches, matrices, captures, Console results, and returned scatter objects are bounded.
- All Unity APIs run from `EditorApplication.update` on the main thread. The socket task only validates/enqueues JSON.

## Capture containment

Unity may write generated PNGs only to `Library/UnityCodexBridge/Captures`. Hub accepts only a normalized relative path under that directory, resolves the project/directory/file canonically, rejects symlinks and traversal, enforces a 20 MiB file limit and PNG signature, reads it as MCP image data, and removes the temporary file. No tool can read an arbitrary local file.

## Explicitly absent

- arbitrary C# source, expression evaluation, method invocation, or reflection tool;
- shell/process execution or package-manager command execution;
- unrestricted filesystem read/write or arbitrary URL/network fetch;
- runtime/player code, telemetry, cloud service, hidden model, or public listener;
- modal Save/Open/confirmation dialogs that could hang an unattended request.

Type discovery internally scans loaded assemblies only to resolve a requested concrete `Component` or `ScriptableObject` type; callers cannot select methods, constructors, members, or invoke arbitrary reflected code.

## Operational advice

Use a version-control branch and a disposable Scene for large automated changes. Review MCP approval prompts for tools marked destructive. Do not set `discardModified=true` or `overwrite=true` unless losing the current data is intended.
