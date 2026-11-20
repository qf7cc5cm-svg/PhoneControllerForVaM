# Phone camera navigation — first installable patch

Based on upstream v0.4 commit `474549cf229113bcd6d6dd7c3b4e13803bbab43e`.

## Install

1. Download the **PhoneControllerNavigation-installers** artifact from the
   [navigation build](https://github.com/qf7cc5cm-svg/PhoneControllerForVaM/actions/workflows/navigation-build.yml).
2. Copy `EugeneHere.PhoneControllerNavigation.1.var` to VaM's `AddonPackages` directory.
3. Remove the previously loaded PhoneController plugin from the active VaM scene/session
   to prevent the old instance from competing for UDP port 62701.
4. Add **PhoneController.cslist** from the navigation package to a Scene or Session plugin,
   or attach it to an atom.
5. Install `PhoneControllerNavigation-debug.apk` on the Android phone, replacing the
   earlier version. Both sides must use the patched build.
6. Put both devices on the same trusted LAN. Allow UDP 62701 only from the phone's IP.

## Use

- Open the phone Camera page. It still starts in regular ARCore Track mode.
- Tap **Orbit** in the top row to enter navigation mode; its label becomes **Track**.
- One finger on the picture: orbit around the selected atom.
- Two-finger drag: pan the view (move the orbit pivot in the camera plane).
- Pinch: dolly closer/farther; camera FOV is changed separately via the FOV slider.
- **Follow** keeps the orbit pivot attached to the selected target's main controller.
  Tap **Fixed** to stop following the target.
- **Frame** fits the selected atom again and clears navigation pan.
- Tap **Track** to leave navigation mode and resume normal ARCore movement.

The app continues using the existing JPEG video stream; no separate server is needed.
Grab mode is unavailable while orbiting. Change back to Track for physical grabbing.

## Wire protocol

`PCN1`: 52 bytes, little-endian, sent from Android to VaM once every 33 ms
while a camera session is active.

| Offset | Type | Value |
|---|---|---|
| 0 | ASCII[4] | PCN1 |
| 4 | uint32 | Camera session |
| 8 | int32 | Plugin instance |
| 12 | int32 | Target revision |
| 16 | uint32 | Increasing navigation sequence (per camera session) |
| 20 | int32 | Frame counter |
| 24 | int32 | Flags: bit 0 Navigate, bit 1 Follow |
| 28 | float32 | Cumulative orbit yaw (degrees) |
| 32 | float32 | Cumulative orbit pitch (degrees) |
| 36 | float32 | Cumulative normalized horizontal pan |
| 40 | float32 | Cumulative normalized vertical pan |
| 44 | float32 | Cumulative log-distance dolly |
| 48 | float32 | Desired vertical FOV (degrees, 10–110) |

The VaM worker only accepts PCN1 from the active camera peer with the correct
session, plugin instance, target revision and increasing sequence. Input is
further bounded and checked for finite float values. These checks **do not**
provide cryptographic authentication: the original trusted-LAN security limitation
still applies. Use firewall restrictions and do not port-forward 62701.

## Architecture

- `CameraNavigation.cs` owns target-centered orbit state and the main-thread
  camera transform.
- `PhoneCamera.cs` routes PCN1 and switches between navigation and physical
  ARCore pose application, without changing JPEG packets or target selection.
- `CameraNavigationControl.java` maintains touch gestures and a cumulative
  state snapshot; `CameraActivity.java` sends it over the existing UDP socket.

## Limitations / verification

This is a first patch, not yet validated in an actual VaM or phone session.
The GitHub Actions workflow compiles the Android APK and runs its Java unit tests,
then packages the VaM source files into a .var. The C# plugin is compiled
dynamically by VaM at load time, so the build artifact alone does not establish
that the script is accepted in every VaM version. Test startup, framing,
mode switches, follow moving targets, camera grab after Track mode resumes,
and UDP loss on a test scene before relying on it in normal use.

No new authentication system, camera roll, collision avoidance, or smoothing
has been introduced in this patch.
