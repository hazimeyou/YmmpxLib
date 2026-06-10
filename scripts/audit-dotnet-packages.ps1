param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('vulnerable', 'deprecated')]
    [string]$Audit,

    [string]$Target = ".\YMMPXLib.slnx"
)

$ErrorActionPreference = 'Stop'

$arguments = @(
    'list',
    $Target,
    'package',
    "--$Audit",
    '--include-transitive',
    '--format',
    'json',
    '--no-restore'
)

$jsonText = & dotnet @arguments | Out-String
if ($LASTEXITCODE -ne 0) {
    throw "dotnet list package --$Audit failed with exit code $LASTEXITCODE."
}

try {
    $report = $jsonText | ConvertFrom-Json
}
catch {
    throw "Failed to parse dotnet package audit JSON: $($_.Exception.Message)"
}

$findings = @(
    foreach ($project in @($report.projects)) {
        foreach ($framework in @($project.frameworks)) {
            foreach ($packageType in @('topLevelPackages', 'transitivePackages')) {
                foreach ($package in @($framework.$packageType)) {
                    if ($null -eq $package) {
                        continue
                    }

                    [PSCustomObject]@{
                        Project = $project.path
                        Framework = $framework.framework
                        Package = $package.id
                        Version = $package.resolvedVersion
                    }
                }
            }
        }
    }
)

if ($findings.Count -gt 0) {
    $findings | Format-Table -AutoSize | Out-String | Write-Host
    throw "Dependency audit found $($findings.Count) $Audit package(s)."
}

Write-Host "Dependency audit passed: no $Audit packages found."
