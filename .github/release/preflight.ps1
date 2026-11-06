$ErrorActionPreference = 'Stop'
$policy = Get-Content "$PSScriptRoot/policy.json" -Raw | ConvertFrom-Json
$tag = "v$($policy.version)"
$reference = gh api "repos/$($policy.repository)/git/ref/tags/$tag" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $reference.object.type -ne 'commit' -or $reference.object.sha -ne $env:GITHUB_SHA) {
    throw 'The protected lightweight release tag must identify this exact source commit'
}
$checks = gh api "repos/$($policy.repository)/commits/$env:GITHUB_SHA/check-runs?per_page=100" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Cannot read source verification checks' }
foreach ($required in $policy.requiredChecks.PSObject.Properties) {
    $check = $checks.check_runs | Where-Object { $_.name -eq $required.Name -and $_.app.slug -eq $required.Value } | Select-Object -First 1
    if (!$check -or $check.status -ne 'completed' -or $check.conclusion -ne 'success') {
        throw "Required source verification is not successful: $($required.Name)"
    }
}
