using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LabKom.Data;

/// <summary>
/// Dipakai oleh `dotnet ef migrations add` saat build-time.
/// Connection string sebenarnya di-resolve runtime dari appsettings Teacher.
/// </summary>
public class LabKomDbContextFactory : IDesignTimeDbContextFactory<LabKomDbContext>
{
    public LabKomDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<LabKomDbContext>()
            .UseSqlite("Data Source=labkom-design.db")
            .Options;
        return new LabKomDbContext(options);
    }
}
