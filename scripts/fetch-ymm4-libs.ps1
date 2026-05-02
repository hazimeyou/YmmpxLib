param(
    [string]$OutputDir = "libs/YMM4"
)

$ErrorActionPreference = 'Stop'

$apiUrl = 'https://api.github.com/repos/manju-summoner/YukkuriMovieMaker4/releases/latest'
$headers = @{ 'User-Agent' = 'YmmpxLib-CI' }

Write-Host "Fetching latest YMM4 release metadata..."
$release = Invoke-RestMethod -Uri $apiUrl -Headers $headers

$zipAsset = $release.assets |
    Where-Object { $_.name -match '\.zip$' } |
    Select-Object -First 1

if (-not $zipAsset) {
    throw "No .zip asset found in latest YMM4 release: $($release.tag_name)"
}

Write-Host "Latest release: $($release.tag_name)"
Write-Host "Using asset: $($zipAsset.name)"

$tmpRoot = Join-Path $env:TEMP ("ymm4-fetch-" + [Guid]::NewGuid().ToString('N'))
$zipPath = Join-Path $tmpRoot $zipAsset.name
$extractDir = Join-Path $tmpRoot 'extract'

New-Item -ItemType Directory -Force -Path $tmpRoot | Out-Null
New-Item -ItemType Directory -Force -Path $extractDir | Out-Null

Invoke-WebRequest -Uri $zipAsset.browser_download_url -Headers $headers -OutFile $zipPath
Expand-Archive -Path $zipPath -DestinationPath $extractDir

$requiredDlls = @(
    'YukkuriMovieMaker.Plugin.dll',
    'YukkuriMovieMaker.dll',
    'YukkuriMovieMaker.Controls.dll'
)

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

foreach ($dllName in $requiredDlls) {
    $dll = Get-ChildItem -Path $extractDir -Recurse -File -Filter $dllName | Select-Object -First 1
    if (-not $dll) {
        throw "Required DLL not found in release archive: $dllName"
    }

    Copy-Item -Path $dll.FullName -Destination (Join-Path $OutputDir $dllName) -Force
    Write-Host "Copied: $dllName"
}

Write-Host "YMM4 libs prepared at: $(Resolve-Path $OutputDir)"
