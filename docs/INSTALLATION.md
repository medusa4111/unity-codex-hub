# Установка на macOS

Репозиторий можно клонировать в любую папку. Unity-проект, которым нужно управлять, также может находиться где угодно.

## 1. Что установить

Нужно:

- macOS 12 или новее;
- Unity Hub и Unity Editor 2021.3 LTS или новее;
- Node.js 20 или новее;
- локальный клиент Codex: ChatGPT desktop app, Codex CLI или IDE extension.

Проверьте Node.js:

```bash
node --version
npm --version
```

Если `node` не найден, один из простых вариантов — Homebrew:

```bash
brew install node
node --version
```

Версия Node должна начинаться с `v20` или большего числа.

## 2. Установить зависимости и собрать Hub

```bash
git clone https://github.com/medusa4111/unity-codex-hub.git
cd unity-codex-hub
export UNITY_CODEX_HUB_DIR="$PWD"
cd hub
npm ci
npm run build
npm test
```

Ожидаемый результат: все тесты пройдены, `fail 0`.

Основной конфиг уже находится здесь:

```text
<unity-codex-hub>/config.json
```

По умолчанию Hub слушает `127.0.0.1:17891`, ждёт ответ Unity 15 секунд и пишет лог в `logs/hub.log`.

## 3. Добавить Unity package

1. Откройте нужный проект в Unity.
2. Выберите **Window → Package Manager**.
3. Нажмите `+` в левом верхнем углу.
4. Выберите **Add package from disk…**.
5. Выберите файл:

```text
<unity-codex-hub>/unity-package/Packages/com.codex.unitybridge/package.json
```

6. Дождитесь окончания импорта и компиляции.

Код пакета находится только в папке `Editor` и editor-only assembly definition, поэтому он не попадает в runtime build игры.

### Необязательная настройка Unity Bridge

Если используется стандартный порт `17891`, ничего создавать не нужно. Чтобы переопределить порт или задержку reconnect, скопируйте пример в открытый Unity-проект:

```bash
UNITY_PROJECT="/полный/путь/к/вашему/UnityProject"
cp "$UNITY_CODEX_HUB_DIR/unity-package/Packages/com.codex.unitybridge/Documentation~/UnityCodexHub.example.json" \
  "$UNITY_PROJECT/ProjectSettings/UnityCodexHub.json"
open -a TextEdit "$UNITY_PROJECT/ProjectSettings/UnityCodexHub.json"
```

`host` намеренно может быть только `127.0.0.1`. Если меняете `port`, укажите одинаковое значение здесь и в корневом `config.json` Hub.

## 4. Проверить Hub и Unity без Codex

Это диагностический запуск. Выполните:

```bash
cd "$UNITY_CODEX_HUB_DIR/hub"
node dist/src/index.js
```

Оставьте Terminal открытым и переключитесь в Unity. После загрузки package в Unity Console должно появиться:

```text
Unity Codex Bridge connected to local Hub.
```

В Terminal/логе появится `Unity connected`. Проверить лог отдельно можно так:

```bash
tail -f "$UNITY_CODEX_HUB_DIR/logs/hub.log"
```

Завершите диагностический Hub сочетанием `Ctrl+C` перед настройкой Codex. Одновременно должен работать только один Hub на порту 17891: при обычной работе его будет запускать сам Codex.

## 5. Добавить MCP server в Codex

В новом окне Terminal выполните:

```bash
NODE_BIN="$(command -v node)"
codex mcp add unityCodexHub \
  --env "UNITY_CODEX_HUB_CONFIG=$UNITY_CODEX_HUB_DIR/config.json" \
  -- "$NODE_BIN" "$UNITY_CODEX_HUB_DIR/hub/dist/src/index.js"
codex mcp list
```

Если используете ChatGPT desktop app, можно вместо CLI открыть **Settings → MCP servers → Add server**, выбрать **STDIO** и указать ту же команду Node.js. После сохранения нажмите **Restart**.

Подробности и вариант `config.toml` находятся в [MCP_SETUP.md](MCP_SETUP.md).

## 6. Проверить подключение

1. Оставьте Unity-проект открытым и дождитесь завершения компиляции.
2. Перезапустите локальный клиент Codex после добавления MCP server.
3. В Codex откройте список MCP servers (`/mcp` там, где он поддерживается) и убедитесь, что `unityCodexHub` активен.
4. Попросите:

```text
Проверь подключение к Unity через unity_status и сообщи название проекта, текущую сцену и isCompiling.
```

Ожидается `connected: true`. Если `connected: false`, смотрите раздел «Диагностика» ниже.

## 7. Первый end-to-end тест

Сначала один раз сохраните активную сцену вручную в Unity, чтобы у неё был путь `.unity`. Затем отправьте Codex:

```text
Проверь подключение к Unity, покажи текущую иерархию, создай пустой объект CodexTest в корне сцены и сохрани сцену.
```

Ожидаемая последовательность tools:

1. `unity_status`
2. `unity_get_hierarchy`
3. `unity_create_game_object` с `name: "CodexTest"`
4. `unity_save_scene`

Объект `CodexTest` должен сразу появиться в Unity Hierarchy. Нажмите `Cmd+Z`: создание должно отмениться, а сцена снова станет изменённой. Это проверяет реальный Unity Undo.

## Диагностика

### `UNITY_NOT_CONNECTED`

Проверьте, что package виден в Unity Package Manager и компиляция закончилась. Затем:

```bash
lsof -nP -iTCP:17891 -sTCP:LISTEN
tail -n 100 "$UNITY_CODEX_HUB_DIR/logs/hub.log"
```

Если порт никто не слушает, проверьте MCP server через `codex mcp list` и перезапустите локальный клиент Codex.

### `EADDRINUSE`

Другой Hub уже слушает этот порт. Обычно это происходит, если диагностический `node dist/src/index.js` остался запущен, а Codex пытается запустить второй процесс. Завершите ручной процесс сочетанием `Ctrl+C`.

### Unity постоянно пишет `waiting for Hub`

Hub не запущен или порт отличается. Сверьте оба файла:

- `<unity-codex-hub>/config.json`
- `<UnityProject>/ProjectSettings/UnityCodexHub.json`, если он существует.

### `UNITY_COMPILING` или disconnect во время компиляции

Это ожидаемо при Domain Reload. Вызовите `unity_wait_for_ready`: Hub дождётся завершения компиляции и автоматического переподключения Bridge. Затем проверьте состояние перед повтором неидемпотентной операции.

### `COMMAND_FAILED` при сохранении

Если сцена новая и `scenePath` пуст, используйте `unity_save_scene_as` с новым путём `Assets/.../*.unity`. После этого `unity_save_scene` сможет сохранять её без диалога.

Расширенная диагностика: [TROUBLESHOOTING.md](TROUBLESHOOTING.md). Полный каталог инструментов: [TOOLS.md](TOOLS.md).
