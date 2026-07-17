namespace ServerControlCenter.Models;

public sealed record RemoteDeletePreview(string Path, int FileCount, int DirectoryCount, long TotalBytes);
