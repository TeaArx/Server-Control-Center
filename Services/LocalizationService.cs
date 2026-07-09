using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ServerControlCenter.Services;

public sealed class LocalizationService : INotifyPropertyChanged
{
    private readonly Dictionary<string, Dictionary<string, string>> resources = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Activity"] = "Activity",
            ["ActivityLog"] = "Activity Log",
            ["AddCommand"] = "Add Command",
            ["AddServer"] = "Add Server",
            ["AllServers"] = "All Servers",
            ["AutoOff"] = "Auto: off",
            ["AutoOn"] = "Auto: on",
            ["Back"] = "Back",
            ["AutoRefreshMonitoring"] = "Auto-refresh monitoring",
            ["Cancel"] = "Cancel",
            ["CheckAllServers"] = "Check all servers",
            ["ClearLog"] = "Clear Log",
            ["Clear"] = "Clear",
            ["ClearTerminal"] = "Clear terminal",
            ["Close"] = "Close",
            ["Commands"] = "Commands",
            ["Confirm"] = "OK",
            ["CreateFolder"] = "Create folder",
            ["DefaultLogPath"] = "Default log path",
            ["DefaultRemoteFolder"] = "Default remote folder",
            ["Delete"] = "Delete",
            ["Details"] = "Details",
            ["DiskIo"] = "Disk I/O",
            ["Download"] = "Download",
            ["Edit"] = "Edit",
            ["Email"] = "Email",
            ["English"] = "English",
            ["Favorite"] = "Favorite",
            ["Favorites"] = "Favorites",
            ["Files"] = "Files",
            ["Folder"] = "Folder",
            ["Group"] = "Group",
            ["Home"] = "Home",
            ["Groups"] = "GROUPS",
            ["Host"] = "IP / Host",
            ["Initials"] = "Initials",
            ["Language"] = "Language",
            ["LoadRoot"] = "Go to root",
            ["Logs"] = "Logs",
            ["MemoryUsage"] = "Memory Usage",
            ["Monitoring"] = "Monitoring",
            ["MonitoringInterval"] = "Monitoring interval, sec.",
            ["MonitoringSettings"] = "Monitoring settings",
            ["More"] = "More",
            ["Name"] = "Name",
            ["NetworkIo"] = "Network I/O",
            ["Notes"] = "Notes",
            ["Open"] = "Open",
            ["Os"] = "OS",
            ["ParentFolder"] = "Parent folder",
            ["Password"] = "Password",
            ["PickPrivateKey"] = "Choose",
            ["Port"] = "Port",
            ["PrivateKey"] = "SSH key",
            ["Profile"] = "Profile",
            ["ProfileStoredLocally"] = "Connection profile is stored locally in SQLite.",
            ["Refresh"] = "Refresh",
            ["RefreshLogs"] = "Refresh logs",
            ["RefreshMonitoring"] = "Refresh monitoring",
            ["Rename"] = "Rename",
            ["RecentLogs"] = "Recent Logs",
            ["Role"] = "Role",
            ["RootFolder"] = "/",
            ["Run"] = "Run",
            ["Russian"] = "Русский",
            ["Save"] = "Save",
            ["SaveProfile"] = "Save Profile",
            ["SaveSettings"] = "Save Settings",
            ["Search"] = "Search",
            ["SearchServers"] = "Search servers",
            ["SelectedFile"] = "Selected file: {0}",
            ["Send"] = "Send",
            ["Servers"] = "SERVERS",
            ["Settings"] = "Settings",
            ["Size"] = "Size",
            ["Ssh"] = "SSH",
            ["TestSsh"] = "Test SSH",
            ["Target"] = "Target",
            ["Time"] = "Time",
            ["Type"] = "Type",
            ["Modified"] = "Modified",
            ["ToggleAutoMonitoring"] = "Toggle auto monitoring",
            ["ToggleFavorite"] = "Toggle favorite",
            ["Upload"] = "Upload",
            ["Uptime"] = "UPTIME",
            ["Username"] = "Login",
            ["ViewAll"] = "View all",
            ["WindowInputTitle"] = "Input",
            ["WindowServerAddTitle"] = "Add server",
            ["WindowServerEditTitle"] = "Edit server",
            ["WindowFileEditorTitle"] = "File editor",

            ["AutoRefreshEvery"] = "Auto-refresh every 30 seconds.",
            ["AutoRefreshStopped"] = "Auto-refresh stopped.",
            ["ChooseCommand"] = "Choose a command.",
            ["ChooseFileForEdit"] = "Choose a file to edit.",
            ["ChooseFileOrFolderForDelete"] = "Choose a file or folder to delete.",
            ["ChooseFileOrFolderForRename"] = "Choose a file or folder to rename.",
            ["ChooseSavedCommand"] = "Choose a saved command.",
            ["ChooseServer"] = "Choose a server.",
            ["ChooseServerFirst"] = "Choose a server first.",
            ["ChooseServerToEdit"] = "Choose a server to edit.",
            ["CommandAdded"] = "Command added",
            ["CommandDeleted"] = "Command deleted",
            ["CommandUpdated"] = "Command updated.",
            ["DangerousCommandCancelled"] = "Dangerous command cancelled.",
            ["DownloadInProgress"] = "Downloading...",
            ["EnterHost"] = "Enter IP or Host.",
            ["EnterLogin"] = "Enter login.",
            ["EnterName"] = "Enter server name.",
            ["EnterPasswordOrKey"] = "Enter password or SSH key.",
            ["EnterPaths"] = "Enter remote and local paths.",
            ["EnterRemoteAndLocalPath"] = "Enter local path and remote path.",
            ["EnterLogPath"] = "Enter log path.",
            ["EnterLocalPath"] = "Enter local path.",
            ["FileLoaded"] = "File loaded.",
            ["FileLoading"] = "Loading file...",
            ["FileMustLoadFirst"] = "The file must be loaded successfully first.",
            ["FileSaving"] = "Saving file...",
            ["FolderCreatePrompt"] = "New folder name:",
            ["FolderCreateTitle"] = "Create folder",
            ["FolderCreating"] = "Creating folder...",
            ["InitializationError"] = "Initialization error: {0}",
            ["LocalFileNotFound"] = "Local file not found: {0}",
            ["LocalFolderNotFound"] = "Local folder not found.",
            ["LogLoading"] = "Loading...",
            ["MonitoringError"] = "Monitoring refresh failed.",
            ["MonitoringLoading"] = "Loading...",
            ["MonitoringRefreshing"] = "Refreshing: {0}",
            ["MonitoringUpdated"] = "Updated: {0}",
            ["NoServerSelected"] = "No server selected.",
            ["OpenFolderFailed"] = "Could not open folder: {0}",
            ["OpenedFolder"] = "Opened folder: {0}",
            ["OpenLocalFolder"] = "Open local folder",
            ["PortRange"] = "Port must be between 1 and 65535.",
            ["ProfileUpdated"] = "Profile updated",
            ["RemoteFilesLoaded"] = "Loaded: {0}",
            ["RenamePrompt"] = "New name:",
            ["RenameTitle"] = "Rename",
            ["Renaming"] = "Renaming...",
            ["RunningCommand"] = "Running command: {0}",
            ["ServerAdded"] = "Server added.",
            ["ServerDeleted"] = "Server deleted: {0}",
            ["ServerNotSelected"] = "Server not selected.",
            ["ServerInspector"] = "Server Inspector",
            ["ServerSaveError"] = "Server save error: {0}",
            ["ServerUpdated"] = "Server updated.",
            ["ServerUpdateError"] = "Server update error: {0}",
            ["SettingsSaved"] = "Settings saved",
            ["TerminalWelcome"] = "Welcome to ServerControl Dashboard.\nChoose a server and run an SSH command.",
            ["TerminalError"] = "Error: {0}",
            ["UploadInProgress"] = "Uploading...",
            ["UploadingFile"] = "Uploading: {0}",
            ["ValidateRemoteNameEmpty"] = "Enter a name.",
            ["ValidateRemoteNameSlash"] = "Name cannot contain '/'."
        },
        ["ru"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Activity"] = "Журнал",
            ["ActivityLog"] = "Журнал действий",
            ["AddCommand"] = "Добавить команду",
            ["AddServer"] = "Добавить сервер",
            ["AllServers"] = "Все серверы",
            ["AutoOff"] = "Авто: выкл",
            ["AutoOn"] = "Авто: вкл",
            ["Back"] = "Назад",
            ["AutoRefreshMonitoring"] = "Автообновление мониторинга",
            ["Cancel"] = "Отмена",
            ["CheckAllServers"] = "Проверить все серверы",
            ["ClearLog"] = "Очистить журнал",
            ["Clear"] = "Очистить",
            ["ClearTerminal"] = "Очистить терминал",
            ["Close"] = "Закрыть",
            ["Commands"] = "Команды",
            ["Confirm"] = "OK",
            ["CreateFolder"] = "Создать папку",
            ["DefaultLogPath"] = "Лог по умолчанию",
            ["DefaultRemoteFolder"] = "Папка сервера по умолчанию",
            ["Delete"] = "Удалить",
            ["Details"] = "Детали",
            ["DiskIo"] = "Диск I/O",
            ["Download"] = "Скачать",
            ["Edit"] = "Редактировать",
            ["Email"] = "Email",
            ["English"] = "English",
            ["Favorite"] = "В избранном",
            ["Favorites"] = "Избранное",
            ["Files"] = "Файлы",
            ["Folder"] = "Папка",
            ["Group"] = "Группа",
            ["Home"] = "Домой",
            ["Groups"] = "ГРУППЫ",
            ["Host"] = "IP / Host",
            ["Initials"] = "Инициалы",
            ["Language"] = "Язык",
            ["LoadRoot"] = "Перейти в корень",
            ["Logs"] = "Логи",
            ["MemoryUsage"] = "Память",
            ["Monitoring"] = "Мониторинг",
            ["MonitoringInterval"] = "Интервал мониторинга, сек.",
            ["MonitoringSettings"] = "Настройки мониторинга",
            ["More"] = "Еще",
            ["Name"] = "Название",
            ["NetworkIo"] = "Сеть I/O",
            ["Notes"] = "Заметки",
            ["Open"] = "Открыть",
            ["Os"] = "ОС",
            ["ParentFolder"] = "Папка выше",
            ["Password"] = "Пароль",
            ["PickPrivateKey"] = "Выбрать",
            ["Port"] = "Порт",
            ["PrivateKey"] = "SSH-ключ",
            ["Profile"] = "Профиль",
            ["ProfileStoredLocally"] = "Профиль подключения хранится локально в SQLite.",
            ["Refresh"] = "Обновить",
            ["RefreshLogs"] = "Обновить логи",
            ["RefreshMonitoring"] = "Обновить мониторинг",
            ["Rename"] = "Переименовать",
            ["RecentLogs"] = "Последние логи",
            ["Role"] = "Роль",
            ["RootFolder"] = "/",
            ["Run"] = "Запуск",
            ["Russian"] = "Русский",
            ["Save"] = "Сохранить",
            ["SaveProfile"] = "Сохранить профиль",
            ["SaveSettings"] = "Сохранить настройки",
            ["Search"] = "Поиск",
            ["SearchServers"] = "Поиск серверов",
            ["SelectedFile"] = "Выбран файл: {0}",
            ["Send"] = "Отправить",
            ["Servers"] = "СЕРВЕРЫ",
            ["Settings"] = "Настройки",
            ["Size"] = "Размер",
            ["Ssh"] = "SSH",
            ["TestSsh"] = "Проверить SSH",
            ["Target"] = "Объект",
            ["Time"] = "Время",
            ["Type"] = "Тип",
            ["Modified"] = "Изменен",
            ["ToggleAutoMonitoring"] = "Включить или выключить автомониторинг",
            ["ToggleFavorite"] = "Добавить или убрать из избранного",
            ["Upload"] = "Загрузить",
            ["Uptime"] = "АПТАЙМ",
            ["Username"] = "Логин",
            ["ViewAll"] = "Открыть",
            ["WindowInputTitle"] = "Ввод",
            ["WindowServerAddTitle"] = "Добавление сервера",
            ["WindowServerEditTitle"] = "Редактирование сервера",
            ["WindowFileEditorTitle"] = "Редактор файла",

            ["AutoRefreshEvery"] = "Автообновление каждые 30 секунд.",
            ["AutoRefreshStopped"] = "Автообновление остановлено.",
            ["ChooseCommand"] = "Выбери команду.",
            ["ChooseFileForEdit"] = "Выбери файл для редактирования.",
            ["ChooseFileOrFolderForDelete"] = "Выбери файл или папку для удаления.",
            ["ChooseFileOrFolderForRename"] = "Выбери файл или папку для переименования.",
            ["ChooseSavedCommand"] = "Выбери сохранённую команду.",
            ["ChooseServer"] = "Выберите сервер.",
            ["ChooseServerFirst"] = "Сначала выбери сервер.",
            ["ChooseServerToEdit"] = "Выбери сервер для редактирования.",
            ["CommandAdded"] = "Команда добавлена",
            ["CommandDeleted"] = "Команда удалена",
            ["CommandUpdated"] = "Команда обновлена.",
            ["DangerousCommandCancelled"] = "Выполнение опасной команды отменено.",
            ["DownloadInProgress"] = "Скачивание...",
            ["EnterHost"] = "Укажи IP или Host.",
            ["EnterLogin"] = "Укажи логин.",
            ["EnterName"] = "Укажи название сервера.",
            ["EnterPasswordOrKey"] = "Укажи пароль или SSH-ключ.",
            ["EnterPaths"] = "Укажи путь на сервере и локальный путь.",
            ["EnterRemoteAndLocalPath"] = "Укажи локальный путь и путь на сервере.",
            ["EnterLogPath"] = "Укажите путь к логу.",
            ["EnterLocalPath"] = "Укажи локальный путь.",
            ["FileLoaded"] = "Файл загружен.",
            ["FileLoading"] = "Загрузка файла...",
            ["FileMustLoadFirst"] = "Сначала файл должен быть успешно загружен.",
            ["FileSaving"] = "Сохранение файла...",
            ["FolderCreatePrompt"] = "Имя новой папки:",
            ["FolderCreateTitle"] = "Создать папку",
            ["FolderCreating"] = "Создание папки...",
            ["InitializationError"] = "Ошибка инициализации: {0}",
            ["LocalFileNotFound"] = "Локальный файл не найден: {0}",
            ["LocalFolderNotFound"] = "Локальная папка не найдена.",
            ["LogLoading"] = "Загрузка...",
            ["MonitoringError"] = "Ошибка обновления мониторинга.",
            ["MonitoringLoading"] = "Загрузка...",
            ["MonitoringRefreshing"] = "Обновление: {0}",
            ["MonitoringUpdated"] = "Обновлено: {0}",
            ["NoServerSelected"] = "Сервер не выбран.",
            ["OpenFolderFailed"] = "Не удалось открыть папку: {0}",
            ["OpenedFolder"] = "Открыта папка: {0}",
            ["OpenLocalFolder"] = "Открыть локальную папку",
            ["PortRange"] = "Порт должен быть от 1 до 65535.",
            ["ProfileUpdated"] = "Профиль обновлён",
            ["RemoteFilesLoaded"] = "Загружено: {0}",
            ["RenamePrompt"] = "Новое имя:",
            ["RenameTitle"] = "Переименовать",
            ["Renaming"] = "Переименование...",
            ["RunningCommand"] = "Выполняется команда: {0}",
            ["ServerAdded"] = "Сервер добавлен.",
            ["ServerDeleted"] = "Сервер удалён: {0}",
            ["ServerNotSelected"] = "Сервер не выбран.",
            ["ServerInspector"] = "Инспектор сервера",
            ["ServerSaveError"] = "Ошибка сохранения сервера: {0}",
            ["ServerUpdated"] = "Сервер обновлён.",
            ["ServerUpdateError"] = "Ошибка обновления сервера: {0}",
            ["SettingsSaved"] = "Настройки сохранены",
            ["TerminalWelcome"] = "Welcome to ServerControl Dashboard.\nВыберите сервер и выполните SSH-команду.",
            ["TerminalError"] = "Ошибка: {0}",
            ["UploadInProgress"] = "Загрузка...",
            ["UploadingFile"] = "Загрузка: {0}",
            ["ValidateRemoteNameEmpty"] = "Укажи имя.",
            ["ValidateRemoteNameSlash"] = "Имя не может содержать '/'."
        }
    };

    private string languageCode = "en";

    public string LanguageCode
    {
        get => languageCode;
        private set
        {
            if (languageCode == value)
            {
                return;
            }

            languageCode = value;
            OnPropertyChanged();
            OnPropertyChanged("Item[]");
        }
    }

    public IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new() { Code = "en", Name = "English" },
        new() { Code = "ru", Name = "Русский" }
    ];

    public string this[string key] => T(key);

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetLanguage(string? code)
    {
        LanguageCode = NormalizeLanguageCode(code);
    }

    public string T(string key)
    {
        if (resources.TryGetValue(LanguageCode, out var current) &&
            current.TryGetValue(key, out var value))
        {
            return value;
        }

        return resources["en"].TryGetValue(key, out var fallback)
            ? fallback
            : key;
    }

    public string Format(string key, params object[] args)
    {
        return string.Format(T(key), args);
    }

    private static string NormalizeLanguageCode(string? code)
    {
        return string.Equals(code, "ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "en";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
