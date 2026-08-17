using System.ComponentModel.DataAnnotations;

namespace Brama.Sample.Api.Orders;

public record CreateOrderBody(
    Guid CustomerId,
    [property: Required, MaxLength(200)] string Description,
    bool Draft = false);

public record OrderResponse(Guid Id, Guid CustomerId, string Description, string Status);

public interface IOrderRepository
{
    OrderResponse? Find(Guid id);

    OrderResponse Create(Guid customerId, CreateOrderBody body);
}

/// <summary>
/// Stands in for persistence so the sample exercises the request pipeline, not a database.
/// </summary>
public sealed class InMemoryOrderRepository : IOrderRepository
{
    private readonly Dictionary<Guid, OrderResponse> _orders = [];

    public OrderResponse? Find(Guid id) => _orders.GetValueOrDefault(id);

    public OrderResponse Create(Guid customerId, CreateOrderBody body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var order = new OrderResponse(Guid.NewGuid(), customerId, body.Description, body.Draft ? "Draft" : "Placed");
        _orders[order.Id] = order;

        return order;
    }
}
