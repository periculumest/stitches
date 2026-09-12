using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StitchHelper;

// Test-only cookie generation. This binary is never included in the application container.
var connection = Environment.GetEnvironmentVariable("STITCH_BROWSER_POSTGRES") ?? throw new InvalidOperationException("Set STITCH_BROWSER_POSTGRES to an isolated test database.");
var cs = new NpgsqlConnectionStringBuilder(connection);
if (cs.Database?.StartsWith("stitch_test_", StringComparison.Ordinal) != true) throw new InvalidOperationException("Browser test database name must start with stitch_test_.");
var services = new ServiceCollection(); services.AddLogging();
services.AddDbContext<StitchDbContext>(o => o.UseNpgsql(connection));
services.AddDataProtection().SetApplicationName("StitchHelper").PersistKeysToDbContext<StitchDbContext>();
using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<StitchDbContext>();
db.Database.Migrate(); db.SeedCatalog();
var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = Guid.NewGuid().ToString("N"), DisplayName = "Browser tester", Email = "browser@example.test", SecurityStamp = Guid.NewGuid().ToString("N") };
db.Users.Add(user); db.SaveChanges();
if (Environment.GetEnvironmentVariable("STITCH_TEST_BETA_ADMIN") == "true")
{
    var role = db.Roles.SingleOrDefault(r => r.NormalizedName == "BETAADMIN");
    if (role is null) { role = new IdentityRole<Guid>(BetaService.AdminRole) { Id = Guid.NewGuid(), NormalizedName = "BETAADMIN" }; db.Roles.Add(role); }
    db.UserRoles.Add(new() { UserId = user.Id, RoleId = role.Id }); db.SaveChanges();
}
if (Environment.GetEnvironmentVariable("STITCH_TEST_LEGAL_EDITOR") == "true")
{
    var role = db.Roles.SingleOrDefault(r => r.NormalizedName == "LEGALEDITOR");
    if (role is null) { role = new IdentityRole<Guid>(LegalDocumentService.EditorRole) { Id = Guid.NewGuid(), NormalizedName = "LEGALEDITOR" }; db.Roles.Add(role); }
    db.UserRoles.Add(new() { UserId = user.Id, RoleId = role.Id }); db.SaveChanges();
}
var protector = provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware", IdentityConstants.ApplicationScheme, "v2");
var principal = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, user.Id.ToString()), new(ClaimTypes.Name, user.UserName), new("AspNet.Identity.SecurityStamp", user.SecurityStamp)], IdentityConstants.ApplicationScheme));
var ticket = new AuthenticationTicket(principal, new AuthenticationProperties { IsPersistent = true, IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(1) }, IdentityConstants.ApplicationScheme);
var cookie = new TicketDataFormat(protector).Protect(ticket);
var uri = new Uri(Environment.GetEnvironmentVariable("STITCH_TEST_URL") ?? "http://127.0.0.1:5068");
var output = args.FirstOrDefault() ?? throw new InvalidOperationException("Supply output storage-state filename.");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output, Json.Write(new { cookies = new[] { new { name = "StitchHelper.Session", value = cookie, domain = uri.Host, path = "/", expires = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds(), httpOnly = true, secure = uri.Scheme == "https", sameSite = "Lax" } }, origins = Array.Empty<object>() }));
Console.WriteLine("Isolated browser user and cookie fixture created.");
