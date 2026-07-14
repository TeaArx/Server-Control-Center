# Server Control Center

Desktop application for managing Linux servers over SSH and SFTP.

Настольное приложение для управления Linux-серверами по SSH и SFTP.

[Русский](#русский) | [English](#english)

---

## Русский

### О проекте

Server Control Center — Windows-приложение на WPF для повседневного администрирования Linux-серверов. Оно объединяет профили подключений, SSH-терминал, мониторинг ресурсов, просмотр журналов, сохранённые команды и работу с удалёнными файлами через SFTP в одном интерфейсе.

Проект находится в активной разработке. Актуальный код разработки расположен в ветке `dev`.

### Возможности

- Управление профилями серверов: создание, редактирование, удаление, поиск, группы и избранное.
- Подключение по SSH с паролем или приватным ключом.
- Проверка доступности одного сервера или всех сохранённых серверов.
- Интерактивный SSH-терминал и запуск отдельных команд.
- Подтверждение перед выполнением потенциально опасных команд.
- Сохранённые команды: категории, сортировка, редактирование и избранное.
- Мониторинг CPU, нагрузки, памяти, диска, сети, времени работы и процессов.
- Автоматическое обновление мониторинга с интервалом от 5 до 3600 секунд.
- Просмотр последних 100 строк удалённого журнала с готовыми путями для Nginx, syslog и auth.log.
- SFTP-файловый менеджер: навигация, загрузка, скачивание, переименование, создание папок и рекурсивное удаление.
- Встроенное редактирование удалённых текстовых файлов с резервным копированием перед сохранением.
- Журнал действий приложения.
- Настройки языка, мониторинга, папки SFTP и пути к журналу.
- Русский и английский интерфейс.
- Поддержка Per-Monitor V2 DPI и длинных путей Windows.

### Технологии

| Область | Технология |
|---|---|
| Платформа | .NET 8, WPF |
| Архитектура интерфейса | MVVM, CommunityToolkit.Mvvm |
| SSH и SFTP | SSH.NET |
| Локальное хранилище | SQLite, Entity Framework Core |
| Иконки | MahApps.Metro.IconPacks.Material |
| Диалоги Windows | Ookii.Dialogs.Wpf |
| Тестирование | xUnit, Docker Compose |

### Системные требования

Для запуска и разработки:

- Windows с поддержкой WPF;
- .NET 8 SDK;
- доступ к Linux-серверу по SSH;
- учётная запись с паролем или поддерживаемым приватным SSH-ключом.

Для интеграционных тестов дополнительно нужны PowerShell, Docker Desktop или совместимый Docker Engine с Compose и `ssh-keygen` в `PATH`.

### Быстрый запуск

```powershell
git clone https://github.com/TeaArx/Server-Control-Center.git
cd Server-Control-Center
git checkout dev
dotnet restore
dotnet run --project ServerControlCenter.csproj
```

При первом запуске Entity Framework Core применит миграции и создаст локальную базу данных.

### Сборка и публикация

```powershell
dotnet build ServerControlCenter.sln --configuration Release
dotnet publish ServerControlCenter.csproj --configuration Release --runtime win-x64 --self-contained false --output publish
```

Для запуска опубликованной framework-dependent версии на компьютере должен быть установлен .NET 8 Desktop Runtime.

### Использование

1. Добавьте сервер.
2. Укажите название, хост или IP-адрес, SSH-порт, имя пользователя и способ аутентификации.
3. Проверьте SSH-подключение и сохраните профиль.
4. Выберите сервер в боковой панели.
5. Используйте терминал, мониторинг, журналы, сохранённые команды или файловый менеджер.

Некоторые данные мониторинга зависят от стандартных Linux-утилит на удалённой системе. У пользователя SSH должны быть права на выполнение нужных команд и доступ к выбранным файлам и журналам.

### Данные и безопасность

База данных хранится локально:

```text
%LOCALAPPDATA%\ServerControlCenter\server_control_center.db
```

В ней находятся профили серверов, сохранённые команды, настройки, профиль пользователя и журнал действий. Код проекта предусматривает защиту паролей через Windows DPAPI в контексте текущего пользователя. Путь к приватному ключу сохраняется в профиле, а сам ключ в базу данных не копируется.

Рекомендации:

- используйте отдельные SSH-ключи и минимально необходимые права;
- не добавляйте приватные ключи и локальную базу данных в Git;
- проверяйте выбранный сервер перед выполнением административных команд;
- создавайте резервные копии важных удалённых файлов.

### Тесты

```powershell
dotnet restore ServerControlCenter.sln
dotnet test ServerControlCenter.sln
```

Интеграционные SSH/SFTP-тесты запускаются в изолированном Docker-контейнере:

```powershell
.\tests\ServerControlCenter.IntegrationTests\run-integration-tests.ps1
```

Скрипт создаёт временную пару ключей, запускает тестовый OpenSSH-сервер на `127.0.0.1:2222`, выполняет тесты и удаляет контейнер вместе с временными файлами.

### Структура проекта

```text
Server-Control-Center/
├── Assets/                         Ресурсы и иконка приложения
├── Data/                           Контекст SQLite и EF Core
├── Migrations/                     Миграции базы данных
├── Models/                         Модели данных и состояния UI
├── Services/                       SSH, SFTP, данные, локализация и правила
├── ViewModels/                     Логика представления и команды
├── Views/                          Дополнительные WPF-окна
├── tests/
│   └── ServerControlCenter.IntegrationTests/
├── App.xaml                        Глобальные ресурсы и стили
├── MainWindow.xaml                 Основной интерфейс
├── ServerControlCenter.csproj      Основной проект
└── ServerControlCenter.sln         Решение
```

### Архитектура

- Представления WPF используют привязки к ViewModel.
- `MainViewModel` координирует серверы, команды, мониторинг, журналы и SFTP-операции.
- `SshService` отвечает за SSH shell, выполнение команд и операции SFTP.
- `DashboardDataService` работает с настройками, профилем и журналом действий.
- `AppDbContext` хранит локальные данные в SQLite.
- `LocalizationService` предоставляет русские и английские строки интерфейса.

### Участие в разработке

1. Создайте отдельную ветку от `dev`.
2. Внесите небольшие, логически связанные изменения.
3. Проверьте сборку и тесты.
4. Откройте pull request в `dev` с описанием изменений и способа проверки.

Не добавляйте в коммиты пароли, приватные ключи, локальные базы данных и другие секреты.

### Лицензия

В репозитории пока нет файла лицензии. До его добавления стандартные авторские права сохраняются за владельцем проекта, а разрешение на использование, изменение и распространение явно не предоставлено.

---

## English

### About

Server Control Center is a Windows WPF application for day-to-day Linux server administration. It combines connection profiles, an SSH terminal, resource monitoring, log viewing, saved commands, and remote file operations over SFTP in a single interface.

The project is under active development. The latest development code is available in the `dev` branch.

### Features

- Server profile management: create, edit, delete, search, group, and mark favorites.
- SSH authentication with a password or private key.
- Connection checks for one server or all saved servers.
- Interactive SSH terminal and one-shot command execution.
- Confirmation before potentially dangerous commands are executed.
- Saved commands with categories, ordering, editing, and favorites.
- CPU, load, memory, disk, network, uptime, and process monitoring.
- Automatic monitoring refresh with a configurable interval from 5 to 3600 seconds.
- Viewing the last 100 lines of remote logs, with presets for Nginx, syslog, and auth.log.
- SFTP file manager with navigation, upload, download, rename, directory creation, and recursive deletion.
- Built-in remote text file editor with backup creation before saving.
- Application activity log.
- Settings for language, monitoring, default SFTP directory, and default log path.
- Russian and English user interface.
- Per-Monitor V2 DPI awareness and Windows long-path support.

### Technology stack

| Area | Technology |
|---|---|
| Platform | .NET 8, WPF |
| UI architecture | MVVM, CommunityToolkit.Mvvm |
| SSH and SFTP | SSH.NET |
| Local storage | SQLite, Entity Framework Core |
| Icons | MahApps.Metro.IconPacks.Material |
| Windows dialogs | Ookii.Dialogs.Wpf |
| Testing | xUnit, Docker Compose |

### Requirements

For development and regular use:

- Windows with WPF support;
- .NET 8 SDK;
- network access to a Linux server over SSH;
- an account with a password or a supported private SSH key.

Integration tests additionally require PowerShell, Docker Desktop or a compatible Docker Engine with Compose, and `ssh-keygen` in `PATH`.

### Quick start

```powershell
git clone https://github.com/TeaArx/Server-Control-Center.git
cd Server-Control-Center
git checkout dev
dotnet restore
dotnet run --project ServerControlCenter.csproj
```

On first launch, Entity Framework Core applies the migrations and creates the local database.

### Build and publish

```powershell
dotnet build ServerControlCenter.sln --configuration Release
dotnet publish ServerControlCenter.csproj --configuration Release --runtime win-x64 --self-contained false --output publish
```

The .NET 8 Desktop Runtime must be installed on a target machine to run this framework-dependent build.

### Usage

1. Add a server.
2. Enter a name, host or IP address, SSH port, user name, and authentication method.
3. Test the SSH connection and save the profile.
4. Select the server in the sidebar.
5. Use the terminal, monitoring, logs, saved commands, or file manager.

Some monitoring data depends on standard Linux utilities installed on the remote system. The SSH user must have permission to run the required commands and access the selected files and logs.

### Data and security

The database is stored locally at:

```text
%LOCALAPPDATA%\ServerControlCenter\server_control_center.db
```

It contains server profiles, saved commands, settings, the local user profile, and activity records. The project code protects passwords with Windows DPAPI in the current-user scope. A profile stores only the private key path; it does not copy the key into the database.

Recommendations:

- use dedicated SSH keys and least-privilege accounts;
- never commit private keys or the local database;
- verify the selected target before running administrative commands;
- back up important remote files.

### Tests

```powershell
dotnet restore ServerControlCenter.sln
dotnet test ServerControlCenter.sln
```

Run the SSH/SFTP integration suite in an isolated Docker container:

```powershell
.\tests\ServerControlCenter.IntegrationTests\run-integration-tests.ps1
```

The script creates a temporary key pair, starts a test OpenSSH server on `127.0.0.1:2222`, runs the tests, and removes the container and temporary files.

### Project structure

```text
Server-Control-Center/
├── Assets/                         Application resources and icon
├── Data/                           SQLite and EF Core context
├── Migrations/                     Database migrations
├── Models/                         Data and UI state models
├── Services/                       SSH, SFTP, data, localization, and rules
├── ViewModels/                     Presentation logic and commands
├── Views/                          Additional WPF windows
├── tests/
│   └── ServerControlCenter.IntegrationTests/
├── App.xaml                        Global resources and styles
├── MainWindow.xaml                 Main user interface
├── ServerControlCenter.csproj      Main project
└── ServerControlCenter.sln         Solution
```

### Architecture

- WPF views use data binding to ViewModels.
- `MainViewModel` coordinates servers, commands, monitoring, logs, and SFTP operations.
- `SshService` handles the SSH shell, command execution, and SFTP operations.
- `DashboardDataService` manages settings, the local profile, and activity records.
- `AppDbContext` persists local data in SQLite.
- `LocalizationService` provides Russian and English UI strings.

### Contributing

1. Create a dedicated branch from `dev`.
2. Keep changes small and logically focused.
3. Verify the build and tests.
4. Open a pull request into `dev` and describe both the change and the verification steps.

Do not commit passwords, private keys, local databases, or other secrets.

### License

The repository does not currently contain a license file. Until a license is added, standard copyright restrictions apply and no permission to use, modify, or distribute the project is explicitly granted.
