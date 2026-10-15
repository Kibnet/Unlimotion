"""Generate an isolated Android probe overlay and collect actual native/UI evidence.

The ordinary product APK never includes this source/overlay. A completed collection
is not a visual PASS: inspect the saved startup frames/video and window dumps.
"""
import argparse
import datetime as dt
import hashlib
import json
import re
import subprocess as sp
import time
from pathlib import Path
from xml.sax.saxutils import escape

PACKAGE = "org.unlimotion.nativegitsmoke"
FILES = f"/storage/emulated/0/Android/data/{PACKAGE}/files"
NEGATIVE = f"{FILES}/native-git-smoke-negative"
RESULT = f"{FILES}/native-git-smoke.json"
HERE = Path(__file__).resolve().parent


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def fresh(path):
    path.mkdir(parents=True, exist_ok=False)


def prepare(args):
    source = args.source.resolve()
    original = source / "src/Unlimotion.Android/MainActivity.cs"
    text = original.read_text(encoding="utf-8-sig")
    call = "            MainActivity.ConfigureCoreAppServices(this);"
    directory = "var nativeLibraryDir = context.ApplicationInfo?.NativeLibraryDir;"
    assert text.count(call) == text.count(directory) == 1, "Overlay anchors must be unique"
    overlay = text.replace(call, call + "\n            NativeGitSmokeProbe.Run(this);")
    overlay = overlay.replace(directory, "var nativeLibraryDir = NativeGitSmokeProbe.GetNativeDirectory(context, context.ApplicationInfo?.NativeLibraryDir);")
    output = args.output.resolve()
    fresh(output)
    (output / "MainActivity.cs").write_text(overlay, encoding="utf-8")
    probe = HERE / "NativeGitSmokeProbe.cs"
    (output / probe.name).write_bytes(probe.read_bytes())
    updater = source / "src/Unlimotion.Android/Properties/AndroidManifest.Updater.xml"
    manifest = updater.read_text(encoding="utf-8-sig")
    assert manifest.count("com.Kibnet.Unlimotion.fileprovider") == 1
    (output / "Updater.xml").write_text(manifest.replace("com.Kibnet.Unlimotion.fileprovider", PACKAGE + ".fileprovider"), encoding="utf-8")
    p = lambda name: escape(str(output / name), {'"': "&quot;"})
    targets = f'''<Project>
  <Target Name="IsolatedNativeGitProbe" BeforeTargets="CoreCompile" Condition="'$(MSBuildProjectName)' == 'Unlimotion.Android'">
    <Error Condition="'$(ApplicationId)' != '{PACKAGE}'" Text="Native Git smoke requires its isolated ApplicationId." />
    <ItemGroup>
      <Compile Remove="MainActivity.cs;$(MSBuildProjectDirectory)/MainActivity.cs" />
      <Compile Include="{p('MainActivity.cs')}" />
      <Compile Include="{p(probe.name)}" />
    </ItemGroup>
  </Target>
  <Target Name="IsolatedNativeGitProvider" BeforeTargets="_ManifestMerger" Condition="'$(MSBuildProjectName)' == 'Unlimotion.Android'">
    <Error Condition="'$(ApplicationId)' != '{PACKAGE}'" Text="Native Git smoke requires its isolated ApplicationId." />
    <ItemGroup>
      <AndroidManifestOverlay Remove="Properties\\AndroidManifest.Updater.xml" />
      <AndroidManifestOverlay Include="{p('Updater.xml')}" />
    </ItemGroup>
  </Target>
</Project>'''
    (output / "probe.targets").write_text(targets, encoding="utf-8")
    metadata = dict(schema=1, package=PACKAGE, original=str(original), originalSha256=sha(original),
                    substitutions=2, productSourceUnchanged=sha(original),
                    files={f.name: sha(f) for f in output.iterdir()}, visualAcceptance=False)
    (output / "source-manifest.json").write_text(json.dumps(metadata, indent=2), encoding="utf-8")
    print(json.dumps(metadata))


def validate_report(report, pid, negative):
    assert report["schema"] == 1 and report["package"] == PACKAGE
    assert report["pid"] == int(pid) and report["abi"] == "x86_64"
    assert re.fullmatch(r"[0-9a-f]{32}", report["runId"])
    assert report["negative"] is negative
    if negative:
        assert report["success"] is False and "NativeMethods" in report.get("error", "")
        assert report["operations"] == []
    else:
        assert report["success"] is True and "error" not in report
        assert report["handlesBefore"] == report["handlesAfter"] and "0x0" not in report["handlesBefore"]
        assert len(report["operations"]) == 2
        for operation in report["operations"]:
            assert re.fullmatch(r"[0-9a-f]{40}", operation["commit"])
            assert all(operation[key] is True for key in ("init", "staged", "committed", "clean", "reopenedExactBlob"))
            assert operation["repository"].startswith(f"/data/user/0/{PACKAGE}/files/native-git-smoke/")


def validate_avd_name(value, expected):
    assert re.fullmatch(re.escape(expected) + r"\s+OK\s*", value), "Unexpected emulator identity"


def collect(args):
    output = args.output.resolve()
    fresh(output)
    metadata = dict(schema=1, package=PACKAGE, serial=args.serial, apk=str(args.apk.resolve()),
                    apkSha256=sha(args.apk), startedUtc=dt.datetime.now(dt.timezone.utc).isoformat(),
                    collectorSha256=sha(Path(__file__)), probeSourceSha256=sha(HERE / "NativeGitSmokeProbe.cs"),
                    collectionComplete=False, visualReview="pending", parentAcceptance=False, trials=[])
    no_window = getattr(sp, "CREATE_NO_WINDOW", 0)

    def adb(*arguments, timeout=25, check=True):
        result = sp.run([str(args.adb), "-s", args.serial, *arguments], capture_output=True, timeout=timeout, creationflags=no_window)
        if check and result.returncode:
            raise RuntimeError(f"ADB {arguments!r}: {result.returncode}: {result.stderr.decode(errors='replace')[:500]}")
        return result.stdout

    def save():
        (output / "collection.json").write_text(json.dumps(metadata, indent=2), encoding="utf-8")

    # Verify before install: never replace the original application by mistake.
    save()
    badging = sp.check_output([str(args.aapt), "dump", "badging", str(args.apk)], timeout=30, creationflags=no_window).decode()
    manifest = sp.check_output([str(args.aapt), "dump", "xmltree", str(args.apk), "AndroidManifest.xml"], timeout=30, creationflags=no_window).decode()
    (output / "badging.txt").write_text(badging, encoding="utf-8")
    (output / "manifest.txt").write_text(manifest, encoding="utf-8")
    assert re.search(r"package: name='" + re.escape(PACKAGE) + r"'", badging)
    assert "'arm64-v8a'" in badging and "'x86_64'" in badging
    assert PACKAGE + ".fileprovider" in manifest
    assert not re.search(r"debuggable.*(?:0xffffffff|0x1)\b", manifest)
    assert re.search(r"extractNativeLibs.*(?:0xffffffff|0x1)\b", manifest)
    assert args.serial.startswith("emulator-")
    validate_avd_name(adb("emu", "avd", "name").decode(), args.avd)
    assert adb("shell", "getprop", "ro.build.version.sdk").strip() == b"34"
    assert adb("shell", "getprop", "ro.product.cpu.abi").strip() == b"x86_64"
    save()
    adb("install", "-r", str(args.apk.resolve()), timeout=120)
    activity = adb("shell", "cmd", "package", "resolve-activity", "--brief", PACKAGE).decode().strip().splitlines()[-1]
    assert activity.startswith(PACKAGE + "/")
    pids, run_ids = set(), set()
    try:
        for label, negative in (("positive1", False), ("positive2", False), ("negative", True)):
            trial_dir = output / label
            trial_dir.mkdir()
            trial = dict(label=label, negative=negative, complete=False)
            metadata["trials"].append(trial)
            adb("shell", "am", "force-stop", PACKAGE)
            adb("shell", "mkdir", "-p", FILES)
            adb("shell", "rm", "-f", RESULT, RESULT + ".tmp", NEGATIVE)
            if negative:
                adb("shell", "touch", NEGATIVE)
            recorder = sp.Popen([str(args.adb), "-s", args.serial, "shell", "screenrecord", "--time-limit", "25", f"{FILES}/native-git-smoke-{label}.mp4"], stdout=sp.DEVNULL, stderr=sp.PIPE, creationflags=no_window)
            recorder_exit_proven = False
            try:
                time.sleep(.5)
                adb("shell", "am", "start", "-n", activity)
                deadline = time.monotonic() + 60
                pid = ""
                while time.monotonic() < deadline:
                    pid = adb("shell", "pidof", PACKAGE, check=False).decode().strip()
                    if pid:
                        break
                    time.sleep(.2)
                assert re.fullmatch(r"\d+", pid) and pid not in pids
                pids.add(pid)
                trial["pid"] = int(pid)
                # Capture immediately; waiting for Git JSON first could miss the
                # transient notification that motivated this regression test.
                for index in range(9):
                    (trial_dir / f"window-{index:02}.txt").write_bytes(adb("shell", "dumpsys", "window", "windows"))
                    (trial_dir / f"frame-{index:02}.png").write_bytes(adb("exec-out", "screencap", "-p"))
                    assert adb("shell", "pidof", PACKAGE).decode().strip() == pid
                    time.sleep(1)
                while time.monotonic() < deadline:
                    raw = adb("shell", "cat", RESULT, check=False)
                    if raw.strip().startswith(b"{"):
                        report = json.loads(raw)
                        break
                    time.sleep(.3)
                else:
                    raise TimeoutError("No complete native smoke report")
                (trial_dir / "report.json").write_bytes(raw)
                validate_report(report, pid, negative)
                assert report["runId"] not in run_ids
                run_ids.add(report["runId"])
                recorder.communicate(timeout=35)
                assert recorder.returncode == 0, "Native recording failed"
                recorder_exit_proven = True
                adb("pull", f"{FILES}/native-git-smoke-{label}.mp4", str(trial_dir / "native.mp4"))
                logs = adb("logcat", "-d", "--pid=" + pid, "-v", "threadtime").decode(errors="replace")
                (trial_dir / "logcat.txt").write_text(logs, encoding="utf-8")
                if negative:
                    assert "Could not load packaged Git dependencies" in logs
                else:
                    assert not re.search(r"Could not load packaged Git dependencies|TypeInitializationException.*NativeMethods|dlopen failed.*lib(?:ssl|crypto)\.so\.3", logs)
                trial.update(complete=True, reportSha256=sha(trial_dir / "report.json"), videoSha256=sha(trial_dir / "native.mp4"))
            finally:
                if not recorder_exit_proven:
                    metadata["recorderCleanupUncertain"] = True
                if recorder.poll() is None:
                    recorder.kill()
                    recorder.communicate(timeout=10)
                    raise RuntimeError("Recording interrupted; remote recording cleanup uncertain")
            save()
        metadata["collectionComplete"] = True
    except BaseException as error:
        metadata["error"] = repr(error)
        raise
    finally:
        cleanup_errors = cleanup_owned(adb)
        if metadata.get("recorderCleanupUncertain"):
            cleanup_errors.append(dict(step="remote-recorder-exit", error="Remote recording exit not proven"))
        metadata["cleanupUncertain"] = bool(cleanup_errors)
        metadata["cleanupErrors"] = cleanup_errors
        if cleanup_errors:
            metadata["collectionComplete"] = False
        save()
        if cleanup_errors:
            raise RuntimeError("Own app cleanup uncertain: " + repr(cleanup_errors))
    print(json.dumps(metadata))


def verify_stopped(adb):
    # A transport failure with empty stdout is not proof of process exit. Require
    # a successful live shell and its explicit sentinel, not pidof's empty output.
    script = f"if pidof {PACKAGE} >/dev/null; then echo OWN_APP_RUNNING; else echo OWN_APP_STOPPED; fi"
    assert adb("shell", script).strip() == b"OWN_APP_STOPPED", "Own app exit not proven"


def cleanup_owned(adb):
    errors = []
    for label, action in (
        ("force-stop", lambda: adb("shell", "am", "force-stop", PACKAGE)),
        ("remove-negative-sentinel", lambda: adb("shell", "rm", "-f", NEGATIVE)),
        ("verify-app-exit", lambda: verify_stopped(adb)),
    ):
        try:
            action()
        except BaseException as error:
            errors.append(dict(step=label, error=repr(error)))
    return errors


def run(args):
    # Preserve preflight failures too, without ever overwriting an older run.
    existed = args.output.exists()
    try:
        collect(args)
    except BaseException as error:
        if not existed and args.output.is_dir():
            path = args.output / "collection.json"
            metadata = json.loads(path.read_text(encoding="utf-8")) if path.exists() else dict(package=PACKAGE)
            metadata.update(collectionComplete=False, collectorError=repr(error), visualReview="pending")
            path.write_text(json.dumps(metadata, indent=2), encoding="utf-8")
        raise


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    prep = sub.add_parser("prepare")
    prep.add_argument("--source", type=Path, required=True)
    prep.add_argument("--output", type=Path, required=True)
    collect = sub.add_parser("run")
    collect.add_argument("--adb", type=Path, required=True)
    collect.add_argument("--aapt", type=Path, required=True)
    collect.add_argument("--serial", required=True)
    collect.add_argument("--avd", default="codex_loading_profile_20261013")
    collect.add_argument("--apk", type=Path, required=True)
    collect.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    (prepare if args.command == "prepare" else run)(args)


if __name__ == "__main__":
    main()
