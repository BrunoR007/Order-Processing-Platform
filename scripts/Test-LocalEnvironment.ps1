$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$artifacts = [IO.Path]::GetFullPath((Join-Path $root 'artifacts'))
$temporary = Join-Path $artifacts "environment-test-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force -Path (Join-Path $temporary 'scripts') | Out-Null
$generator = Join-Path $temporary 'scripts/Initialize-LocalEnvironment.ps1'
$target = Join-Path $temporary '.env'
$template = Join-Path $temporary '.env.example'
Copy-Item (Join-Path $PSScriptRoot 'Initialize-LocalEnvironment.ps1') $generator

function Assert-GeneratorRejects {
    $rejected = $false
    try { & $generator } catch { $rejected = $true }
    if (-not $rejected) { throw 'Expected the generator to reject invalid input.' }
}

try {
    Assert-GeneratorRejects
    if (Test-Path $target) { throw 'Missing template must not create .env.' }
    [IO.File]::WriteAllText($template, '')
    Assert-GeneratorRejects
    if (Test-Path $target) { throw 'Empty template must not create .env.' }
    Copy-Item (Join-Path $root '.env.example') $template -Force
    & $generator
    $lines = @(Get-Content -LiteralPath $target)
    $passwords = @($lines | Where-Object { $_ -match '^[A-Z_]*PASSWORD=' } | ForEach-Object { ($_ -split '=', 2)[1] })
    if ($passwords.Count -ne 7 -or @($passwords | Sort-Object -Unique).Count -ne 7 -or
        @($lines | Where-Object { $_ -match '=replace-with-' }).Count -gt 0) {
        throw 'Expected seven distinct generated passwords and no remaining placeholders.'
    }
    $before = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    Assert-GeneratorRejects
    if ($before -ne (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash) {
        throw 'Existing credentials must never be overwritten.'
    }
    Write-Host 'Environment regression checks passed: missing/empty templates rejected, random credentials generated, existing file preserved.'
} finally {
    $resolved = [IO.Path]::GetFullPath($temporary)
    if (-not $resolved.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing cleanup outside the expected artifacts directory.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
