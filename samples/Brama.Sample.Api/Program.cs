using Brama;
using Brama.Configuration;
using Brama.Hosting;
using Brama.Sample.Api.Orders;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();

builder.Services.AddBrama(api =>
{
    api.ProblemDetails(p => p
        .Map(ErrorKind.Validation, StatusCodes.Status400BadRequest)
        .Map(ErrorKind.NotFound, StatusCodes.Status404NotFound)
        .Map(ErrorKind.Conflict, StatusCodes.Status409Conflict)
        .Map(ErrorKind.Unauthorized, StatusCodes.Status401Unauthorized)
        .Map(ErrorKind.Forbidden, StatusCodes.Status403Forbidden)
        .Map(ErrorKind.Problem, StatusCodes.Status400BadRequest)
        .OnUnhandledException((exception, context) => context.IsDevelopment
            ? exception.Message
            : "An unexpected error occurred"));

    api.Versioning(v => v.UseUrlSegment());

    EndpointGroup orders = api.Group("Orders", g => g.Version(1));

    orders.MapGet<OrderResponse>(
        "/{id:guid}",
        (Guid id, IEndpointContext ctx) =>
        {
            OrderResponse? order = ctx.Get<IOrderRepository>().Find(id);

            return order is null
                ? ctx.NotFound($"No order with id {id}.")
                : ctx.Ok(order)
                    .WithLink("self", $"/v1/orders/{order.Id}")
                    .WithLink("cancel", $"/v1/orders/{order.Id}/cancel", when: order.Status != "Cancelled");
        });

    // POSTed to the collection, so ctx.Created's "request path + id" Location convention yields
    // /v1/orders/{newId} — the customer is part of the payload, not the address of the collection.
    orders.MapPost<OrderResponse>(
        "/",
        (CreateOrderBody body, IEndpointContext ctx) =>
        {
            if (string.IsNullOrWhiteSpace(body.Description))
            {
                return ctx.Invalid(
                    "The order could not be created.",
                    [new ValidationFailure(nameof(body.Description), "Description is required.")]);
            }

            OrderResponse order = ctx.Get<IOrderRepository>().Create(body.CustomerId, body);

            return ctx.Created(order, id: order.Id);
        });
});

WebApplication app = builder.Build();

app.MapGet("/", () => Brama.Generated.Greeting.Message);
app.MapBrama();

await app.RunAsync();

/// <summary>Exposed so integration tests can host this exact application.</summary>
public partial class Program
{
    protected Program()
    {
    }
}
