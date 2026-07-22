# Recreates the docs/ demo end-to-end: seeds clock-relative demo data, launches
# the Windows app with CDP enabled, captures every screen in both themes via
# tools/UiTest's capture mode, and rebuilds tour.html.
#
#   .\docs\capture.ps1                       # local: uses the Debug build
#   .\docs\capture.ps1 -ExePath <exe> -Embed # CI: published exe + single-file tour
#
# The user's real tasks.json is backed up before seeding and restored after the
# app has exited — restoring while the app still runs would be undone by its
# shutdown write. Runs on Windows PowerShell 5.1 and pwsh alike.

param(
    [string]$ExePath,
    [int]$Port = 9333,
    [switch]$Embed
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
if (-not $ExePath) {
    $ExePath = Join-Path $repo 'TaskDashboard\bin\Debug\net10.0-windows10.0.19041.0\win-x64\TaskDashboard.exe'
}
if (-not (Test-Path $ExePath)) {
    throw "App not found at $ExePath -- build the Windows TFM first (or pass -ExePath)."
}

# FileSystem.AppDataDirectory for the unpackaged app: the publisher display
# name comes from Package.appxmanifest, so this path is the same everywhere.
$dataDir = Join-Path $env:LOCALAPPDATA 'User Name\com.aedev2025.taskdashboard\Data'
$dataFile = Join-Path $dataDir 'tasks.json'
$backup = "$dataFile.pre-capture"

function Iso([DateTime]$t) { return $t.ToString('o') }

function Build-DemoJson {
    $now = Get-Date
    $tomorrow9 = $now.Date.AddDays(1).AddHours(9)

    # Everything is relative to now (the fixture rule from CLAUDE.md): the
    # in-progress task anchors the now-line, completions sit in the few hours
    # behind it, and the queue plans out ahead -- so one calendar viewport
    # holds history, marker and plan at whatever hour this runs.
    $tasks = @(
        [ordered]@{ Id = '00000001-1111-2222-3333-444444444444'; Title = 'Write the quarterly report'; IsDone = $false
                    StartedAt = Iso $now.AddMinutes(-23); CompletedAt = $null; CreatedAt = Iso $now.AddDays(-1)
                    Deadline = Iso $now.AddHours(3); NotBefore = $null; Priority = 'High'; EstimatedTime = '01:30:00' }
        [ordered]@{ Id = '00000002-1111-2222-3333-444444444444'; Title = 'Prepare the demo slides'; IsDone = $false
                    StartedAt = $null; CompletedAt = $null; CreatedAt = Iso $now.AddDays(-1)
                    Deadline = Iso $tomorrow9; NotBefore = $null; Priority = 'Urgent'; EstimatedTime = '01:00:00' }
        [ordered]@{ Id = '00000003-1111-2222-3333-444444444444'; Title = 'Review pull request #42'; IsDone = $false
                    StartedAt = $null; CompletedAt = $null; CreatedAt = Iso $now.AddDays(-1)
                    Deadline = $null; NotBefore = $null; Priority = 'Normal'; EstimatedTime = '00:30:00' }
        [ordered]@{ Id = '00000004-1111-2222-3333-444444444444'; Title = 'Send the design feedback'; IsDone = $false
                    StartedAt = $null; CompletedAt = $null; CreatedAt = Iso $now.AddDays(-1)
                    Deadline = $null; NotBefore = $null; Priority = 'Normal'; EstimatedTime = '00:15:00' }
        [ordered]@{ Id = '00000005-1111-2222-3333-444444444444'; Title = 'Update dependency versions'; IsDone = $false
                    StartedAt = $null; CompletedAt = $null; CreatedAt = Iso $now.AddDays(-1)
                    Deadline = $null; NotBefore = $null; Priority = 'Normal'; EstimatedTime = '00:45:00' }
        [ordered]@{ Id = '00000006-1111-2222-3333-444444444444'; Title = 'Book a dentist appointment'; IsDone = $false
                    StartedAt = $null; CompletedAt = $null; CreatedAt = Iso $now.AddDays(-1)
                    Deadline = $null; NotBefore = Iso $tomorrow9; Priority = 'Low'; EstimatedTime = '00:10:00' }
        [ordered]@{ Id = '00000007-1111-2222-3333-444444444444'; Title = 'Write up the retro notes'; IsDone = $true
                    StartedAt = Iso $now.AddMinutes(-272); CompletedAt = Iso $now.AddMinutes(-257); CreatedAt = Iso $now.AddDays(-1)
                    Deadline = $null; NotBefore = $null; Priority = 'Normal'; EstimatedTime = '00:15:00' }
        [ordered]@{ Id = '00000008-1111-2222-3333-444444444444'; Title = 'Fix the login redirect'; IsDone = $true
                    StartedAt = Iso $now.AddMinutes(-247); CompletedAt = Iso $now.AddMinutes(-182); CreatedAt = Iso $now.AddDays(-1)
                    Deadline = $null; NotBefore = $null; Priority = 'High'; EstimatedTime = '01:00:00' }
        [ordered]@{ Id = '00000009-1111-2222-3333-444444444444'; Title = 'Triage the inbox'; IsDone = $true
                    StartedAt = Iso $now.AddMinutes(-172); CompletedAt = Iso $now.AddMinutes(-142); CreatedAt = Iso $now.AddDays(-1)
                    Deadline = $null; NotBefore = $null; Priority = 'Low'; EstimatedTime = '00:30:00' }
    )

    $blocked = @(
        [ordered]@{ Id = '00000050-1111-2222-3333-444444444444'; Label = 'Sleep'; IsRecurring = $true
                    Days = @(0, 1, 2, 3, 4, 5, 6); StartTime = '23:00:00'; EndTime = '07:00:00'; Start = $null; End = $null }
        [ordered]@{ Id = '00000051-1111-2222-3333-444444444444'; Label = 'Lunch'; IsRecurring = $true
                    Days = @(1, 2, 3, 4, 5); StartTime = '12:30:00'; EndTime = '13:15:00'; Start = $null; End = $null }
        [ordered]@{ Id = '00000052-1111-2222-3333-444444444444'; Label = 'Team sync'; IsRecurring = $false
                    Days = @(); StartTime = $null; EndTime = $null
                    Start = Iso $now.Date.AddDays(1).AddHours(15); End = Iso $now.Date.AddDays(1).AddHours(16) }
    )

    $doc = [ordered]@{ Version = 2; Tasks = $tasks; BlockedPeriods = $blocked; BreakMinutes = 15; Theme = 'system' }
    return ($doc | ConvertTo-Json -Depth 6)
}

$hadBackup = $false
try {
    Get-Process TaskDashboard -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2

    New-Item -ItemType Directory -Force $dataDir | Out-Null
    if (Test-Path $dataFile) {
        Copy-Item $dataFile $backup -Force
        $hadBackup = $true
        Write-Host "backed up tasks.json"
    }
    [IO.File]::WriteAllText($dataFile, (Build-DemoJson), (New-Object Text.UTF8Encoding $false))
    Write-Host "seeded demo data"

    $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = "--remote-debugging-port=$Port"
    Start-Process $ExePath
    $ready = $false
    foreach ($i in 1..30) {
        Start-Sleep -Seconds 2
        try {
            Invoke-WebRequest "http://localhost:$Port/json/version" -UseBasicParsing -TimeoutSec 5 | Out-Null
            $ready = $true
            break
        } catch { }
    }
    if (-not $ready) { throw "App did not expose CDP on port $Port within 60s." }
    Write-Host "app is up, CDP reachable"

    Push-Location $repo
    try {
        dotnet run --project tools/UiTest -- capture (Join-Path $repo 'docs\screenshots')
        if ($LASTEXITCODE -ne 0) { throw "capture mode failed with exit code $LASTEXITCODE" }

        python (Join-Path $repo 'docs\build-tour.py')
        if ($LASTEXITCODE -ne 0) { throw "build-tour.py failed" }
        if ($Embed) {
            python (Join-Path $repo 'docs\build-tour.py') --embed
            if ($LASTEXITCODE -ne 0) { throw "build-tour.py --embed failed" }
        }
    } finally {
        Pop-Location
    }
    Write-Host "demo recreated"
}
finally {
    Get-Process TaskDashboard -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
    if ($hadBackup) {
        Move-Item $backup $dataFile -Force
        Write-Host "restored tasks.json"
    } elseif (Test-Path $dataFile) {
        Remove-Item $dataFile -Force
        Write-Host "removed seeded tasks.json (no prior data)"
    }
}
