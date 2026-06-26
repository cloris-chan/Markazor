param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [Parameter(Mandatory = $true)]
    [string] $PackageSource,

    [Parameter(Mandatory = $true)]
    [string] $Repository,

    [Parameter(Mandatory = $true)]
    [string] $Token,

    [string] $WorkDirectory = 'artifacts/template-sync',

    [string] $ProjectName = 'MarkazorSite',

    [string] $Branch = 'main'
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ge 7) {
    $PSNativeCommandUseErrorActionPreference = $true
}

$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')
$packageSourcePath = Resolve-Path -LiteralPath $PackageSource
$workPath = Join-Path $repoRoot $WorkDirectory
$generatedPath = Join-Path $workPath 'generated'
$clonePath = Join-Path $workPath 'repository'
$nugetPackagesPath = Join-Path ([System.IO.Path]::GetTempPath()) ('markazor-template-sync-' + [guid]::NewGuid().ToString('N'))

function Get-AuthenticatedRepositoryUrl {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Repository,

        [Parameter(Mandatory = $true)]
        [string] $Token
    )

    $escapedToken = [System.Uri]::EscapeDataString($Token)
    return "https://x-access-token:$escapedToken@github.com/$Repository.git"
}

function Assert-ChildPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Parent
    )

    $resolvedParent = [System.IO.Path]::GetFullPath($Parent)
    if (-not $resolvedParent.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $resolvedParent += [System.IO.Path]::DirectorySeparatorChar
    }

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    if (-not $resolvedPath.StartsWith($resolvedParent, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate outside $resolvedParent`: $resolvedPath"
    }
}

function Reset-Directory {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    Assert-ChildPath -Path $Path -Parent $repoRoot

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

function Remove-IfExists {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    Assert-ChildPath -Path $Path -Parent $clonePath

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Copy-GeneratedItem {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RelativePath
    )

    $sourcePath = Join-Path $generatedPath $RelativePath
    $destinationPath = Join-Path $clonePath $RelativePath
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        throw "Generated template is missing $RelativePath."
    }

    Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Recurse -Force
}

if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
    throw "Template repository must use owner/repo format: $Repository"
}

if ([string]::IsNullOrWhiteSpace($Token)) {
    throw 'Template repository token is required.'
}

Reset-Directory -Path $workPath
New-Item -ItemType Directory -Path $nugetPackagesPath | Out-Null
$env:NUGET_PACKAGES = $nugetPackagesPath

$repositoryUrl = "https://github.com/$Repository.git"
$authenticatedRepositoryUrl = Get-AuthenticatedRepositoryUrl -Repository $Repository -Token $Token

git clone `
    --branch $Branch `
    --single-branch `
    $authenticatedRepositoryUrl `
    $clonePath
git -C $clonePath remote set-url origin $repositoryUrl

function Push-Authenticated {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $GitArguments
    )

    git -C $clonePath remote set-url origin $authenticatedRepositoryUrl
    try {
        git -C $clonePath @GitArguments
    }
    finally {
        git -C $clonePath remote set-url origin $repositoryUrl
    }
}

dotnet new install "Markazor.Templates@$Version" `
    --add-source $packageSourcePath `
    --force

dotnet new markazor-site `
    --name $ProjectName `
    --MarkazorPackageVersion $Version `
    --output $generatedPath

Remove-IfExists -Path (Join-Path $clonePath 'Directory.Build.props')
Remove-IfExists -Path (Join-Path $clonePath 'Directory.Packages.props')
$generatedSolutionFiles = @(Get-ChildItem -LiteralPath $clonePath -Filter '*.slnx' -File)
foreach ($generatedSolutionFile in $generatedSolutionFiles) {
    Remove-IfExists -Path $generatedSolutionFile.FullName
}
Remove-IfExists -Path (Join-Path $clonePath 'src')
Remove-IfExists -Path (Join-Path $clonePath 'content')
Remove-IfExists -Path (Join-Path $clonePath 'markazor.settings.json')

Copy-GeneratedItem -RelativePath 'Directory.Build.props'
Copy-GeneratedItem -RelativePath 'Directory.Packages.props'
Copy-GeneratedItem -RelativePath "$ProjectName.slnx"
Copy-GeneratedItem -RelativePath 'src'

git -C $clonePath config user.name 'markazor-release'
git -C $clonePath config user.email 'markazor-release@users.noreply.github.com'
git -C $clonePath add -A .

$protectedChanges = @(git -C $clonePath diff --cached --name-only -- README.md .gitignore .gitattributes)
if ($protectedChanges.Count -gt 0) {
    throw "Template sync must not modify protected files: $($protectedChanges -join ', ')"
}

$changes = @(git -C $clonePath diff --cached --name-only)
if ($changes.Count -gt 0) {
    git -C $clonePath commit -m "Release Markazor $Version"
    Push-Authenticated -GitArguments @('push', 'origin', "HEAD:$Branch")
}
else {
    Write-Output 'Template repository already matches the generated skeleton.'
}

git -C $clonePath tag -f "v$Version"
Push-Authenticated -GitArguments @('push', '--force', 'origin', "v$Version")

Write-Output $clonePath
