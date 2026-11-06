"""
Build the release package for this plugin and publish it as a GitHub release with the GitHub CLI (gh).
(Per-plugin copy of ValheimModInstaller/release/make_release.py.)

    python release/make_release.py              # build + publish if this version isn't released yet
    python release/make_release.py --build-only # just build the zip into release/out/

Assets (names are stable, so .../releases/latest/download/<asset> always works):
    <Id>.zip  (BepInEx/plugins/<Id>/<Id>.dll + README.md)
    <Id>.dll

A release whose tag already exists is skipped, so bump the version (plugin.json + Plugin.cs) to publish again.
"""

import argparse
import json
import re
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent           # this plugin's repo
OUT = ROOT / "release" / "out"
INSTALLER_REPO = "Glutoblop/ValheimPlugins"


def find_gh():
    gh = shutil.which("gh")
    if gh:
        return gh
    for p in (Path(r"C:\Program Files\GitHub CLI\gh.exe"), Path(r"C:\Program Files (x86)\GitHub CLI\gh.exe")):
        if p.exists():
            return str(p)
    return None


def add_file(z, src, arcname):
    info = zipfile.ZipInfo(arcname, date_time=(2020, 1, 1, 0, 0, 0))
    info.compress_type = zipfile.ZIP_DEFLATED
    info.external_attr = (0o644 | 0o100000) << 16
    z.writestr(info, Path(src).read_bytes())


def build_plugin(pdir):
    m = json.loads((pdir / "plugin.json").read_text(encoding="utf-8"))
    pid, version = m["id"], m["version"]
    dll = pdir / "dist" / (pid + ".dll")
    if not dll.exists():
        raise SystemExit("{}: {} missing - run dotnet build -c Release first".format(pid, dll))
    src = pdir / "src" / "Plugin.cs"
    if src.exists():
        m2 = re.search(r'Version\s*=\s*"([^"]+)"', src.read_text(encoding="utf-8"))
        if m2 and m2.group(1) != version:
            raise SystemExit("{}: plugin.json says {} but Plugin.cs says {} - make them match".format(pid, version, m2.group(1)))
    out_dll = OUT / (pid + ".dll")
    shutil.copy2(dll, out_dll)
    zpath = OUT / (pid + ".zip")
    with zipfile.ZipFile(zpath, "w") as z:
        add_file(z, dll, "BepInEx/plugins/{}/{}.dll".format(pid, pid))
        if (pdir / "README.md").exists():
            add_file(z, pdir / "README.md", "BepInEx/plugins/{}/README.md".format(pid))
    vh = m.get("valheim", {})
    changes = m.get("changelog", {}).get(version, "")
    notes = (
        "## {name} {v}\n\n{desc}\n\n"
        "{changes}"
        "**Tested on Valheim {vh}** (network version {nv}, Steam build {sb}) on {lt}. "
        "Requires BepInExPack_Valheim {bx}+. Client-side only.\n\n"
        "### Install\n"
        "- **Easiest:** use the [Valheim Mod Installer](https://github.com/{irepo}/releases/latest/download/ValheimModInstaller.zip).\n"
        "- **Manual:** unzip `{id}.zip` into your Valheim folder (or drop `{id}.dll` into `BepInEx/plugins/{id}/`)."
    ).format(name=m["name"], v=version, desc=m.get("description", ""),
             changes="### Changes\n- {}\n\n".format(changes) if changes else "",
             vh=vh.get("version", "?"), nv=vh.get("network_version", "?"), sb=vh.get("steam_build", "?"),
             lt=vh.get("last_tested", "?"), bx=m.get("requires", {}).get("BepInExPack_Valheim", "5.4"),
             irepo=INSTALLER_REPO, id=pid)
    return {"repo": m["repo"], "tag": "v" + version, "title": "{} v{}".format(m["name"], version),
            "assets": [zpath, out_dll], "notes": notes}


def gh_run(gh, args, check=True):
    return subprocess.run([gh] + args, capture_output=True, text=True, check=check)


def publish(gh, rel):
    exists = gh_run(gh, ["release", "view", rel["tag"], "--repo", rel["repo"]], check=False).returncode == 0
    if exists:
        print("  {} {} already released - skipped (bump the version to publish a new one)".format(rel["repo"], rel["tag"]))
        return
    notes_file = OUT / "notes.md"
    notes_file.write_text(rel["notes"], encoding="utf-8")
    r = gh_run(gh, ["release", "create", rel["tag"], *[str(a) for a in rel["assets"]],
                    "--repo", rel["repo"], "--target", "main", "--title", rel["title"],
                    "--notes-file", str(notes_file), "--latest"], check=False)
    if r.returncode != 0:
        raise SystemExit("gh release create failed for {}:\n{}".format(rel["repo"], r.stderr.strip()))
    print("  published {}".format(r.stdout.strip()))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--build-only", action="store_true")
    args = ap.parse_args()

    shutil.rmtree(OUT, ignore_errors=True)
    OUT.mkdir(parents=True)

    rel = build_plugin(ROOT)
    print("built {:<28} {:<8} {}".format(rel["repo"], rel["tag"], ", ".join(a.name for a in rel["assets"])))
    if args.build_only:
        print("\nPackages are in", OUT)
        return

    gh = find_gh()
    if not gh:
        raise SystemExit("GitHub CLI not found. Install it (winget install GitHub.cli) and run: gh auth login")
    if gh_run(gh, ["auth", "status"], check=False).returncode != 0:
        raise SystemExit("GitHub CLI isn't logged in. Run: gh auth login")
    print("\nPublishing...")
    publish(gh, rel)
    print("\nDone. https://github.com/{}/releases/latest".format(rel["repo"]))


if __name__ == "__main__":
    sys.exit(main())
