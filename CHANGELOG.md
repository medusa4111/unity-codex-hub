# Changelog

## 0.2.0 — 2026-08-13

- Upgraded Hub ↔ Unity protocol from v2 to v3 and expanded the MCP surface from 10 to 73 explicit tools.
- Added readiness and Play Mode waiters that survive compilation, Domain Reload, disconnect, and reconnect.
- Added bounded hierarchy/search, deep serialized property inspection, multi-property/array edits, and safe asset/scene object references.
- Added assets and previews, Prefabs, Materials, ScriptableObjects, multi-Scene lifecycle, Play Mode, filtered Console access, and MCP image captures.
- Added safe batch operations, deterministic Prefab scatter, Terrain editing, selection, Scene View helpers, and project settings summaries.
- Added strict dirty-Scene and overwrite policies, capture-directory containment/symlink/PNG checks, payload bounds, and authenticated-connection replacement.
- Expanded automated tests and compiled the Unity package against Unity 6000.5.8f1.

## 0.1.1 — 2026-08-12

- Added Unity 6000.5 `EntityId` compatibility while retaining legacy InstanceID support.

## 0.1.0 — 2026-08-11

- Initial editor-only MCP/WebSocket bridge with ten semantic Unity tools.
