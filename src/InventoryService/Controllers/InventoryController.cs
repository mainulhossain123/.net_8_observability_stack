using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace InventoryService.Controllers;

/// <summary>
/// Handles inventory availability queries.
/// Demonstrates end-of-chain trace propagation — spans here appear deep
/// inside the trace tree originating from ApiService.
/// </summary>
[ApiController]
[Route("inventory")]
public class InventoryController : ControllerBase
{
    private readonly ILogger<InventoryController> _logger;
    private readonly ActivitySource _activitySource;

    // Simulated in-memory inventory for the demo
    private static readonly Dictionary<string, int> _stock = new()
    {
        ["Widget A"]   = 100,
        ["Gadget B"]   = 50,
        ["Doohickey"]  = 25,
        ["Widget-0"]   = 200,
        ["Widget-1"]   = 150,
        ["Widget-2"]   = 75,
        ["Widget-3"]   = 30,
        ["Widget-4"]   = 10
    };

    public InventoryController(
        ILogger<InventoryController> logger,
        ActivitySource activitySource)
    {
        _logger         = logger;
        _activitySource = activitySource;
    }

    [HttpGet("check")]
    public async Task<IActionResult> CheckInventory(
        [FromQuery] string product,
        [FromQuery] int qty = 1)
    {
        using var activity = _activitySource.StartActivity("InventoryService.CheckStock");
        activity?.SetTag("inventory.product",  product);
        activity?.SetTag("inventory.requested_qty", qty);

        _logger.LogInformation(
            "Checking stock for {Product} x{Quantity}", product, qty);

        // Simulate DB read delay
        using (var dbSpan = _activitySource.StartActivity("InventoryService.ReadStock"))
        {
            dbSpan?.SetTag("db.system",    "sqlserver");
            dbSpan?.SetTag("db.operation", "SELECT");
            dbSpan?.SetTag("db.table",     "StockLevels");

            await Task.Delay(Random.Shared.Next(20, 100));
        }

        var available = _stock.TryGetValue(product, out var stock) ? stock : 50;
        activity?.SetTag("inventory.available_qty", available);

        if (available < qty)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Insufficient stock");
            _logger.LogWarning(
                "Insufficient stock for {Product}: requested={Requested}, available={Available}",
                product, qty, available);

            return Conflict(new
            {
                product,
                requested  = qty,
                available,
                status     = "InsufficientStock"
            });
        }

        _logger.LogInformation(
            "Stock confirmed for {Product}: available={Available}", product, available);

        return Ok(new
        {
            product,
            requested  = qty,
            available,
            status     = "Available",
            service    = "InventoryService"
        });
    }

    [HttpGet]
    public IActionResult GetAllStock()
    {
        _logger.LogInformation("Returning full stock levels");
        return Ok(_stock.Select(kvp => new
        {
            product   = kvp.Key,
            available = kvp.Value
        }));
    }
}
