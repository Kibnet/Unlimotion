# Android build notes (Termux)

This repo is built on-device (Termux). Use these steps to avoid broken APKs and runtime crashes.

## Prereqs
- Android SDK installed at `/data/data/com.termux/files/home/android-sdk`
- Working Android binutils (Termux) at `/data/data/com.termux/files/home/android-binutils`
- JDK 17 in Termux: `/data/data/com.termux/files/usr/lib/jvm/java-17-openjdk`
- `aapt2` and `zipalign` in `/data/data/com.termux/files/usr/bin`

## Build (clean, safe)
```bash
rm -rf /storage/emulated/0/unlimotion/src/Unlimotion.Android/bin \
       /storage/emulated/0/unlimotion/src/Unlimotion.Android/obj

dotnet build /storage/emulated/0/unlimotion/src/Unlimotion.Android/Unlimotion.Android.csproj \
  -c Debug -t:Package \
  -p:AndroidBinUtilsDirectory=/data/data/com.termux/files/home/android-binutils
```

### Why the binutils flag matters
The default toolchain in `Microsoft.Android.Sdk.Linux` uses `llc` built for x86_64 and will crash on Android ("Exec format error").
The `AndroidBinUtilsDirectory` override forces Termux-compatible tools.

## Install
Use the fresh APK from:
```
/storage/emulated/0/unlimotion/src/Unlimotion.Android/bin/Debug/net8.0-android/com.Kibnet.Unlimotion-Signed.apk
```
Then install via:
```bash
termux-open /storage/emulated/0/unlimotion/src/Unlimotion.Android/bin/Debug/net8.0-android/com.Kibnet.Unlimotion-Signed.apk
```

## Wireless debugging (ADB)
Enable in Android:
- Developer options -> Wireless debugging
- Pair device with code

Then in Termux:
```bash
adb pair IP:PORT CODE
adb connect IP:PORT
adb devices
```

## Crash logs
- App startup crashes are written to Downloads as `unlimotion-startup-YYYYMMDD-HHmmss.txt`.
- If nothing appears, use ADB logcat:
```bash
adb logcat -v time | grep -i unlimotion
```
