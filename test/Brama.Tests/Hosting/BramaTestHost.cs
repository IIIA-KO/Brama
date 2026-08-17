using Brama.Configuration;
using Brama.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Brama.Tests.Hosting;

/// <summary>
/// Hosts a real ASP.NET Core application wired with Brama, over an in-memory transport, so tests
/// exercise the actual request pipeline rather than a stand-in for it.
/// </summary>
public sealed class BramaTestHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private BramaTestHost(WebApplication app, HttpClient client)
    {
        _app = app;
        Client = client;
    }

    public HttpClient Client { get; }

    public static async Task<BramaTestHost> StartAsync(
        Action<ApiConfig> configure,
        Action<IServiceCollection>? configureServices = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();

        configureServices?.Invoke(builder.Services);
        builder.Services.AddBrama(configure);

        WebApplication app = builder.Build();
        app.MapBrama();

        await app.StartAsync();

        return new BramaTestHost(app, app.GetTestClient());
    }

    /// <summary>
    /// A problem-details table with every <see cref="ErrorKind"/> mapped, so a test that is not
    /// about the mapping table does not have to spell one out.
    /// </summary>
    public static void MapAllErrorKinds(ApiConfig api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.ProblemDetails(p => p
            .Map(ErrorKind.Validation, StatusCodes.Status400BadRequest)
            .Map(ErrorKind.NotFound, StatusCodes.Status404NotFound)
            .Map(ErrorKind.Conflict, StatusCodes.Status409Conflict)
            .Map(ErrorKind.Unauthorized, StatusCodes.Status401Unauthorized)
            .Map(ErrorKind.Forbidden, StatusCodes.Status403Forbidden)
            .Map(ErrorKind.Problem, StatusCodes.Status400BadRequest));
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
