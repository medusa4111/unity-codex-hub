# Unity Codex Bridge 0.2.0

This editor-only package is the Unity side of Unity Codex Hub protocol v3. Installation, tool catalog, security, testing, and troubleshooting documents are in the enclosing `UnityBridgeHub/docs` directory.

The package reconnects automatically to the local Hub after Domain Reload. All Unity API calls are dispatched on `EditorApplication.update`; no player/runtime assembly is included.
