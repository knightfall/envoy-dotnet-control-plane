param([Parameter(Mandatory)][ValidateSet('candidate', 'nuget', 'github')][string]$Registry,
      [Parameter(Mandatory)][string]$ReleaseDirectory)
$ErrorActionPreference = 'Stop'
$policy = Get-Content "$PSScriptRoot/policy.json" -Raw | ConvertFrom-Json
$consumer = Join-Path $env:RUNNER_TEMP "consumer-$Registry"
New-Item -ItemType Directory -Path $consumer | Out-Null
Copy-Item 'global.json' $consumer
$source = switch ($Registry) {
    'candidate' { [System.Security.SecurityElement]::Escape((Resolve-Path "$ReleaseDirectory/packages").Path) }
    'nuget' { 'https://api.nuget.org/v3/index.json' }
    'github' { 'https://nuget.pkg.github.com/knightfall/index.json' }
}
$dependencySource = ''
$dependencyMapping = ''
$releasePattern = '*'
if ($Registry -ne 'nuget') {
    $dependencySource = '<add key="dependencies" value="https://api.nuget.org/v3/index.json"/>'
    $dependencyMapping = '<packageSource key="dependencies"><package pattern="*"/></packageSource>'
    $releasePattern = 'Knightfall.Envoy.*'
}
@"
<configuration>
  <packageSources><clear/><add key="release" value="$source"/>$dependencySource</packageSources>
  <packageSourceMapping>
    <packageSource key="release"><package pattern="$releasePattern"/></packageSource>
    $dependencyMapping
  </packageSourceMapping>
</configuration>
"@ | Set-Content "$consumer/NuGet.Config"
if ($Registry -eq 'github') { $env:NuGetPackageSourceCredentials_release = "Username=knightfall;Password=$env:GH_TOKEN;ValidAuthenticationTypes=Basic" }
$versions = ($policy.packages | ForEach-Object { "<PackageVersion Include=`"$_`" Version=`"$($policy.version)`"/>" }) -join ''
"<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup><ItemGroup>$versions</ItemGroup></Project>" | Set-Content "$consumer/Directory.Packages.props"
$references = ($policy.packages | ForEach-Object { "<PackageReference Include=`"$_`"/>" }) -join ''
"<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile></PropertyGroup><ItemGroup><FrameworkReference Include=`"Microsoft.AspNetCore.App`"/>$references</ItemGroup></Project>" | Set-Content "$consumer/Consumer.csproj"
Copy-Item "$PSScriptRoot/consumer.cs" "$consumer/Program.cs"
$cache = Join-Path $consumer 'obj/packages'
$restored = $false
for ($attempt = 0; $attempt -lt 12; $attempt++) {
    dotnet restore "$consumer/Consumer.csproj" --packages $cache --no-http-cache
    if ($LASTEXITCODE -eq 0) { $restored = $true; break }
    Start-Sleep -Seconds 10
}
if (!$restored) { throw 'Published packages did not become available' }
dotnet restore "$consumer/Consumer.csproj" --locked-mode --packages $cache
if ($LASTEXITCODE -ne 0) { throw 'Published locked consumer restore failed' }
foreach ($id in $policy.packages) {
    $name = $id.ToLowerInvariant()
    $downloaded = Join-Path $cache "$name/$($policy.version)/$name.$($policy.version).nupkg"
    $original = Join-Path $ReleaseDirectory "packages/$id.$($policy.version).nupkg"
    python3 "$PSScriptRoot/compare-packages.py" $original $downloaded
    if ($LASTEXITCODE -ne 0) { throw "Published package content differs: $id" }
    if ($Registry -eq 'nuget') {
        dotnet nuget verify $downloaded --all
        if ($LASTEXITCODE -ne 0) { throw "NuGet repository signature verification failed: $id" }
    }
}
dotnet run --project "$consumer/Consumer.csproj" --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Published consumer execution failed' }
