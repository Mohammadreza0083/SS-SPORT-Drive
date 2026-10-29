using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Infrastructure.Persistence;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Two editors change the same aggregate at once (D-13): the <c>xmin</c> check refuses the later save, and the unit of
/// work reports it as <see cref="ConcurrencyConflictException"/> (409), not as a raw EF Core error (500).
/// </summary>
/// <param name="fixture">The migrated PostgreSQL database.</param>
public sealed class UnitOfWorkConcurrencyTests(AppDbContextFixture fixture) : IClassFixture<AppDbContextFixture>
{
    /// <summary>The later of two saves on the same player is refused as a conflict, and dispatches no events.</summary>
    [Fact]
    public async Task Later_save_of_the_same_aggregate_is_a_conflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var player = Player.Create(PlayerCategory.Men, DateTimeOffset.UtcNow);
        await using (var creator = fixture.CreateContext())
        {
            creator.Add(player);
            await creator.SaveChangesAsync(cancellationToken);
        }

        await using var first = fixture.CreateContext();
        await using var second = fixture.CreateContext();
        var firstCopy = await first.Set<Player>().SingleAsync(entity => entity.Id == player.Id, cancellationToken);
        var secondCopy = await second.Set<Player>().SingleAsync(entity => entity.Id == player.Id, cancellationToken);
        var firstEvents = new RecordingDomainEventDispatcher();

        secondCopy.ChangeNationality(Nationality.Create("IT"));
        await new UnitOfWork(second, new RecordingDomainEventDispatcher()).SaveChangesAsync(cancellationToken);
        firstCopy.ChangeNationality(Nationality.Create("PL"));
        var save = () => new UnitOfWork(first, firstEvents).SaveChangesAsync(cancellationToken);

        (await save.Should().ThrowAsync<ConcurrencyConflictException>()).WithInnerException<DbUpdateConcurrencyException>();
        firstEvents.Dispatched.Should().BeEmpty("a refused save raises nothing");
        await using var reader = fixture.CreateContext();
        (await reader.Set<Player>().SingleAsync(entity => entity.Id == player.Id, cancellationToken)).Nationality!.Code.Should().Be("IT", "the first save to land wins");
    }
}
