#!/usr/bin/env bash
# =============================================================================
# demo-failure.sh — Observability POC End-to-End Demo Failure Scenario
#
# Triggers a cascading failure visible across all four observability pillars:
#   Logs (Seq) → Traces (Jaeger) → Metrics (Grafana) → Alerts (AlertManager)
#
# Prerequisites: docker-compose up --build  (wait for all services healthy)
# Usage:         ./demo-failure.sh
# =============================================================================
set -euo pipefail

API="http://localhost:8080"
SEQ="http://localhost:8888"
JAEGER="http://localhost:16686"
GRAFANA="http://localhost:3000"
AM="http://localhost:9093"

BOLD="\033[1m"
GREEN="\033[0;32m"
YELLOW="\033[0;33m"
RED="\033[0;31m"
RESET="\033[0m"

echo ""
echo -e "${BOLD}=================================================${RESET}"
echo -e "${BOLD}  Observability POC — Demo Failure Scenario      ${RESET}"
echo -e "${BOLD}=================================================${RESET}"
echo ""
echo "Services:"
echo "  API         → $API"
echo "  Seq (Logs)  → $SEQ"
echo "  Jaeger      → $JAEGER"
echo "  Grafana     → $GRAFANA"
echo "  AlertMgr    → $AM"
echo ""

# ─── Pre-flight check ─────────────────────────────────────────────────────────
echo -e "${BOLD}[Pre-flight] Checking API health...${RESET}"
if ! curl -sf "$API/health/live" > /dev/null; then
  echo -e "${RED}ERROR: API is not reachable. Run: docker-compose up --build${RESET}"
  exit 1
fi
echo -e "${GREEN}  ✓ API is healthy${RESET}"
echo ""

# ─── Step 1: Baseline traffic ─────────────────────────────────────────────────
echo -e "${BOLD}[Step 1] Generating baseline traffic (100 requests)...${RESET}"
for i in $(seq 1 50); do
  curl -sf "$API/weatherforecast" > /dev/null
  curl -sf "$API/orders" > /dev/null
done
echo -e "${GREEN}  ✓ 100 requests sent${RESET}"
echo -e "${YELLOW}  → Grafana: request rate panel should show spike${RESET}"
sleep 5

echo ""
# ─── Step 2: Slow database queries ────────────────────────────────────────────
echo -e "${BOLD}[Step 2] Simulating slow database queries (10 x 3s)...${RESET}"
for i in $(seq 1 10); do
  curl -sf "$API/simulate/slow?delayMs=3000" > /dev/null &
done
wait
echo -e "${GREEN}  ✓ 10 simultaneous 3s slow queries completed${RESET}"
echo -e "${YELLOW}  → Jaeger: search 'SlowDatabaseQuery' — look for spans > 2s${RESET}"
echo -e "${YELLOW}  → Grafana: P95 latency panel spiking${RESET}"
sleep 10

echo ""
# ─── Step 3: Error rate spike ─────────────────────────────────────────────────
echo -e "${BOLD}[Step 3] Triggering error rate spike (30 errors over 60s)...${RESET}"
for i in $(seq 1 30); do
  curl -sf "$API/simulate/error" > /dev/null || true
  sleep 2
done
echo -e "${GREEN}  ✓ 30 simulated errors triggered${RESET}"
echo -e "${YELLOW}  → Seq: filter @Level='Error' — CorrelationIds visible${RESET}"
echo -e "${YELLOW}  → Jaeger: red error spans with full exception detail${RESET}"
echo -e "${YELLOW}  → Grafana: error rate crossing 5% threshold${RESET}"
echo -e "${YELLOW}  → Wait ~2 minutes for HighErrorRate alert to fire in AlertManager${RESET}"
sleep 15

echo ""
# ─── Step 4: Kill Redis to trigger health check failure ───────────────────────
echo -e "${BOLD}[Step 4] Stopping Redis to trigger dependency health check failure...${RESET}"
docker-compose stop redis
echo -e "${GREEN}  ✓ Redis stopped${RESET}"
echo -e "${YELLOW}  → curl $API/health → redis = Unhealthy${RESET}"
echo -e "${YELLOW}  → Health UI: $API/health-ui → red indicator for redis${RESET}"
echo -e "${YELLOW}  → Grafana: health_check_status{check_name='redis'} = -1${RESET}"
echo -e "${YELLOW}  → Wait ~1 minute for DependencyUnhealthy alert to fire${RESET}"

echo ""
echo "  Waiting 90 seconds for alerts to trigger..."
sleep 90

echo ""
# ─── Step 5: Restore Redis ────────────────────────────────────────────────────
echo -e "${BOLD}[Step 5] Restoring Redis...${RESET}"
docker-compose start redis
echo -e "${GREEN}  ✓ Redis restored${RESET}"
echo -e "${YELLOW}  → Alerts should resolve in ~30 seconds${RESET}"
echo -e "${YELLOW}  → AlertManager: status changes from FIRING to RESOLVED${RESET}"

echo ""
echo -e "${BOLD}=================================================${RESET}"
echo -e "${BOLD}  Demo complete! Review each pillar:              ${RESET}"
echo -e "${BOLD}=================================================${RESET}"
echo ""
echo -e "  ${GREEN}Logs:${RESET}    $SEQ"
echo -e "           Filter: @Level='Error'  |  CorrelationId='<id>'"
echo ""
echo -e "  ${GREEN}Traces:${RESET}  $JAEGER"
echo -e "           Service: ApiService  |  Operation: SlowDatabaseQuery"
echo ""
echo -e "  ${GREEN}Metrics:${RESET} $GRAFANA"
echo -e "           Dashboard: Observability POC — ApiService"
echo ""
echo -e "  ${GREEN}Alerts:${RESET}  $AM"
echo -e "           Check: HighErrorRate, DependencyUnhealthy"
echo ""
