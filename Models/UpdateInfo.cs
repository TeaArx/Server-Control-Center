namespace ServerControlCenter.Models;

public sealed record UpdateInfo(
    Version Version,
    Uri InstallerUrl,
    Uri ChecksumUrl,
    Uri ReleasePageUrl,
    string InstallerFileName);