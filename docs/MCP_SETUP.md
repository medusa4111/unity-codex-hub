# Настройка MCP в Codex

Unity Codex Hub — локальный STDIO MCP server. Codex запускает Node-процесс сам; отдельный HTTP endpoint для MCP не нужен. WebSocket `127.0.0.1:17891` используется только между этим процессом и Unity Editor.

Актуальная официальная документация: [Model Context Protocol in Codex](https://developers.openai.com/codex/mcp/).

## Рекомендуемый способ: Codex CLI

```bash
cd /полный/путь/к/unity-codex-hub
export UNITY_CODEX_HUB_DIR="$PWD"
NODE_BIN="$(command -v node)"
codex mcp add unityCodexHub \
  --env "UNITY_CODEX_HUB_CONFIG=$UNITY_CODEX_HUB_DIR/config.json" \
  -- "$NODE_BIN" "$UNITY_CODEX_HUB_DIR/hub/dist/src/index.js"
codex mcp list
```

Команда сохраняет полный путь к Node, entry point и конфигу. Перезапустите ChatGPT desktop app, IDE extension или текущую Codex-сессию, чтобы клиент перечитал MCP configuration.

Дополнительные команды:

```bash
codex mcp get unityCodexHub
codex mcp --help
```

Чтобы сознательно удалить настройку:

```bash
codex mcp remove unityCodexHub
```

## Ручной `config.toml`

Codex читает пользовательский `~/.codex/config.toml`. Для trusted project также поддерживается `.codex/config.toml` внутри проекта.

Добавьте:

```toml
[mcp_servers.unityCodexHub]
command = "/полный/путь/к/node"
args = ["/полный/путь/к/unity-codex-hub/hub/dist/src/index.js"]
cwd = "/полный/путь/к/unity-codex-hub/hub"
startup_timeout_sec = 10
tool_timeout_sec = 30
enabled = true
default_tools_approval_mode = "writes"

[mcp_servers.unityCodexHub.env]
UNITY_CODEX_HUB_CONFIG = "/полный/путь/к/unity-codex-hub/config.json"
```

Узнать полный путь к Node:

```bash
command -v node
```

`default_tools_approval_mode = "writes"` позволяет клиенту отличать read-only tools от мутаций по MCP annotations и запрашивать подтверждение для записывающих tools. Это можно изменить согласно вашей политике безопасности.

## ChatGPT desktop app

1. Откройте **Settings → MCP servers**.
2. Нажмите **Add server**.
3. Название: `unityCodexHub`.
4. Transport: **STDIO**.
5. Command: полный путь из `command -v node`.
6. Arguments: `/полный/путь/к/unity-codex-hub/hub/dist/src/index.js`.
7. Environment: `UNITY_CODEX_HUB_CONFIG=/полный/путь/к/unity-codex-hub/config.json`.
8. Сохраните и нажмите **Restart**.

После перезапуска в composer можно использовать `/mcp`, чтобы увидеть подключённые servers.

## Что должен увидеть Codex

Server 0.2.0 публикует 73 отдельных tools: от `unity_status`, `unity_wait_for_ready` и глубокой инспекции до Prefab/Material/Scene/Play Mode/capture/batch/Terrain. Полный список находится в [TOOLS.md](TOOLS.md).

Если в списке есть один универсальный `unity_execute`, подключён не этот server или используется другая версия конфигурации.

## Таймауты

Hub `requestTimeout` по умолчанию равен 15 секундам. Для обычных команд MCP `tool_timeout_sec` рекомендуется оставить больше, например 30 секунд. Для `unity_wait_for_ready` и `unity_wait_for_play_mode` клиентский timeout должен быть больше переданного `timeoutMs` (например 180 секунд при ожидании 120 секунд).
