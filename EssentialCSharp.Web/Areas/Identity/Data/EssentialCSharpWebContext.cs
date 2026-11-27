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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Identity checks RequireUniqueEmail with a SELECT before a separate INSERT, so concurrent registrations
        // can both pass the check. Enforce uniqueness in the database too: a second row with the same
        // NormalizedEmail would make every FindByEmailAsync for that address throw.
        builder.Entity<EssentialCSharpWebUser>()
            .HasIndex(u => u.NormalizedEmail)
            .HasDatabaseName("EmailIndex")
            .IsUnique()
            .HasFilter("[NormalizedEmail] IS NOT NULL");
    }
}
