using System.Reflection;
using NetArchTest.Rules;

namespace SsSport.Drive.ArchitectureTests;

/// <summary>
/// Verifies the Clean Architecture dependency rule: inner layers never depend on outer layers or frameworks.
/// </summary>
public sealed class LayerDependencyTests
{
    /// <summary>The Domain layer must not reference any other layer.</summary>
    [Fact]
    public void Domain_does_not_depend_on_other_layers()
    {
        AssertNoDependency(
            Layers.Domain,
            Layers.ApplicationNamespace,
            Layers.InfrastructureNamespace,
            Layers.ApiNamespace,
            Layers.WorkersNamespace);
    }

    /// <summary>The Domain layer must stay free of persistence, web and messaging frameworks.</summary>
    [Fact]
    public void Domain_does_not_depend_on_frameworks()
    {
        AssertNoDependency(
            Layers.Domain,
            "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore",
            "MediatR",
            "AutoMapper",
            "Hangfire",
            "Npgsql");
    }

    /// <summary>The Application layer may depend on Domain only.</summary>
    [Fact]
    public void Application_does_not_depend_on_outer_layers()
    {
        AssertNoDependency(
            Layers.Application,
            Layers.InfrastructureNamespace,
            Layers.ApiNamespace,
            Layers.WorkersNamespace);
    }

    /// <summary>The Application layer must not reach for ASP.NET Core, Hangfire, Npgsql or the Telegram Bot API directly.</summary>
    [Fact]
    public void Application_does_not_depend_on_infrastructure_frameworks()
    {
        AssertNoDependency(
            Layers.Application,
            "Microsoft.AspNetCore",
            "Hangfire",
            "Npgsql",
            "Telegram.Bot");
    }

    /// <summary>Infrastructure must not depend on either host.</summary>
    [Fact]
    public void Infrastructure_does_not_depend_on_hosts()
    {
        AssertNoDependency(Layers.Infrastructure, Layers.ApiNamespace, Layers.WorkersNamespace);
    }

    /// <summary>The API host must not depend on the worker host.</summary>
    [Fact]
    public void Api_does_not_depend_on_workers()
    {
        AssertNoDependency(Layers.Api, Layers.WorkersNamespace);
    }

    /// <summary>The worker host must not depend on the API host.</summary>
    [Fact]
    public void Workers_do_not_depend_on_api()
    {
        AssertNoDependency(Layers.Workers, Layers.ApiNamespace);
    }

    /// <summary>Each layer's types live under its own root namespace.</summary>
    [Fact]
    public void Every_layer_uses_its_own_root_namespace()
    {
        Layers.Domain.GetName().Name.Should().Be(Layers.DomainNamespace);
        Layers.Application.GetName().Name.Should().Be(Layers.ApplicationNamespace);
        Layers.Infrastructure.GetName().Name.Should().Be(Layers.InfrastructureNamespace);
        Layers.Api.GetName().Name.Should().Be(Layers.ApiNamespace);
        Layers.Workers.GetName().Name.Should().Be(Layers.WorkersNamespace);
    }

    /// <summary>
    /// Asserts that no type in <paramref name="assembly"/> depends on any of the given namespaces.
    /// </summary>
    /// <param name="assembly">The assembly under test.</param>
    /// <param name="forbiddenNamespaces">Namespaces the assembly must not reference.</param>
    private static void AssertNoDependency(Assembly assembly, params string[] forbiddenNamespaces)
    {
        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbiddenNamespaces)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "{0} must not depend on {1}, but these types do: {2}",
            assembly.GetName().Name,
            string.Join(", ", forbiddenNamespaces),
            string.Join(", ", result.FailingTypeNames ?? []));
    }
}
