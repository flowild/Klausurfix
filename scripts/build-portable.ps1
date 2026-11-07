param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$PackageVersion = '0.1.9',
    [switch]$UpdateOnly
)

$ErrorActionPreference = 'Stop'
$packageVersion = $PackageVersion
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = Join-Path $projectRoot 'release'
$projectPath = Join-Path $projectRoot 'src\Klausurfix.Web\Klausurfix.Web.csproj'
$launcherPath = Join-Path $projectRoot 'src\Klausurfix.Launcher\Klausurfix.Launcher.csproj'
$nugetRoot = Join-Path $env:USERPROFILE '.nuget\packages'
$runtimes = @('win-arm64', 'win-x64')
$packageBuilder = Join-Path $projectRoot 'update-entwicklung\Paketbau\Paketbau.csproj'
$assistantBuilder = Join-Path $projectRoot 'update-entwicklung\erzeuge-pruef-assistent.ps1'
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
foreach ($rid in $runtimes) {
    $names = if ($UpdateOnly) { @("Klausurfix-Update-$packageVersion-$rid.exe") } else { @("Klausurfix-$packageVersion-$rid.zip", "Klausurfix-Update-$packageVersion-$rid.zip", "Klausurfix-Update-$packageVersion-$rid.exe") }
    foreach ($name in $names) {
        if (Test-Path -LiteralPath (Join-Path $outputRoot $name)) { throw "Ausgabedatei existiert bereits: $name" }
    }
}
$workRoot = Join-Path $outputRoot ('.portable-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workRoot | Out-Null

try {
    foreach ($rid in $runtimes) {
        $zipPath = Join-Path $outputRoot "Klausurfix-$packageVersion-$rid.zip"
        if (-not $UpdateOnly -and (Test-Path -LiteralPath $zipPath)) { throw "Ausgabedatei existiert bereits: $zipPath" }
        $packageRoot = Join-Path $workRoot "Klausurfix-$packageVersion-$rid"
        $appDir = Join-Path $packageRoot 'app'
        $buildDir = Join-Path $workRoot "build-$rid"
        New-Item -ItemType Directory -Force -Path $appDir | Out-Null
        & dotnet publish $projectPath -c Release -r $rid --self-contained true -o $appDir --artifacts-path $buildDir -p:Version=$packageVersion -p:DebugType=None -p:NuGetAudit=false
        if ($LASTEXITCODE -ne 0) { throw "Build fehlgeschlagen: $rid" }
        & dotnet publish $launcherPath -c Release -r $rid --self-contained true -o $packageRoot --artifacts-path (Join-Path $workRoot "launcher-$rid") -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=$packageVersion -p:DebugType=None -p:NuGetAudit=false
        if ($LASTEXITCODE -ne 0) { throw "Starter-Build fehlgeschlagen: $rid" }
        [System.IO.File]::WriteAllText((Join-Path $packageRoot 'update-starter-v1.txt'), 'Klausurfix update starter v1', [System.Text.Encoding]::ASCII)
        [System.IO.File]::WriteAllText((Join-Path $packageRoot 'basis-version.txt'), "$packageVersion|$rid", [System.Text.Encoding]::ASCII)
        $licensesDir = Join-Path $packageRoot 'Lizenzen'
        New-Item -ItemType Directory -Force -Path $licensesDir | Out-Null
        $readme = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'PORTABEL-LESEN.txt') -Raw -Encoding UTF8
        $readme = [regex]::Replace($readme, '(?m)^(Klausurfix )\d+\.\d+\.\d+', { param($match) $match.Groups[1].Value + $packageVersion })
        [System.IO.File]::WriteAllText((Join-Path $packageRoot 'Bitte zuerst lesen.txt'), $readme, [System.Text.UTF8Encoding]::new($false))

        # Include the license expressions and packaged license/notice files for the resolved dependencies.
        $assetsPath = Join-Path $buildDir 'obj\Klausurfix.Web\project.assets.json'
        $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
        $inventory = [System.Collections.Generic.List[string]]::new()
        foreach ($library in $assets.libraries.PSObject.Properties) {
            if ($library.Value.type -ne 'package') { continue }
            $packagePath = Join-Path $nugetRoot $library.Value.path
            $nuspec = Get-ChildItem -LiteralPath $packagePath -Filter '*.nuspec' -File | Select-Object -First 1
            if (-not $nuspec) { throw "Paketmetadaten fehlen: $($library.Name)" }
            [xml]$metadata = Get-Content -LiteralPath $nuspec.FullName -Raw
            $license = $metadata.package.metadata.license
            $licenseText = if ($license -is [string]) { $license } elseif ($license) { $license.'#text' } else { $metadata.package.metadata.licenseUrl }
            $inventory.Add("$($library.Name) - $licenseText")
            $files = @(Get-ChildItem -LiteralPath $packagePath -File | Where-Object { $_.Name -match '^(license|licence|copying|notice|third.?party.?notices)(\.|$)' })
            if ($files.Count -gt 0) {
                $licensePackageDir = Join-Path $licensesDir ($library.Name -replace '/', '-')
                New-Item -ItemType Directory -Force -Path $licensePackageDir | Out-Null
                foreach ($file in $files) { Copy-Item -LiteralPath $file.FullName -Destination $licensePackageDir }
            }
        }
        foreach ($runtimeName in @("microsoft.netcore.app.runtime.$rid", "microsoft.aspnetcore.app.runtime.$rid", "microsoft.windowsdesktop.app.runtime.$rid")) {
            $runtimePackage = Get-ChildItem -LiteralPath (Join-Path $nugetRoot $runtimeName) -Directory | Sort-Object Name -Descending | Select-Object -First 1
            if (-not $runtimePackage) { throw "Laufzeitlizenz fehlt: $runtimeName" }
            $licensePackageDir = Join-Path $licensesDir "$runtimeName-$($runtimePackage.Name)"
            New-Item -ItemType Directory -Force -Path $licensePackageDir | Out-Null
            Get-ChildItem -LiteralPath $runtimePackage.FullName -File | Where-Object { $_.Name -match '^(license|licence|copying|notice|third.?party.?notices)(\.|$)' } | Copy-Item -Destination $licensePackageDir
        }
        $questPackage = Join-Path $nugetRoot 'questpdf\2026.9.1\ExternalDependencyLicenses'
        if (Test-Path -LiteralPath $questPackage) { Copy-Item -LiteralPath $questPackage -Destination (Join-Path $licensesDir 'QuestPDF-Abhaengigkeiten') -Recurse }
        $inventory | Sort-Object | Set-Content -LiteralPath (Join-Path $licensesDir 'Paketliste.txt') -Encoding UTF8

        if (-not $UpdateOnly) {
            Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath -CompressionLevel Optimal
            Write-Output "Erstellt: $zipPath"
        }
        $updatePackage = Join-Path $(if ($UpdateOnly) { $workRoot } else { $outputRoot }) "Klausurfix-Update-$packageVersion-$rid.zip"
        & dotnet run --project $packageBuilder -- $packageRoot $packageVersion $rid $updatePackage
        if ($LASTEXITCODE -ne 0) { throw "Updatepaket konnte nicht erstellt werden: $rid" }
        $updateExe = Join-Path $outputRoot "Klausurfix-Update-$packageVersion-$rid.exe"
        & powershell -NoProfile -ExecutionPolicy Bypass -File $assistantBuilder -Paket $updatePackage -Architektur $rid -Ausgabedatei $updateExe
        if ($LASTEXITCODE -ne 0) { throw "Update-EXE konnte nicht erstellt werden: $rid" }
    }
} finally {
    # Only remove the unique staging directory created above, after verifying its resolved location.
    $resolvedWork = (Resolve-Path -LiteralPath $workRoot).Path
    $resolvedOutput = (Resolve-Path -LiteralPath $outputRoot).Path.TrimEnd('\') + '\'
    if ($resolvedWork.StartsWith($resolvedOutput, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedWork) -match '^\.portable-[0-9a-f]{32}$') {
        Remove-Item -LiteralPath $resolvedWork -Recurse -Force
    }
}
