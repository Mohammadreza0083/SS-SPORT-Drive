using System.Collections;
using System.Reflection;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Paging;

namespace SsSport.Drive.ArchitectureTests;

/// <summary>A query that returns a whole collection without saying why; the rule must catch it.</summary>
internal sealed record ListEverythingProbe : IQuery<IReadOnlyList<string>>;

/// <summary>A small fixed catalogue, explicitly allowed to be returned whole.</summary>
[UnpagedList("A fixed catalogue of six positions.")]
internal sealed record ListPositionsProbe : IQuery<IReadOnlyList<string>>;

/// <summary>A paged query; the rule must accept it.</summary>
/// <param name="Paging">The page to read.</param>
internal sealed record ListPlayersProbe(PageRequest Paging) : IPagedQuery<string>;

/// <summary>
/// No query sends a whole table to the client (decisions §2b): a query that returns a collection must be paged
/// (<see cref="PagedResult{T}"/> / <see cref="CursorResult{T}"/>) or be marked <see cref="UnpagedListAttribute"/> with a reason.
/// </summary>
public sealed class PagingTests
{
    /// <summary>Every Application query that returns a collection is paged or explicitly exempt.</summary>
    [Fact]
    public void Application_queries_never_return_unbounded_collections()
    {
        var unbounded = UnboundedQueries(Layers.Application);

        unbounded.Should().BeEmpty("lists must be paged (decisions §2b) or carry [UnpagedList(reason)], but these return whole collections");
    }

    /// <summary>The rule catches an unmarked collection query and accepts exempt and paged ones, so it is not vacuous.</summary>
    [Fact]
    public void Rule_catches_unbounded_queries()
    {
        var unbounded = UnboundedQueries(typeof(PagingTests).Assembly);

        unbounded.Should().Equal(nameof(ListEverythingProbe));
    }

    /// <summary>An exemption must give its reason.</summary>
    [Fact]
    public void Exemptions_need_a_reason()
    {
        var act = () => new UnpagedListAttribute(" ");

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>Finds the queries in an assembly whose result is a collection and that are neither paged nor exempt.</summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The offending query type names.</returns>
    private static List<string> UnboundedQueries(Assembly assembly) =>
        [.. assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .Where(type => type.GetCustomAttribute<UnpagedListAttribute>() is null)
            .Where(type => type.GetInterfaces()
                .Where(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IQuery<>))
                .Select(contract => contract.GetGenericArguments()[0])
                .Any(IsCollection))
            .Select(type => type.Name)];

    /// <summary>Checks whether a result type is a bare collection (text is not).</summary>
    /// <param name="type">The result type.</param>
    /// <returns><see langword="true"/> for arrays, lists and other enumerables.</returns>
    private static bool IsCollection(Type type) => type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);
}
