# Changelog

## 3.0.1 — Hotfix

### Исправления

- убрана системная голубая подсветка невидимой области WPF-слайдеров;
- снижена нагрузка click-worker: удалён busy-spin и возвращён нормальный приоритет потоков;
- устранены сильные просадки Minecraft при работе Hold на малых интервалах;
- события назначенных хоткеев больше не подавляются и всегда передаются в игру;
- Minecraft fullscreen теперь переключается реальным маркированным F11 через `SendInput`, после чего HUD повторно поднимается в `TOPMOST`;
- сохранён безопасный compositor/borderless-подход без DLL-инъекции;
- переработана компактная компоновка Hotkey Studio;
- удалены модули физического ввода и подавления событий;
- заменён перегруженный логотип в сайдбаре на лаконичный знак мыши;
- источник обновлений жёстко закреплён за `netrox336-ops/AutoClickerMinecraft`;
- все тексты релизов, документация и задачи репозитория переведены на русский язык.

## 3.0.0 — Product Release

### Engine

- отдельные интервалы LMB/RMB `1–1000 мс`;
- независимые worker и счётчики фактически доставленных кликов;
- drift-compensated scheduling;
- единый гарантированный release-path;
- double button-up на Panic;
- reconciliation пропущенного release через физическое состояние;
- уменьшенное потребление CPU в ожидании.

### Hotkeys

- независимые Mouse 3/4/5 и клавиатурные бинды;
- отдельные Hold/Toggle/Press;
- отдельное подавление каждого хоткея;
- тест без запуска кликов;
- визуальное состояние нажатия;
- отображение физического ввода;
- проверка конфликтов;
- смена каналов вместе с интервалами;
- hook health indicator.

### UI

- полный переход WinForms → WPF;
- Mica/Acrylic;
- угольно-чёрная дизайн-система;
- Red/Blue/Purple/Emerald/Amber themes;
- закруглённые окна, карточки и кнопки;
- полностью тёмные шаблоны Button/Toggle/TextBox/ComboBox/Slider/ScrollBar;
- hover/press/toggle/page-transition анимации;
- настройка плотности поверхности;
- Reduced Motion;
- исправлена навигация между Управлением, Хоткеями, Overlay и Настройками;
- Per-Monitor DPI V2.

### Overlay

- OFF/LMB/RMB/BOTH;
- фактический CPS и отдельные интервалы;
- compact/extended;
- масштаб и прозрачность;
- четыре угла;
- произвольное перетаскивание;
- click-through/no-activate;
- активный монитор;
- запоминаемый Minecraft borderless workflow;
- восстановление исходного окна.

### Quality of Life

- запуск с Windows;
- запуск свёрнутым;
- системный трей;
- Pause on Lock;
- настраиваемое закрытие;
- auto-save и backup;
- import/export/reset;
- portable mode;
- Inno Setup installer;
- GitHub Releases updater;
- обезличенная диагностика;
- новая многоразмерная иконка;
- расширенный self-test.

## 2.1.0 — Core

- два независимых канала LMB/RMB;
- Mouse 4/5;
- Hold/Toggle/Press;
- базовый overlay;
- удалены Ultra/Burst/Random/Benchmark.
