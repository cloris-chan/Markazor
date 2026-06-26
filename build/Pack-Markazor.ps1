param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [string] $Configuration = 'Release',

    [string] $ArtifactsDirectory = 'artifacts/packages'
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ge 7) {
    $PSNativeCommandUseErrorActionPreference = $true
}

$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')
$artifactsPath = Join-Path $repoRoot $ArtifactsDirectory

if (Test-Path -LiteralPath $artifactsPath) {
    Remove-Item -LiteralPath $artifactsPath -Recurse -Force
}

New-Item -ItemType Directory -Path $artifactsPath | Out-Null

dotnet pack (Join-Path $repoRoot 'Markazor.slnx') `
    --configuration $Configuration `
    --no-build `
    -p:PackageVersion=$Version `
    -p:Version=$Version `
    --output $artifactsPath

Write-Output $artifactsPath
