using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Models;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EssentialCSharp.Web.Data;

public class EssentialCSharpWebContext(DbContextOptions<EssentialCSharpWebContext> options)
    : IdentityDbContext<EssentialCSharpWebUser>(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;
    public DbSet<McpApiToken> McpApiTokens { get; set; } = null!;
    public DbSet<ReadingActivity> ReadingActivities { get; set; } = null!;
    public DbSet<UserReadingProfile> UserReadingProfiles { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<UserReadingProfile>(entity =>
        {
            entity.HasKey(e => e.UserId);
            entity.HasOne(e => e.User)
                  .WithOne()
                  .HasForeignKey<UserReadingProfile>(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ReadingActivity>(entity =>
        {
            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
