using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SsSport.Drive.Api.Security;

namespace SsSport.Drive.Api.IntegrationTests.Security;

/// <summary>
/// The admin area is deny-by-default at the HTTP layer too (decisions §1b, 2026-11-13): every <c>/admin/*</c> endpoint
/// carries the <see cref="AdminAuthorization.PolicyName"/> policy, so an endpoint whose request forgets its role marker is
/// still closed. The MediatR authorization behaviour stays as the second check. These tests need no database.
/// </summary>
public sealed class AdminAuthorizationTests : IDisposable
{
    /// <summary>The API host in its default (Development) environment.</summary>
    private readonly WebApplicationFactory<Program> _factory = new();

    /// <summary>Every mapped <c>/admin</c> route requires the Admin policy.</summary>
    [Fact]
    public void Every_admin_endpoint_requires_the_admin_policy()
    {
        using var client = _factory.CreateClient();
        var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/').StartsWith("admin", StringComparison.OrdinalIgnoreCase))
            .ToList();

        endpoints.Should().NotBeEmpty();
        foreach (var endpoint in endpoints)
        {
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Should().Contain(data => data.Policy == AdminAuthorization.PolicyName, endpoint.RoutePattern.RawText);
        }
    }

    /// <summary>An anonymous call to an admin endpoint is refused with a 401 ProblemDetails before any handler runs.</summary>
    /// <param name="path">An admin path.</param>
    [Theory]
    [InlineData("/admin/emails")]
    [InlineData("/admin/links")]
    public async Task Anonymous_admin_calls_get_401_problem_details(string path)
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    /// <summary>The Admin policy needs an authenticated caller with the Admin role.</summary>
    [Fact]
    public async Task The_admin_policy_requires_the_admin_role()
    {
        var policy = await _factory.Services.GetRequiredService<IAuthorizationPolicyProvider>().GetPolicyAsync(AdminAuthorization.PolicyName);

        policy.Should().NotBeNull();
        policy!.Requirements.OfType<Microsoft.AspNetCore.Authorization.Infrastructure.RolesAuthorizationRequirement>()
            .Should().ContainSingle().Which.AllowedRoles.Should().BeEquivalentTo(["Admin"]);
    }

    /// <summary>Disposes the in-memory API host.</summary>
    public void Dispose() => _factory.Dispose();
}
