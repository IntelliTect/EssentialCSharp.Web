using EssentialCSharp.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EssentialCSharp.Web.Areas.Identity.Data;

/// <summary>
/// The Entity Framework user store, except that a save rejected by the unique <c>EmailIndex</c> (two requests
/// that both passed Identity's "email already taken" check at the same moment) returns the usual
/// <see cref="IdentityErrorDescriber.DuplicateEmail"/> error instead of throwing and failing the request with a 500.
/// </summary>
public sealed class UniqueEmailUserStore(EssentialCSharpWebContext context, IdentityErrorDescriber? describer = null)
    : UserOnlyStore<EssentialCSharpWebUser, EssentialCSharpWebContext>(context, describer)
{
    public override async Task<IdentityResult> CreateAsync(EssentialCSharpWebUser user, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.CreateAsync(user, cancellationToken);
        }
        catch (DbUpdateException) when (user.NormalizedEmail is not null)
        {
            Context.Entry(user).State = EntityState.Detached;
            if (!await IsEmailUsedByAnotherUserAsync(user, cancellationToken))
            {
                throw;
            }
            return IdentityResult.Failed(ErrorDescriber.DuplicateEmail(user.Email ?? string.Empty));
        }
    }

    public override async Task<IdentityResult> UpdateAsync(EssentialCSharpWebUser user, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.UpdateAsync(user, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException && user.NormalizedEmail is not null)
        {
            string attemptedEmail = user.Email ?? string.Empty;
            string? attemptedNormalizedEmail = user.NormalizedEmail;

            // Undo the rejected change so the tracked entity doesn't break later saves in this request.
            Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<EssentialCSharpWebUser> entry = Context.Entry(user);
            entry.CurrentValues.SetValues(entry.OriginalValues);
            entry.State = EntityState.Unchanged;

            if (!await IsEmailUsedByAnotherUserAsync(user.Id, attemptedNormalizedEmail, cancellationToken))
            {
                throw;
            }
            return IdentityResult.Failed(ErrorDescriber.DuplicateEmail(attemptedEmail));
        }
    }

    private Task<bool> IsEmailUsedByAnotherUserAsync(EssentialCSharpWebUser user, CancellationToken cancellationToken) =>
        IsEmailUsedByAnotherUserAsync(user.Id, user.NormalizedEmail, cancellationToken);

    private Task<bool> IsEmailUsedByAnotherUserAsync(string userId, string? normalizedEmail, CancellationToken cancellationToken) =>
        normalizedEmail is null
            ? Task.FromResult(false)
            : Users.AnyAsync(u => u.NormalizedEmail == normalizedEmail && u.Id != userId, cancellationToken);
}
