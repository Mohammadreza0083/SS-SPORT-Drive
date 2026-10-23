using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SsSport.Drive.Api.Authentication;
using SsSport.Drive.Application.Abstractions.Authentication;

namespace SsSport.Drive.Api.IntegrationTests;

/// <summary>
/// The API host reads the caller from the request's signed-in principal and wires the MediatR pipeline.
/// </summary>
/// <param name="factory">The in-memory API host.</param>
public sealed class CurrentUserTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>Without a signed-in principal the caller is anonymous.</summary>
    [Fact]
    public void Anonymous_request_has_no_user()
    {
        var user = new HttpContextCurrentUser(new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        user.IsAuthenticated.Should().BeFalse();
        user.UserId.Should().BeNull();
        user.IsInRole("Admin").Should().BeFalse();
    }

    /// <summary>A signed-in principal exposes its id and roles.</summary>
    [Fact]
    public void Signed_in_request_exposes_id_and_roles()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "user-42"), new Claim(ClaimTypes.Role, "Admin")],
            authenticationType: "Test"));
        var user = new HttpContextCurrentUser(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } });

        user.IsAuthenticated.Should().BeTrue();
        user.UserId.Should().Be("user-42");
        user.IsInRole("Admin").Should().BeTrue();
        user.IsInRole("Analyst").Should().BeFalse();
    }

    /// <summary>The host registers MediatR and replaces the anonymous caller with the request-based one.</summary>
    [Fact]
    public void Host_wires_the_pipeline_and_the_request_user()
    {
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<ISender>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<ICurrentUser>().Should().BeOfType<HttpContextCurrentUser>();
    }
}
