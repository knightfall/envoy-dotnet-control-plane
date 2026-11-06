$ErrorActionPreference = 'Stop'
$policy = Get-Content "$PSScriptRoot/policy.json" -Raw | ConvertFrom-Json
$tag = "v$($policy.version)"
$reference = gh api "repos/$($policy.repository)/git/ref/tags/$tag" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $reference.object.type -ne 'commit' -or $reference.object.sha -ne $env:GITHUB_SHA) {
    throw 'The protected lightweight release tag must identify this exact source commit'
}
$checks = gh api "repos/$($policy.repository)/commits/$env:GITHUB_SHA/check-runs?per_page=100" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Cannot read source verification checks' }
$required = @('Workflow locks and security', 'Buf binding proof (ubuntu-latest)', 'Buf binding proof (macos-latest)',
              'Buf binding proof (windows-latest)', 'Offline CLI tests (ubuntu-latest)',
              'Offline CLI tests (macos-latest)', 'Offline CLI tests (windows-latest)')
foreach ($name in $required) {
    $check = $checks.check_runs | Where-Object { $_.name -eq $name -and $_.app.slug -eq 'github-actions' } | Select-Object -First 1
    if (!$check -or $check.status -ne 'completed' -or $check.conclusion -ne 'success') {
        throw "Required source verification is not successful: $name"
    }
}
