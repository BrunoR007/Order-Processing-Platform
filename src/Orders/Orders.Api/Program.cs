using System.Diagnostics;
using OrderProcessing.Messaging;
using OrderProcessing.Orders.Application;
using OrderProcessing.Orders.Infrastructure;

namespace OrderProcessing.Orders.Api;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddProblemDetails();
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
        builder.AddMessaging<OrdersDb, OrderEventHandler>("orders");
        builder.Services.AddScoped<IOrderStore, OrderStore>();
        var app = builder.Build();
        app.UseExceptionHandler();
        app.MapServiceHealth();
        app.MapPost("/orders", async (CreateOrderRequest request, HttpContext context, IOrderStore store, CancellationToken ct) =>
        {
            var key = context.Request.Headers["Idempotency-Key"].ToString();
            if (key.Length is < 1 or > 128 || key.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.')))
                return Results.Problem("Supply an Idempotency-Key of 1–128 ASCII letters, digits, dots, dashes, or underscores.", statusCode: 400);
            var rawCorrelation = context.Request.Headers["X-Correlation-Id"].ToString();
            var correlationId = Guid.NewGuid();
            if (rawCorrelation.Length > 0 && (!Guid.TryParse(rawCorrelation, out correlationId) || correlationId == Guid.Empty))
                return Results.Problem("X-Correlation-Id must be a nonempty UUID.", statusCode: 400);
            Activity.Current?.SetTag("correlation.id", correlationId);
            try
            {
                var result = await store.Create(request, key, correlationId, ct);
                context.Response.Headers["X-Correlation-Id"] = result.Order.CorrelationId.ToString();
                var response = ToResponse(result.Order);
                return result.Created ? Results.Created($"/orders/{result.Order.Id}", response) : Results.Ok(response);
            }
            catch (OrderValidationException exception) { return Results.Problem(exception.Message, statusCode: 400); }
            catch (IdempotencyConflictException) { return Results.Problem("Idempotency-Key was already used with a different order.", statusCode: 409); }
        });
        app.MapGet("/orders/{id:guid}", async (Guid id, IOrderStore store, CancellationToken ct) =>
        {
            var order = await store.Find(id, ct);
            return order is null ? Results.NotFound() : Results.Ok(ToResponse(order));
        });
        if (!app.Environment.IsEnvironment("Testing")) await app.InitializeDatabase<OrdersDb>();
        await app.RunAsync();
    }

    private static object ToResponse(Domain.Order order) => new
    {
        order.Id,
        order.Status,
        order.Amount,
        order.Currency,
        order.CorrelationId,
        order.FailureReason,
        order.CreatedAt,
        Lines = System.Text.Json.JsonSerializer.Deserialize<Contracts.OrderLine[]>(order.LinesJson, EventEnvelope.Json)
    };
}
