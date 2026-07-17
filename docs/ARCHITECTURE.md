# Архитектура Server Control Center

## UI-композиция

`MainWindow` отвечает только за компоновку приложения, глобальные shortcuts,
подтверждения опасных действий и оформление нативной рамки Windows.

Функциональные представления находятся в `Views/Features`:

- `ServersSidebarView` — группы и список серверов;
- `DashboardHeaderView` — выбранный сервер, поиск и глобальные действия;
- `MonitoringSummaryView` — карточки CPU, RAM, диска и uptime;
- `TerminalView` — интерактивный терминал и локальная история ввода;
- `OperationsView` — навигационный контейнер функциональных вкладок;
- `RemoteFilesView` — SFTP и удалённые файлы;
- `LogsView` — просмотр удалённого журнала;
- `CommandsView` — сохранённые команды;
- `ServerInspectorView` — сведения о сервере и последние события;
- `OverlayHostView` — настройки, избранное и activity log.

UI-specific обработчики находятся рядом со своим представлением. В `MainWindow.xaml.cs`
нельзя добавлять обработчики отдельных функций.

## ViewModel-модули

`MainViewModel.cs` содержит состояние композиции, lifecycle, переключение выбранного
сервера и общие cross-feature операции. Функциональная логика разделена на partial-модули:

- `MainViewModel.Servers.cs`;
- `MainViewModel.Monitoring.cs`;
- `MainViewModel.Commands.cs`;
- `MainViewModel.Terminal.cs`;
- `MainViewModel.RemoteFiles.cs`.

Partial-модули сохраняют единый binding-контракт WPF и позволяют проводить рефакторинг
без массового изменения XAML. Новый функционал должен добавляться в соответствующий
модуль либо в отдельную ViewModel, но не в основной файл.

Если модулю перестанет требоваться общее состояние выбранного сервера, его можно вынести
в самостоятельную дочернюю ViewModel и подключить как свойство композиции.

## Ресурсы

`App.xaml` подключает корневой словарь коллекций. Словари образуют явную цепочку
зависимостей `Theme → TypographyAndLayout → InputControls → CollectionControls`, чтобы
`StaticResource` разрешался при загрузке каждого отдельного `ResourceDictionary`:

- `Resources/Theme.xaml` — цвета и кисти;
- `Resources/TypographyAndLayout.xaml` — типографика, окна, панели и карточки;
- `Resources/InputControls.xaml` — кнопки, поля ввода, меню и прокрутка;
- `Resources/CollectionControls.xaml` — списки, вкладки, таблицы и progress bar.

Стили отдельного feature-контрола следует размещать в его локальных ресурсах. Глобальный
словарь используется только для повторно применяемых visual tokens и controls.

## Правила изменений

1. UI-событие обрабатывается в code-behind соответствующего `UserControl` и вызывает команду.
2. Бизнес- и инфраструктурная логика не размещается в code-behind.
3. Смена сервера отменяет старые операции; feature-код обязан учитывать cancellation/version.
4. Изменение данных сопровождается migration test.
5. Новое правило безопасности сопровождается unit-тестом.
