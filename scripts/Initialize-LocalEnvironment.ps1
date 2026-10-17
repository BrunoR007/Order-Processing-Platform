$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$target = Join-Path $root '.env'
if (Test-Path $target) {
    throw '.env already exists. Preserve existing credentials or edit it manually.'
}
$lines = foreach ($line in Get-Content (Join-Path $root '.env.example')) {
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
