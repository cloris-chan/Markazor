param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [Parameter(Mandatory = $true)]
    [string] $PackageSource,

    [string] $OutputDirectory = 'artifacts/template-repo',

    [string] $ProjectName = 'MarkazorSite'
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ge 7) {
    $PSNativeCommandUseErrorActionPreference = $true
}

$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')
$packageSourcePath = Resolve-Path -LiteralPath $PackageSource
$outputPath = Join-Path $repoRoot $OutputDirectory
$nugetPackagesPath = Join-Path ([System.IO.Path]::GetTempPath()) ('markazor-template-repo-' + [guid]::NewGuid().ToString('N'))

function Reset-Directory {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    if (Test-Path -LiteralPath $Path) {
        for ($attempt = 1; $attempt -le 3; $attempt++) {
            try {
                Remove-Item -LiteralPath $Path -Recurse -Force
                break
            }
            catch {
                if ($attempt -eq 3) {
                    throw
                }

                Start-Sleep -Seconds $attempt
            }
        }
    }

    New-Item -ItemType Directory -Path $Path | Out-Null
}

Reset-Directory -Path $outputPath
New-Item -ItemType Directory -Path $nugetPackagesPath | Out-Null
$env:NUGET_PACKAGES = $nugetPackagesPath

dotnet new install "Markazor.Templates@$Version" `
    --add-source $packageSourcePath `
    --force

$newArguments = @(
    'new',
    'markazor-site',
    '--name',
    $ProjectName,
    '--MarkazorPackageVersion',
    $Version,
    '--output',
    $outputPath
)

dotnet @newArguments

Write-Output $outputPath
