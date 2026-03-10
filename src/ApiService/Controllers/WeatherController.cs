using Microsoft.AspNetCore.Mvc;

namespace ApiService.Controllers;

[ApiController]
[Route("[controller]")]
public class WeatherController : ControllerBase
{
    private static readonly string[] Summaries =
    [
        "Freezing", "Bracing", "Chilly", "Cool", "Mild",
        "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    ];

    private readonly ILogger<WeatherController> _logger;

    public WeatherController(ILogger<WeatherController> logger)
        => _logger = logger;

    [HttpGet]
    public IActionResult Get()
    {
        _logger.LogInformation(
            "WeatherForecast requested. {RequestId}", HttpContext.TraceIdentifier);

        var forecast = Enumerable.Range(1, 5).Select(index =>
        {
            var temp    = Random.Shared.Next(-20, 55);
            var summary = Summaries[Random.Shared.Next(Summaries.Length)];

            // Business event log — searchable in Seq
            _logger.LogInformation(
                "Forecast generated: Day+{Day} {Temperature}°C ({Summary})",
                index, temp, summary);

            return new
            {
                Date        = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                TemperatureC = temp,
                TemperatureF = 32 + (int)(temp / 0.5556),
                Summary      = summary
            };
        }).ToArray();

        return Ok(forecast);
    }
}
