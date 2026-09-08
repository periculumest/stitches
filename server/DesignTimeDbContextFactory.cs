using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StitchHelper;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<StitchDbContext>
{
    public StitchDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<StitchDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__StitchHelper") ?? "Host=localhost;Port=5433;Database=stitch_helper;Username=stitch;Password=local-development-only").Options);
}
