using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Domain.UnitTests.Users;

/// <summary>Roles are a closed but extensible list; names match the role claims checked by the API.</summary>
public sealed class RoleTests
{
    /// <summary>Known roles resolve by name, ignoring case.</summary>
    [Fact]
    public void Known_roles_resolve_by_name()
    {
        Role.FromName("admin").Should().Be(Role.Admin);
        Role.FromName("Analyst").Should().Be(Role.Analyst);
        Role.Admin.Name.Should().Be("Admin");
        Role.All.Should().Equal(Role.Analyst, Role.Admin);
    }

    /// <summary>Unknown roles are rejected.</summary>
    [Fact]
    public void Unknown_roles_are_rejected()
    {
        var resolve = () => Role.FromName("Owner");

        resolve.Should().Throw<DomainException>().Which.Code.Should().Be("user.role_unknown");
    }
}
