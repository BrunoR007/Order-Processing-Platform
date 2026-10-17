$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

foreach ($port in 5001..5004) {
    foreach ($probe in @('live', 'ready')) {
        $response = Invoke-WebRequest -UseBasicParsing -Uri "http://localhost:$port/health/$probe" -TimeoutSec 10
        if ($response.StatusCode -ne 200 -or $response.Content -ne 'Healthy') {
            throw "Health check failed on port $port ($probe)."
        }
    }
}

$databaseCheck = @'
set -eu
for service in orders inventory payments notifications; do
    case "$service" in
        orders) password="$ORDERS_DB_PASSWORD" ;;
        inventory) password="$INVENTORY_DB_PASSWORD" ;;
        payments) password="$PAYMENTS_DB_PASSWORD" ;;
        notifications) password="$NOTIFICATIONS_DB_PASSWORD" ;;
    esac
    owner=$(PGPASSWORD="$password" psql -h 127.0.0.1 -U "$service" -d "$service" -Atc "SELECT current_user")
    test "$owner" = "$service"
    for other in orders inventory payments notifications; do
        if [ "$other" != "$service" ]; then
            if PGPASSWORD="$password" psql -h 127.0.0.1 -U "$service" -d "$other" -Atc 'SELECT 1' >/dev/null 2>&1; then
                echo "Isolation failure: $service connected to $other" >&2
                exit 1
            fi
        fi
    done
done
'@

# Windows PowerShell adds CRLF when piping text to native commands.
$databaseCheck | docker compose exec -T postgres bash -c "tr -d '\r' | bash -s"
if ($LASTEXITCODE -ne 0) {
    throw 'Database ownership or connection isolation check failed.'
}

Write-Host 'Verified: eight health endpoints, four database logins, and twelve denied cross-service connections.'
