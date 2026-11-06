param(
    [Parameter(Mandatory)]
    [string] $Version,

    [Parameter(Mandatory)]
    [string] $PackageSource,

    [string] $WorkDirectory = 'artifacts/template-smoke',
    [string] $ProjectName = 'SmokeSite'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$packageSourcePath = (Resolve-Path -LiteralPath $PackageSource).Path
$workPath = Join-Path $repoRoot $WorkDirectory
$generatedPath = Join-Path $workPath $ProjectName
$publishPath = Join-Path $workPath 'publish'
$emptyPublishPath = Join-Path $workPath 'publish-empty'
$webPublishPath = Join-Path $publishPath 'web'
$functionsPublishPath = Join-Path $publishPath 'functions'
$emptyWebPublishPath = Join-Path $emptyPublishPath 'web'
$nugetPackagesPath = Join-Path ([IO.Path]::GetTempPath()) ('markazor-template-smoke-' + [guid]::NewGuid().ToString('N'))
$solutionPath = Join-Path $generatedPath "$ProjectName.slnx"
$webProjectDirectory = Join-Path $generatedPath "src/$ProjectName.Web"
$webProjectPath = Join-Path $webProjectDirectory "$ProjectName.Web.csproj"
$functionsProjectPath = Join-Path $generatedPath "src/$ProjectName.Functions/$ProjectName.Functions.csproj"

function Reset-Directory([string] $Path) {
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function Write-SmokeFile([string] $RelativePath, [string] $Content) {
    $path = Join-Path $generatedPath $RelativePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    Set-Content -LiteralPath $path -Value $Content -Encoding utf8 -NoNewline
}

function Assert-Files([string] $Root, [string[]] $Paths) {
    foreach ($path in $Paths) {
        if (-not (Test-Path -LiteralPath (Join-Path $Root $path))) {
            throw "Missing expected output: $Root/$path"
        }
    }
}

function Assert-BuildFails([string] $Diagnostic) {
    $PSNativeCommandUseErrorActionPreference = $false
    $output = @(& dotnet build $webProjectPath --configuration Release --no-restore 2>&1) -join [Environment]::NewLine
    if ($LASTEXITCODE -eq 0 -or -not $output.Contains($Diagnostic)) {
        throw "Expected build failure '$Diagnostic'. Actual output: $output"
    }
}

foreach ($path in @($generatedPath, $publishPath, $emptyPublishPath)) {
    Reset-Directory $path
}
New-Item -ItemType Directory -Path $nugetPackagesPath | Out-Null
$env:NUGET_PACKAGES = $nugetPackagesPath
$nugetConfigPath = Join-Path $nugetPackagesPath 'NuGet.config'
Set-Content -LiteralPath $nugetConfigPath -Value @"
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

dotnet new install "Markazor.Templates@$Version" --add-source $packageSourcePath --force

$consumerPath = Join-Path $nugetPackagesPath 'consumer'
$consumerProjectPath = Join-Path $consumerPath 'ExistingSite.csproj'
dotnet new blazorwasm --name ExistingSite --framework net10.0 --output $consumerPath --no-restore
dotnet add $consumerProjectPath package Markazor --version $Version --no-restore
dotnet restore $consumerProjectPath --configfile $nugetConfigPath
$consumerStaticFiles = Get-ChildItem -LiteralPath (Join-Path $consumerPath 'wwwroot') -Recurse -File | Get-FileHash
if ((dotnet msbuild $consumerProjectPath -getProperty:MarkazorEnableSiteBuild).Trim() -ne 'false') {
    throw 'Ordinary package consumers must not enable site generation.'
}
dotnet build $consumerProjectPath --configuration Release --no-restore
foreach ($file in $consumerStaticFiles) {
    if ((Get-FileHash -LiteralPath $file.Path).Hash -ne $file.Hash) {
        throw "Markazor changed an existing consumer static file: $($file.Path)"
    }
}

$siteConsumerPath = Join-Path $nugetPackagesPath 'site-consumer'
$siteConsumerWebPath = Join-Path $siteConsumerPath 'src/SiteBuildConsumer.Web'
$siteConsumerProjectPath = Join-Path $siteConsumerWebPath 'SiteBuildConsumer.csproj'
$siteConsumerSourceWebRoot = Join-Path $siteConsumerWebPath 'wwwroot'
$siteConsumerPublishPath = Join-Path $nugetPackagesPath 'site-consumer-publish'
dotnet new blazorwasm --name SiteBuildConsumer --framework net10.0 --output $siteConsumerWebPath --no-restore
Copy-Item -LiteralPath $siteConsumerSourceWebRoot -Destination (Join-Path $siteConsumerPath 'public') -Recurse
Remove-Item -LiteralPath $siteConsumerSourceWebRoot -Recurse -Force
[xml] $project = Get-Content -LiteralPath $siteConsumerProjectPath
$properties = $project.CreateElement('PropertyGroup')
$properties.InnerXml = '<MarkazorEnableSiteBuild>true</MarkazorEnableSiteBuild>'
$project.Project.AppendChild($properties) | Out-Null
$project.Save($siteConsumerProjectPath)
dotnet add $siteConsumerProjectPath package Markazor --version $Version --no-restore
dotnet restore $siteConsumerProjectPath --configfile $nugetConfigPath
if ((dotnet msbuild $siteConsumerProjectPath -getProperty:ServiceWorkerAssetsManifest).Trim() -ne 'service-worker-assets.js') {
    throw 'Explicit site generation must enable the service worker manifest.'
}
dotnet build $siteConsumerProjectPath --configuration Release --no-restore
if (Test-Path -LiteralPath $siteConsumerSourceWebRoot) {
    throw 'Site generation must not recreate the consumer source wwwroot.'
}
dotnet publish $siteConsumerProjectPath --configuration Release --no-restore --output $siteConsumerPublishPath
Assert-Files (Join-Path $siteConsumerPublishPath 'wwwroot') @('index.html', 'service-worker.js', 'service-worker-assets.js', 'css/app.css', '_content/Markazor.Themes/theme.css')

$themesConsumerPath = Join-Path $nugetPackagesPath 'themes-consumer'
$themesConsumerProjectPath = Join-Path $themesConsumerPath 'ThemesOnly.csproj'
dotnet new blazorwasm --name ThemesOnly --framework net10.0 --output $themesConsumerPath --no-restore
dotnet add $themesConsumerProjectPath package Markazor.Themes --version $Version --no-restore
dotnet restore $themesConsumerProjectPath --configfile $nugetConfigPath
dotnet build $themesConsumerProjectPath --configuration Release --no-restore

dotnet new markazor-site --name $ProjectName --output $generatedPath
[xml] $packages = Get-Content -LiteralPath (Join-Path $generatedPath 'Directory.Packages.props')
foreach ($name in @('Markazor', 'Markazor.Api', 'Markazor.Themes')) {
    $package = @($packages.Project.ItemGroup.PackageVersion | Where-Object Include -eq $name)
    if ($package.Count -ne 1 -or $package[0].Version -ne $Version) {
        throw "The installed template must use $name version $Version."
    }
}
Copy-Item -LiteralPath $nugetConfigPath -Destination (Join-Path $generatedPath 'NuGet.config')
dotnet restore $solutionPath
$web = (dotnet msbuild $webProjectPath '-getProperty:TargetFramework,MarkazorEnableSiteBuild,MarkazorEnableSourceGenerator' | ConvertFrom-Json).Properties
$functions = (dotnet msbuild $functionsProjectPath '-getProperty:TargetFramework,MarkazorEnableSourceGenerator' | ConvertFrom-Json).Properties
if ($web.TargetFramework -ne 'net10.0' -or $web.MarkazorEnableSiteBuild -ne 'true' -or $web.MarkazorEnableSourceGenerator -ne 'true' -or
    $functions.TargetFramework -ne 'net10.0' -or $functions.MarkazorEnableSourceGenerator -eq 'true') {
    throw 'Installed template projects did not enable the expected frameworks and generators.'
}
dotnet build $solutionPath --configuration Release --no-restore
$sourceWebRoot = Join-Path $webProjectDirectory 'wwwroot'
if (Test-Path -LiteralPath $sourceWebRoot) {
    throw 'Site generation must not create a source wwwroot.'
}
$generatedWebRoot = (dotnet msbuild $webProjectPath -property:Configuration=Release -getProperty:MarkazorGeneratedWebRoot).Trim()
$intermediateRoot = [IO.Path]::GetFullPath((Join-Path $webProjectDirectory 'obj')) + [IO.Path]::DirectorySeparatorChar
if (-not [IO.Path]::GetFullPath($generatedWebRoot).StartsWith($intermediateRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Generated web root must be inside obj: $generatedWebRoot"
}
Assert-Files $generatedWebRoot @(
    'index.html', '404.html', 'manifest.webmanifest', 'staticwebapp.config.json',
    'service-worker.js', 'service-worker.published.js', 'studio/setup/index.html',
    'studio/setup/github-callback/index.html', 'styles/site.css', 'scripts/site.js',
    '_content/Markazor.Themes/theme.css'
)

New-Item -ItemType Directory -Path $sourceWebRoot | Out-Null
Set-Content -LiteralPath (Join-Path $sourceWebRoot 'source-wwwroot.css') -Value 'User content'
Assert-BuildFails 'Markazor site build owns the final web root'
Remove-Item -LiteralPath $sourceWebRoot -Recurse -Force
$reservedPublicAssets = Join-Path $generatedPath 'public/assets'
New-Item -ItemType Directory -Path $reservedPublicAssets -Force | Out-Null
Set-Content -LiteralPath (Join-Path $reservedPublicAssets 'blocked.txt') -Value 'User content'
Assert-BuildFails 'Markazor public overlay cannot contain reserved paths'
Remove-Item -LiteralPath $reservedPublicAssets -Recurse -Force

dotnet publish $webProjectPath --configuration Release --no-restore --output $emptyWebPublishPath
dotnet publish $functionsProjectPath --configuration Release --no-restore --output (Join-Path $emptyPublishPath 'functions')
$emptyWebRoot = Join-Path $emptyWebPublishPath 'wwwroot'
Assert-Files $emptyWebRoot @('index.html', '_markazor/app.html', 'styles/site.css', '_content/Markazor.Themes/theme.css')
foreach ($path in @('markazor.settings.json', 'content')) {
    if (Test-Path -LiteralPath (Join-Path $emptyWebRoot $path)) {
        throw "Empty template publish must not create $path."
    }
}

$publishedMarkazorRoot = Join-Path $emptyWebRoot '_content/Markazor'
Assert-Files $publishedMarkazorRoot @('markazor-markdown-editor.js', 'markazor-markdown-editor.css', 'editor')
$hasMuyaLicense = $false
foreach ($assetFile in Get-ChildItem -LiteralPath $publishedMarkazorRoot -File -Recurse | Where-Object Extension -In '.js', '.css') {
    $assetText = Get-Content -LiteralPath $assetFile.FullName -Raw
    $hasMuyaLicense = $hasMuyaLicense -or $assetText.Contains('Copyright (c) 2018-present Luo Ran')
    foreach ($reference in [regex]::Matches($assetText, '/_content/Markazor/[A-Za-z0-9_./-]+')) {
        if ($reference.Value -match '\.(js|css|woff2?|ttf|svg|png)$') {
            Assert-Files $publishedMarkazorRoot @($reference.Value.Substring('/_content/Markazor/'.Length))
        }
    }
}
if (-not $hasMuyaLicense) {
    throw 'Published editor assets must retain the Muya MIT license notice.'
}

$siteSettingsPath = Join-Path $generatedPath 'public/markazor.settings.json'
Write-SmokeFile 'public/markazor.settings.json' @"
{
  "site": {
    "name": "$ProjectName",
    "description": "Smoke test site generated from Markazor templates.",
    "baseUrls": ["https://example.test"]
  },
  "theme": { "name": "default" }
}
"@
Write-SmokeFile 'public/index.html' @'
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
Write-SmokeFile 'public/styles/site.css' 'body { --markazor-smoke-site-css: custom; }'
Write-SmokeFile 'public/scripts/site.js' 'document.documentElement.dataset.markazorSmokeSiteJs = "custom";'
Write-SmokeFile 'public/robots.txt' "User-agent: *`nAllow: /"
Write-SmokeFile 'assets/smoke-asset.txt' 'smoke asset'
[IO.File]::WriteAllBytes((Join-Path $generatedPath 'assets/site-icon.png'), [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAFgwJ/lPyo1wAAAABJRU5ErkJggg=='))
Write-SmokeFile 'posts/C# hello%.md' @'
---
slug: hello-world
title: Hello World
summary: First Markazor post.
publishedAt: 2026-06-03T00:00:00Z
tags: [intro, markazor, C#]
category: General
---

# Hello World

This file is compiled into `Markazor.Generated.SiteIndex`.
'@
Write-SmokeFile 'posts/first-update.md' @'
---
slug: first-update
title: First Update
publishedAt: 2026-06-06T08:00:00Z
tags: [writing, markazor]
category: Writing
---

Short updates and longer articles share the same public content history.
'@
for ($index = 0; $index -lt 10; $index++) {
    Write-SmokeFile "posts/pagination-$index.md" "# Pagination $index"
}
Write-SmokeFile 'drafts/work-in-progress.md' "---`nslug: work-in-progress`ndraft: true`n---`nPrivate draft."

$watchOutput = (& dotnet watch --project $webProjectPath --list 2>&1) -join [Environment]::NewLine
$watchOutput = $watchOutput.Replace('\', '/')
foreach ($relative in @('public/markazor.settings.json', 'assets/smoke-asset.txt', 'public/index.html', 'public/styles/site.css', 'public/scripts/site.js', 'posts/C# hello%.md', 'posts/first-update.md', 'drafts/work-in-progress.md')) {
    $path = [IO.Path]::GetFullPath((Join-Path $generatedPath $relative)).Replace('\', '/')
    if (-not $watchOutput.Contains($path)) {
        throw "Watch list is missing source file: $path"
    }
}
if ($watchOutput.Contains([IO.Path]::GetFullPath($generatedWebRoot).Replace('\', '/').TrimEnd('/') + '/')) {
    throw 'The intermediate web root must not be watched.'
}

dotnet build $solutionPath --configuration Release --no-restore
$validSiteSettings = Get-Content -Raw -LiteralPath $siteSettingsPath
Set-Content -LiteralPath $siteSettingsPath -Value $validSiteSettings.Replace('"name": "default"', '"name": "missing-theme"') -NoNewline
Assert-BuildFails "Unknown Markazor theme 'missing-theme'"
Set-Content -LiteralPath $siteSettingsPath -Value $validSiteSettings.Replace('"name": "default"', '"name": "none"') -NoNewline
dotnet build $webProjectPath --configuration Release --no-restore
foreach ($file in @('theme.css', 'theme.js')) {
    if (-not [string]::IsNullOrWhiteSpace((Get-Content -Raw -LiteralPath (Join-Path $generatedWebRoot "_content/Markazor.Themes/$file")))) {
        throw "The none theme must emit an empty $file."
    }
}
Set-Content -LiteralPath $siteSettingsPath -Value $validSiteSettings -NoNewline
dotnet publish $webProjectPath --configuration Release --no-restore --output $webPublishPath
dotnet publish $functionsProjectPath --configuration Release --no-restore --output $functionsPublishPath

$functionsMetadata = Get-Content -Raw -LiteralPath (Join-Path $functionsPublishPath 'functions.metadata') | ConvertFrom-Json
foreach ($name in @('GitHubAuthStart', 'GitHubAuthCallback', 'GitHubAuthRefresh', 'SetupStatus')) {
    if ($name -notin $functionsMetadata.name) {
        throw "Published Functions metadata is missing $name."
    }
}
$functionRoutes = $functionsMetadata.bindings | Where-Object type -eq httpTrigger | Select-Object -ExpandProperty route
foreach ($route in @('auth/github/start', 'auth/github/callback', 'auth/github/refresh', 'setup/status')) {
    if ($route -notin $functionRoutes) {
        throw "Published Functions metadata is missing route $route."
    }
}
if ((Get-Content -Raw -LiteralPath (Join-Path $functionsPublishPath 'host.json') | ConvertFrom-Json).version -ne '2.0') {
    throw 'Published Functions host.json must use version 2.0.'
}
if ((Get-FileHash -LiteralPath (Join-Path $functionsPublishPath 'markazor.settings.json')).Hash -ne (Get-FileHash -LiteralPath $siteSettingsPath).Hash) {
    throw 'Published Functions must use the canonical public settings.'
}

$publishedWebRoot = Join-Path $webPublishPath 'wwwroot'
Assert-Files $publishedWebRoot @(
    '_markazor/content/posts/C# hello%.md.json', '_markazor/content/posts/first-update.md.json',
    'tags/c#/index.html', 'manifest.webmanifest', 'service-worker.js', 'service-worker-assets.js'
)
foreach ($relative in @('assets/smoke-asset.txt', 'assets/site-icon.png', 'public/markazor.settings.json', 'public/styles/site.css', 'public/scripts/site.js', 'public/robots.txt')) {
    $source = Join-Path $generatedPath $relative
    $published = Join-Path $publishedWebRoot ($relative -replace '^public/', '')
    if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $published).Hash) {
        throw "Publish did not preserve the user asset: $relative"
    }
}
$publishedWorkerSource = (Get-Content -Raw -LiteralPath (Join-Path $generatedWebRoot 'service-worker.published.js')).ReplaceLineEndings("`n").Trim()
foreach ($root in @($emptyWebRoot, $publishedWebRoot)) {
    $worker = (Get-Content -Raw -LiteralPath (Join-Path $root 'service-worker.js')).ReplaceLineEndings("`n").Trim()
    if (-not $worker.EndsWith($publishedWorkerSource, [StringComparison]::Ordinal)) {
        throw 'Publish must select the production service worker.'
    }
    foreach ($file in @('index.html', '_markazor/app.html')) {
        $html = Get-Content -Raw -LiteralPath (Join-Path $root $file)
        if ($html.Contains('#[.{fingerprint}]')) {
            throw "Published $file contains an unresolved SDK fingerprint."
        }
        if ($root -eq $publishedWebRoot -and -not $html.Contains('data-smoke-shell="custom"')) {
            throw "Publish did not use the custom shell for $file."
        }
    }
}
if (Test-Path -LiteralPath (Join-Path $publishedWebRoot '_markazor/content/drafts')) {
    throw 'Published output must not contain drafts.'
}
$manifest = Get-Content -Raw -LiteralPath (Join-Path $publishedWebRoot 'service-worker-assets.js')
foreach ($excluded in @('drafts/', 'work-in-progress.md', 'staticwebapp.config.json')) {
    if ($manifest.Contains($excluded, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Service worker assets must not include $excluded."
    }
}
foreach ($required in @('_markazor/content/posts/first-update.md.json', '_markazor/content/posts/C# hello%.md.json', 'tags/c#/index.html', 'assets/smoke-asset.txt', '_content/Markazor/markazor-markdown-editor.js')) {
    if (-not $manifest.Contains($required, [StringComparison]::Ordinal)) {
        throw "Service worker assets are missing $required."
    }
}

$run = dotnet msbuild $webProjectPath -property:Configuration=Release '-getProperty:RunCommand,RunArguments' | ConvertFrom-Json
$server = [Diagnostics.Process]::Start([Diagnostics.ProcessStartInfo] @{
    FileName = $run.Properties.RunCommand
    Arguments = $run.Properties.RunArguments + ' --urls http://127.0.0.1:0'
    WorkingDirectory = $webProjectDirectory
    RedirectStandardOutput = $true
    UseShellExecute = $false
    CreateNoWindow = $true
})
try {
    while (($line = $server.StandardOutput.ReadLineAsync().WaitAsync([TimeSpan]::FromSeconds(30)).GetAwaiter().GetResult()) -notmatch 'Now listening on: (http://127\.0\.0\.1:\d+)') {
        if ($null -eq $line) { throw 'The local development server exited before starting.' }
    }
    $origin = $Matches[1]
    $routes = (Invoke-WebRequest "$origin/_markazor/routes.json" -TimeoutSec 10).Content | ConvertFrom-Json -AsHashtable
    if (-not $routes.Contains('/posts/page/2')) { throw 'Local route coverage must include pagination.' }
    foreach ($path in @('/studio', '/studio/write', '/studio/settings', '/studio/setup', '/studio/setup/github-callback')) {
        $routes[$path] = '/_markazor/app.html'
    }
    foreach ($route in $routes.GetEnumerator()) {
        $expected = (Invoke-WebRequest "$origin$($route.Value)" -TimeoutSec 10).Content
        foreach ($path in @($route.Key, ($route.Key.TrimEnd('/') + '/'))) {
            if ((Invoke-WebRequest "$origin$path" -TimeoutSec 10).Content -cne $expected) { throw "Local route did not serve its generated page: $path" }
        }
    }
}
finally {
    if (-not $server.HasExited) { $server.Kill($true) }
    $server.Dispose()
}

if ($IsLinux) {
    docker run --rm --volume "${repoRoot}:${repoRoot}" --workdir $repoRoot `
        --env ENABLE_DYNAMIC_INSTALL=true --env ORYX_APP_TYPE=static-sites `
        --env "MARKAZOR_SITE=$generatedPath" --env "MARKAZOR_WEB=$webProjectDirectory" `
        --entrypoint bash mcr.microsoft.com/appsvc/staticappsclient:stable -ec @'
mv "$MARKAZOR_SITE/posts" "$MARKAZOR_SITE/posts-oryx"
oryx build "$MARKAZOR_WEB" --output "$MARKAZOR_SITE/../oryx-empty" --platform dotnet
mv "$MARKAZOR_SITE/posts-oryx" "$MARKAZOR_SITE/posts"
oryx build "$MARKAZOR_WEB" --output "$MARKAZOR_SITE/../oryx-posts" --platform dotnet
'@
}

dotnet new uninstall Markazor.Templates
Write-Output $generatedPath
