using ServerControlCenter.Services;
using Xunit;

namespace ServerControlCenter.UnitTests;

public sealed class ApplicationRulesTests
{
    [Theory]
    [InlineData("rm -rf /var/www")]
    [InlineData("sudo shutdown -h now")]
    [InlineData("echo ok && mkfs.ext4 /dev/sdb1")]
    [InlineData("dd if=/dev/zero of=/dev/sda")]
    [InlineData("systemctl restart nginx")]
    public void RequiresConfirmationForDangerousCommands(string command)
    {
        Assert.True(ApplicationRules.RequiresCommandConfirmation(command));
    }

    [Theory]
    [InlineData("echo firmware")]
    [InlineData("printf 'rebooting is disabled'")]
    [InlineData("ls -la /var/www")]
    public void DoesNotUseUnsafeSubstringMatching(string command)
    {
        Assert.False(ApplicationRules.RequiresCommandConfirmation(command));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/etc/")]
    [InlineData("/var/../usr")]
    public void ProtectsSystemRoots(string path)
    {
        Assert.True(ApplicationRules.IsProtectedRemotePath(path));
    }

    [Fact]
    public void AllowsApplicationSubdirectories()
    {
        Assert.False(ApplicationRules.IsProtectedRemotePath("/var/www/my-app"));
    }
}
