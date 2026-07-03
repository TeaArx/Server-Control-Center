namespace ServerControlCenter.Models;

public class RemoteFileItem
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public DateTime LastWriteTime { get; set; }

    public string TypeText => IsDirectory ? "Папка" : "Файл";

    public string SizeText
    {
        get
        {
            if (IsDirectory)
            {
                return "";
            }

            if (Size >= 1024 * 1024 * 1024)
            {
                return $"{Size / 1024.0 / 1024.0 / 1024.0:F2} ГБ";
            }

            if (Size >= 1024 * 1024)
            {
                return $"{Size / 1024.0 / 1024.0:F2} МБ";
            }

            if (Size >= 1024)
            {
                return $"{Size / 1024.0:F2} КБ";
            }

            return $"{Size} Б";
        }
    }

    public string LastWriteTimeText => LastWriteTime == default
        ? ""
        : LastWriteTime.ToString("dd.MM.yyyy HH:mm");
}
