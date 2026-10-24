using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SsSport.Drive.Api.IntegrationTests;

/// <summary>API responses write enums as their names, so clients and Scalar read <c>"Waiting"</c> rather than <c>0</c>.</summary>
/// <param name="factory">The in-memory API host.</param>
public sealed class JsonOptionsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>Enums are serialized as strings by the endpoint JSON options.</summary>
    [Fact]
    public void Enums_are_written_as_names()
    {
        var options = factory.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;

        JsonSerializer.Serialize(DayOfWeek.Monday, options).Should().Be("\"Monday\"");
    }
}
