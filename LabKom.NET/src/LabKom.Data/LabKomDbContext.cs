using LabKom.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LabKom.Data;

public class LabKomDbContext : DbContext
{
    public LabKomDbContext(DbContextOptions<LabKomDbContext> options) : base(options) { }

    public DbSet<StudentRecord> Students => Set<StudentRecord>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<ActivityLog> Activities => Set<ActivityLog>();
    public DbSet<ClassConfig> Configs => Set<ClassConfig>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<StudentRecord>()
            .HasIndex(x => x.Nis)
            .IsUnique();

        b.Entity<Session>()
            .HasIndex(x => new { x.PcName, x.LogoutAt });

        b.Entity<Session>()
            .HasOne(x => x.Student)
            .WithMany()
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<ActivityLog>()
            .HasIndex(x => new { x.PcName, x.At });

        b.Entity<ActivityLog>()
            .HasOne(x => x.Session)
            .WithMany()
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
