# Copies the recording build's hand files off the headset into Recordings/, deleting each from the headset only
# after its copy is verified. While the app runs, files are copied but kept, because the newest one is still open.
# A file kept as a replay fixture is gzipped into Assets/Edith/Tests/Editor/Fixtures.
$ErrorActionPreference = "Stop"
$package = "com.hackwestern.edith"
$remote = "/sdcard/Android/data/$package/files/hands"
$local = Join-Path $PSScriptRoot "..\Recordings"

$adb = @(
    "C:\Program Files\Unity\Hub\Editor\6000.6.9f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe",
    $(if ($env:ANDROID_HOME) { Join-Path $env:ANDROID_HOME "platform-tools\adb.exe" }),
    (Get-Command adb -ErrorAction SilentlyContinue).Source
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $adb) { throw "adb not found: install Unity's Android tools, set ANDROID_HOME, or put adb on PATH." }

New-Item -ItemType Directory -Force $local | Out-Null
$files = & $adb shell ls $remote 2>$null | Where-Object { $_ -like "*.jsonl" }
if (-not $files) {
    Write-Output "No hand recordings on the headset."
    exit 0
}

$running = [bool](& $adb shell pidof $package)
if ($running) { Write-Output "$package is running, so recordings are copied but left on the headset." }

foreach ($file in $files) {
    $target = Join-Path $local $file
    & $adb pull "$remote/$file" $target | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "adb pull failed for $file; it stays on the headset." }
    $remoteSize = [long](& $adb shell stat -c %s "$remote/$file")
    $localSize = (Get-Item $target).Length
    if ($localSize -ne $remoteSize) { throw "$file copied $localSize of $remoteSize bytes; it stays on the headset." }
    if (-not $running) {
        & $adb shell rm "$remote/$file"
        if ($LASTEXITCODE -ne 0) { throw "Copied $file but could not delete it from the headset." }
    }
    Write-Output "Pulled $file ($localSize bytes)"
}
