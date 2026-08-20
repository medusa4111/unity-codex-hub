# MCP tool catalog

All tools are closed-world (`openWorldHint=false`) and return a structured envelope. For per-tool argument examples, result shapes, Undo semantics, and errors, see [TOOL_REFERENCE.md](TOOL_REFERENCE.md).

```json
{ "success": true, "result": {}, "error": null }
```

Failures set `isError=true` and return `{ success:false, result:null, error:{code,message,details?} }`. Object tools accept exactly one `instanceId` or `hierarchyPath`; asset tools accept exactly one normalized `Assets/...` path or 32-character GUID.

## Synchronization and inspection

| Tool | Purpose |
| --- | --- |
| `unity_status` | Connection, project, Scene, compilation/update, Play Mode, build target, pipeline |
| `unity_wait_for_ready` | Wait across compile/update/Domain Reload/reconnect |
| `unity_wait_for_play_mode` | Wait for `playing`, `paused`, or `stopped` |
| `unity_refresh_assets` | Refresh Asset Database; does not imply completion |
| `unity_request_script_compilation` | Request compilation; follow with readiness wait |
| `unity_get_hierarchy` | Bounded hierarchy (default 500, maximum 1000 objects) with truncation metadata |
| `unity_get_game_object` | Detailed GameObject/Transform/Component/Prefab state |
| `unity_find_game_objects` | Paginated search across loaded Scenes |
| `unity_get_component_properties` | Bounded serialized-property descriptors and values |
| `unity_get_project_info` | Project, pipeline, target, and open Scenes |
| `unity_get_open_scenes` | Loaded/dirty/active Scene state |

## GameObjects and Components

| Tool | Purpose |
| --- | --- |
| `unity_create_game_object` | Create empty object |
| `unity_create_primitive` | Create Cube/Sphere/Capsule/Cylinder/Plane/Quad |
| `unity_duplicate_game_object` | Duplicate and optionally reparent/transform |
| `unity_delete_game_object` | Delete object subtree |
| `unity_reparent_game_object` | Reparent with explicit transform preservation |
| `unity_set_game_object_properties` | Name, active, tag, layer, static flags |
| `unity_set_transform` | Local/world transform update |
| `unity_add_component` | Add concrete Component type |
| `unity_remove_component` | Remove non-Transform Component |
| `unity_set_component_property` | Set one serialized property |
| `unity_set_component_properties` | Set several properties in one Undo group |
| `unity_resize_serialized_array` | Resize bounded serialized array |
| `unity_set_serialized_array_element` | Set one serialized array element |

## Assets, Prefabs, Materials, ScriptableObjects

| Tool | Purpose |
| --- | --- |
| `unity_find_assets` | Asset Database query with folder/type/pagination |
| `unity_get_asset_info` | Type-specific asset metadata and optional dependencies |
| `unity_get_asset_dependencies` | Paginated dependencies |
| `unity_import_asset` | Import an existing `Assets/...` path |
| `unity_get_asset_preview` | Return Unity AssetPreview as MCP PNG image |
| `unity_instantiate_prefab` | Instantiate Prefab with parent/transform |
| `unity_get_prefab_info` | Source/status/root/override information |
| `unity_save_game_object_as_prefab` | Create or explicitly overwrite Prefab asset |
| `unity_apply_prefab_instance` | Apply instance overrides to asset |
| `unity_revert_prefab_instance` | Revert instance overrides |
| `unity_create_material` | Create/overwrite Material with public Shader |
| `unity_get_material_properties` | Enumerate public Shader properties and values |
| `unity_set_material_property` | Set Color/Vector/number/Integer/Texture value |
| `unity_create_scriptable_object` | Create concrete asset and initialize serialized fields |

## Scenes, Play Mode, captures, Console

| Tool | Purpose |
| --- | --- |
| `unity_list_scenes` | Scene assets, build membership, open state |
| `unity_new_scene` | New empty/default Scene, Single/Additive |
| `unity_open_scene` | Open Scene, Single/Additive |
| `unity_save_scene` | Save loaded Scene to existing path |
| `unity_save_scene_as` | Save to normalized `Assets/*.unity` path |
| `unity_close_scene` | Close Scene with explicit dirty policy |
| `unity_set_active_scene` | Make loaded Scene active |
| `unity_enter_play_mode` | Request Play Mode entry |
| `unity_exit_play_mode` | Request Play Mode exit |
| `unity_pause_play_mode` | Pause/resume Play Mode |
| `unity_step_frame` | Step one paused frame |
| `unity_capture_game_view` | Main/active Game camera PNG |
| `unity_capture_camera` | Referenced Camera PNG |
| `unity_capture_scene_view` | Active Scene View PNG |
| `unity_get_console` | Severity/search/sequence/limit/stack filtering |
| `unity_clear_console_buffer` | Clear Bridge buffer, not Unity Console UI |

Capture tools render at 64–4096 pixels per dimension with a 16-megapixel cap. PNG is created under `Library/UnityCodexBridge/Captures`, independently validated/read by Hub, returned as MCP image content, then removed.

## Batch, scatter, and Terrain

| Tool | Purpose |
| --- | --- |
| `unity_batch` | Up to 100 allowlisted GameObject operations, per-item results |
| `unity_batch_instantiate_prefab` | Up to 500 placements in one Undo group |
| `unity_batch_set_transforms` | Up to 1000 transform updates |
| `unity_scatter_prefab` | Seeded box/disk placement, spacing, yaw, scale, optional raycast alignment |
| `unity_create_terrain` | Create TerrainData and Scene Terrain |
| `unity_get_terrain_info` | Dimensions, resolutions, layers, tree counts |
| `unity_set_terrain_heights` | Replace complete normalized heightmap |
| `unity_set_terrain_heights_patch` | Apply normalized rectangular height patch |
| `unity_set_terrain_layers` | Replace TerrainLayer assets |
| `unity_set_terrain_alphamap_patch` | Apply normalized multi-layer weights |
| `unity_add_terrain_trees` | Add normalized TreeInstances/Prefab prototypes |

`unity_batch` deliberately allows only create object/primitive, set transform/properties, and delete. It cannot nest batches, save/open Scenes, import assets, execute scripts, or call arbitrary commands.

## Editor helpers and settings

| Tool | Purpose |
| --- | --- |
| `unity_get_selection` | Inspect Editor selection |
| `unity_set_selection` | Select scene objects/assets |
| `unity_frame_object_in_scene_view` | Select and frame GameObject |
| `unity_ping_asset` | Ping asset in Project window |
| `unity_get_build_settings` | Build target and Scene list |
| `unity_get_quality_settings` | Current quality summary |
| `unity_get_player_settings_summary` | Safe public PlayerSettings summary |
| `unity_get_packages` | Registered project packages |

## Undo and destructive operations

Scene-object and serialized mutations use Unity Undo where the public API supports it. Asset creation/import/save and some Prefab/Terrain asset writes cannot always be fully undone; their results expose this limitation. `overwrite`, `discardModified`, or similar data-loss flags are never inferred and must be explicitly `true`.
