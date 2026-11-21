# EDITH headset app

The Unity project for the EDITH Quest 3 and 3S app. You use it with bare hands: pinch and drag a box to Look at an object, make a fist on a card to collect it, tap your thumb to Ask, hold both palms out to Scan, and pair your phone for location. The backend that answers the Look, Ask and Scan calls is in [`../backend`](../backend/README.md).

This page covers building, installing, testing and checking the app. The package id is `com.hackwestern.edith`, and the Unity version is 6000.6.9f1 (`ProjectSettings/ProjectVersion.txt`).

## Prerequisites

1. Install Unity Hub, and in it the Unity editor 6000.6.9f1. On a Windows Arm laptop, pick the x64 editor, which runs under emulation. Meta's editor plugins target x64.
2. In the same install, add the Android Build Support module with OpenJDK and Android SDK & NDK Tools.
3. Turn on Developer Mode for the Quest in the Meta Horizon phone app, under Devices.
4. Connect the headset with a USB-C cable that carries data. A charge-only cable never shows up in `adb devices`.

The adb that this page uses ships inside the Unity install:

```powershell
$unity = "C:\Program Files\Unity\Hub\Editor\6000.6.9f1\Editor\Unity.exe"
$adb = "C:\Program Files\Unity\Hub\Editor\6000.6.9f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
```

## Open the project

In Unity Hub, click **Add** > **Add project from disk** and select this `headset` folder. The first open downloads the packages in `Packages/manifest.json` and compiles them, which takes several minutes.

Unity can crash on project open (exit code `0xc0000409` in Meta's `OVRPlugin.dll`) while an adb server is running with the Quest connected. Run `& $adb kill-server` before you open the project or start a headless run, and start adb again only to install.

## Set up the project

`Assets/Edith/Editor/ProjectSetup.cs` adds an **EDITH** menu. **EDITH** > **Configure Project** does the whole setup. It switches to Android, sets the package id, IL2CPP, ARM64, minimum API 32 and linear color, targets Quest 3 and 3S with hands-only input, creates `Assets/Edith/Scenes/Main.unity` if it is missing, and adds every feature to the scene. Then it writes the backend settings and runs Meta's project fixes. Every step converges, so running it again is safe.

The feature setups that `Configure` calls can also run on their own against an existing scene:

| Setup | What it adds to `Main.unity` | Batch entry |
| --- | --- | --- |
| `DepthSetup` | The Depth API and the hand occluders | none, run by `Configure` |
| `PinchSetup` | The right hand and the pinch input | none, run by `Configure` |
| `GrabSetup` | The fist grab | `Edith.Editor.GrabSetup.BatchAdd` |
| `AskSetup` | Ask, and the microphone permission | `Edith.Editor.AskSetup.BatchAdd` |
| `PairingSetup` | The phone pairing panel | `Edith.Editor.PairingSetup.BatchAdd` |
| `LookSetup` | The headset camera and the Look controller | `Edith.Editor.LookSetup.BatchAdd` |
| `ScanSetup` | The left hand and Scan | `Edith.Editor.ScanSetup.BatchAdd` |
| `DiagnosticsSetup` | The status panel | `Edith.Editor.DiagnosticsSetup.BatchAdd` |

Each setup needs the objects of the ones before it, in the order of the table, and throws an error that names the missing one. Only `ProjectSetup` has menu items.

## Build from a terminal

Run setup and build as two separate Unity sessions. Meta's fixes change scripting defines, and the recompile that follows blocks a build in the same session. Run both from the `headset` folder. Do not pass `-quit` to `BatchConfigure`, because Meta's fixes are asynchronous and the method exits the editor itself.

```powershell
& $adb kill-server
& $unity -batchmode -nographics -projectPath . -buildTarget Android -executeMethod Edith.Editor.ProjectSetup.BatchConfigure -logFile setup.log
& $unity -batchmode -nographics -quit -projectPath . -buildTarget Android -executeMethod Edith.Editor.ProjectSetup.BatchBuild -logFile build.log
```

The first APK build is slow because IL2CPP compiles all native code. Later builds reuse that work. The APK builds are:

| Menu item | Batch entry | APK | Extra define |
| --- | --- | --- | --- |
| **Build APK** | `BatchBuild` | `Builds/edith.apk` | none |
| **Build Diagnostics APK** | `BatchBuildDiagnostics` | `Builds/edith-diagnostics.apk` | `EDITH_DIAGNOSTICS` |
| **Build Recording APK** | `BatchBuildRecording` | `Builds/edith-record.apk` | `EDITH_RECORD_HANDS` |

Do not use Unity's Development Build flag. Meta's SDK then writes this machine's network address and an access token into the build. The two defines above compile the extra code in without it.

## Install and start the app

```powershell
& $adb devices -l
& $adb install -r Builds\edith.apk
& $adb shell monkey -p com.hackwestern.edith 1
```

The headset shows as `device`. If it shows as `unauthorized`, unplug the cable, plug it back in with the headset on, and accept the **Allow USB debugging?** prompt. Start adb only for the install. If Unity then crashes on open, see [Open the project](#open-the-project).

## Backend settings

`Assets/Edith/Runtime/Backend/BackendSettings.cs` holds the backend URL and the device token the build carries. `Assets/Edith/Editor/BackendSettingsSetup.cs` writes them to `Assets/Resources/EdithBackend.asset`, which git ignores. It reads `EDITH_BACKEND_URL` and `DEVICE_TOKEN` from the environment, then from `../backend/.env`. Copy `../backend/.env.example` to `../backend/.env` and fill both in.

`Configure`, `AskSetup.BatchAdd`, `PairingSetup.BatchAdd` and `ScanSetup.BatchAdd` all write the asset, so rerun one of them after you change the URL or the token, then rebuild. Use an `https` URL. Android blocks plain `http` unless the network security config allows it, and the app logs a warning at launch when the URL is plain HTTP. Without a URL, Ask, Scan and pairing are off and the app logs `[EDITH backend] no backend URL in this build`.

## Run the tests

The EditMode tests need no headset. Close any editor that has the project open, then run:

```powershell
& $adb kill-server
& $unity -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults results.xml -logFile tests.log
```

Do not pass `-quit`. The editor exits when the tests finish, and the totals are in the `test-run` line at the top of `results.xml`. You can also open **Window** > **General** > **Test Runner** in the editor. The pinch replay tests read the recordings in `Assets/Edith/Tests/Editor/Fixtures`.

## Read the headset's log

The app copies every Unity log line to `edith.log` in its files folder, and keeps the run before in `edith.log.old`. The file rolls over at 2 MB. Pull both:

```powershell
& $adb pull /sdcard/Android/data/com.hackwestern.edith/files/edith.log
& $adb pull /sdcard/Android/data/com.hackwestern.edith/files/edith.log.old
```

For a live view, filter logcat by the log prefix of a feature:

```powershell
& $adb logcat -s Unity | Select-String "EDITH pinch|EDITH grab"
```

The prefixes are `EDITH pinch`, `EDITH grab`, `EDITH look`, `EDITH ask`, `EDITH scan`, `EDITH pair`, `EDITH camera`, `EDITH backend` and `EDITH gesture`.

### Tune the fist grab from the log

Each fist logs its curl when it closes and when it opens, and the open line adds the deepest curl of that fist:

```
[EDITH grab] closed curl 1.08
[EDITH grab] opened curl 1.52, deepest 0.97
```

The curl is the mean fingertip-to-wrist over knuckle-to-wrist distance of the index, middle and ring fingers. It reads about 1.9 for an open hand and under 1.1 for a fist. Collect several lines from real hands, then adjust `closedCurl` and `openCurl` in `Assets/Edith/Runtime/Grab/GrabTuning.cs`. A fist that never reaches `closedCurl` shows a deepest curl above it. A hand that opens without crossing `openCurl` never logs an open line.

## Diagnostics build

A diagnostics build puts a status panel in the headset. The panel is a small world-space card that follows your head at the lower left of your view. It shows:

- the pinch phase and the last rejection, with how long ago it happened;
- the grab phase, the card held and the last grab event;
- counts of Looks (releases that hung a snapshot), taps, and snapshots now hung;
- the camera status;
- the pairing and phone location status;
- the last Scan result, as a pin count or the reason there were none;
- the backend host, and whether its `/health` answers, checked every 45 seconds;
- the last error, assert or exception line the log saw.

Hold your left hand in a closed fist for one second to hide or show the panel. Open the hand before the next hold. The left hand is read only by Scan, which needs it open, and the pinch, grab and thumb gestures read the right hand, so the toggle collides with none of them.

Build it with **EDITH** > **Build Diagnostics APK**, or `BatchBuildDiagnostics` as in [Build from a terminal](#build-from-a-terminal). The panel's code compiles only with the `EDITH_DIAGNOSTICS` define. In every other build, including `Builds/edith.apk`, the `Diagnostics` object in the scene destroys itself on start and no panel code is present. The text layout lives in `Assets/Edith/Runtime/Diagnostics/StatusText.cs` and is covered by EditMode tests.

## Record hands

The recording build writes every right-hand frame to a file, one JSON line per frame, in the format the replay tests read. Build it with **EDITH** > **Build Recording APK**, install it, use the app, and pull the files:

```powershell
.\Tools\pull-hand-recordings.ps1
```

The script copies `files/hands/*.jsonl` from the headset into `Recordings/`, which git ignores because hand data is personal. It deletes each headset copy only after it checks the copied size. While the app runs, it copies the files and keeps them on the headset. It finds adb in the Unity install, then in `ANDROID_HOME`, then on `PATH`. To keep a recording as a test fixture, gzip it into `Assets/Edith/Tests/Editor/Fixtures`.

## Check each feature on the headset

Install the build, put the headset on, and allow the permission prompts (microphone, headset camera, spatial data).

**Look.** Pinch your right thumb and index finger with the palm turned away from you, drag at least 5 cm to draw a box around an object, and let go. A snapshot with the photo hangs where you drew it, and the card text fills in once the backend answers. The log shows `[EDITH look] snapshot <id> hung with a <w>x<h> photo`. A pinch that lets go before 5 cm is a tap, and a tap in line with a snapshot clears it. A pinch with the palm toward your face is ignored and logs `reject PalmFacingUser`. In a diagnostics build, the panel shows each rejection (`TooSmall`, `TooNarrow`, `TooClose`, `Lost`).

**Collect.** Open your right hand, then close it into a fist on a snapshot, so your knuckles sit beside its photo. The log shows `[EDITH grab] Grabbed card <id>`. Pull the fist toward your chest. `Pocketed card <id>` means the memory was collected. Opening the hand early logs `Dropped`, and the card floats back.

**Ask.** Tap the side of your right index finger with your thumb. EDITH chimes and listens, then speaks the answer and shows it on a card. A thumb swipe left or right sends the current answer away. The log shows `[EDITH ask] <phase> -> <phase>` lines, and a failure names its problem (`NotConfigured`, `MicDenied`, `Unreachable`, `Refused`, `Dropped`, `TimedOut`, `NotHeard`).

**Scan.** Hold both hands up, open, with the palms facing away, for half a second. A ring between your palms fills and a chime plays. A pin with a short hook lands on each object the backend picked, and a higher chime means pins landed. A low tone and a line of text mean none did. A near miss logs `[EDITH scan] near: left ..., right ...` at most once a second, with the numbers to tune `ScanTuning` from.

**Pairing.** On a fresh install with a backend, a panel with a four-digit code and a short address floats in front of you, and nothing else responds: Look, Collect, Ask, Scan and the diagnostics panel stay off until the phone is paired. Open the address on the phone that runs the hotspot, type the code, and tap **Share location**. Once the backend confirms the phone, every feature turns on, and the panel shows **Phone connected** and fades. The log shows `[EDITH pair]` lines. Unpaired, the panel never times out: an expired code is replaced and backend errors are retried every 10 s. The headset remembers the pairing, so later launches start unlocked. It checks every 30 s that the backend still keeps the phone's position; after three misses in a row it locks again and shows a new code. A backend it cannot reach never locks it. In the Unity editor the gate is always open.

## Where things live

| Path | What it holds |
| --- | --- |
| `Assets/Edith/Runtime/` | The app code, one folder per feature, in the `Edith` assembly |
| `Assets/Edith/Editor/` | The setup and build scripts above |
| `Assets/Edith/Tests/Editor/` | EditMode tests and the recordings they replay |
| `Assets/Edith/Scenes/Main.unity` | The one scene in the build |
| `Tools/pull-hand-recordings.ps1` | Copies hand recordings off the headset |
