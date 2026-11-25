using System.Text.Json.Serialization;
using ClaimsApi.Api;
using ClaimsApi.Data;
using ClaimsApi.Messaging;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// The X-User-Id header is a demo stand-in for authentication (DESIGN.md §2), and startup seeds demo users.
// Fail closed: refuse to run anywhere except local development.
if (!builder.Environment.IsDevelopment())
    throw new InvalidOperationException(
        "ClaimsApi trusts a development-only identity header (X-User-Id). Configure real authentication before running outside Development.");

builder.Services.AddDbContext<ClaimsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Claims")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ErrorHandler>();
builder.Services.AddOpenApi();
builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection("Kafka"));
builder.Services.AddHostedService<OutboxPublisher>();

var app = builder.Build();

// Apply migrations and seed demo data on startup (local development convenience).
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();
    await db.Database.MigrateAsync();
    await SeedData.EnsureSeededAsync(db, TimeProvider.System.GetUtcNow());
}

app.UseExceptionHandler(); // rule violations → 400/403/409 ProblemDetails (ErrorHandler)
app.UseStatusCodePages(); // empty error responses (e.g. 404) also get a ProblemDetails body

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapUserEndpoints();
app.MapClaimEndpoints();

app.Run();
