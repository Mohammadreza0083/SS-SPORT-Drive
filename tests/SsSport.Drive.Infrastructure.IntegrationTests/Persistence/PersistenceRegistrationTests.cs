using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SsSport.Drive.Application;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Infrastructure.Persistence;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// <c>AddApplication</c> + <c>AddInfrastructure</c> (as in the hosts) wire the Application persistence ports to their EF Core implementations, one per request scope.
/// </summary>
public sealed class PersistenceRegistrationTests
{
    /// <summary>The unit of work and the generic repository resolve, sharing one DbContext within a scope.</summary>
    [Fact]
    public void Unit_of_work_and_repository_resolve_per_scope()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=unused;Username=unused;Password=unused",
            })
            .Build();
        using var provider = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddApplication()
            .AddInfrastructure()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Note, Guid>>();

        unitOfWork.Should().BeOfType<UnitOfWork>();
        scope.ServiceProvider.GetRequiredService<IPagedReader>().Should().BeOfType<PagedReader>();
        repository.Should().BeOfType<Repository<Note, Guid>>();
        scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Should().BeSameAs(scope.ServiceProvider.GetRequiredService<Microsoft.EntityFrameworkCore.DbContext>());
    }
}
