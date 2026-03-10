$endpoints = @(
  @{ Name="ApiService  /Weather";        Url="http://localhost:8080/Weather";                   Expect=200 },
  @{ Name="ApiService  /swagger";        Url="http://localhost:8080/swagger";                   Expect=200 },
  @{ Name="ApiService  /metrics";        Url="http://localhost:8080/metrics";                   Expect=200 },
  @{ Name="ApiService  /health";         Url="http://localhost:8080/health";                    Expect=200 },
  @{ Name="ApiService  /health/live";    Url="http://localhost:8080/health/live";               Expect=200 },
  @{ Name="ApiService  /health/ready";   Url="http://localhost:8080/health/ready";              Expect=200 },
  @{ Name="ApiService  /health-ui";      Url="http://localhost:8080/health-ui";                 Expect=200 },
  @{ Name="ApiService  /simulate/slow";  Url="http://localhost:8080/simulate/slow?delayMs=100"; Expect=200 },
  @{ Name="ApiService  /simulate/error"; Url="http://localhost:8080/simulate/error";            Expect=500 },
  @{ Name="ApiService  /orders (GET)";   Url="http://localhost:8080/orders";                    Expect=200 },
  @{ Name="OrderService /orders/process"; Url="http://localhost:8081/orders/process";             Expect=200; Method="POST"; Body='{"product":"Widget A","quantity":2,"unitPrice":9.99}' },
  @{ Name="OrderService /health";        Url="http://localhost:8081/health";                    Expect=200 },
  @{ Name="InventoryService /inventory"; Url="http://localhost:8082/inventory";                 Expect=200 },
  @{ Name="InventoryService /health";    Url="http://localhost:8082/health";                    Expect=200 },
  @{ Name="Seq UI";                      Url="http://localhost:8888";                           Expect=200 },
  @{ Name="Jaeger UI";                   Url="http://localhost:16686";                          Expect=200 },
  @{ Name="Prometheus UI";               Url="http://localhost:9090";                           Expect=200 },
  @{ Name="Prometheus /api/v1/targets";  Url="http://localhost:9090/api/v1/targets";            Expect=200 },
  @{ Name="Grafana UI";                  Url="http://localhost:3000";                           Expect=200 },
  @{ Name="AlertManager UI";             Url="http://localhost:9093";                           Expect=200 },
  @{ Name="RabbitMQ UI";                 Url="http://localhost:15672";                          Expect=200 }
)

$pass = 0
$fail = 0
$results = @()

foreach ($ep in $endpoints) {
  try {
    $iwArgs = @{ Uri=$ep.Url; UseBasicParsing=$true; TimeoutSec=10; ErrorAction="Stop" }
    if ($ep.Method -eq "POST") {
      $iwArgs["Method"] = "POST"
      $iwArgs["Body"]   = $ep.Body
      $iwArgs["ContentType"] = "application/json"
    }
    $resp = Invoke-WebRequest @iwArgs
    $code = $resp.StatusCode
  } catch {
    $code = $_.Exception.Response.StatusCode.value__
    if (-not $code) { $code = "CONN_ERR" }
  }

  $ok = ($code -eq $ep.Expect)
  if ($ok) { $pass++ } else { $fail++ }

  $results += [PSCustomObject]@{
    Status  = if ($ok) { "OK  " } else { "FAIL" }
    Service = $ep.Name
    Got     = $code
    Want    = $ep.Expect
  }
}

Write-Host ""
Write-Host "=== Endpoint Health Check ===" -ForegroundColor Cyan
Write-Host ""
foreach ($r in $results) {
  $color = if ($r.Status -eq "OK  ") { "Green" } else { "Red" }
  Write-Host ("[{0}]  {1,-38}  got={2,-8} want={3}" -f $r.Status, $r.Service, $r.Got, $r.Want) -ForegroundColor $color
}
Write-Host ""
$summaryColor = if ($fail -eq 0) { "Green" } else { "Yellow" }
Write-Host ("  PASSED {0}/{1}    FAILED {2}" -f $pass, $endpoints.Count, $fail) -ForegroundColor $summaryColor
Write-Host ""

# Extra detail: parse Prometheus targets
Write-Host "=== Prometheus Scrape Targets ===" -ForegroundColor Cyan
try {
  $targets = (Invoke-RestMethod "http://localhost:9090/api/v1/targets" -TimeoutSec 10).data.activeTargets
  foreach ($t in $targets) {
    $c = if ($t.health -eq "up") { "Green" } else { "Red" }
    Write-Host ("  [{0}]  {1}" -f $t.health.ToUpper(), $t.labels.job) -ForegroundColor $c
  }
} catch {
  Write-Host "  Could not reach Prometheus targets API" -ForegroundColor Yellow
}
Write-Host ""
