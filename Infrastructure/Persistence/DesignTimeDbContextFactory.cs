using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TrailQuest.Api.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TrailQuestDbContext>
{
    public TrailQuestDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TrailQuestDbContext>()
            .UseSqlite("Data Source=:memory:").Options; // Schema scaffolding never opens the personal database.
        return new TrailQuestDbContext(options);
    }
}
