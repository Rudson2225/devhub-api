using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace DevHub.IntegrationTests;

/// <summary>
/// Test-only startup filter that reports a fixed peer address to the pipeline.
/// <para>
/// The in-memory test server leaves <c>Connection.RemoteIpAddress</c> as null, but forwarded header
/// trust is decided from that address. Supplying it lets a test exercise both a trusted reverse proxy
/// and an untrusted client. Registered only by the security test fixtures, never by the production app.
/// </para>
/// </summary>
public sealed class TestPeerAddressStartupFilter : IStartupFilter
{
    private readonly IPAddress _peerAddress;

    public TestPeerAddressStartupFilter(string peerAddress)
    {
        _peerAddress = IPAddress.Parse(peerAddress);
    }

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = _peerAddress;
                context.Response.OnStarting(() =>
                {
                    // Written when the response starts, which is after the forwarded headers
                    // middleware inside the pipeline has had its say.
                    context.Response.Headers["X-Test-Remote-IP"] =
                        context.Connection.RemoteIpAddress?.ToString() ?? "none";
                    return Task.CompletedTask;
                });

                await nextMiddleware();
            });

            next(app);
        };
}
