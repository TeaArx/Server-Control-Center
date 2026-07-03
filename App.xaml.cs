using System.Windows;
using Microsoft.EntityFrameworkCore;
using ServerControlCenter.Data;

namespace ServerControlCenter;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        using var db = new AppDbContext();
        db.Database.Migrate();
    }
}
