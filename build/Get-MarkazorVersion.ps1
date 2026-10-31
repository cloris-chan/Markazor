param(
    [string] $Version,
    [string] $RefName
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $RefName
}

$resolvedVersion = ([string] $Version).Trim()

if ($resolvedVersion.StartsWith('v', [System.StringComparison]::OrdinalIgnoreCase)) {
    $resolvedVersion = $resolvedVersion.Substring(1)
}

if ($resolvedVersion -notmatch '^\d+\.\d+\.\d+([-.+][0-9A-Za-z.-]+)?$') {
    throw "Invalid Markazor version '$resolvedVersion'. Specify a release tag or version such as 0.3.0-preview.2."
}

Write-Output $resolvedVersion
