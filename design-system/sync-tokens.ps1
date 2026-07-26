# Copies the real token declarations out of app.css into every preview in this
# folder, replacing whatever sits between the ds-tokens markers.
#
# The previews are published standalone to a Claude Design project, so they
# cannot <link> app.css -- they have to carry a copy. Copying it mechanically is
# the difference between "a copy" and "a copy that drifts": before this script
# the previews inlined per-theme hex by hand, and reproduced the hardcoded
# primary-button blue that the audit flagged in the app itself, so the docs
# blessed the bug instead of catching it.
#
# Run after changing tokens in app.css, then re-publish the previews.

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$appCss = Join-Path $root '..\TaskDashboard\wwwroot\app.css'

if (-not (Test-Path $appCss)) {
    throw "Cannot find app.css at $appCss"
}

# Explicit UTF-8 both ways. PowerShell 5.1's Get-Content decodes BOM-less files
# as the system ANSI codepage, which turns every em dash in these previews into
# mojibake on the round trip.
$utf8NoBom = New-Object System.Text.UTF8Encoding $false

function Read-Utf8([string]$path) {
    return [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
}

$css = Read-Utf8 $appCss

# Both token blocks are flat declaration lists, so a non-greedy match to the
# first closing brace is sufficient -- no nesting to balance.
function Get-Block([string]$selector) {
    $pattern = [regex]::Escape($selector) + '\s*\{[^}]*\}'
    $match = [regex]::Match($css, $pattern)
    if (-not $match.Success) {
        throw "Could not find '$selector' in app.css"
    }
    return $match.Value
}

$tokens = (Get-Block ':root'), (Get-Block '[data-bs-theme=dark]') -join "`n"

$startMarker = '<style id="ds-tokens">'
$endMarker = '</style><!-- /ds-tokens -->'
$written = 0

foreach ($file in Get-ChildItem -Path $root -Filter *.html) {
    $html = Read-Utf8 $file.FullName
    $start = $html.IndexOf($startMarker)
    $end = $html.IndexOf($endMarker)

    if ($start -lt 0 -or $end -lt 0) {
        Write-Warning "$($file.Name): no ds-tokens markers, skipped"
        continue
    }

    $head = $html.Substring(0, $start + $startMarker.Length)
    $tail = $html.Substring($end)
    $updated = $head + "`n" + $tokens + "`n" + $tail

    if ($updated -ne $html) {
        [System.IO.File]::WriteAllText($file.FullName, $updated, $utf8NoBom)
        Write-Host "updated $($file.Name)"
        $written++
    }
    else {
        Write-Host "unchanged $($file.Name)"
    }
}

Write-Host "`n$written file(s) rewritten from app.css"
