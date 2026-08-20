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

## Easy installation with Codex

If you prefer not to run the setup commands yourself, Codex can inspect the downloaded source, adapt it to the Unity Editor installed on your computer, build the Hub, configure MCP, and explain any step that still requires a click in Unity.

1. On this GitHub page, choose **Code → Download ZIP**.
2. Extract the archive to any convenient permanent location. Do not leave it in the Downloads folder if you regularly clean that folder.
3. Copy the full path to the extracted `unity-codex-hub` folder.
4. Open a new local Codex task, keep the target Unity project open, and paste the prompt below after replacing `{INSTALLATION_PATH}`:

```text
Install and configure Unity Codex Hub from:

{INSTALLATION_PATH}

Upstream repository:
https://github.com/medusa4111/unity-codex-hub

The target Unity project is currently open. Perform a safe guided installation:

1. Resolve and validate the installation path. Inspect the local Hub and Unity package versions.
2. Detect the installed Node.js and Codex CLI locations and determine the exact version of the open Unity Editor. If the Unity version cannot be determined automatically, ask me for it once.
3. Check whether this source version supports my Unity version before changing anything.
4. If compatibility changes are required, create a recoverable backup of the affected source files, inspect the upstream implementation, and adapt the local bridge to my Unity version. Preserve all practical features. Prefer version-conditional compilation and safe API fallbacks instead of removing functionality.
5. In the hub directory, install locked dependencies with npm ci, run typecheck and tests, and create a production build.
6. Inspect the existing Codex MCP configuration. Register or safely update the unityCodexHub server using the absolute Node.js, config.json, and built dist/src/index.js paths. Preserve all unrelated MCP servers.
7. Connect the Unity package from unity-package/Packages/com.codex.unitybridge. Perform the step automatically only when it is safe and unambiguous. Otherwise give me the exact Unity Package Manager menu action and the exact package.json path to select.
8. Tell me clearly if I need to restart Codex, reopen Unity, wait for compilation, or perform another manual action. After I complete it, verify the connection with unity_wait_for_ready and unity_status when those tools become available.
9. Do not modify project Scenes, Assets, gameplay code, or remote GitHub content. Do not use destructive Git commands.

At the end, report the detected Unity version, installed Hub/package/protocol versions, build and test results, MCP registration status, any compatibility changes, and the remaining manual steps in order.
```

Codex can complete the source, build, test, and MCP configuration work locally. Unity may still require you to select the package file, wait for compilation, or restart the editor; when that happens, Codex should give you the exact action rather than a generic instruction.

## Updating

To update an existing installation, start a new local Codex task, keep the target Unity project open, and use the prompt below. Replace `{INSTALLATION_PATH}` with the folder that contains your installed Hub.

```text
Audit and safely update my local Unity Codex Hub installation:

Installation path:
{INSTALLATION_PATH}

Upstream repository:
https://github.com/medusa4111/unity-codex-hub

The target Unity project is currently open. Complete the update as follows:

1. Determine the local Hub version, Unity package version, protocol version, exact open Unity Editor version, current Git commit when available, and whether the installation contains local changes.
2. Inspect the latest stable GitHub release or tag and its changelog. If the repository has no releases, compare against main. Review source changes as well as version numbers.
3. Before editing, preserve config.json, logs, local settings, and user changes. Create a recoverable Git branch, commit, or backup snapshot. Do not use git reset --hard, force push, or destructive cleanup.
4. If the upstream version supports my Unity version, update the Hub and Unity package together. Then run npm ci, typecheck, all tests, and a production build.
5. If the upstream version does not support my Unity version, inspect the new upstream code and adapt my local bridge so that it remains compatible with the installed Unity while retaining every practical new feature. Use conditional compilation or compatible API implementations where Unity versions differ. Clearly identify any feature that cannot be reproduced because the required Unity API does not exist.
6. Preserve unrelated MCP servers and the existing installation path. Update the unityCodexHub MCP entry only if its executable, config, or build path is incorrect.
7. After the update, wait for Unity compilation, inspect the Console, call unity_wait_for_ready and unity_status, confirm that Hub/package/protocol versions agree, and run a safe smoke test. Do not modify working Scenes, Assets, or gameplay code.
8. Do not push changes to GitHub or modify the remote repository without separate permission.

At the end, report the old and new versions, every compatibility adaptation, build/test/Unity Console results, remaining limitations, required manual actions, and exact rollback instructions.
```

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
