# Unity Codex Hub 0.2.0

Unity Codex Hub is a local, editor-only MCP bridge between Codex and Unity Editor. It lets Codex inspect a project, edit Scenes and components, work with assets and Prefabs, control Play Mode, read Console output, capture visual results, and perform bounded batch and Terrain operations through 73 explicit tools.

```text
Codex ── MCP stdio ──> Node.js Hub ── loopback WebSocket/JSON ──> Unity Editor Bridge
                                                                    │
                                                         EditorApplication.update
                                                                    │
                                                         public Unity Editor APIs
```

The Hub binds only to `127.0.0.1`. There is no generic execute tool, arbitrary C# input, shell/process execution, unrestricted file read/write, arbitrary URL access, or runtime/player bridge.

## Capabilities

- Reliable status, compilation/Asset Database readiness, Domain Reload reconnect, and Play Mode waiting.
- Bounded hierarchy/search plus deep `SerializedObject` inspection and editing, including arrays and safe object references.
- GameObject, Component, Transform, Prefab, Material, ScriptableObject, asset, and multi-Scene workflows.
- Play, pause, step, Console filtering, Game/Camera/Scene View PNG capture returned as MCP image content.
- Safe batch operations, deterministic Prefab scatter, and bounded Terrain height/layer/alphamap/tree tools.
- Selection, Scene View framing, build/quality/player/package inspection.
- Unity Undo for supported scene/serialized mutations; explicit overwrite/discard flags where operations can lose data.

See the complete [tool catalog](docs/TOOLS.md), [security model](docs/SECURITY.md), and [architecture](docs/ARCHITECTURE.md).

## Requirements

- macOS 12 or newer;
- Unity Editor 2021.3 LTS or newer;
- Node.js 20 or newer;
- Codex CLI, the Codex desktop app, or another local MCP client.

## Quick installation

Clone and build the Hub:

```bash
git clone https://github.com/medusa4111/unity-codex-hub.git
cd unity-codex-hub
export UNITY_CODEX_HUB_DIR="$PWD"
cd hub
npm ci
npm run build
npm test
```

In Unity, open **Window → Package Manager**, choose **+ → Add package from disk…**, then select:

```text
<unity-codex-hub>/unity-package/Packages/com.codex.unitybridge/package.json
```

Register the local MCP server:

```bash
NODE_BIN="$(command -v node)"
codex mcp add unityCodexHub \
  --env "UNITY_CODEX_HUB_CONFIG=$UNITY_CODEX_HUB_DIR/config.json" \
  -- "$NODE_BIN" "$UNITY_CODEX_HUB_DIR/hub/dist/src/index.js"
codex mcp list
```

Restart Codex, keep the target Unity project open, wait for compilation to finish, then ask:

```text
Call unity_status and show the result.
```

For screenshots and a detailed macOS walkthrough, see [INSTALLATION.md](docs/INSTALLATION.md).

## Everyday use

1. Open the Unity project you want Codex to control.
2. Wait for Unity compilation/import to finish.
3. Open or restart a Codex task so it starts the configured Hub.
4. Use `unity_status` before a multi-step workflow.

Codex starts the Node Hub automatically for each task/session. A separate Terminal process is needed only for diagnostics. Unity itself must stay open for Editor tools to work.

## Development checks

```bash
cd unity-codex-hub/hub
npm install
npm run typecheck
npm test
```

The suite builds TypeScript, drives a real MCP stdio client, simulates Unity WebSocket handshakes/reloads/errors, validates every tool/schema/annotation, and tests capture-path security. The editor assembly has also been compiled against the installed Unity 6000.5.8f1 public API surface. Live scene/Undo verification requires an open licensed Unity Editor and should use a disposable test Scene.

## Documentation

- [Installation on macOS](docs/INSTALLATION.md)
- [Codex MCP setup](docs/MCP_SETUP.md)
- [Tool catalog](docs/TOOLS.md) and [per-tool reference](docs/TOOL_REFERENCE.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Wire protocol](docs/PROTOCOL.md)
- [Security](docs/SECURITY.md)
- [Testing](docs/TESTING.md)
- [Troubleshooting](docs/TROUBLESHOOTING.md)
- [Changelog](CHANGELOG.md)
- [MIT License](LICENSE)

## Versioning

Hub and Unity package versions move together. This project uses semantic versioning while pre-1.0: minor releases may expand the protocol/tool surface, while patch releases remain compatible bug fixes. Protocol v3 requires Hub 0.2.x and Unity package 0.2.x.
