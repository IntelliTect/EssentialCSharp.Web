using EssentialCSharp.Web.Areas.Identity.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EssentialCSharp.Web.Data;

public class EssentialCSharpWebContext(DbContextOptions<EssentialCSharpWebContext> options)
    : IdentityDbContext<EssentialCSharpWebUser>(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<IdentityUserLogin<string>>(login =>
        {
            login.Property(entry => entry.LoginProvider).HasMaxLength(EssentialCSharpWebIdentitySchema.KeyMaxLength);
            login.Property(entry => entry.ProviderKey).HasMaxLength(EssentialCSharpWebIdentitySchema.KeyMaxLength);
        });

        builder.Entity<IdentityUserToken<string>>(token =>
        {
            token.Property(entry => entry.LoginProvider).HasMaxLength(EssentialCSharpWebIdentitySchema.KeyMaxLength);
            token.Property(entry => entry.Name).HasMaxLength(EssentialCSharpWebIdentitySchema.KeyMaxLength);
        });
    }
}
