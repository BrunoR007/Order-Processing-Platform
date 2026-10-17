param(
    [string]$BaseUrl = 'http://localhost:5001',
    [ValidateSet('Confirmed', 'Cancelled', 'Either')]
    [string]$Expected = 'Either',
    [ValidateRange(1, 100)]
    [int]$Quantity = 1
)
$ErrorActionPreference = 'Stop'
$headers = @{ 'Idempotency-Key' = [Guid]::NewGuid().ToString(); 'X-Correlation-Id' = [Guid]::NewGuid().ToString() }
$payload = @{ lines = @(@{ sku = 'BOOK-DOTNET'; quantity = $Quantity }) } | ConvertTo-Json -Depth 4
$order = Invoke-RestMethod -Uri "$BaseUrl/orders" -Method Post -ContentType 'application/json' -Headers $headers -Body $payload
Write-Host "Order $($order.id), correlation $($order.correlationId)"
$deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
do {
    $order = Invoke-RestMethod "$BaseUrl/orders/$($order.id)"
    if ($order.status -in @('Confirmed', 'Cancelled')) { break }
    Start-Sleep -Milliseconds 250
} while ([DateTimeOffset]::UtcNow -lt $deadline)
if ($order.status -notin @('Confirmed', 'Cancelled')) { throw "Order did not reach a terminal state: $($order.status)" }
if ($Expected -ne 'Either' -and $order.status -ne $Expected) { throw "Expected $Expected, received $($order.status)" }
$order | Select-Object id, status, amount, currency, correlationId, failureReason | Format-List
