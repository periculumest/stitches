using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace StitchHelper;

public interface ICurrentUserContext { Guid UserId { get; } }
public sealed class CurrentUserContext(IHttpContextAccessor accessor) : ICurrentUserContext
{
    public Guid UserId => accessor.HttpContext?.User.Identity?.IsAuthenticated == true &&
        Guid.TryParse(accessor.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw new UserError("Sign in to continue.", 401);
}

public sealed class GoogleIdentityResolver(StitchDbContext db, UserManager<ApplicationUser> users)
{
    public async Task<ApplicationUser> Resolve(ExternalLoginInfo info)
    {
        if (info.LoginProvider != "Google" || string.IsNullOrWhiteSpace(info.ProviderKey)) throw new UserError("Google sign-in could not be verified.", 401);
        // Serializes simultaneous first callbacks for the same provider subject across instances.
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"Google:" + info.ProviderKey}, 0))");
        var user = await users.FindByLoginAsync("Google", info.ProviderKey);
        if (user is null)
        {
            var id = Guid.NewGuid();
            user = new ApplicationUser { Id = id, UserName = id.ToString("N"), Email = info.Principal.FindFirstValue(ClaimTypes.Email), DisplayName = info.Principal.FindFirstValue(ClaimTypes.Name) ?? "Stitcher" };
            Ensure(await users.CreateAsync(user));
            Ensure(await users.AddLoginAsync(user, info));
        }
        else
        {
            // Email is a profile snapshot, never an identity lookup or account-linking key.
            user.Email = info.Principal.FindFirstValue(ClaimTypes.Email);
            user.DisplayName = info.Principal.FindFirstValue(ClaimTypes.Name) ?? user.DisplayName;
            Ensure(await users.UpdateAsync(user));
        }
        await transaction.CommitAsync();
        return user;
    }
    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded) throw new UserError("We couldn't finish creating your session. Please try Google sign-in again.", 503);
    }
}

public static class AuthenticationEndpoints
{
    public static void MapStitchAuthentication(this WebApplication app)
    {
        app.MapGet("/auth/google", async (HttpContext context, SignInManager<ApplicationUser> signIn, IAuthenticationSchemeProvider schemes) =>
        {
            if (await schemes.GetSchemeAsync("Google") is null) throw new UserError("Google sign-in is not configured yet.", 503);
            var properties = signIn.ConfigureExternalAuthenticationProperties("Google", "/auth/callback");
            return Results.Challenge(properties, ["Google"]);
        }).AllowAnonymous();
        app.MapGet("/auth/callback", async (HttpContext context, SignInManager<ApplicationUser> signIn, GoogleIdentityResolver resolver) =>
        {
            var info = await signIn.GetExternalLoginInfoAsync();
            if (info is null) return Results.LocalRedirect("/?signin=failed");
            var user = await resolver.Resolve(info);
            await context.SignOutAsync(IdentityConstants.ExternalScheme);
            await signIn.SignInAsync(user, isPersistent: true, authenticationMethod: "Google");
            return Results.LocalRedirect("/");
        }).AllowAnonymous();
        app.MapPost("/auth/logout", async (SignInManager<ApplicationUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization();
        app.MapGet("/api/me", async (ICurrentUserContext current, UserManager<ApplicationUser> users) =>
        {
            var user = await users.FindByIdAsync(current.UserId.ToString()) ?? throw new UserError("Sign in again.", 401);
            return new { user.Id, user.DisplayName, user.Email };
        }).RequireAuthorization();
        app.MapGet("/api/antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
            new { token = antiforgery.GetAndStoreTokens(context).RequestToken }).RequireAuthorization();
    }
}
