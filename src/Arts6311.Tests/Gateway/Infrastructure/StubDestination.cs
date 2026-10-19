using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Arts6311.Tests.Gateway.Infrastructure;

/// <summary>
/// A real HTTP server on a loopback port that reports back the request it received.
/// </summary>
/// <remarks>
/// It has to be real. YARP forwards over actual sockets through its own <c>HttpMessageInvoker</c>, so a
/// <c>TestServer</c> — which has no listening socket — cannot be a destination. This is the only way to assert
/// what *arrives* at the API rather than what the gateway intended to send, which is the whole question for header
/// stripping and token attachment.
/// </remarks>
internal sealed class StubDestination : IAsyncDisposable
{
    private readonly WebApplication app;

    private StubDestination(WebApplication app, int port)
    {
        this.app = app;
        Port = port;
    }

    public int Port { get; }

    /// <summary>
    /// What service discovery should be pointed at (<c>Services:api:http:0</c>). A full URI, not a bare
    /// <c>host:port</c>: that is the form Aspire's <c>WithReference</c> injects, and the scheme in the value is
    /// what stops a <c>https+http://</c> cluster address from resolving to https against a plain HTTP destination.
    /// </summary>
    public string Address => $"http://127.0.0.1:{Port}";

    /// <summary>Set to make the destination answer with this status instead of 200.</summary>
    public int StatusCode { get; set; } = StatusCodes.Status200OK;

    /// <summary>How many requests reached it. Zero is the assertion for anything the gateway should refuse.</summary>
    public int Requests { get; private set; }

    public static async Task<StubDestination> StartAsync()
    {
        var port = FreePort();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

        var app = builder.Build();
        StubDestination? destination = null;

        app.Run(async context =>
        {
            destination!.Requests++;
            context.Response.StatusCode = destination.StatusCode;
            context.Response.ContentType = "application/json";

            // Echoes the headers and the path, so a test can assert on exactly what crossed the wire.
            await context.Response.WriteAsync(JsonSerializer.Serialize(new ReceivedRequest(
                context.Request.Method,
                context.Request.Path + context.Request.QueryString,
                context.Request.Headers.ToDictionary(
                    header => header.Key,
                    header => header.Value.ToString(),
                    StringComparer.OrdinalIgnoreCase))));
        });

        await app.StartAsync();
        destination = new StubDestination(app, port);
        return destination;
    }

    /// <summary>
    /// Binds port 0 to let the OS pick a free one, then releases it. A fixed port would collide with a parallel
    /// test class or with whatever else is running on the machine.
    /// </summary>
    private static int FreePort()
    {
        using var socket = new System.Net.Sockets.Socket(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Stream,
            System.Net.Sockets.ProtocolType.Tcp);

        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }
}

/// <summary>What the destination saw.</summary>
internal sealed record ReceivedRequest(string Method, string Path, Dictionary<string, string> Headers)
{
    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;

    public bool Has(string name) => Headers.ContainsKey(name);
}
