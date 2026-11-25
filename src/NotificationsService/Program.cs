using Microsoft.EntityFrameworkCore;
using NotificationsService;

var builder = WebApplication.CreateBuilder(args);

// Like the Claims API, this trusts a development-only X-User-Id header (DESIGN.md §2). Fail closed elsewhere.
if (!builder.Environment.IsDevelopment())
    throw new InvalidOperationException(
        "NotificationsService trusts a development-only identity header (X-User-Id). Configure real authentication before running outside Development.");

builder.Services.AddDbContext<NotificationsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Notifications")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHostedService<ClaimEventsConsumer>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();
}

app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/api/notifications", async (HttpContext http, NotificationsDbContext db) =>
{
    if (!Guid.TryParse(http.Request.Headers["X-User-Id"], out var userId))
        return Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
            title: "Unknown user", detail: "Send your user id in the X-User-Id header.");

    var latest = await db.Notifications.AsNoTracking()
        .Where(n => n.RecipientId == userId)
        .OrderByDescending(n => n.CreatedAt)
        .Take(50)
        .Select(n => new NotificationDto(n.Id, n.ClaimId, n.ClaimNumber, n.Message, n.CreatedAt))
        .ToListAsync();
    return Results.Ok(latest);
});

app.Run();
