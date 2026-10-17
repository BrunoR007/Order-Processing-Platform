$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$target = Join-Path $root '.env'
if (Test-Path $target) {
    throw '.env already exists. Preserve existing credentials or edit it manually.'
}
$template = Join-Path $root '.env.example'
if (-not (Test-Path -LiteralPath $template)) {
    throw 'The public .env.example template is missing. Restore it from Git; keep real credentials in .env.'
}
$templateLines = @(Get-Content -LiteralPath $template)
foreach ($key in @('POSTGRES_ADMIN_PASSWORD', 'ORDERS_DB_PASSWORD', 'INVENTORY_DB_PASSWORD',
    'PAYMENTS_DB_PASSWORD', 'NOTIFICATIONS_DB_PASSWORD', 'RABBITMQ_PASSWORD', 'GRAFANA_ADMIN_PASSWORD')) {
    if (@($templateLines | Where-Object { $_ -match "^${key}=replace-with-.+" }).Count -ne 1) {
        throw "The public template must define exactly one placeholder for $key. No .env was created."
    }
}
$lines = foreach ($line in $templateLines) {
    if ($line -match '^([A-Z_]+)=replace-with-') {
        $name = $Matches[1]
        $bytes = New-Object byte[] 24
        $random = [Security.Cryptography.RandomNumberGenerator]::Create()
        $random.GetBytes($bytes)
        $random.Dispose()
        "$name=$([Convert]::ToBase64String($bytes))"
    } else { $line }
}
[IO.File]::WriteAllLines($target, $lines, (New-Object Text.UTF8Encoding($false)))
Write-Host 'Created ignored .env with distinct random local passwords. No credentials were printed.'
