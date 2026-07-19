param(
    [string] $Version,
    [string] $RefName
)

$ErrorActionPreference = 'Stop'

function Get-RepositoryVersion {
    $repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')
    [xml] $directoryBuildProps = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props')
    $versionPrefix = $directoryBuildProps.Project.PropertyGroup.VersionPrefix |
        Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($versionPrefix)) {
        throw 'Directory.Build.props does not define VersionPrefix.'
    }

    $versionSuffix = $directoryBuildProps.Project.PropertyGroup.VersionSuffix |
        Select-Object -First 1

    $resolvedVersion = $versionPrefix.Trim()
    if (-not [string]::IsNullOrWhiteSpace($versionSuffix)) {
        $resolvedVersion += "-$($versionSuffix.Trim())"
    }

    return $resolvedVersion
}

if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $resolvedVersion = $Version.Trim()
}
elseif (-not [string]::IsNullOrWhiteSpace($RefName) -and $RefName.StartsWith('v', [System.StringComparison]::OrdinalIgnoreCase)) {
    $resolvedVersion = $RefName.Substring(1)
}
else {
    $resolvedVersion = Get-RepositoryVersion
}

if ($resolvedVersion.StartsWith('v', [System.StringComparison]::OrdinalIgnoreCase)) {
    $resolvedVersion = $resolvedVersion.Substring(1)
}

if ($resolvedVersion -notmatch '^\d+\.\d+\.\d+([-.+][0-9A-Za-z.-]+)?$') {
    throw "Invalid Markazor version '$resolvedVersion'. Expected a SemVer-like value such as 0.2.0-preview.1."
}

Write-Output $resolvedVersion
