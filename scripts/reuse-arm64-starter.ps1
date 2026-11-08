function Copy-VerifiedArm64Starter {
    param([Parameter(Mandatory)][string]$ProjectRoot, [Parameter(Mandatory)][string]$Destination)

    $expectedSources = @{
        'Klausurfix.Launcher.csproj' = '1BAF3DFBD04A2B5DDC2466CA41F4109CD12446E362883ABC346A6112E9FBA750'
        'Program.cs' = '82059ABE342327822DD89E4A58BA5A57415E460D989F69EF2B486A75ED4AD3C2'
        'TrayApplicationContext.cs' = '6CF2A7CE673C7142671E9C81063F28D76139D6A89DAAE60E9E102A8EC28ACF45'
        'Klausurfix.ico' = '77A5819597C98ADBBAB816FF8C2D482FE5C069D44248732BFE7D1E508591103A'
        'Klausurfix.png' = '69B10F1A80F565E77AF3B86855025304EABD88FDF88E916FD13A63EC00D8A425'
    }
    $launcherRoot = Join-Path $ProjectRoot 'src\Klausurfix.Launcher'
    $files = @(Get-ChildItem -LiteralPath $launcherRoot -File -Recurse | Where-Object {
        $_.FullName.Substring($launcherRoot.Length).TrimStart('\') -notmatch '^(bin|obj)(\\|$)'
    })
    if ($files.Count -ne $expectedSources.Count) { throw 'Starterquellen haben sich geändert. Alten Starter nicht ungeprüft wiederverwenden; Kompatibilität prüfen.' }
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($launcherRoot.Length).TrimStart('\')
            if (-not $expectedSources.ContainsKey($relative)) { throw "Unbekannte Starterdatei: $relative. Kompatibilität prüfen." }
            if ($file.Extension -in @('.cs', '.csproj')) {
                $content = [IO.File]::ReadAllText($file.FullName).Replace("`r`n", "`n")
                $hash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($content))).Replace('-', '')
            } else { $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
            if ($hash -ne $expectedSources[$relative]) { throw "Starterquelle geändert: $relative. Alten Starter nicht ungeprüft wiederverwenden." }
        }
        $activePath = Join-Path $ProjectRoot 'update-entwicklung\Versionsablage\ActiveVersion.cs'
        $active = [IO.File]::ReadAllText($activePath).Replace("`r`n", "`n")
        $activeHash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($active))).Replace('-', '')
        if ($activeHash -ne '6737B34DE46D7E88CA9184493F1E0E8A963CB4D9B29D5CFC81B054695FAC8574') { throw 'Versionsauflösung des Starters geändert. Kompatibilität prüfen.' }
    } finally { $sha.Dispose() }

    $package = Join-Path $ProjectRoot 'release\0.2.12\Klausurfix-Update-0.2.12-win-arm64.zip'
    if (-not (Test-Path -LiteralPath $package -PathType Leaf)) { throw "Geprüftes Starterpaket fehlt: $package" }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($package)
    try {
        $entries = @($zip.Entries | Where-Object { $_.FullName -ceq 'app/Klausurfix.exe' })
        if ($entries.Count -ne 1 -or $entries[0].Length -ne 129296552) { throw 'Starter im Ausgangspaket fehlt oder hat eine falsche Größe.' }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entries[0], $Destination)
    } finally { $zip.Dispose() }
    $starterHash = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash
    if ($starterHash -ne '77E2F69BD8C7BFCB58F86C399B2E021A32CD1B5F5B45D32526CE4999F7800229') {
        Remove-Item -LiteralPath $Destination -Force
        throw 'Prüfsumme des ARM64-Starters stimmt nicht. Build abgebrochen.'
    }
    Write-Output 'Geprüfter ARM64-Starter 0.2.12 wird bytegleich wiederverwendet.'
}
