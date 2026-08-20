# Per-tool argument and result reference

This complements [TOOLS.md](TOOLS.md). Every row includes an argument example (the complete object may contain the optional fields documented by the MCP schema), concise result shape, Undo behavior, and primary tool-specific errors. All tools may also return `UNITY_NOT_CONNECTED`, `UNITY_BUSY`, `UNITY_COMPILING`, `TIMEOUT`, `INVALID_ARGUMENT`, `COMMAND_FAILED`, or `INTERNAL_ERROR` when applicable.

Shorthand used below:

- `objectRef`: exactly one of `{ "instanceId":"4294967297" }` or `{ "hierarchyPath":"Root/Child" }`.
- `assetRef`: exactly one of `{ "assetPath":"Assets/X.ext" }` or `{ "guid":"32 hex characters" }`.
- All results are inside `{success:true,result:…,error:null}`; failures use the structured error envelope.

## Status and inspection

| Tool | Argument example | Result | Undo / primary errors |
| --- | --- | --- | --- |
| `unity_status` | `{}` | Connection, project, active Scene, compile/update/play/pipeline state | Read-only |
| `unity_wait_for_ready` | `{"timeoutMs":120000,"pollIntervalMs":250}` | `{ready:true,status}` | Read-only; `TIMEOUT` includes last state |
| `unity_wait_for_play_mode` | `{"state":"playing","timeoutMs":120000}` | `{reached,status}` | Read-only; `TIMEOUT` |
| `unity_refresh_assets` | `{"forceUpdate":false}` | Requested flag and current compile/update state | No Undo; `UNITY_BUSY` |
| `unity_request_script_compilation` | `{}` | Requested flag and retry guidance | No Undo; reconnect expected |
| `unity_get_hierarchy` | `{"maxDepth":16,"maxItems":500}` | Scene record, nested objects, `truncated` | Read-only; maximum 1000 objects |
| `unity_get_game_object` | `{"instanceId":"42"}` | Detailed object, transforms, components, Prefab/Scene state | Read-only; `OBJECT_NOT_FOUND` |
| `unity_find_game_objects` | `{"partialName":"Enemy","componentType":"Rigidbody","limit":100}` | `{results,totalMatches,offset,limit,truncated}` | Read-only; `TYPE_NOT_FOUND` |
| `unity_get_component_properties` | `{"hierarchyPath":"Player","componentType":"Rigidbody","maxDepth":4,"maxItems":200}` | Component plus property descriptors/values/truncation | Read-only; `COMPONENT_NOT_FOUND` |
| `unity_get_project_info` | `{}` | Project/build target/pipeline/open Scenes | Read-only |
| `unity_get_open_scenes` | `{}` | `{count,scenes[]}` with dirty/active state | Read-only |

## GameObjects and serialized editing

| Tool | Argument example | Result | Undo / primary errors |
| --- | --- | --- | --- |
| `unity_create_game_object` | `{"name":"Container","parentPath":"Root"}` | Detailed new object | Undo create; `OBJECT_NOT_FOUND` |
| `unity_create_primitive` | `{"primitiveType":"Cube","position":{"x":0,"y":1,"z":0}}` | Detailed new primitive | Undo create |
| `unity_duplicate_game_object` | `{"instanceId":"42","newName":"Copy","worldPositionStays":true}` | Detailed duplicate | Undo create; `OBJECT_NOT_FOUND` |
| `unity_delete_game_object` | `{"hierarchyPath":"Root/Old"}` | Deleted ID/name/path | Undo delete; `OBJECT_NOT_FOUND` |
| `unity_reparent_game_object` | `{"instanceId":"42","parentPath":"Root","worldPositionStays":true}` | Detailed moved object | Undo; cycle → `INVALID_ARGUMENT` |
| `unity_set_game_object_properties` | `{"instanceId":"42","active":true,"layer":"Default"}` | Detailed final object | Undo; invalid tag/layer → `INVALID_ARGUMENT` |
| `unity_set_transform` | `{"instanceId":"42","space":"local","position":{"x":1,"y":2,"z":3}}` | Detailed final object | Undo; world scale → `INVALID_ARGUMENT` |
| `unity_add_component` | `{"instanceId":"42","componentType":"BoxCollider"}` | Object and new component | Undo add; `TYPE_NOT_FOUND` |
| `unity_remove_component` | `{"instanceId":"42","componentType":"BoxCollider"}` | Removed component summary | Undo remove; Transform rejected |
| `unity_set_component_property` | `{"instanceId":"42","componentType":"Rigidbody","propertyPath":"m_Mass","value":2}` | Target and changed property | Undo; `PROPERTY_NOT_FOUND`, incompatible reference |
| `unity_set_component_properties` | `{"instanceId":"42","componentType":"Rigidbody","properties":[{"propertyPath":"m_Mass","value":2}]}` | Target and changed paths | One Undo group; `PROPERTY_NOT_FOUND` |
| `unity_resize_serialized_array` | `{"instanceId":"42","componentType":"MyComponent","propertyPath":"items","size":4}` | `{propertyPath,size}` | Undo; non-array rejected |
| `unity_set_serialized_array_element` | `{"instanceId":"42","componentType":"MyComponent","propertyPath":"items","index":0,"value":1}` | Path/index/final value | Undo; invalid index/type rejected |

## Assets, Prefabs, Materials, ScriptableObjects

| Tool | Argument example | Result | Undo / primary errors |
| --- | --- | --- | --- |
| `unity_find_assets` | `{"query":"crate","type":"Prefab","folders":["Assets/Props"],"limit":100}` | GUID/path/name/type/category page | Read-only; `INVALID_ASSET_PATH` |
| `unity_get_asset_info` | `{"assetPath":"Assets/Props/Crate.prefab","includeDependencies":true}` | Labels, subassets, importer, type-specific metadata | Read-only; `ASSET_NOT_FOUND` |
| `unity_get_asset_dependencies` | `{"guid":"0123456789abcdef0123456789abcdef","recursive":true,"limit":500}` | Paginated dependency paths | Read-only; `ASSET_NOT_FOUND` |
| `unity_import_asset` | `{"assetPath":"Assets/Props/Crate.fbx","forceUpdate":false}` | Imported asset and compile/update state | No Undo; `ASSET_NOT_FOUND` |
| `unity_get_asset_preview` | `{"assetPath":"Assets/Props/Crate.prefab","width":256,"height":256}` | MCP PNG + metadata, or `{ready:false,retryable:true}` | Read-only/temp capture; `ASSET_NOT_FOUND` |
| `unity_instantiate_prefab` | `{"assetPath":"Assets/Props/Crate.prefab","position":{"x":0,"y":0,"z":0}}` | Detailed connected Prefab instance | Undo create; `PREFAB_NOT_FOUND` |
| `unity_get_prefab_info` | `{"instanceId":"42"}` | Asset path/type/status/root/source/override count | Read-only; `OBJECT_NOT_FOUND` |
| `unity_save_game_object_as_prefab` | `{"instanceId":"42","assetPath":"Assets/Prefabs/New.prefab","overwrite":false}` | Saved asset/overwrite/Undo flag | Asset write not normal Undo; existing path rejected |
| `unity_apply_prefab_instance` | `{"instanceId":"42"}` | Applied count and bounded changed paths | Consequential, Unity Prefab Undo; non-instance rejected |
| `unity_revert_prefab_instance` | `{"instanceId":"42"}` | Reverted count/changes/final object | Consequential, Unity Prefab Undo |
| `unity_create_material` | `{"assetPath":"Assets/Materials/Blue.mat","shaderName":"Universal Render Pipeline/Lit","overwrite":false}` | Material reference and shader | Asset creation not normal Undo; missing Shader |
| `unity_get_material_properties` | `{"assetPath":"Assets/Materials/Blue.mat"}` | Shader, keywords, public property values | Read-only; wrong asset type |
| `unity_set_material_property` | `{"assetPath":"Assets/Materials/Blue.mat","propertyName":"_BaseColor","value":{"r":0,"g":0,"b":1,"a":1}}` | Final typed value | Undo; missing/unsupported property |
| `unity_create_scriptable_object` | `{"type":"MySettings","assetPath":"Assets/Data/MySettings.asset","initialProperties":[],"overwrite":false}` | Asset reference and initialized count | Asset creation not normal Undo; `TYPE_NOT_FOUND` |

## Scene, Play Mode, capture, and Console

| Tool | Argument example | Result | Undo / primary errors |
| --- | --- | --- | --- |
| `unity_list_scenes` | `{"includePackages":false,"offset":0,"limit":200}` | Scene asset page/build/open state | Read-only |
| `unity_new_scene` | `{"setup":"EmptyScene","mode":"Single","saveModified":true}` | Created Scene record | Not normal Undo; dirty policy required |
| `unity_open_scene` | `{"scenePath":"Assets/Scenes/Level.unity","mode":"Additive"}` | Opened Scene record | Not normal Undo; `SCENE_NOT_FOUND` |
| `unity_save_scene` | `{"scenePath":"Assets/Scenes/Level.unity"}` | Saved Scene record | Save not Undo; untitled → `INVALID_SCENE_STATE` |
| `unity_save_scene_as` | `{"destinationPath":"Assets/Scenes/New.unity","overwrite":false}` | Saved Scene/new path/overwrite flag | Asset write not Undo; path conflict |
| `unity_close_scene` | `{"scenePath":"Assets/Scenes/Level.unity","saveModified":true}` | Closed path/remove state | Not Undo; dirty policy required |
| `unity_set_active_scene` | `{"sceneName":"Level"}` | Active Scene record | Editor state; ambiguous name rejected |
| `unity_enter_play_mode` | `{}` | Request/current transition state | State transition; compilation may prevent entry |
| `unity_exit_play_mode` | `{}` | Request/current transition state | State transition |
| `unity_pause_play_mode` | `{"paused":true}` | Current play/pause state | Idempotent state update; stopped rejected |
| `unity_step_frame` | `{}` | `{stepped:true,isPlaying:true,isPaused:true}` | Non-idempotent; requires paused Play Mode |
| `unity_capture_game_view` | `{"width":1280,"height":720,"transparentBackground":false}` | MCP PNG and source/size/time/play metadata | Read-only/temp capture; no Camera → `CAPABILITY_UNAVAILABLE` |
| `unity_capture_camera` | `{"hierarchyPath":"Main Camera","width":1280,"height":720}` | MCP PNG and Camera metadata | Read-only/temp capture; no Camera component |
| `unity_capture_scene_view` | `{"width":1280,"height":720}` | MCP PNG and Scene View metadata | Read-only/temp capture; no Scene View → `CAPABILITY_UNAVAILABLE` |
| `unity_get_console` | `{"severities":["Error"],"sinceSequence":0,"maxResults":200}` | Messages/latest sequence/truncation/scope | Read-only |
| `unity_clear_console_buffer` | `{}` | Removed count/latest sequence/scope | Clears Bridge memory only; not Unity UI |

## Batch, scatter, and Terrain

| Tool | Argument example | Result | Undo / primary errors |
| --- | --- | --- | --- |
| `unity_batch` | `{"operations":[{"command":"create_game_object","name":"A"}],"stopOnError":true,"undoGroupName":"Codex: Batch"}` | Per-index success/error, counts, stopped flag | One Undo group; unsupported command rejected |
| `unity_batch_instantiate_prefab` | `{"assetPath":"Assets/Tree.prefab","placements":[{"position":{"x":0,"y":0,"z":0}}]}` | Created count and objects | One Undo group; `PREFAB_NOT_FOUND` |
| `unity_batch_set_transforms` | `{"items":[{"instanceId":"42","position":{"x":0,"y":0,"z":0}}],"stopOnError":true}` | Per-item results/counts | One Undo group; `OBJECT_NOT_FOUND` |
| `unity_scatter_prefab` | `{"assetPath":"Assets/Tree.prefab","count":100,"seed":42,"center":{"x":0,"y":0,"z":0},"radius":20}` | Created/attempt counts, seed, bounded objects | One Undo group; placement limit reported |
| `unity_create_terrain` | `{"assetPath":"Assets/Terrain/TerrainData.asset","size":{"x":500,"y":100,"z":500},"heightmapResolution":513}` | Terrain object and TerrainData asset | Object creation Undo; asset creation not normal Undo |
| `unity_get_terrain_info` | `{"hierarchyPath":"Terrain"}` | Size/resolutions/layers/tree metadata | Read-only; missing Terrain rejected |
| `unity_set_terrain_heights` | `{"hierarchyPath":"Terrain","heights":[[0,0],[0,0]]}` | Updated region/data reference | TerrainData Undo; dimensions must match full map |
| `unity_set_terrain_heights_patch` | `{"hierarchyPath":"Terrain","xBase":0,"yBase":0,"heights":[[0.5]]}` | Updated bounded region | TerrainData Undo; out-of-bounds rejected |
| `unity_set_terrain_layers` | `{"hierarchyPath":"Terrain","layers":[{"assetPath":"Assets/Terrain/Grass.terrainlayer"}]}` | Final layer count | TerrainData Undo; incompatible asset rejected |
| `unity_set_terrain_alphamap_patch` | `{"hierarchyPath":"Terrain","x":0,"y":0,"values":[[[1.0]]]}` | Updated area/layer count | TerrainData Undo; weights normalized, bounds enforced |
| `unity_add_terrain_trees` | `{"hierarchyPath":"Terrain","trees":[{"prefab":{"assetPath":"Assets/Tree.prefab"},"position":{"x":0.5,"y":0,"z":0.5}}]}` | Added/total/prototype counts | TerrainData Undo; positions must be normalized |

## Editor helpers and settings

| Tool | Argument example | Result | Undo / primary errors |
| --- | --- | --- | --- |
| `unity_get_selection` | `{}` | Selected records and active object | Read-only |
| `unity_set_selection` | `{"objects":[{"hierarchyPath":"Player"}],"activeIndex":0}` | Final selection | Editor state, no Scene mutation |
| `unity_frame_object_in_scene_view` | `{"instanceId":"42"}` | Framed object | Editor state; no Scene View → `CAPABILITY_UNAVAILABLE` |
| `unity_ping_asset` | `{"assetPath":"Assets/Tree.prefab"}` | Pinged asset record | Editor state; `ASSET_NOT_FOUND` |
| `unity_get_build_settings` | `{}` | Active target and build Scene entries | Read-only |
| `unity_get_quality_settings` | `{}` | Current level/names/render values | Read-only |
| `unity_get_player_settings_summary` | `{}` | Company/product/version/color/screen summary | Read-only |
| `unity_get_packages` | `{}` | Registered package records | Read-only |
