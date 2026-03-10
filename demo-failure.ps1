## PowerShell Demo Script — Windows Users
## Run this from the project root when docker-compose is up
## Requirements: Docker Desktop for Windows

param(
    [string]$Api         = "http://localhost:8080",
    [string]$Seq         = "http://localhost:8888",
    [string]$Jaeger      = "http://localhost:16686",
    [string]$Grafana     = "http://localhost:3000",
    [string]$AlertMgr    = "http://localhost:9093"
)

Write-Host ""
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  Observability POC — Demo Failure Scenario" -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host ""

# Pre-flight
Write-Host "[Pre-flight] Checking API health..." -ForegroundColor White
try {
    $health = Invoke-RestMethod "$Api/health/live" -TimeoutSec 5
    Write-Host "  ✓ API is healthy" -ForegroundColor Green
} catch {
    Write-Host "  ERROR: API not reachable. Run: docker-compose up --build" -ForegroundColor Red
    exit 1
}

# Step 1 — Baseline traffic
Write-Host ""
Write-Host "[Step 1] Generating baseline traffic (100 requests)..." -ForegroundColor White
for ($i = 0; $i -lt 50; $i++) {
    try { Invoke-RestMethod "$Api/weatherforecast" -TimeoutSec 5 | Out-Null } catch {}
    try { Invoke-RestMethod "$Api/orders" -TimeoutSec 5 | Out-Null } catch {}
}
Write-Host "  ✓ 100 requests sent" -ForegroundColor Green
Write-Host "  → Grafana: check request rate panel" -ForegroundColor Yellow
Start-Sleep 5

# Step 2 — Slow queries
Write-Host ""
Write-Host "[Step 2] Simulating slow database queries (10 x 3s)..." -ForegroundColor White
$jobs = 1..10 | ForEach-Object {
    Start-Job -ScriptBlock {
        param($url)
        try { Invoke-RestMethod "$url/simulate/slow?delayMs=3000" -TimeoutSec 10 | Out-Null } catch {}
    } -ArgumentList $Api
}
$jobs | Wait-Job | Remove-Job
Write-Host "  ✓ Slow queries completed" -ForegroundColor Green
Write-Host "  → Jaeger: search 'SlowDatabaseQuery' — spans > 2s" -ForegroundColor Yellow
Write-Host "  → Grafana: P95 latency panel spiking" -ForegroundColor Yellow
Start-Sleep 10

# Step 3 — Error rate spike
Write-Host ""
Write-Host "[Step 3] Triggering error rate spike (30 errors)..." -ForegroundColor White
for ($i = 0; $i -lt 30; $i++) {
    try { Invoke-RestMethod "$Api/simulate/error" -TimeoutSec 5 | Out-Null } catch {}
    Start-Sleep 2
}
Write-Host "  ✓ 30 errors triggered" -ForegroundColor Green
Write-Host "  → Seq: filter @Level='Error' — CorrelationIds visible" -ForegroundColor Yellow
Write-Host "  → Jaeger: red error spans with exception details" -ForegroundColor Yellow
Write-Host "  → Grafana: error rate > 5% threshold" -ForegroundColor Yellow
Write-Host "  → Wait ~2 min for HighErrorRate alert in AlertManager" -ForegroundColor Yellow
Start-Sleep 15

# Step 4 — Kill Redis
Write-Host ""
Write-Host "[Step 4] Stopping Redis to simulate dependency failure..." -ForegroundColor White
docker-compose stop redis
Write-Host "  ✓ Redis stopped" -ForegroundColor Green
Write-Host "  → $Api/health → redis = Unhealthy" -ForegroundColor Yellow
Write-Host "  → $Api/health-ui → red indicator" -ForegroundColor Yellow
Write-Host "  → Grafana: health_check_status{check_name='redis'} = -1" -ForegroundColor Yellow
Write-Host "  → Waiting 90 seconds for alert to fire..." -ForegroundColor Yellow
Start-Sleep 90

# Step 5 — Restore Redis
Write-Host ""
Write-Host "[Step 5] Restoring Redis..." -ForegroundColor White
docker-compose start redis
Write-Host "  ✓ Redis restored — alerts should resolve in ~30 seconds" -ForegroundColor Green

Write-Host ""
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  Demo complete! Review:" -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Logs:    $Seq" -ForegroundColor Green
Write-Host "  Traces:  $Jaeger" -ForegroundColor Green
Write-Host "  Metrics: $Grafana" -ForegroundColor Green
Write-Host "  Alerts:  $AlertMgr" -ForegroundColor Green
Write-Host ""
