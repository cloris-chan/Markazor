[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrWhiteSpace()]
    [string] $Version,

    [ValidateRange(1, 3600)]
    [int] $TimeoutSeconds = 600,

    [ValidateRange(1, 300)]
    [int] $PollSeconds = 15,

    [ValidateNotNullOrEmpty()]
    [string[]] $PackageIds = @(
        'Markazor.SourceGen',
        'Markazor.Core',
        'Markazor.Api',
        'Markazor.Themes',
        'Markazor.Templates',
        'Markazor'
    )
)

$ErrorActionPreference = 'Stop'
$deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)

while ($true) {
    $pending = [System.Collections.Generic.List[string]]::new()

    foreach ($packageId in $PackageIds) {
        $flatContainerId = $packageId.ToLowerInvariant()
        $indexUrl = "https://api.nuget.org/v3-flatcontainer/$flatContainerId/index.json"

        try {
            $index = Invoke-RestMethod -Uri $indexUrl -TimeoutSec 30
            if ($index.versions -contains $Version) {
                Write-Host "$packageId $Version is available from NuGet.org."
                continue
            }

            $pending.Add($packageId)
        }
        catch {
            Write-Host "$packageId $Version is not available from NuGet.org yet: $($_.Exception.Message)"
            $pending.Add($packageId)
        }
    }

    if ($pending.Count -eq 0) {
        Write-Host "All Markazor packages for $Version are available from NuGet.org."
        break
    }

    if ([DateTimeOffset]::UtcNow -ge $deadline) {
        $pendingList = [string]::Join(', ', $pending)
        throw "Timed out waiting for Markazor packages to become available from NuGet.org: $pendingList."
    }

    $remainingSeconds = [int]($deadline - [DateTimeOffset]::UtcNow).TotalSeconds
    $pendingList = [string]::Join(', ', $pending)
    Write-Host "Waiting $PollSeconds seconds for NuGet.org propagation. Pending: $pendingList. Remaining timeout: $remainingSeconds seconds."
    Start-Sleep -Seconds $PollSeconds
}
