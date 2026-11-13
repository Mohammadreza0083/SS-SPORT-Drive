using System.Reflection;
using FluentValidation;
using MediatR;
using SsSport.Drive.Application.Abstractions.Messaging;

namespace SsSport.Drive.ArchitectureTests;

/// <summary>A command with input and no validator; the rule must catch it.</summary>
/// <param name="Name">Some input.</param>
internal sealed record UnvalidatedProbeCommand(string Name) : ICommand;

/// <summary>A command with input and a validator; the rule must accept it.</summary>
/// <param name="Name">Some input.</param>
internal sealed record ValidatedProbeCommand(string Name) : ICommand;

/// <summary>The validator of <see cref="ValidatedProbeCommand"/>.</summary>
internal sealed class ValidatedProbeCommandValidator : AbstractValidator<ValidatedProbeCommand>
{
    /// <summary>Builds the rules.</summary>
    public ValidatedProbeCommandValidator() => RuleFor(command => command.Name).NotEmpty();
}

/// <summary>A command without input; the rule must accept it.</summary>
internal sealed record NoInputProbeCommand : ICommand;

/// <summary>A command only the system sends; the rule must accept it.</summary>
/// <param name="Now">The clock's time.</param>
[InternalRequest("Sent only by a scheduled job with the clock's time.")]
internal sealed record InternalProbeCommand(DateTimeOffset Now) : ICommand;

/// <summary>
/// Every input is validated on the server (decisions §1b rule 3, owner 2026-11-13): each command and query that carries
/// input has a FluentValidation validator, run by the MediatR pipeline. A request only the system itself sends is marked
/// <see cref="InternalRequestAttribute"/> with its reason.
/// </summary>
public sealed class ValidationTests
{
    /// <summary>Every Application request with input has a validator or is explicitly internal.</summary>
    [Fact]
    public void Every_request_with_input_has_a_validator()
    {
        var missing = UnvalidatedRequests(Layers.Application);

        string.Join(", ", missing).Should().BeEmpty("every input is validated on the server (decisions §1b), but these requests have no validator");
    }

    /// <summary>The rule catches an unvalidated request and accepts validated, input-less and internal ones.</summary>
    [Fact]
    public void Rule_catches_unvalidated_requests()
    {
        var missing = UnvalidatedRequests(typeof(ValidationTests).Assembly).Where(name => name.EndsWith("ProbeCommand", StringComparison.Ordinal));

        missing.Should().Equal(nameof(UnvalidatedProbeCommand));
    }

    /// <summary>An internal mark must give its reason.</summary>
    [Fact]
    public void Internal_marks_need_a_reason()
    {
        var act = () => new InternalRequestAttribute(" ");

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>Finds the requests in an assembly that carry input but have no validator and no internal mark.</summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The offending request type names.</returns>
    private static List<string> UnvalidatedRequests(Assembly assembly)
    {
        var types = assembly.GetTypes();
        var validated = types
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .SelectMany(type => AncestorsOf(type))
            .Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(AbstractValidator<>))
            .Select(type => type.GetGenericArguments()[0])
            .ToHashSet();

        return [.. types
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .Where(type => typeof(IBaseRequest).IsAssignableFrom(type))
            .Where(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Any(property => property.Name != "RequiredRoles"))
            .Where(type => type.GetCustomAttribute<InternalRequestAttribute>() is null)
            .Where(type => !validated.Contains(type))
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>Walks a type's base classes.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The base types, nearest first.</returns>
    private static IEnumerable<Type> AncestorsOf(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }
}
