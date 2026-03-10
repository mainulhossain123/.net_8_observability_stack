# .NET Observability POC

A production-grade, fully containerised observability stack demonstrating the **four pillars of observability** using .NET 8 Web APIs.

> **Note:** Built on .NET 8 (`net8.0`). All patterns, packages, and APIs are identical to .NET 9 — you can swap `net8.0` → `net9.0` in the `.csproj` files and use `mcr.microsoft.com/dotnet/sdk:9.0` in the Dockerfiles if you prefer the latest SDK.

---

## Quick Start

```bash
# Clone and start the full stack
docker-compose up --build

# Run the end-to-end demo failure scenario
./demo-failure.sh          # Linux / macOS / Git Bash
./demo-failure.ps1         # Windows PowerShell
```

**Wait ~2 minutes** for all services to become healthy, then open the UIs below.

> **Note on `payment_gateway` health check:** The external stub (`httpstat.us`) is not
> reachable from inside Docker by default. This check intentionally reports `Degraded`
> (not `Unhealthy`) so it does not block the stack. Override the endpoint via the
> `ExternalApis__PaymentGateway` environment variable to point at an accessible URL.

---

## Service Access Points

| Service          | URL                               | Credentials         |
|-----------------|-----------------------------------|---------------------|
| ApiService       | http://localhost:8080             | —                   |
| Swagger UI       | http://localhost:8080/swagger     | —                   |
| Health Check UI  | http://localhost:8080/health-ui   | —                   |
| Metrics endpoint | http://localhost:8080/metrics     | —                   |
| OrderService     | http://localhost:8081             | —                   |
| InventoryService | http://localhost:8082             | —                   |
| Seq (Logs)       | http://localhost:8888             | No auth (dev)       |
| Jaeger (Traces)  | http://localhost:16686            | No auth (dev)       |
| Prometheus       | http://localhost:9090             | No auth (dev)       |
| Grafana          | http://localhost:3000             | admin / observability |
| AlertManager     | http://localhost:9093             | No auth (dev)       |
| RabbitMQ UI      | http://localhost:15672            | guest / guest       |

---

## Project Structure

```
observability-poc/
├── src/
│   ├── ApiService/               # Primary .NET API (port 8080)
│   │   ├── Controllers/
│   │   │   ├── WeatherController.cs    # Basic endpoint with structured logs
│   │   │   ├── OrdersController.cs     # Multi-service orchestration (M2 tracing)
│   │   │   └── DemoController.cs       # /simulate/slow, /simulate/error
│   │   ├── HealthChecks/
│   │   │   ├── DatabaseMigrationHealthCheck.cs
│   │   │   ├── ExternalApiHealthCheck.cs
│   │   │   └── HealthMetricsPublisher.cs  # health → Prometheus gauge
│   │   ├── Metrics/
│   │   │   └── AppMetrics.cs           # Custom business metrics
│   │   ├── Middleware/
│   │   │   └── CorrelationIdMiddleware.cs
│   │   └── Program.cs                  # All configuration
│   ├── OrderService/              # Order processing (port 8081)
│   └── InventoryService/          # Stock check (port 8082)
├── docker/
│   ├── prometheus/
│   │   ├── prometheus.yml         # Scrape config (all 3 services)
│   │   └── alert_rules.yml        # HighErrorRate, HighLatency, HealthCheck alerts
│   ├── alertmanager/
│   │   └── alertmanager.yml       # Routing + receivers
│   └── grafana/
│       ├── provisioning/
│       │   ├── datasources/prometheus.yml   # Auto-connects Prometheus + Jaeger
│       │   └── dashboards/dashboards.yml    # Auto-loads dashboard JSON
│       └── dashboards/
│           └── api-service.json   # Pre-built dashboard (9 panels)
├── docker-compose.yml             # Milestone 5 — full stack
├── docker-compose.m1.yml          # Milestone 1 — logging only
├── docker-compose.m2.yml          # Milestone 2 — + tracing
├── docker-compose.m3.yml          # Milestone 3 — + metrics/alerting
├── docker-compose.m4.yml          # Milestone 4 — + health checks + dependencies
├── demo-failure.sh                # End-to-end demo (bash)
└── demo-failure.ps1               # End-to-end demo (PowerShell)
```

---

## Milestones

### Milestone 1 — Structured Logging (Serilog → Seq)

```bash
docker-compose -f docker-compose.m1.yml up --build
```

**What's implemented:**
- Serilog with Console + Seq sinks
- Enrichers: MachineName, ProcessId, ThreadId, CorrelationId
- `CorrelationIdMiddleware` — generates/propagates `X-Correlation-ID` header
- `UseSerilogRequestLogging` — structured HTTP access logs
- Bootstrap logger — catches startup failures before DI is ready
- Configuration via `appsettings.json` `Serilog` block

**Validate:**
```bash
curl http://localhost:8080/Weather
# Open Seq at http://localhost:8888
# Search: CorrelationId = "<value from X-Correlation-ID response header>"
# Filter: @Level = 'Error'
```

---

### Milestone 2 — Distributed Tracing (OpenTelemetry → Jaeger)

```bash
docker-compose -f docker-compose.m2.yml up --build
```

**What's implemented:**
- OpenTelemetry SDK configured on all 3 services
- Auto-instrumentation: HTTP server, HttpClient, SQL Client
- Custom `ActivitySource` spans in business operations
- W3C TraceContext propagation (automatic via OTel HttpClient instrumentation)
- Multi-service trace: ApiService → OrderService + InventoryService
- `ActivityStatusCode.Error` + `RecordException` for error spans

**Validate:**
```bash
curl -X POST http://localhost:8080/orders \
  -H "Content-Type: application/json" \
  -d '{"product":"Widget A","quantity":2,"unitPrice":9.99}'
# Open Jaeger at http://localhost:16686
# Search service: ApiService — find trace spanning all 3 services

curl "http://localhost:8080/simulate/slow?delayMs=3000"
# Jaeger: find SlowDatabaseQuery span > 3s

curl http://localhost:8080/simulate/error
# Jaeger: red error span with exception stack trace
```

---

### Milestone 3 — Metrics & Alerting (Prometheus + Grafana)

```bash
docker-compose -f docker-compose.m3.yml up --build
```

**What's implemented:**
- `OpenTelemetry.Exporter.Prometheus.AspNetCore` → `/metrics` endpoint
- Runtime metrics: GC collections, heap size, thread pool queue depth
- Custom business metrics: `AppMetrics` (counters, histograms, gauges)
- Prometheus scraping all 3 services every 10s
- Alert rules: HighErrorRate (>5%), HighLatencyP95 (>2s), ServiceDown, HighGCPressure, DependencyUnhealthy
- AlertManager with routing and inhibit rules
- Grafana with **pre-provisioned** dashboard — no manual setup required

**Grafana Dashboard Panels:**
1. Request Rate (RPS)
2. Error Rate (%)
3. P50/P95/P99 Latency (ms)
4. Active HTTP Connections
5. GC Collections by Generation
6. Heap Size (MB)
7. ThreadPool Queue Depth
8. Dependency Health Status
9. Orders Created vs Failed

**Validate:**
```bash
curl http://localhost:8080/metrics  # Prometheus text format
# Prometheus: http://localhost:9090/targets → all jobs "UP"
# Grafana: http://localhost:3000 (admin/observability) → dashboard loads

# Generate load:
for i in {1..200}; do curl -s http://localhost:8080/Weather > /dev/null; done
# Trigger errors:
for i in {1..30}; do curl -s http://localhost:8080/simulate/error > /dev/null; done
# AlertManager: http://localhost:9093 → HighErrorRate firing
```

---

### Milestone 4 — Health Checks & Proactive Monitoring

```bash
docker-compose -f docker-compose.m4.yml up --build
```

**What's implemented:**
- `AspNetCore.HealthChecks.*` for SQL Server, Redis, upstream URLs
- Custom health checks: `DatabaseMigrationHealthCheck`, `ExternalApiHealthCheck`
- ASP.NET Health Check UI at `/health-ui`
- Three endpoints: `/health` (all), `/health/live` (liveness), `/health/ready` (readiness)
- `HealthMetricsPublisher` background service → publishes `health_check_status` gauge to Prometheus
- Grafana alert rule for `DependencyUnhealthy`

**Demo — Degraded Dependency:**
```bash
# Stop Redis
docker-compose stop redis
# Watch:
curl http://localhost:8080/health     # redis → Unhealthy
curl http://localhost:8080/health-ui  # visual red indicator
# Grafana: health_check_status{check_name="redis"} = -1
# AlertManager: DependencyUnhealthy fires after ~1min

# Restore:
docker-compose start redis
```

---

### Milestone 5 — Full Integration

```bash
docker-compose up --build
./demo-failure.sh   # or ./demo-failure.ps1 on Windows
```

**What's included:** All 3 .NET services + Seq + Jaeger + Prometheus + AlertManager + Grafana + SQL Server + Redis + RabbitMQ — everything in one compose file with health checks and restart policies.

---

## Architecture Decisions

| Decision | Rationale |
|----------|-----------|
| **W3C TraceContext** | Vendor-neutral; works with Jaeger, Zipkin, Datadog, Tempo out of the box |
| **OpenTelemetry SDK** | No vendor lock-in for instrumentation; swap exporters without code changes |
| **Serilog** | Richest enrichment ecosystem for .NET; output to any sink (Seq, ELK, Loki) |
| **Prometheus pull model** | Services don't need to know about monitoring; Prometheus scrapes on its schedule |
| **ASP.NET Health Checks** | First-class framework support; integrates with K8s probes directly |
| **Correlation ID middleware** | Cross-cutting concern placed before all other middleware; links logs ↔ traces |

---

## Kubernetes Migration

### docker-compose → Kubernetes mapping

| docker-compose | Kubernetes equivalent |
|---------------|----------------------|
| Service name (e.g., `seq`) | `seq-service.namespace.svc.cluster.local` |
| Environment variables | ConfigMap + Secrets |
| `healthcheck:` | `livenessProbe` + `readinessProbe` |
| Named volumes | PersistentVolumeClaim (PVC) |
| Bridge network | ClusterIP Services + Ingress |
| Jaeger all-in-one | Jaeger Operator (DaemonSet agent) |
| Prometheus static scrape | ServiceMonitor CRD (kube-prometheus-stack) |

### Recommended Helm charts

```bash
# Prometheus + Grafana + AlertManager (all-in-one)
helm repo add prometheus-community https://prometheus-community.github.io/helm-charts
helm install kube-prom-stack prometheus-community/kube-prometheus-stack \
  --namespace monitoring --create-namespace \
  --set grafana.adminPassword=observability

# Jaeger Operator
helm repo add jaegertracing https://jaegertracing.github.io/helm-charts
helm install jaeger-operator jaegertracing/jaeger-operator \
  --namespace observability --create-namespace

# Seq
helm repo add datalust https://helm.datalust.co
helm install seq datalust/seq --namespace observability
```

### Sample K8s Deployment

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: api-service
  namespace: observability-poc
spec:
  replicas: 2
  selector:
    matchLabels:
      app: api-service
  template:
    metadata:
      labels:
        app: api-service
      annotations:
        prometheus.io/scrape: "true"
        prometheus.io/port:   "8080"
        prometheus.io/path:   "/metrics"
    spec:
      containers:
        - name: api-service
          image: your-registry/api-service:latest
          ports:
            - containerPort: 8080
          env:
            - name: Seq__ServerUrl
              valueFrom:
                configMapKeyRef:
                  name: observability-config
                  key: seq-url
          livenessProbe:
            httpGet:
              path: /health/live
              port: 8080
            initialDelaySeconds: 10
            periodSeconds: 15
          readinessProbe:
            httpGet:
              path: /health/ready
              port: 8080
            initialDelaySeconds: 15
            periodSeconds: 10
```

---

## Validation Checklists

### Milestone 1 — Logging
```
[ ] docker-compose -f docker-compose.m1.yml up --build  → services green
[ ] curl http://localhost:8080/Weather           → 200 OK
[ ] Seq http://localhost:8888                           → logs arriving
[ ] Search CorrelationId in Seq                         → single request trace
[ ] Filter @Level='Error' in Seq                        → only errors shown
[ ] CorrelationId in response header X-Correlation-ID   → enricher working
```

### Milestone 2 — Tracing
```
[ ] All 3 services start, health checks pass
[ ] Jaeger: ApiService, OrderService, InventoryService all appear
[ ] POST /orders → end-to-end trace spanning all 3 services
[ ] /simulate/slow → Jaeger shows span > 3s
[ ] /simulate/error → Jaeger shows red error span with stack trace
[ ] W3C traceparent header in outbound HTTP calls
```

### Milestone 3 — Metrics
```
[ ] curl http://localhost:8080/metrics  → Prometheus text format
[ ] Prometheus: Status → Targets → all jobs "UP"
[ ] Grafana: dashboard loads, all 9 panels render
[ ] Load test → request rate panel spikes
[ ] Error test → error rate crosses 5%, HighErrorRate alert fires
[ ] GC panel shows Gen0/Gen1/Gen2 collection rates
```

### Milestone 4 — Health Checks
```
[ ] curl http://localhost:8080/health → all checks shown
[ ] /health-ui → visual dashboard green
[ ] docker-compose stop redis → redis check Unhealthy  
[ ] Grafana: health_check_status{check_name="redis"} = -1
[ ] DependencyUnhealthy alert fires in AlertManager
[ ] docker-compose start redis → alert resolves
```

### Milestone 5 — Full Integration
```
[ ] docker-compose up --build → all 12 services start
[ ] ./demo-failure.sh runs without errors
[ ] Seq: correlated logs across all 3 services for same CorrelationId
[ ] Jaeger: full trace ApiService → OrderService → InventoryService
[ ] Grafana: all dashboards load without manual provisioning
[ ] AlertManager: both alerts fire and resolve during demo
```
