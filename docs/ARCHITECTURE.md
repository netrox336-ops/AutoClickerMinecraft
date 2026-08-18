# Архитектура Auto Clicker 3.0.1

## Слои

| Слой | Ответственность |
|---|---|
| WPF UI | навигация, темы, анимации, настройки |
| GlobalInputHook | WH_KEYBOARD_LL/WH_MOUSE_LL, capture, pass-through hotkeys |
| ClickEngine | два worker, интервалы, статистика, Panic |
| SettingsStore | schema 5, migration, atomic save, recovery |
| OverlayWindow | HUD, click-through, монитор, DPI, drag mode |
| MinecraftBorderlessService | detect, consent, borderless, restore |
| OS services | startup, tray, session lock, diagnostics, updates |

## Safety invariants

1. Один канал имеет максимум один живой worker.
2. LMB и RMB не разделяют stop-сигнал или интервал.
3. Каждый click отправляется одним массивом `down + up`.
4. Каждый выход worker заканчивается отдельным `up`.
5. Panic отправляет `up` до и после join.
6. Собственные события помечены `dwExtraInfo` и игнорируются hook.
7. Инъецированные события драйверов не запускают хоткеи.
8. Panic получает приоритет при выборе совпадающего бинда.
9. Повреждённая конфигурация не может отключить Panic.
10. Overlay никогда не становится интерактивным вне edit mode.
11. Изменённое окно Minecraft сохраняет достаточно состояния для восстановления.
12. Обычные хоткеи никогда не блокируют исходное событие игры; подавление применяется только в диалоге захвата нового бинда.

## Потоки

- WPF Dispatcher: UI, hooks, настройки и координация;
- AutoClicker.LMB: только LMB SendInput schedule;
- AutoClicker.RMB: только RMB SendInput schedule;
- HttpClient: асинхронная проверка GitHub Releases.

В idle click-worker отсутствуют. UI использует три редких DispatcherTimer: 250 мс для статистики/reconciliation, 600 мс debounce сохранения и 900 мс для Minecraft detection.

## Интервалы

Worker хранит абсолютный deadline на `Stopwatch`:

`deadline += intervalTicks`

Это предотвращает накопление drift от времени выполнения `SendInput`. Если поток отстал больше одного интервала, deadline безопасно пересинхронизируется. Ожидание блокирующее, без busy-spin, а worker работает с обычным приоритетом и не отнимает время у рендера игры.

## Settings schema 5

Главные изменения:

- `LeftIntervalMs`;
- `RightIntervalMs`;
- совместимость со старыми suppression flags с их последующим игнорированием;
- WPF theme/window settings;
- расширенные overlay settings;
- Minecraft consent;
- QoL/update settings.

## Fullscreen boundary

Приложение не пытается рисовать поверх exclusive swap chain. После явного согласия Minecraft получает маркированный F11 через обычный `SendInput` и переводится в compositor-managed borderless window. Затем overlay повторно устанавливается в `TOPMOST`. DLL-инъекция, чтение памяти и вмешательство в рендер отсутствуют.
