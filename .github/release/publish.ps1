param([Parameter(Mandatory)][string]$ReleaseDirectory)
$ErrorActionPreference = 'Stop'
$policy = Get-Content "$PSScriptRoot/policy.json" -Raw | ConvertFrom-Json
$tag = "v$($policy.version)"
python3 "$PSScriptRoot/check_existing.py" $ReleaseDirectory
if ($LASTEXITCODE -ne 0) { throw 'Existing registry versions conflict with the verified candidate or could not be checked' }
foreach ($id in $policy.packages) {
    $package = Join-Path $ReleaseDirectory "packages/$id.$($policy.version).nupkg"
    # A resumed run can encounter an already-pushed version. Both registries are
    # subsequently restored fresh and compared entry-for-entry before announcement.
    dotnet nuget push $package --source https://api.nuget.org/v3/index.json --api-key $env:NUGET_API_KEY --skip-duplicate
    if ($LASTEXITCODE -ne 0) { throw "NuGet publication failed: $id" }
    dotnet nuget push $package --source "https://nuget.pkg.github.com/knightfall/index.json" --api-key $env:GH_TOKEN --skip-duplicate --no-symbols
    if ($LASTEXITCODE -ne 0) { throw "GitHub Packages publication failed: $id" }
}
$assets = Join-Path $env:RUNNER_TEMP 'release-assets'
New-Item -ItemType Directory -Path $assets | Out-Null
Copy-Item "$ReleaseDirectory/packages/*" $assets
Copy-Item "$ReleaseDirectory/release.json", "$ReleaseDirectory/source-inputs.spdx.json" $assets
Copy-Item "$ReleaseDirectory/_manifest/spdx_2.2/manifest.spdx.json" "$assets/build.spdx.json"
foreach ($bundle in @(@('provenance', $env:PROVENANCE_BUNDLE), @('build-sbom', $env:BUILD_SBOM_BUNDLE), @('source-sbom', $env:SOURCE_SBOM_BUNDLE))) {
    $digest = (Get-FileHash $bundle[1] -Algorithm SHA256).Hash.ToLowerInvariant()
    Copy-Item $bundle[1] (Join-Path $assets "$($bundle[0])-$digest.sigstore.json")
}
python3 "$PSScriptRoot/release_archive.py" $ReleaseDirectory "$assets/release-evidence.zip"
if ($LASTEXITCODE -ne 0) { throw 'Cannot archive the complete release evidence' }
gh release view $tag --repo $policy.repository *> $null
if ($LASTEXITCODE -ne 0) {
    gh release create $tag --repo $policy.repository --verify-tag --draft --prerelease --title $tag --notes-file docs/releases/1.0.0-rc.1.md
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create draft release' }
}
$existing = (gh release view $tag --repo $policy.repository --json assets | ConvertFrom-Json).assets.name
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect existing release assets' }
foreach ($file in Get-ChildItem $assets -File) {
    if ($existing -contains $file.Name) {
        $previous = Join-Path $env:RUNNER_TEMP ('previous-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $previous | Out-Null
        gh release download $tag --repo $policy.repository --pattern $file.Name --dir $previous
        if ($LASTEXITCODE -ne 0) { throw 'Cannot verify existing release asset' }
        $old = Join-Path $previous $file.Name
        if ((Get-FileHash $old).Hash -ne (Get-FileHash $file.FullName).Hash) {
            if ($file.Extension -ne '.zip') { throw "Existing release asset differs: $($file.Name)" }
            python3 "$PSScriptRoot/compare-packages.py" $old $file.FullName
            if ($LASTEXITCODE -ne 0) { throw 'Existing release evidence differs' }
        }
        continue
    }
    gh release upload $tag $file.FullName --repo $policy.repository
    if ($LASTEXITCODE -ne 0) { throw 'Cannot attach release evidence; assets are never overwritten' }
}
