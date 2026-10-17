$ErrorActionPreference = 'Stop'
$tracked = @(git ls-files)
if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate tracked files.' }
$forbidden = @($tracked | Where-Object {
    $_ -match '(^|/)\.env($|\.)' -and $_ -notmatch '(^|/)\.env\.example$' -or
    $_ -match '(?i)\.(pfx|p12|pem|key|crt|cer|jks|keystore|kdbx|credentials|dump|backup|dmp|binlog)$' -or
    $_ -match '(?i)(^|/)(\.aws|\.azure|\.kube|\.ssh|secrets|\.secrets)/' -or
    $_ -match '(?i)(^|/)(\.envrc|id_rsa|id_ecdsa|id_ed25519)$' -or
    $_ -match '(?i)(^|/)(secrets\.json|appsettings\..*local\.json|appsettings\.Development\.json|launchSettings\.json)$'
})
if ($forbidden.Count -gt 0) {
    # Report paths only, never file contents or credential values.
    $forbidden | Write-Host
    throw 'Sensitive local file paths are tracked. Remove them from the index before publishing.'
}
Write-Host 'Tracked-file check passed.'
