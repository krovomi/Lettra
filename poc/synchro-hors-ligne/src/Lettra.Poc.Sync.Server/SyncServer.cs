using Lettra.Poc.Sync.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Lettra.Poc.Sync.Server;

public static class SyncServer
{
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ServerState>();
        builder.Services.AddProblemDetails();
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = SyncHttp.Json.PropertyNamingPolicy;
        });
        configure?.Invoke(builder);

        var app = builder.Build();
        MapEndpoints(app);
        return app;
    }

    public static void MapEndpoints(IEndpointRouteBuilder app)
    {
        // La sonde attend ce corps précis : un portail captif qui répond 200 en HTML n'est pas du réseau.
        app.MapGet("/api/health", (TimeProvider clock) => new HealthResponse(SyncHttp.ServiceName, clock.GetUtcNow()));

        app.MapPost("/api/auth/login", (LoginRequest r, ServerState s) => ToHttp(s.Login(r.DeviceId, r.Secret)));
        app.MapPost("/api/auth/refresh", (RefreshRequest r, ServerState s) => ToHttp(s.Refresh(r.DeviceId, r.RefreshToken)));

        var sync = app.MapGroup("/api/sync").AddEndpointFilter(RequireDevice);

        sync.MapPost("/facts", (PushFactsRequest r, HttpContext ctx, ServerState s) =>
        {
            var device = (Device)ctx.Items[nameof(Device)]!;
            // Ordre chronologique par série : un maillon manquant fait échouer les suivants, sans trou.
            try
            {
                var results = r.Facts.OrderBy(f => f.SeriesCode).ThenBy(f => f.SequenceNumber)
                    .Select(f => s.ApplyFact(device, f)).ToList();
                return Results.Ok(new PushFactsResponse(results));
            }
            catch (IdempotencyKeyReusedException e) { return Results.Problem(e.Message, statusCode: StatusCodes.Status422UnprocessableEntity); }
        });

        sync.MapPost("/intents", (PushIntentsRequest r, ServerState s) =>
        {
            try { return Results.Ok(new PushIntentsResponse([.. r.Commands.Select(s.ApplyIntent)])); }
            catch (IdempotencyKeyReusedException e) { return Results.Problem(e.Message, statusCode: StatusCodes.Status422UnprocessableEntity); }
        });

        sync.MapGet("/changes", ([FromQuery] long since, ServerState s) => s.ChangesSince(since));
    }

    private static async ValueTask<object?> RequireDevice(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        var state = http.RequestServices.GetRequiredService<ServerState>();
        var header = http.Request.Headers.Authorization.ToString();
        var device = header.StartsWith("Bearer ", StringComparison.Ordinal) ? state.Authenticate(header["Bearer ".Length..]) : null;

        if (device is null) return Results.Unauthorized();
        if (device.Revoked) return RevokedProblem();

        http.Items[nameof(Device)] = device;
        return await next(ctx);
    }

    private static IResult ToHttp(AuthResult r) => r.Status switch
    {
        AuthStatus.Ok => Results.Ok(r.Token),
        AuthStatus.Revoked => RevokedProblem(),
        _ => Results.Unauthorized(),
    };

    private static IResult RevokedProblem() => Results.Problem(
        type: SyncHttp.DeviceRevokedProblem, title: "Appareil révoqué", statusCode: StatusCodes.Status403Forbidden);
}
