param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [Parameter(Mandatory = $true)]
    [string] $PackageSource,

    [string] $WorkDirectory = 'artifacts/template-smoke',

    [string] $ProjectName = 'SmokeSite'
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ge 7) {
    $PSNativeCommandUseErrorActionPreference = $true
}

$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')
$packageSourcePath = Resolve-Path -LiteralPath $PackageSource
$workPath = Join-Path $repoRoot $WorkDirectory
$generatedPath = Join-Path $workPath $ProjectName
$publishPath = Join-Path $workPath 'publish'
$emptyPublishPath = Join-Path $workPath 'publish-empty'
$webPublishPath = Join-Path $publishPath 'web'
$functionsPublishPath = Join-Path $publishPath 'functions'
$emptyWebPublishPath = Join-Path $emptyPublishPath 'web'
$emptyFunctionsPublishPath = Join-Path $emptyPublishPath 'functions'
$nugetPackagesPath = Join-Path ([System.IO.Path]::GetTempPath()) ('markazor-template-smoke-' + [guid]::NewGuid().ToString('N'))
$consumerPath = Join-Path $nugetPackagesPath 'consumer'
$siteConsumerPath = Join-Path $nugetPackagesPath 'site-consumer'
$siteConsumerWebPath = Join-Path (Join-Path $siteConsumerPath 'src') 'SiteBuildConsumer.Web'
$siteConsumerPublishPath = Join-Path $nugetPackagesPath 'site-consumer-publish'
$themesConsumerPath = Join-Path $nugetPackagesPath 'themes-consumer'
$solutionPath = Join-Path $generatedPath "$ProjectName.slnx"
$webProjectDirectory = Join-Path (Join-Path $generatedPath 'src') "$ProjectName.Web"
$webProjectPath = Join-Path (Join-Path (Join-Path $generatedPath 'src') "$ProjectName.Web") "$ProjectName.Web.csproj"
$functionsProjectPath = Join-Path (Join-Path (Join-Path $generatedPath 'src') "$ProjectName.Functions") "$ProjectName.Functions.csproj"

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

function Write-SmokeFile {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RelativePath,

        [Parameter(Mandatory = $true)]
        [string] $Content
    )

    $path = Join-Path $generatedPath $RelativePath
    $directory = Split-Path -Parent $path
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory | Out-Null
    }

    Set-Content -LiteralPath $path -Value $Content -Encoding utf8 -NoNewline
}

function Test-StringContains {
    param(
        [AllowNull()]
        [string] $Text,

        [Parameter(Mandatory = $true)]
        [string] $Value,

        [System.StringComparison] $Comparison = [System.StringComparison]::Ordinal
    )

    if ($null -eq $Text) {
        return $false
    }

    return $Text.IndexOf($Value, $Comparison) -ge 0
}

function Convert-ToSlashPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    return [System.IO.Path]::GetFullPath($Path).Replace('\', '/')
}

function Assert-NoMixedLineEndings {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RootPath
    )

    $excludedDirectoryNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $excludedDirectoryNames.Add('bin') | Out-Null
    $excludedDirectoryNames.Add('obj') | Out-Null

    $mixedFiles = @(
        Get-ChildItem -LiteralPath $RootPath -Recurse -File |
            Where-Object {
                $directory = $_.Directory
                while ($null -ne $directory) {
                    if ($excludedDirectoryNames.Contains($directory.Name)) {
                        return $false
                    }

                    $directory = $directory.Parent
                }

                return $true
            } |
            ForEach-Object {
                $bytes = [System.IO.File]::ReadAllBytes($_.FullName)
                if ($bytes.Length -gt 0 -and [Array]::IndexOf($bytes, [byte] 0) -lt 0) {
                    $lfCount = 0
                    $crlfCount = 0
                    $bareCrCount = 0
                    for ($index = 0; $index -lt $bytes.Length; $index++) {
                        if ($bytes[$index] -eq 10) {
                            $lfCount++
                            if ($index -gt 0 -and $bytes[$index - 1] -eq 13) {
                                $crlfCount++
                            }
                        }
                        elseif ($bytes[$index] -eq 13 -and ($index + 1 -ge $bytes.Length -or $bytes[$index + 1] -ne 10)) {
                            $bareCrCount++
                        }
                    }

                    $bareLfCount = $lfCount - $crlfCount
                    if ((($crlfCount -gt 0) -and ($bareLfCount -gt 0)) -or $bareCrCount -gt 0) {
                        [pscustomobject] @{
                            Path = $_.FullName
                            CrlfCount = $crlfCount
                            LfCount = $bareLfCount
                            CrCount = $bareCrCount
                        }
                    }
                }
            }
    )

    if ($mixedFiles.Count -gt 0) {
        $formatted = $mixedFiles |
            ForEach-Object { "$($_.Path) (CRLF=$($_.CrlfCount), LF=$($_.LfCount), CR=$($_.CrCount))" }
        throw "Generated template output must not contain mixed line endings. Files: $($formatted -join '; ')"
    }
}

function Invoke-DotNetBuildCapturingFailure {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ProjectPath
    )

    $previousNativeCommandPreference = $null
    if ($PSVersionTable.PSVersion.Major -ge 7) {
        $previousNativeCommandPreference = $PSNativeCommandUseErrorActionPreference
        $PSNativeCommandUseErrorActionPreference = $false
    }

    try {
        $output = @(& dotnet build $ProjectPath --configuration Release --no-restore 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        if ($PSVersionTable.PSVersion.Major -ge 7) {
            $PSNativeCommandUseErrorActionPreference = $previousNativeCommandPreference
        }
    }

    return [pscustomobject] @{
        ExitCode = $exitCode
        Output = $output
    }
}

if (-not (Test-Path -LiteralPath $workPath)) {
    New-Item -ItemType Directory -Path $workPath | Out-Null
}

Reset-Directory -Path $generatedPath
Reset-Directory -Path $publishPath
Reset-Directory -Path $emptyPublishPath
New-Item -ItemType Directory -Path $nugetPackagesPath | Out-Null
$env:NUGET_PACKAGES = $nugetPackagesPath

dotnet new install "Markazor.Templates@$Version" `
    --add-source $packageSourcePath `
    --force

dotnet new blazorwasm `
    --name ExistingSite `
    --framework net10.0 `
    --output $consumerPath `
    --no-restore

$consumerProjectPath = Join-Path $consumerPath 'ExistingSite.csproj'
$consumerNuGetConfigPath = Join-Path $consumerPath 'NuGet.config'
Set-Content `
    -LiteralPath $consumerNuGetConfigPath `
    -Value @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <config>
    <add key="globalPackagesFolder" value="$nugetPackagesPath" />
  </config>
  <packageSources>
    <clear />
    <add key="markazor-local" value="$packageSourcePath" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@

dotnet add $consumerProjectPath package Markazor --version $Version --no-restore
dotnet restore $consumerProjectPath --configfile $consumerNuGetConfigPath

$consumerWebRoot = Join-Path $consumerPath 'wwwroot'
$consumerStaticFiles = Get-ChildItem -LiteralPath $consumerWebRoot -Recurse -File |
    ForEach-Object {
        [pscustomobject] @{
            Path = $_.FullName
            Hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    }

$consumerSiteBuildProperty = (dotnet msbuild $consumerProjectPath -getProperty:MarkazorEnableSiteBuild).Trim()
if ($consumerSiteBuildProperty -ne 'false') {
    throw "Markazor package consumers must not enable destructive site generation by default. Actual: $consumerSiteBuildProperty"
}

dotnet build $consumerProjectPath --configuration Release --no-restore

foreach ($staticFile in $consumerStaticFiles) {
    if (-not (Test-Path -LiteralPath $staticFile.Path)) {
        throw "Markazor package build removed an existing consumer static file: $($staticFile.Path)"
    }

    $currentHash = (Get-FileHash -LiteralPath $staticFile.Path -Algorithm SHA256).Hash
    if ($currentHash -ne $staticFile.Hash) {
        throw "Markazor package build changed an existing consumer static file: $($staticFile.Path)"
    }
}

dotnet new blazorwasm `
    --name SiteBuildConsumer `
    --framework net10.0 `
    --output $siteConsumerWebPath `
    --no-restore

$siteConsumerProjectPath = Join-Path $siteConsumerWebPath 'SiteBuildConsumer.csproj'
$siteConsumerNuGetConfigPath = Join-Path $siteConsumerWebPath 'NuGet.config'
$siteConsumerSourceWebRoot = Join-Path $siteConsumerWebPath 'wwwroot'
$siteConsumerPublicRoot = Join-Path $siteConsumerPath 'public'
Copy-Item -LiteralPath $consumerNuGetConfigPath -Destination $siteConsumerNuGetConfigPath
New-Item -ItemType Directory -Path $siteConsumerPublicRoot | Out-Null
Get-ChildItem -LiteralPath $siteConsumerSourceWebRoot -Force |
    Move-Item -Destination $siteConsumerPublicRoot
Remove-Item -LiteralPath $siteConsumerSourceWebRoot -Recurse -Force

[xml] $siteConsumerProjectXml = Get-Content -LiteralPath $siteConsumerProjectPath
$siteConsumerPropertyGroup = $siteConsumerProjectXml.CreateElement('PropertyGroup')
$siteBuildProperty = $siteConsumerProjectXml.CreateElement('MarkazorEnableSiteBuild')
$siteBuildProperty.InnerText = 'true'
$siteConsumerPropertyGroup.AppendChild($siteBuildProperty) | Out-Null
$siteConsumerProjectXml.Project.AppendChild($siteConsumerPropertyGroup) | Out-Null
$siteConsumerProjectXml.Save($siteConsumerProjectPath)

dotnet add $siteConsumerProjectPath package Markazor --version $Version --no-restore
dotnet restore $siteConsumerProjectPath --configfile $siteConsumerNuGetConfigPath

$siteConsumerSiteBuildProperty = (dotnet msbuild $siteConsumerProjectPath -getProperty:MarkazorEnableSiteBuild).Trim()
$siteConsumerServiceWorkerManifest = (dotnet msbuild $siteConsumerProjectPath -getProperty:ServiceWorkerAssetsManifest).Trim()
if ($siteConsumerSiteBuildProperty -ne 'true') {
    throw 'Direct package consumers must be able to explicitly enable the Markazor site build.'
}

if ($siteConsumerServiceWorkerManifest -ne 'service-worker-assets.js') {
    throw "Markazor site build must default ServiceWorkerAssetsManifest. Actual: $siteConsumerServiceWorkerManifest"
}

dotnet build $siteConsumerProjectPath --configuration Release --no-restore
if (Test-Path -LiteralPath $siteConsumerSourceWebRoot) {
    throw 'Markazor site build recreated the direct consumer source wwwroot.'
}

dotnet publish $siteConsumerProjectPath `
    --configuration Release `
    --no-restore `
    --output $siteConsumerPublishPath

$siteConsumerPublishedWebRoot = Join-Path $siteConsumerPublishPath 'wwwroot'
foreach ($siteConsumerExpectedFile in @(
        'index.html',
        'service-worker.js',
        'service-worker-assets.js',
        (Join-Path 'css' 'app.css'),
        (Join-Path (Join-Path '_content' 'Markazor.Themes') 'theme.css'))) {
    $siteConsumerPublishedFile = Join-Path $siteConsumerPublishedWebRoot $siteConsumerExpectedFile
    if (-not (Test-Path -LiteralPath $siteConsumerPublishedFile)) {
        throw "Direct Markazor site build publish is missing expected file: $siteConsumerPublishedFile"
    }
}

dotnet new blazorwasm `
    --name ThemesOnly `
    --framework net10.0 `
    --output $themesConsumerPath `
    --no-restore

$themesConsumerProjectPath = Join-Path $themesConsumerPath 'ThemesOnly.csproj'
$themesConsumerNuGetConfigPath = Join-Path $themesConsumerPath 'NuGet.config'
Copy-Item -LiteralPath $consumerNuGetConfigPath -Destination $themesConsumerNuGetConfigPath
dotnet add $themesConsumerProjectPath package Markazor.Themes --version $Version --no-restore
dotnet restore $themesConsumerProjectPath --configfile $themesConsumerNuGetConfigPath
dotnet build $themesConsumerProjectPath --configuration Release --no-restore

dotnet new markazor-site `
    --name $ProjectName `
    --output $generatedPath

Assert-NoMixedLineEndings -RootPath $generatedPath

$generatedNuGetConfigPath = Join-Path $generatedPath 'NuGet.config'
if (Test-Path -LiteralPath $generatedNuGetConfigPath) {
    throw 'Generated template must not include NuGet.config.'
}

$generatedCentralPackagesPath = Join-Path $generatedPath 'Directory.Packages.props'
[xml] $generatedCentralPackagesXml = Get-Content -LiteralPath $generatedCentralPackagesPath
$generatedMarkazorPackageVersions = @(
    $generatedCentralPackagesXml.Project.ItemGroup.PackageVersion |
        Where-Object { $_.Include -in @('Markazor', 'Markazor.Api', 'Markazor.Core', 'Markazor.Themes') } |
        ForEach-Object {
            [pscustomobject] @{
                Include = $_.Include
                Version = $_.Version
            }
        }
)

foreach ($expectedPackage in @('Markazor', 'Markazor.Api', 'Markazor.Core', 'Markazor.Themes')) {
    $generatedPackage = @($generatedMarkazorPackageVersions | Where-Object { $_.Include -eq $expectedPackage })[0]
    if ($null -eq $generatedPackage) {
        throw "Generated Directory.Packages.props is missing $expectedPackage."
    }

    if ($generatedPackage.Version -ne $Version) {
        throw "Generated Directory.Packages.props must use the installed template package version for $expectedPackage. Expected: $Version. Actual: $($generatedPackage.Version)"
    }
}

Set-Content `
    -LiteralPath $generatedNuGetConfigPath `
    -Value @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <config>
    <add key="globalPackagesFolder" value="$nugetPackagesPath" />
  </config>
  <packageSources>
    <clear />
    <add key="markazor-local" value="$packageSourcePath" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@

$packageReferenceFiles = Get-ChildItem -LiteralPath $generatedPath -Recurse -Filter '*.csproj'
$markazorPackageReferences = foreach ($file in $packageReferenceFiles) {
    [xml] $projectXml = Get-Content -LiteralPath $file.FullName
    $projectXml.Project.ItemGroup.PackageReference |
        Where-Object { $_.Include -like 'Markazor*' } |
        ForEach-Object {
            [pscustomobject] @{
                Project = $file.FullName
                Include = $_.Include
                ExcludeAssets = $_.ExcludeAssets
            }
        }
}

$unexpectedReferences = @($markazorPackageReferences | Where-Object { $_.Include -notin @('Markazor', 'Markazor.Api', 'Markazor.Themes') })

if ($unexpectedReferences.Count -gt 0) {
    $formatted = $unexpectedReferences |
        ForEach-Object { "$($_.Project): $($_.Include)" }
    throw "Generated template must reference only Markazor packages directly. Unexpected references: $($formatted -join ', ')"
}

[xml] $webProjectXml = Get-Content -LiteralPath $webProjectPath
[xml] $functionsProjectXml = Get-Content -LiteralPath $functionsProjectPath
$functionsApiReference = @($functionsProjectXml.Project.ItemGroup.PackageReference |
        Where-Object { $_.Include -eq 'Markazor.Api' })[0]
$functionsMarkazorReference = @($functionsProjectXml.Project.ItemGroup.PackageReference |
        Where-Object { $_.Include -eq 'Markazor' })[0]
$webMarkazorReference = @($webProjectXml.Project.ItemGroup.PackageReference |
        Where-Object { $_.Include -eq 'Markazor' })[0]
$webThemesReference = @($webProjectXml.Project.ItemGroup.PackageReference |
        Where-Object { $_.Include -eq 'Markazor.Themes' })[0]

if ($null -eq $functionsApiReference) {
    throw 'Generated Functions project must reference Markazor.Api.'
}

if ($null -ne $functionsMarkazorReference) {
    throw 'Generated Functions project must not reference Markazor directly.'
}

if ($null -eq $webMarkazorReference) {
    throw 'Generated Web project must reference Markazor.'
}

if ($null -eq $webThemesReference) {
    throw 'Generated Web project must reference Markazor.Themes.'
}

if (-not [string]::IsNullOrWhiteSpace($webMarkazorReference.ExcludeAssets)) {
    throw 'Generated Web project must include Markazor analyzers.'
}

if ($webProjectXml.Project.PropertyGroup.TargetFramework -ne 'net9.0') {
    throw "Generated Web project must explicitly target net9.0 for Azure Static Web Apps Oryx detection. Actual: $($webProjectXml.Project.PropertyGroup.TargetFramework)"
}

if ($functionsProjectXml.Project.PropertyGroup.TargetFramework -ne 'net9.0') {
    throw "Generated Functions project must explicitly target net9.0 for Azure Static Web Apps Oryx detection. Actual: $($functionsProjectXml.Project.PropertyGroup.TargetFramework)"
}

$webTargetFramework = (dotnet msbuild $webProjectPath -getProperty:TargetFramework).Trim()
$functionsTargetFramework = (dotnet msbuild $functionsProjectPath -getProperty:TargetFramework).Trim()
if ($webTargetFramework -ne 'net9.0') {
    throw "Generated Web project must target net9.0. Actual: $webTargetFramework"
}

if ($functionsTargetFramework -ne 'net9.0') {
    throw "Generated Functions project must target net9.0. Actual: $functionsTargetFramework"
}

$generatedReadmePath = Join-Path $generatedPath 'README.md'
if (Test-Path -LiteralPath $generatedReadmePath) {
    throw 'Generated template must not include README.md initially.'
}

$siteSettingsPath = Join-Path (Join-Path $generatedPath 'public') 'markazor.settings.json'
if (Test-Path -LiteralPath $siteSettingsPath) {
    throw 'Generated template must not include public/markazor.settings.json initially.'
}

$contentPath = Join-Path $generatedPath 'content'
if (Test-Path -LiteralPath $contentPath) {
    throw 'Generated template must not include content/ initially.'
}

foreach ($rootName in @('posts', 'notes', 'drafts', 'assets', 'public')) {
    $rootPath = Join-Path $generatedPath $rootName
    if (Test-Path -LiteralPath $rootPath) {
        throw "Generated template must not include $rootName/ initially."
    }
}

$generatedGitIgnorePath = Join-Path $generatedPath '.gitignore'
if (Test-Path -LiteralPath $generatedGitIgnorePath) {
    throw 'Generated template must not include .gitignore initially.'
}

$webSourceWwwrootPath = Join-Path $webProjectDirectory 'wwwroot'
if (Test-Path -LiteralPath $webSourceWwwrootPath) {
    throw "Generated Web project must not include a source-controlled wwwroot directory: $webSourceWwwrootPath"
}

$webSourceStaticWebAppConfigPath = Join-Path $webProjectDirectory 'staticwebapp.config.json'
if (Test-Path -LiteralPath $webSourceStaticWebAppConfigPath) {
    throw "Generated Web project must not include a source-controlled staticwebapp.config.json: $webSourceStaticWebAppConfigPath"
}

$localSettingsPath = Join-Path (Join-Path (Join-Path $generatedPath 'src') "$ProjectName.Functions") 'local.settings.json'
if (Test-Path -LiteralPath $localSettingsPath) {
    throw 'Generated template must not include Functions local.settings.json initially.'
}

$hostJsonPath = Join-Path (Join-Path (Join-Path $generatedPath 'src') "$ProjectName.Functions") 'host.json'
if (Test-Path -LiteralPath $hostJsonPath) {
    throw 'Generated template must not include Functions host.json initially.'
}

dotnet restore $solutionPath

$webSourceGeneratorProperty = (dotnet msbuild $webProjectPath -getProperty:MarkazorEnableSourceGenerator).Trim()
$functionsSourceGeneratorProperty = (dotnet msbuild $functionsProjectPath -getProperty:MarkazorEnableSourceGenerator).Trim()
$webSiteBuildProperty = (dotnet msbuild $webProjectPath -getProperty:MarkazorEnableSiteBuild).Trim()
if ($webSiteBuildProperty -ne 'true') {
    throw 'Generated Web project must explicitly enable the Markazor site build.'
}

if ($webSourceGeneratorProperty -ne 'true') {
    throw 'Generated Web project must enable the Markazor source generator.'
}

if ($functionsSourceGeneratorProperty -eq 'true') {
    throw 'Generated Functions project must not enable the Markazor source generator.'
}

dotnet build $solutionPath --configuration Release --no-restore

$sourceWebRoot = Join-Path $webProjectDirectory 'wwwroot'
if (Test-Path -LiteralPath $sourceWebRoot) {
    throw "Markazor site build must not create a source wwwroot directory: $sourceWebRoot"
}

$generatedWebRoot = (dotnet msbuild $webProjectPath --property:Configuration=Release -getProperty:MarkazorGeneratedWebRoot).Trim()
$expectedGeneratedWebRootPrefix = [System.IO.Path]::GetFullPath((Join-Path $webProjectDirectory 'obj'))
if (-not ([System.IO.Path]::GetFullPath($generatedWebRoot).StartsWith(
            $expectedGeneratedWebRootPrefix + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase))) {
    throw "Markazor generated web root must be inside obj. Actual: $generatedWebRoot"
}

$generatedWebRootExpectedFiles = @(
    'index.html',
    '404.html',
    'manifest.webmanifest',
    'staticwebapp.config.json',
    'service-worker.js',
    'service-worker.published.js',
    (Join-Path 'styles' 'site.css'),
    (Join-Path 'scripts' 'site.js')
)

foreach ($expectedFile in $generatedWebRootExpectedFiles) {
    $generatedFile = Join-Path $generatedWebRoot $expectedFile
    if (-not (Test-Path -LiteralPath $generatedFile)) {
        throw "Generated Markazor intermediate web root is missing expected build-time file: $generatedFile"
    }
}

$generatedThemeCss = Join-Path (Join-Path (Join-Path $generatedWebRoot '_content') 'Markazor.Themes') 'theme.css'
if (-not (Test-Path -LiteralPath $generatedThemeCss)) {
    throw "Generated Markazor intermediate web root is missing selected theme CSS: $generatedThemeCss"
}

$generatedStaticWebAppConfigPath = Join-Path $generatedWebRoot 'staticwebapp.config.json'
$generatedStaticWebAppConfig = Get-Content -Raw -LiteralPath $generatedStaticWebAppConfigPath
if (-not (Test-StringContains -Text $generatedStaticWebAppConfig -Value '"route": "/manage*"')) {
    throw 'Generated staticwebapp.config.json is missing the /manage no-store route.'
}

if (-not (Test-StringContains -Text $generatedStaticWebAppConfig -Value '"apiRuntime": "dotnet-isolated:9.0"')) {
    throw 'Generated staticwebapp.config.json is missing the Azure Functions runtime declaration.'
}

$sourceWebRootMarker = Join-Path $sourceWebRoot 'source-wwwroot.css'
New-Item -ItemType Directory -Path $sourceWebRoot | Out-Null
Set-Content -LiteralPath $sourceWebRootMarker -Value 'source web root must be rejected' -Encoding utf8 -NoNewline
$sourceWebRootBuild = Invoke-DotNetBuildCapturingFailure -ProjectPath $webProjectPath
if ($sourceWebRootBuild.ExitCode -eq 0) {
    throw 'Markazor site build must fail when a source wwwroot contains user files.'
}

if (-not (Test-StringContains -Text ($sourceWebRootBuild.Output -join [Environment]::NewLine) -Value 'Markazor site build owns the final web root')) {
    throw 'Source wwwroot build failure did not include the expected ownership diagnostic.'
}

Remove-Item -LiteralPath $sourceWebRoot -Recurse -Force

$reservedPublicAsset = Join-Path (Join-Path (Join-Path $generatedPath 'public') 'assets') 'blocked.txt'
New-Item -ItemType Directory -Path (Split-Path -Parent $reservedPublicAsset) | Out-Null
Set-Content -LiteralPath $reservedPublicAsset -Value 'public/assets must be rejected' -Encoding utf8 -NoNewline
$reservedPublicBuild = Invoke-DotNetBuildCapturingFailure -ProjectPath $webProjectPath
if ($reservedPublicBuild.ExitCode -eq 0) {
    throw 'Markazor site build must fail when public/assets contains user files.'
}

if (-not (Test-StringContains -Text ($reservedPublicBuild.Output -join [Environment]::NewLine) -Value 'Markazor public overlay cannot contain reserved paths')) {
    throw 'Reserved public asset build failure did not include the expected ownership diagnostic.'
}

Remove-Item -LiteralPath (Split-Path -Parent $reservedPublicAsset) -Recurse -Force

dotnet publish $webProjectPath `
    --configuration Release `
    --no-restore `
    --output $emptyWebPublishPath
dotnet publish $functionsProjectPath `
    --configuration Release `
    --no-restore `
    --output $emptyFunctionsPublishPath

$emptyPublishedSiteSettings = Join-Path (Join-Path $emptyWebPublishPath 'wwwroot') 'markazor.settings.json'
if (Test-Path -LiteralPath $emptyPublishedSiteSettings) {
    throw "Empty generated web output must not include markazor.settings.json: $emptyPublishedSiteSettings"
}

$emptyPublishedContent = Join-Path (Join-Path $emptyWebPublishPath 'wwwroot') 'content'
if (Test-Path -LiteralPath $emptyPublishedContent) {
    throw "Empty generated web output must not include content/: $emptyPublishedContent"
}

$emptyPublishedEditorBundle = Join-Path (Join-Path (Join-Path (Join-Path $emptyWebPublishPath 'wwwroot') '_content') 'Markazor') 'markazor-markdown-editor.js'
if (-not (Test-Path -LiteralPath $emptyPublishedEditorBundle)) {
    throw "Published web output is missing the Markazor markdown editor bundle: $emptyPublishedEditorBundle"
}

$emptyPublishedThemeCss = Join-Path (Join-Path (Join-Path (Join-Path $emptyWebPublishPath 'wwwroot') '_content') 'Markazor.Themes') 'theme.css'
if (-not (Test-Path -LiteralPath $emptyPublishedThemeCss)) {
    throw "Published web output is missing the selected Markazor theme CSS: $emptyPublishedThemeCss"
}

$emptyPublishedSiteCss = Join-Path (Join-Path (Join-Path $emptyWebPublishPath 'wwwroot') 'styles') 'site.css'
if (-not (Test-Path -LiteralPath $emptyPublishedSiteCss)) {
    throw "Published web output is missing generated fallback styles/site.css: $emptyPublishedSiteCss"
}

$emptyPublishedIndex = Join-Path (Join-Path $emptyWebPublishPath 'wwwroot') 'index.html'
$emptyPublishedIndexText = Get-Content -Raw -LiteralPath $emptyPublishedIndex
if (Test-StringContains -Text $emptyPublishedIndexText -Value '#[.{fingerprint}]') {
    throw 'Default published index.html must not contain unresolved framework fingerprint placeholders.'
}

if (-not (Test-StringContains -Text $emptyPublishedIndexText -Value '_framework/blazor.webassembly.js')) {
    throw 'Default published index.html must reference the net9-compatible Blazor WebAssembly boot script.'
}

$postMarkdownPath = Join-Path 'posts' 'hello-world.md'
$noteMarkdownPath = Join-Path 'notes' 'first-note.md'
$draftMarkdownPath = Join-Path 'drafts' 'private-notes.md'
$assetPath = Join-Path 'assets' 'smoke-asset.txt'
$publicIndexPath = Join-Path 'public' 'index.html'
$publicCssPath = Join-Path (Join-Path 'public' 'styles') 'site.css'
$publicJsPath = Join-Path (Join-Path 'public' 'scripts') 'site.js'
$publicRobotsPath = Join-Path 'public' 'robots.txt'

Write-SmokeFile -RelativePath (Join-Path 'public' 'markazor.settings.json') -Content @"
{
  "site": {
    "name": "$ProjectName",
    "description": "Smoke test site generated from Markazor templates.",
    "baseUrls": [
      "https://example.test"
    ]
  },
  "github": {
    "clientId": ""
  },
  "repository": {
    "owner": "your-user",
    "name": "$ProjectName",
    "defaultBranch": "main"
  },
  "theme": {
    "name": "default"
  }
}
"@

Write-SmokeFile -RelativePath $publicIndexPath -Content @'
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Custom Markazor shell</title>
    <base href="/" />
    <link rel="stylesheet" href="_content/Markazor/markazor.css" />
    <link rel="stylesheet" href="_content/Markazor.Themes/theme.css" />
    <link rel="stylesheet" href="styles/site.css" />
    <link href="manifest.webmanifest" rel="manifest" />
</head>
<body data-smoke-shell="custom">
    <div id="app">Custom shell loading</div>
    <script src="_framework/blazor.webassembly.js"></script>
    <script type="module" src="_content/Markazor.Themes/theme.js"></script>
    <script type="module" src="scripts/site.js"></script>
    <script>navigator.serviceWorker.register('service-worker.js', { updateViaCache: 'none' });</script>
</body>
</html>
'@

Write-SmokeFile -RelativePath $publicCssPath -Content 'body { --markazor-smoke-site-css: custom; }'
Write-SmokeFile -RelativePath $publicJsPath -Content 'document.documentElement.dataset.markazorSmokeSiteJs = "custom";'
Write-SmokeFile -RelativePath $publicRobotsPath -Content "User-agent: *`nAllow: /"
New-Item -ItemType Directory -Path (Join-Path $generatedPath 'assets') -Force | Out-Null
[System.IO.File]::WriteAllBytes(
    (Join-Path $generatedPath (Join-Path 'assets' 'site-icon.png')),
    [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAFgwJ/lPyo1wAAAABJRU5ErkJggg=='))

Write-SmokeFile -RelativePath $postMarkdownPath -Content @'
---
slug: hello-world
title: Hello World
summary: First Markazor post.
publishedAt: 2026-06-03T00:00:00Z
tags: [intro, markazor]
category: General
---

# Hello World

This file is compiled into `Markazor.Generated.SiteIndex`.
'@

Write-SmokeFile -RelativePath $noteMarkdownPath -Content @'
---
slug: first-note
title: First Note
summary: A short note published beside the site posts.
publishedAt: 2026-06-06T08:00:00Z
tags: [notes, markazor]
category: Notes
---

Notes are for short-form updates that still belong in the public, versioned content history.
'@

Write-SmokeFile -RelativePath $draftMarkdownPath -Content @'
---
slug: private-notes
title: Private notes
kind: note
summary: This draft must never appear in the published site.
publishedAt: 2026-06-06T00:00:00Z
tags: [private]
category: Notes
draft: true
---

# Private notes

This file is available to the authenticated editor through GitHub, not to public readers.
'@

Write-SmokeFile -RelativePath $assetPath -Content 'smoke asset'

$watchOutput = & dotnet watch --project $webProjectPath --list 2>&1
$watchOutputText = ($watchOutput -join [Environment]::NewLine).Replace('\', '/')
$expectedWatchSources = @(
    (Convert-ToSlashPath -Path $siteSettingsPath),
    (Convert-ToSlashPath -Path (Join-Path $generatedPath $assetPath)),
    (Convert-ToSlashPath -Path (Join-Path $generatedPath $publicIndexPath)),
    (Convert-ToSlashPath -Path (Join-Path $generatedPath $publicCssPath)),
    (Convert-ToSlashPath -Path (Join-Path $generatedPath $publicJsPath)),
    (Convert-ToSlashPath -Path (Join-Path $generatedPath $postMarkdownPath)),
    (Convert-ToSlashPath -Path (Join-Path $generatedPath $noteMarkdownPath)),
    (Convert-ToSlashPath -Path (Join-Path $generatedPath $draftMarkdownPath))
)

foreach ($expectedWatchSource in $expectedWatchSources) {
    if (-not (Test-StringContains -Text $watchOutputText -Value $expectedWatchSource)) {
        throw "Generated Web project watch list is missing source file: $expectedWatchSource"
    }
}

$generatedWebRootWatchPath = (Convert-ToSlashPath -Path $generatedWebRoot) + '/'
if (Test-StringContains -Text $watchOutputText -Value $generatedWebRootWatchPath) {
    throw "Generated Web project must not watch the Markazor intermediate web root directly: $generatedWebRootWatchPath"
}

$siteSettings = Get-Content -Raw -LiteralPath $siteSettingsPath
if (-not (Test-StringContains -Text $siteSettings -Value ('"name": "' + $ProjectName + '"'))) {
    throw 'Smoke markazor.settings.json is missing the configured site title.'
}

dotnet build $solutionPath --configuration Release --no-restore

$validSiteSettings = Get-Content -Raw -LiteralPath $siteSettingsPath
$unknownThemeSettings = $validSiteSettings.Replace('"name": "default"', '"name": "missing-theme"')
Set-Content -LiteralPath $siteSettingsPath -Value $unknownThemeSettings -Encoding utf8 -NoNewline

$unknownThemeBuild = Invoke-DotNetBuildCapturingFailure -ProjectPath $webProjectPath
if ($unknownThemeBuild.ExitCode -eq 0) {
    throw 'Generated Web project must fail build for an unknown Markazor theme.'
}

if (-not (Test-StringContains -Text ($unknownThemeBuild.Output -join [Environment]::NewLine) -Value "Unknown Markazor theme 'missing-theme'")) {
    throw 'Unknown theme build failure did not include the expected diagnostic.'
}

$noThemeSettings = $validSiteSettings.Replace('"name": "default"', '"name": "none"')
Set-Content -LiteralPath $siteSettingsPath -Value $noThemeSettings -Encoding utf8 -NoNewline
dotnet build $webProjectPath --configuration Release --no-restore
if (-not [string]::IsNullOrWhiteSpace((Get-Content -Raw -LiteralPath $generatedThemeCss))) {
    throw 'The none theme must emit an empty theme.css.'
}

$generatedThemeJs = Join-Path (Split-Path -Parent $generatedThemeCss) 'theme.js'
if (-not [string]::IsNullOrWhiteSpace((Get-Content -Raw -LiteralPath $generatedThemeJs))) {
    throw 'The none theme must emit an empty theme.js.'
}

Set-Content -LiteralPath $siteSettingsPath -Value $validSiteSettings -Encoding utf8 -NoNewline
dotnet build $webProjectPath --configuration Release --no-restore

dotnet publish $webProjectPath `
    --configuration Release `
    --no-restore `
    --output $webPublishPath
dotnet publish $functionsProjectPath `
    --configuration Release `
    --no-restore `
    --output $functionsPublishPath

$functionsMetadataPath = Join-Path $functionsPublishPath 'functions.metadata'
if (-not (Test-Path -LiteralPath $functionsMetadataPath)) {
    throw "Published Functions output is missing functions.metadata: $functionsMetadataPath"
}

$functionsHostJsonPath = Join-Path $functionsPublishPath 'host.json'
if (-not (Test-Path -LiteralPath $functionsHostJsonPath)) {
    throw "Published Functions output is missing generated host.json: $functionsHostJsonPath"
}

$functionsHostJson = Get-Content -Raw -LiteralPath $functionsHostJsonPath | ConvertFrom-Json
if ($functionsHostJson.version -ne '2.0') {
    throw "Published Functions host.json has an unexpected version: $($functionsHostJson.version)"
}

$functionsMetadata = Get-Content -Raw -LiteralPath $functionsMetadataPath | ConvertFrom-Json
$functionNames = @($functionsMetadata | ForEach-Object { $_.name })
foreach ($expectedFunctionName in @('GitHubAuthStart', 'GitHubAuthCallback', 'GitHubAuthRefresh', 'SetupStatus')) {
    if ($expectedFunctionName -notin $functionNames) {
        throw "Published Functions metadata is missing expected function: $expectedFunctionName"
    }
}

$functionRoutes = @(
    $functionsMetadata |
        ForEach-Object { $_.bindings } |
        Where-Object { $_.type -eq 'httpTrigger' } |
        ForEach-Object { $_.route }
)
foreach ($expectedFunctionRoute in @('auth/github/start', 'auth/github/callback', 'auth/github/refresh', 'setup/status')) {
    if ($expectedFunctionRoute -notin $functionRoutes) {
        throw "Published Functions metadata is missing expected route: $expectedFunctionRoute"
    }
}

$publicPost = Join-Path (Join-Path (Join-Path (Join-Path $webPublishPath 'wwwroot') '_markazor') 'content') (Join-Path 'posts' 'hello-world.md')
if (-not (Test-Path -LiteralPath $publicPost)) {
    throw "Published output is missing public post markdown: $publicPost"
}

$publicNote = Join-Path (Join-Path (Join-Path (Join-Path $webPublishPath 'wwwroot') '_markazor') 'content') (Join-Path 'notes' 'first-note.md')
if (-not (Test-Path -LiteralPath $publicNote)) {
    throw "Published output is missing public note markdown: $publicNote"
}

$publicAsset = Join-Path (Join-Path (Join-Path $webPublishPath 'wwwroot') 'assets') 'smoke-asset.txt'
if (-not (Test-Path -LiteralPath $publicAsset)) {
    throw "Published output is missing root asset under wwwroot/assets: $publicAsset"
}

$publishedSiteSettings = Join-Path (Join-Path $webPublishPath 'wwwroot') 'markazor.settings.json'
if (-not (Test-Path -LiteralPath $publishedSiteSettings)) {
    throw "Published web output is missing markazor.settings.json: $publishedSiteSettings"
}

$publishedManifest = Join-Path (Join-Path $webPublishPath 'wwwroot') 'manifest.webmanifest'
if (-not (Test-Path -LiteralPath $publishedManifest)) {
    throw "Published web output is missing manifest.webmanifest: $publishedManifest"
}

$publishedManifestJson = Get-Content -Raw -LiteralPath $publishedManifest | ConvertFrom-Json
if ($publishedManifestJson.name -ne $ProjectName) {
    throw "Published manifest.webmanifest did not use the configured site name. Actual: $($publishedManifestJson.name)"
}

if ($publishedManifestJson.short_name -ne $ProjectName) {
    throw "Published manifest.webmanifest did not use the configured short site name. Actual: $($publishedManifestJson.short_name)"
}

if ($publishedManifestJson.description -ne 'Smoke test site generated from Markazor templates.') {
    throw "Published manifest.webmanifest did not use the configured site description. Actual: $($publishedManifestJson.description)"
}

$publishedFunctionsSettings = Join-Path $functionsPublishPath 'markazor.settings.json'
if (-not (Test-Path -LiteralPath $publishedFunctionsSettings)) {
    throw "Published Functions output is missing markazor.settings.json: $publishedFunctionsSettings"
}

if (-not (Test-StringContains -Text (Get-Content -Raw -LiteralPath $publishedFunctionsSettings) -Value ('"name": "' + $ProjectName + '"'))) {
    throw 'Published Functions markazor.settings.json is not the canonical public settings file.'
}

$publishedIndex = Join-Path (Join-Path $webPublishPath 'wwwroot') 'index.html'
if (-not (Test-StringContains -Text (Get-Content -Raw -LiteralPath $publishedIndex) -Value 'data-smoke-shell="custom"')) {
    throw 'Published index.html did not use the full public shell override.'
}

if (Test-StringContains -Text (Get-Content -Raw -LiteralPath $publishedIndex) -Value '#[.{fingerprint}]') {
    throw 'Published index.html must not contain unresolved framework fingerprint placeholders.'
}

$publishedFavicon = Join-Path (Join-Path (Join-Path $webPublishPath 'wwwroot') 'assets') 'site-icon.png'
if (-not (Test-Path -LiteralPath $publishedFavicon)) {
    throw "Published web output is missing the configured favicon asset: $publishedFavicon"
}

$publishedSiteCss = Join-Path (Join-Path (Join-Path $webPublishPath 'wwwroot') 'styles') 'site.css'
if (-not (Test-StringContains -Text (Get-Content -Raw -LiteralPath $publishedSiteCss) -Value '--markazor-smoke-site-css: custom')) {
    throw 'Published styles/site.css did not use the public CSS override.'
}

$publishedSiteJs = Join-Path (Join-Path (Join-Path $webPublishPath 'wwwroot') 'scripts') 'site.js'
if (-not (Test-StringContains -Text (Get-Content -Raw -LiteralPath $publishedSiteJs) -Value 'markazorSmokeSiteJs')) {
    throw 'Published scripts/site.js did not use the public JavaScript override.'
}

$publishedRobots = Join-Path (Join-Path $webPublishPath 'wwwroot') 'robots.txt'
if (-not (Test-Path -LiteralPath $publishedRobots)) {
    throw 'Published web output is missing an arbitrary public overlay file.'
}

$draftFile = Join-Path (Join-Path (Join-Path (Join-Path $webPublishPath 'wwwroot') '_markazor') 'content') (Join-Path 'drafts' 'private-notes.md')
if (Test-Path -LiteralPath $draftFile) {
    throw "Published output must not include draft markdown: $draftFile"
}

$serviceWorkerAssets = Join-Path (Join-Path $webPublishPath 'wwwroot') 'service-worker-assets.js'
if (-not (Test-Path -LiteralPath $serviceWorkerAssets)) {
    throw "Published output is missing service-worker-assets.js: $serviceWorkerAssets"
}

$publishedServiceWorker = Join-Path (Join-Path $webPublishPath 'wwwroot') 'service-worker.js'
if (-not (Test-Path -LiteralPath $publishedServiceWorker)) {
    throw "Published output is missing service-worker.js: $publishedServiceWorker"
}

$publishedServiceWorkerText = Get-Content -Raw -LiteralPath $publishedServiceWorker
if (-not (Test-StringContains -Text $publishedServiceWorkerText -Value "event.data?.type === 'SKIP_WAITING'" -Comparison ([System.StringComparison]::Ordinal))) {
    throw 'Published service-worker.js must support explicit SKIP_WAITING activation.'
}

if (Test-StringContains -Text $publishedServiceWorkerText -Value 'await self.skipWaiting();' -Comparison ([System.StringComparison]::Ordinal)) {
    throw 'Published service-worker.js must not automatically skip waiting during install.'
}

if (-not (Test-StringContains -Text $publishedServiceWorkerText -Value 'return await fetch(event.request);' -Comparison ([System.StringComparison]::Ordinal))) {
    throw 'Published service-worker.js must await network fetches so rejected navigation requests are handled.'
}

if (-not (Test-StringContains -Text $publishedServiceWorkerText -Value 'The site shell is not available offline yet.' -Comparison ([System.StringComparison]::Ordinal))) {
    throw 'Published service-worker.js must return a deterministic navigation fallback when the network fails before the app shell is cached.'
}

$serviceWorkerAssetsText = Get-Content -Raw -LiteralPath $serviceWorkerAssets
if ((Test-StringContains -Text $serviceWorkerAssetsText -Value 'drafts/' -Comparison ([System.StringComparison]::OrdinalIgnoreCase)) -or
    (Test-StringContains -Text $serviceWorkerAssetsText -Value 'private-notes.md' -Comparison ([System.StringComparison]::OrdinalIgnoreCase))) {
    throw 'service-worker-assets.js must not contain draft content paths.'
}

if (Test-StringContains -Text $serviceWorkerAssetsText -Value 'staticwebapp.config.json' -Comparison ([System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'service-worker-assets.js must not contain staticwebapp.config.json because Azure Static Web Apps handles it as deployment configuration.'
}

if (-not (Test-StringContains -Text $serviceWorkerAssetsText -Value '_markazor/content/notes/first-note.md' -Comparison ([System.StringComparison]::OrdinalIgnoreCase))) {
    throw 'service-worker-assets.js must contain public note content paths.'
}

if (-not (Test-StringContains -Text $serviceWorkerAssetsText -Value 'assets/smoke-asset.txt' -Comparison ([System.StringComparison]::OrdinalIgnoreCase))) {
    throw 'service-worker-assets.js must contain root asset paths.'
}

if (-not (Test-StringContains -Text $serviceWorkerAssetsText -Value '_content/Markazor/markazor-markdown-editor.js' -Comparison ([System.StringComparison]::OrdinalIgnoreCase))) {
    throw 'service-worker-assets.js must contain the Markazor markdown editor bundle.'
}

dotnet new uninstall Markazor.Templates
Write-Output $generatedPath
