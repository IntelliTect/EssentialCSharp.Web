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
    public DbSet<UserReadingProfile> UserReadingProfiles { get; set; } = null!;
}
