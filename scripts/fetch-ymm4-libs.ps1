param(
    [string]$OutputDir = "libs/YMM4",
    [string]$ReleaseTag = "v4.53.0.2",
    [string]$AssetName = "YukkuriMovieMaker_v4.53.0.2.zip",
    [string]$ExpectedSha256 = "60134ca2366e467544a8681fc083b1697691bfda3ef4caee25129071fe885ecf"
)

$ErrorActionPreference = 'Stop'

$apiUrl = "https://api.github.com/repos/manju-summoner/YukkuriMovieMaker4/releases/tags/$ReleaseTag"
$headers = @{ 'User-Agent' = 'YmmpxLib-CI' }

Write-Host "Fetching YMM4 release metadata for tag $ReleaseTag..."
$release = Invoke-RestMethod -Uri $apiUrl -Headers $headers

$zipAsset = $release.assets |
    Where-Object { $_.name -eq $AssetName } |
    Select-Object -First 1

if (-not $zipAsset) {
    throw "Expected YMM4 asset was not found in release $($release.tag_name): $AssetName"
}

Write-Host "Release: $($release.tag_name)"
Write-Host "Using asset: $($zipAsset.name)"

$tmpRoot = Join-Path $env:TEMP ("ymm4-fetch-" + [Guid]::NewGuid().ToString('N'))
$zipPath = Join-Path $tmpRoot $zipAsset.name
$extractDir = Join-Path $tmpRoot 'extract'

try {
    New-Item -ItemType Directory -Force -Path $tmpRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $extractDir | Out-Null

    Invoke-WebRequest -Uri $zipAsset.browser_download_url -Headers $headers -OutFile $zipPath
    $actualSha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    if (-not $actualSha256.Equals($ExpectedSha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "YMM4 asset SHA-256 mismatch. Expected $ExpectedSha256, got $actualSha256."
    }

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
}
finally {
    if (Test-Path -LiteralPath $tmpRoot) {
        Remove-Item -LiteralPath $tmpRoot -Recurse -Force
    }
}
