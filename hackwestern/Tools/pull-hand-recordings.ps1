# Copies the recording build's hand files off the headset into Recordings/, then deletes them from the headset.
# A file kept as a replay fixture is gzipped into Assets/Edith/Tests/Editor/Fixtures.
$ErrorActionPreference = "Stop"
$adb = "C:\Program Files\Unity\Hub\Editor\6000.6.9f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
$remote = "/sdcard/Android/data/com.hackwestern.edith/files/hands"
$local = Join-Path $PSScriptRoot "..\Recordings"

New-Item -ItemType Directory -Force $local | Out-Null
$files = & $adb shell ls $remote 2>$null | Where-Object { $_ -like "*.jsonl" }
if (-not $files) {
    Write-Output "No hand recordings on the headset."
    exit 0
}
foreach ($file in $files) {
    & $adb pull "$remote/$file" (Join-Path $local $file) | Out-Null
    & $adb shell rm "$remote/$file"
    Write-Output "Pulled $file"
}
