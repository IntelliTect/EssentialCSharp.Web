using System.ComponentModel.DataAnnotations;
using EssentialCSharp.Web.Areas.Identity.Data;
using Microsoft.EntityFrameworkCore;

namespace EssentialCSharp.Web.Models;

[Index(nameof(TokenHash), IsUnique = true)]
[Index(nameof(UserId))]
public class McpApiToken
{
    public Guid Id { get; set; }
    public required string UserId { get; set; }
    [MaxLength(256)]
    public required string Name { get; set; }
    // SHA-256 hash stored as varbinary(32) — avoids SQL Server case-insensitive collation issues
    [MaxLength(32)]
    public required byte[] TokenHash { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public long UsageCount { get; set; }
    public EssentialCSharpWebUser? User { get; set; }
}

