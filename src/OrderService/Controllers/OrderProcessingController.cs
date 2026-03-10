using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace OrderService.Controllers;

/// <summary>
/// Receives order processing requests forwarded from ApiService.
/// Demonstrates mid-chain trace propagation — all spans here appear as
/// children of the originating ApiService trace.
/// </summary>
[ApiController]
[Route("orders")]
public class OrderProcessingController : ControllerBase
{
    private readonly ILogger<OrderProcessingController> _logger;
    private readonly ActivitySource _activitySource;

    public OrderProcessingController(
        ILogger<OrderProcessingController> logger,
        ActivitySource activitySource)
    {
        _logger         = logger;
        _activitySource = activitySource;
    }

    [HttpPost("process")]
    public async Task<IActionResult> ProcessOrder([FromBody] OrderRequest request)
    {
        using var activity = _activitySource.StartActivity("OrderService.ProcessOrder");
        activity?.SetTag("order.product",  request.Product);
        activity?.SetTag("order.quantity", request.Quantity);

        _logger.LogInformation(
            "OrderService processing order: {Product} x{Quantity}",
            request.Product, request.Quantity);

        // Simulate database write with a realistic delay
        using (var dbSpan = _activitySource.StartActivity("OrderService.PersistOrder"))
        {
            dbSpan?.SetTag("db.system",    "sqlserver");
            dbSpan?.SetTag("db.operation", "INSERT");
            dbSpan?.SetTag("db.table",     "Orders");

            await Task.Delay(Random.Shared.Next(50, 200));

            var orderId = Guid.NewGuid();
            dbSpan?.SetTag("order.id", orderId.ToString());

            _logger.LogInformation(
                "Order {OrderId} persisted to database for {Product}",
                orderId, request.Product);

            activity?.SetTag("order.id", orderId.ToString());

            return Ok(new
            {
                orderId,
                product  = request.Product,
                quantity = request.Quantity,
                status   = "Processed",
                service  = "OrderService"
            });
        }
    }
}

public record OrderRequest(string Product, int Quantity, decimal UnitPrice);
