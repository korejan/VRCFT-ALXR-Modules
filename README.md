# VRCFT-ALXR-Modules

This repository contains the source and binaries for two OpenXR based [VRCFT](https://docs.vrcft.io/) module plugins, ALXR [Local](#alxr-local-module) and [Remote](#alxr-remote-module) modules.

### Table of Contents
- [VRCFT-ALXR-Modules](#vrcft-alxr-modules)
    - [Table of Contents](#table-of-contents)
  - [Supported Extensions/Devices](#supported-extensionsdevices)
  - [ALXR Local Module](#alxr-local-module)
    - [Local Module Basic Setup](#local-module-basic-setup)
  - [ALXR Remote Module](#alxr-remote-module)
    - [Remote Module Basic Setup](#remote-module-basic-setup)
  - [Module Settings](#module-settings)
    - [Common Settings](#common-settings)
      - [Eye Tracking Config](#eye-tracking-config)
      - [Tracking Sensitivity Config](#tracking-sensitivity-config)
    - [Local Module Settings](#local-module-settings)

## Supported Extensions/Devices
Both the ALXR local and remote modules currently support the following OpenXR extensions:

| Extension Name | Supported Devices |
|----------------|-------------------|
| [XR_EXT_eye_gaze_interaction](https://registry.khronos.org/OpenXR/specs/1.0/html/xrspec.html#XR_EXT_eye_gaze_interaction) | [VDXR](https://github.com/mbucchia/VirtualDesktop-OpenXR), Pico 4 Pro/Enterprise, Pico Neo 3 Pro Eye, *Vive Pro Eye, Focus 3 / XR Elite add-ons, Magic Leap 2, WMR / Hololens 2, Varjo, Quest Pro (standalone runtime only), and more |
| [XR_FB_eye_tracking_social](https://registry.khronos.org/OpenXR/specs/1.0/html/xrspec.html#XR_FB_eye_tracking_social) | Quest Pro standalone & Link runtimes, [VDXR](https://github.com/mbucchia/VirtualDesktop-OpenXR) |
| [XR_HTC_facial_tracking](https://registry.khronos.org/OpenXR/specs/1.0/html/xrspec.html#XR_HTC_facial_tracking) | Vive Facial Tracker *, Focus 3 / XR Elite add-ons |
| XR_FB_face_tracking2 | Quest Pro standalone & Link runtimes |
| [XR_FB_face_tracking](https://registry.khronos.org/OpenXR/specs/1.0/html/xrspec.html#XR_FB_face_tracking) | Quest Pro standalone & Link runtimes, [VDXR](https://github.com/mbucchia/VirtualDesktop-OpenXR) |
| [XR_ANDROID_avatar_eyes](https://developer.android.com/develop/xr/openxr/extensions/XR_ANDROID_avatar_eyes) | Android XR (Project Moohan?) **
| [XR_ANDROID_face_tracking](https://developer.android.com/develop/xr/openxr/extensions/XR_ANDROID_face_tracking) | Android XR ** |

A full list of supported runtimes/devices can be found [here](https://github.khronos.org/OpenXR-Inventory/extension_support.html#matrix).
     
* Vive Pro Eye / Facial Tracker requires "Vive console for SteamVR" to be installed for OpenXR support.

** Android XR supporting devices not yet known.

## ALXR Local Module

This module is exclusively for PC OpenXR runtimes on Windows/Linux such as Oculus's PC runtime for (Air)Link. It does not do any kind of VR streaming itself, runs completely standalone and does not require any additional software.

### Local Module Basic Setup

1. Set the relevant active OpenXR runtime.
    * Runtimes such as Oculus-Link require enabling additional settings and/or require a dev account associated with the device before facial/eye tracking can be used.
2. Install the "ALXR Local Module" from VRCFT Module Registry Page. <b><i>For manual installation only do the following:</i></b>
   1. Download via the release page (`ALXRLocalModule.zip`)
   2. Delete any previous versions
   3. Extract the archive to:
      * Windows - `C:\Users\your-user-name\AppData\Roaming\VRCFaceTracking\CustomLibs`
      * Linux - `/home/your-user-name/.config/VRCFaceTracking/CustomLibs`
   5. Delete `module.json` from the extracted location.
3. Run VRCFT

## ALXR Remote Module

This module is used to receive facial/eye tracking data from ALXR clients over a network socket connection.

### Remote Module Basic Setup

1. Download and install the relevant ALXR client and server from the [ALXR-nightly](https://github.com/korejan/ALXR-nightly/releases) repository.
   * The VRCFT v4 module method of obtaining the client from the ALXR-experimental repository is no longer supported, all facial/eye support has been merged into the main branch.
2. Install the "ALXR Remote Module" from VRCFT Module Registry Page. <b><i>For manual installation only do the following:</i></b>
   1. Download via the release page (`ALXRRemoteModule.zip`)
   2. Delete any previous versions
   3. Extract the archive to:
      * Windows - `C:\Users\your-user-name\AppData\Roaming\VRCFaceTracking\CustomLibs`
      * Linux - `/home/your-user-name/.config/VRCFaceTracking/CustomLibs`
   4. Delete `module.json` from the extracted location.
4. In `ALXRModuleConfig.json` (desktop shortcut is generated on first run), in the `"RemoteConfig"` section set `"ClientIpAddress"` to the headset IP, this can be found in the ALVR server dashboard.
   * If the client is being run on the same host as the server (e.g. alxr windows client), use localhost IP (default) and set the server to TCP protocol.
5. Run VRCFT.

## Module Settings

Both modules come with a configuration file named `ALXRModuleConfig.json` (if it does not exist one will be generated). The first time VRCFT is run with either module loaded a desktop shortcut to this file will be generated for convenience.

### Common Settings
The following settings are applicable to both the local and remote modules

#### Eye Tracking Config
```
"EyeTrackingConfig": {
  "FBEyeOpennessMode": "LinearLidTightening",
  "UseEyeExpressionForGazePose": false,
  "EyeTrackingFilterParams": {
    "Enable": false,
    "Rot1EuroFilterParams": {
      "MinCutoff": 1,
      "Beta": 0.5,
      "DCutoff": 1
    },
    "Pos1EuroFilterParams": {
      "MinCutoff": 1,
      "Beta": 0.5,
      "DCutoff": 1
    }
  }
}
```

`"FBEyeOpennessMode"` - Sets the eye openness behaviour for `XR_FB_face_tracking`, the following options are:
* `"LinearLidTightening"` - Default, adjusts eye openness in a simple, linear manner considering the effect of lid tightening. This is the same as Tofu's v5 module
* `"NonLinearLidTightening"` - Adjusts eye openness using a non-linear function of the lid tightening effect. This is from a pre-v5 unified expression based module.
* `"SmoothTransition"` - Provides a smooth transition between eye-closed and eye-open states for a natural-looking effect.
* `"MultiExpression"` - Adjusts eye openness by considering multiple facial expressions, including looking down, lid tightening, and upper lid raising. This is the same as Adjerry's v4 module.

`"UseEyeExpressionForGazePose"` - When enabled uses the eye expression weights of the [`XR_FB_face_tracking`](https://registry.khronos.org/OpenXR/specs/1.0/html/xrspec.html#XR_FB_face_tracking) extension for eye tracking instead of eye gaze pose(s) of [`XR_FB_eye_tracking_social`](https://registry.khronos.org/OpenXR/specs/1.0/html/xrspec.html#XR_FB_eye_tracking_social) or [`XR_EXT_eye_gaze_interaction`](https://registry.khronos.org/OpenXR/specs/1.0/html/xrspec.html#XR_EXT_eye_gaze_interaction). Disabled by default.

`"EyeTrackingFilterParams"` - Some runtimes may output jittery eye-tracking data, when this option is enabled applies smoothing to eye-tracking data using the [1€ Filter](https://gery.casiez.net/1euro/) to both eye position(s) and rotation(s). Disabled by default.

#### Tracking Sensitivity Config
```
"TrackingSensitivityConfig": {
  "Enable": false,
  "ProfileFilename": "AdjerryV4DefaultMultipliers.json"
}
```
`"TrackingSensitivityConfig"` - When enabled applies scaling multipliers to expressions weights of [`XR_FB_face_tracking`](https://registry.khronos.org/OpenXR/specs/1.0/html/xrspec.html#XR_FB_face_tracking) extension only. Same functionality as Adjerry's v4 module. Please refer to the `AdjerryV4DefaultMultipliers.json` template file which comes with the modules (or can be found [here](https://github.com/korejan/VRCFT-ALXR-Modules/blob/main/AdjerryV4DefaultMultipliers.json)) to make your own profiles.

### Local Module Settings

The following entries in `ALXRModuleConfig.json` are specifically for configuring the local module:

```
"LocalConfig": {
  "VerboseLogs": false,
  "HeadlessSession": true,
  "SimulateHeadless": true,
  "GraphicsApi": "Auto",
  "EyeTrackingExt": "Auto",
  "FacialTrackingExt": "Auto",
  "FaceTrackingDataSources": [
      "VisualSource"
  ]
}
```

`VerboseLogs` - When enabled more logging information is printed, use for debugging issues.

`HeadlessSession` - Enables the [`XR_MND_headless`](https://registry.khronos.org/OpenXR/specs/1.0/html/xrspec.html#XR_MND_headless) extension on supported runtimes. If enabled, graphics subsystems/resources will not be created. This is preferred for dealing with purely tracking data.

`SimulateHeadless` - Same as `HeadlessSession` in that the option will first attempt to enable the headless extension but if not supported the runtime will be treated as if such an extension is supported.

<strong>Warning:</strong> this option is not standard conforming OpenXR, there is no guarantee this setting will work with any runtime. Only Oculus link's runtime is known to be able to work this way. It does not support the `XR_MND_headless` extension.

`GraphicsApi` - If `HeadlessSession` / `SimulateHeadless` is not enabled & active a graphics subsystem must be created. The following options are:
* `"Vulkan2"`
* `"Vulkan"`
* `"D3D12"`
* `"D3D11"`
* `"Auto"` - default, auto selects an API in the above order.

`EyeTrackingExt` - Sets the eye-tracking OpenXR extensions or can be used to disable. The following options are:
* `"None"` - No eye-tracking extensions enabled
* [`"FBEyeTrackingSocial"`](https://registry.khronos.org/OpenXR/specs/1.0/html/xrspec.html#XR_FB_eye_tracking_social) - Typically the Quest Pro
* [`"ExtEyeGazeInteraction"`](https://registry.khronos.org/OpenXR/specs/1.0/html/xrspec.html#XR_EXT_eye_gaze_interaction) - Multi-vendor eye tracking extension, supported by a variety of runtimes.
* `"Auto"` - default, auto selects any available in the above order.

`FacialTrackingExt` - Sets the face tracking OpenXR extensions or can be used disable. The following options are:
* `"None"` - No face-tracking extensions enabled
* `"FB_V2"` - Typically Quest Pro
* [`"FB"`](https://registry.khronos.org/OpenXR/specs/1.0/html/xrspec.html#XR_FB_face_tracking) - Typically Quest Pro
* [`"HTC"`](https://registry.khronos.org/OpenXR/specs/1.0/html/xrspec.html#XR_HTC_facial_tracking) - Typically vive facial tracker (requires "Vive console for SteamVR") or Focus 3 facial tracker add-on
* `"Auto"` - default, auto selects any available in the above order.

`FaceTrackingDataSources` - Sets one or more data sources for face tracking. The following options are:
* `"VisualSource"` - default, enable visual data source for face tracking.
* `"AudioSource"` -  enable audio data source for face tracking ([AudioToExpression](https://developers.meta.com/horizon/blog/audio-to-expression-mixed-reality-blendshapes-movement-sdk-avatars)).
