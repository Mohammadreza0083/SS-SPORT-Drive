using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Application.Paging;

namespace SsSport.Drive.Application.UnitTests.Paging;

/// <summary>A paged query used to prove every paged query is validated without writing its own paging rules.</summary>
/// <param name="Paging">The page to read.</param>
/// <param name="Search">A filter with its own rule.</param>
public sealed record ListTeams(PageRequest Paging, string Search = "") : IPagedQuery<string>;

/// <summary>The query's own rule; paging rules come from the shared validator.</summary>
public sealed class ListTeamsValidator : AbstractValidator<ListTeams>
{
    /// <summary>Limits the search text.</summary>
    public ListTeamsValidator() => RuleFor(query => query.Search).MaximumLength(5);
}

/// <summary>Answers <see cref="ListTeams"/> with one page of fixed names.</summary>
public sealed class ListTeamsHandler : IQueryHandler<ListTeams, PagedResult<string>>
{
    /// <summary>Returns a single page.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>The page.</returns>
    public Task<PagedResult<string>> Handle(ListTeams request, CancellationToken cancellationToken) =>
        Task.FromResult(PagedResult<string>.Create(["Lions"], request.Paging, 1));
}

/// <summary>A cursor-paged query.</summary>
/// <param name="Paging">Where to continue from.</param>
public sealed record ListTeamEvents(CursorRequest Paging) : ICursorQuery<string>;

/// <summary>Answers <see cref="ListTeamEvents"/> with a last page.</summary>
public sealed class ListTeamEventsHandler : IQueryHandler<ListTeamEvents, CursorResult<string>>
{
    /// <summary>Returns a last page.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>The page.</returns>
    public Task<CursorResult<string>> Handle(ListTeamEvents request, CancellationToken cancellationToken) =>
        Task.FromResult(new CursorResult<string>(["joined"], null));
}

/// <summary>
/// Through the real pipeline, every paged query's page and size are checked before its handler runs,
/// alongside the query's own rules.
/// </summary>
public sealed class PagedQueryPipelineTests : IDisposable
{
    /// <summary>The container under test.</summary>
    private readonly ServiceProvider _services;

    /// <summary>Builds the container the way the hosts do, plus this assembly's handlers and validators.</summary>
    public PagedQueryPipelineTests()
    {
        var services = new ServiceCollection().AddLogging().AddApplication();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssemblyContaining<PagedQueryPipelineTests>());
        services.AddValidatorsFromAssemblyContaining<PagedQueryPipelineTests>();
        services.AddSingleton(NSubstitute.Substitute.For<Abstractions.Persistence.IUnitOfWork>());
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>A valid page is answered.</summary>
    [Fact]
    public async Task Valid_page_is_answered()
    {
        var page = await SendAsync(new ListTeams(new PageRequest(2, 10)));

        page.Items.Should().Equal("Lions");
        page.Page.Should().Be(2);
    }

    /// <summary>An oversized page is rejected under <c>pageSize</c>, with the query's own errors.</summary>
    [Fact]
    public async Task Oversized_page_is_rejected_with_the_query_rules()
    {
        var send = () => SendAsync(new ListTeams(new PageRequest(0, 101), "far too long"));

        var failure = await send.Should().ThrowAsync<RequestValidationException>();
        failure.Which.Errors.Keys.Should().BeEquivalentTo("page", "pageSize", nameof(ListTeams.Search));
    }

    /// <summary>A tampered cursor is rejected under <c>cursor</c>.</summary>
    [Fact]
    public async Task Tampered_cursor_is_rejected()
    {
        using var scope = _services.CreateScope();

        var send = () => scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new ListTeamEvents(new CursorRequest("tampered!")), TestContext.Current.CancellationToken);

        var failure = await send.Should().ThrowAsync<RequestValidationException>();
        failure.Which.Errors.Keys.Should().Equal("cursor");
    }

    /// <summary>A first cursor page is answered.</summary>
    [Fact]
    public async Task First_cursor_page_is_answered()
    {
        using var scope = _services.CreateScope();

        var page = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new ListTeamEvents(new CursorRequest()), TestContext.Current.CancellationToken);

        page.Items.Should().Equal("joined");
        page.NextCursor.Should().BeNull();
    }

    /// <summary>Disposes the container.</summary>
    public void Dispose() => _services.Dispose();

    /// <summary>Sends a paged query in a fresh scope.</summary>
    /// <param name="query">The query.</param>
    /// <returns>The page.</returns>
    private async Task<PagedResult<string>> SendAsync(ListTeams query)
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(query, TestContext.Current.CancellationToken);
    }
}
