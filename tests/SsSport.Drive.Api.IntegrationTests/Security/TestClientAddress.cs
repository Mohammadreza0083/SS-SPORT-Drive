using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace SsSport.Drive.Api.IntegrationTests.Security;

/// <summary>
/// The in-memory test server has no client address. This startup filter runs before the API's own middleware and
/// sets the connection's remote address from the <see cref="Header"/> test header, so tests can act as different
/// clients or as a trusted reverse proxy.
/// </summary>
internal sealed class TestClientAddress : IStartupFilter
{
    /// <summary>The test-only header carrying the address the connection should come from.</summary>
    public const string Header = "X-Test-Remote-Ip";

    /// <summary>Wraps the pipeline with the address-setting middleware.</summary>
    /// <param name="next">The rest of the pipeline configuration.</param>
    /// <returns>The wrapped configuration.</returns>
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            if (context.Request.Headers.TryGetValue(Header, out var value) && IPAddress.TryParse(value, out var address))
            {
                context.Connection.RemoteIpAddress = address;
            }

            return nextMiddleware(context);
        });
        next(app);
    };
}
