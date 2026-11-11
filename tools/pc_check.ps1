# Dream Game: a full check on a Windows PC, in one go.
#
#   powershell -ExecutionPolicy Bypass -File tools\pc_check.ps1 [-Godot <path to the console exe>] [-Quick]
#
# Run from the repository folder. Builds the game, imports its assets, runs the benchmark scenes one after
# another (each in its own game window, each with a time limit), and writes everything to check\report.md:
# the summaries, the averages from each run's CSV, every error line the game printed, and where the screenshots
# are (copied into check\shots). -Quick runs just the look and idle scenes.
param(
    [string]$Godot = "",
    [switch]$Quick
)

$ErrorActionPreference = "Continue"
$repo = (Get-Location).Path
if (-not (Test-Path "$repo\project.godot")) { Write-Error "Run this from the dream-game folder."; exit 1 }
if ($Godot -eq "") {
    $found = Get-ChildItem -Path (Split-Path $repo -Parent) -Recurse -Depth 2 -Filter "Godot_v4.7*_console.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) { $Godot = $found.FullName } else { Write-Error "Couldn't find the Godot console exe next to the repo; pass -Godot."; exit 1 }
}

$check = "$repo\check"
Remove-Item -Recurse -Force $check -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force "$check\runs", "$check\shots", "$check\logs" | Out-Null
$report = New-Object System.Collections.Generic.List[string]
$report.Add("# Dream Game check, $(Get-Date -Format 'yyyy-MM-dd HH:mm')")
$commit = & git rev-parse --short HEAD 2>$null
$report.Add("Commit $commit, Godot $Godot")

# Build and import.
$build = & dotnet build DreamGame.csproj 2>&1 | Out-String
$buildErrors = ($build -split "`n") | Where-Object { $_ -match " error | warning " } | Select-Object -Unique
$report.Add("`n## Build`n" + $(if ($buildErrors) { ($buildErrors -join "`n") } else { "OK, no errors or warnings." }))
$import = & $Godot --path . --headless --import 2>&1 | Out-String
$import | Set-Content "$check\logs\import.txt"
$importErrors = ($import -split "`n") | Where-Object { $_ -match "ERROR|SHADER|Exception|error" } | Select-Object -Unique
$report.Add("`n## Import`n" + $(if ($importErrors) { ($importErrors | Select-Object -First 40) -join "`n" } else { "OK." }))

$runs = @(
    @("look-warden", "look", 30, "--design=Warden"),
    @("look-colossus", "look", 30, "--design=Colossus"),
    @("look-falcon", "look", 30, "--design=Falcon"),
    @("idle", "idle", 40, ""),
    @("village", "village", 40, ""),
    @("evening", "village", 30, "--time=evening"),
    @("night", "look", 30, "--design=Warden --time=night"),
    @("rain", "look", 30, "--design=Warden --weather=rain"),
    @("fog", "idle", 30, "--weather=fog"),
    @("deck", "deck", 50, ""),
    @("drill", "drill", 60, ""),
    @("bigdrill", "bigdrill", 60, "")
)
if ($Quick) { $runs = $runs | Where-Object { $_[1] -in @("look", "idle") } }

$report.Add("`n## Runs")
foreach ($r in $runs) {
    $name, $scene, $secs, $extra = $r
    $out = "$check\runs\$name"
    New-Item -ItemType Directory -Force $out | Out-Null
    $gargs = @("--path", ".", "--maximized", "--", "--bench=$scene", "--seconds=$secs", "--out=$out") + ($extra -split " " | Where-Object { $_ -ne "" })
    $log = "$check\logs\$name.txt"
    $p = Start-Process -FilePath $Godot -ArgumentList $gargs -RedirectStandardOutput $log -RedirectStandardError "$log.err" -PassThru -NoNewWindow
    if (-not $p.WaitForExit(($secs + 120) * 1000)) {
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        $report.Add("`n### $name: HUNG (stopped after $($secs + 120) s)")
    }
    $text = (Get-Content $log -ErrorAction SilentlyContinue) + (Get-Content "$log.err" -ErrorAction SilentlyContinue)
    $errors = $text | Where-Object { $_ -match "ERROR|SHADER|Exception|error:" } | Group-Object | ForEach-Object { "$($_.Count)x $($_.Name)" }
    $folder = Get-ChildItem $out -Directory | Select-Object -First 1
    $report.Add("`n### $name ($scene $secs s $extra)")
    if ($folder -and (Test-Path "$($folder.FullName)\summary.txt")) {
        $report.Add((Get-Content "$($folder.FullName)\summary.txt") -join "`n")
        $csv = Import-Csv "$($folder.FullName)\bench.csv"
        if ($csv) {
            $cols = "frame_ms", "scripts_ms", "physics_ms", "draw_calls", "objects", "awake_bodies", "shells", "vehicles"
            $avg = foreach ($c in $cols) { "$c " + [math]::Round(($csv | Measure-Object -Property $c -Average).Average, 2) }
            $report.Add("Averages: " + ($avg -join ", ") + "; worst frame_ms " + [math]::Round(($csv | Measure-Object -Property frame_ms -Maximum).Maximum, 1))
        }
        New-Item -ItemType Directory -Force "$check\shots\$name" | Out-Null
        Copy-Item "$($folder.FullName)\*.png" "$check\shots\$name\" -ErrorAction SilentlyContinue
    } else {
        $report.Add("No summary written (it crashed, hung, or quit early).")
    }
    if ($errors) { $report.Add("Errors:`n" + (($errors | Select-Object -First 30) -join "`n")) }
}

$report | Set-Content "$check\report.md"
Write-Host "Done: $check\report.md, screenshots in $check\shots"
