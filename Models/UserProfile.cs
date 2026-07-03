namespace ServerControlCenter.Models;

public class UserProfile
{
    public int Id { get; set; }

    public string DisplayName { get; set; } = "Admin";

    public string Initials { get; set; } = "AD";

    public string Role { get; set; } = "Server Administrator";

    public string Email { get; set; } = "";
}
