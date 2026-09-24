# Builds a Release DLL and packs a Thunderstore/Gale-ready zip into release\.
# Usage (from this folder):  powershell -ExecutionPolicy Bypass -File package.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$env:PATH = "C:\Program Files\dotnet;$env:PATH"

# The plugin version in code must match the Thunderstore manifest.
$code = Get-Content "$root\BossGatedPortals\BossGatedPortals.cs" -Raw
$version = [regex]::Match($code, 'PluginVersion\s*=\s*"([^"]+)"').Groups[1].Value
$manifest = Get-Content "$root\Package\manifest.json" -Raw | ConvertFrom-Json
if ($manifest.version_number -ne $version) {
    throw "Version mismatch: BossGatedPortals.cs says $version, Package\manifest.json says $($manifest.version_number)."
}

dotnet build "$root\BossGatedPortals\BossGatedPortals.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

# Thunderstore layout: manifest, icon, README, CHANGELOG at the top; the DLL under plugins\.
$stage = "$root\release\stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory "$stage\plugins" | Out-Null
Copy-Item "$root\Package\manifest.json", "$root\Package\icon.png", "$root\README.md", "$root\CHANGELOG.md" $stage
Copy-Item "$root\BossGatedPortals\bin\Release\net48\BossGatedPortals.dll" "$stage\plugins"

$zip = "$root\release\BossGatedPortals-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
# Add entries one by one with "/" separators: Compress-Archive and ZipFile.CreateFromDirectory
# on Windows PowerShell 5.1 write "plugins\x.dll", which Thunderstore and Linux servers mishandle.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($zip, 'Create')
try {
    Get-ChildItem $stage -Recurse -File | ForEach-Object {
        $name = $_.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $name) | Out-Null
    }
} finally { $archive.Dispose() }
Remove-Item $stage -Recurse -Force
Write-Host "Created $zip"
