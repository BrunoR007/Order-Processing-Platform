$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Check-TrackedFiles.ps1')
$root = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
$bundle = Join-Path $artifacts 'history.bundle'
git -C $root bundle create $bundle --all
if ($LASTEXITCODE -ne 0) { throw 'Could not export Git history.' }
# Only versioned history enters this network-isolated scanner, never the working tree or .env.
$image = 'ghcr.io/gitleaks/gitleaks:v8.30.0@sha256:691af3c7c5a48b16f187ce3446d5f194838f91238f27270ed36eef6359a574d9'
docker run --rm --network none --entrypoint sh -v "${bundle}:/history.bundle:ro" $image -c 'git clone /history.bundle /scan >/dev/null 2>&1 && gitleaks git /scan --redact --no-banner'
if ($LASTEXITCODE -ne 0) { throw 'Secret scan failed. Review the redacted findings before publishing.' }
