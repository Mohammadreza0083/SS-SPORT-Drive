using NetArchTest.Rules;

namespace SsSport.Drive.ArchitectureTests;

/// <summary>Entities never leave the Application layer: DTOs carry plain values, and the API host never sees aggregates.</summary>
public sealed class DtoBoundaryTests
{
    /// <summary>The Domain namespaces that hold aggregates, entities and value objects (everything but the shared primitives).</summary>
    private static readonly string[] _domainModelNamespaces =
    [
        $"{Layers.DomainNamespace}.Players",
        $"{Layers.DomainNamespace}.Assets",
        $"{Layers.DomainNamespace}.DriveFolders",
        $"{Layers.DomainNamespace}.Users",
        $"{Layers.DomainNamespace}.Audit",
        $"{Layers.DomainNamespace}.Countries",
    ];

    /// <summary>Response and request DTOs (types named <c>*Dto</c>) do not reference any Domain type.</summary>
    [Fact]
    public void Dtos_do_not_reference_the_domain()
    {
        var result = Types.InAssembly(Layers.Application)
            .That().HaveNameEndingWith("Dto")
            .ShouldNot().HaveDependencyOn(Layers.DomainNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue("DTOs must hold plain values only, but these reference the Domain: {0}", string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>The API host works with DTOs only; it may use Domain primitives such as <c>DomainException</c>.</summary>
    [Fact]
    public void Api_does_not_reference_domain_models()
    {
        var result = Types.InAssembly(Layers.Api)
            .ShouldNot().HaveDependencyOnAny(_domainModelNamespaces)
            .GetResult();

        result.IsSuccessful.Should().BeTrue("the API must not expose entities, but these types use them: {0}", string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>The Application layer has at least one DTO, so the rule above is not vacuous.</summary>
    [Fact]
    public void Application_defines_dtos()
    {
        Types.InAssembly(Layers.Application).That().HaveNameEndingWith("Dto").GetTypes().Should().NotBeEmpty();
    }
}
