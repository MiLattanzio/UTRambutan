#!/usr/bin/env python3
"""Fetch pinned ARM64 sidecars, verify bytes before extracting into the Click."""
import hashlib
import json
from pathlib import Path
import shutil
import tarfile
import urllib.request
import zipfile

root = Path(__file__).resolve().parents[1]
downloads = root / "artifacts/downloads"
output = root / "artifacts/native-tools"
downloads.mkdir(parents=True, exist_ok=True)
output.mkdir(parents=True, exist_ok=True)
pins = json.loads((root / "packaging/native-tools.json").read_text())
for name, pin in pins.items():
    archive = downloads / pin["archive"]
    if not archive.exists():
        temporary = archive.with_suffix(archive.suffix + ".download")
        with urllib.request.urlopen(pin["url"], timeout=60) as source, temporary.open("wb") as target:
            shutil.copyfileobj(source, target)
        temporary.replace(archive)
    with archive.open("rb") as stream:
        actual = hashlib.file_digest(stream, "sha256").hexdigest()
    if actual != pin["sha256"]:
        raise SystemExit(f"SHA-256 mismatch: {archive}; remove the damaged archive and retry")
    if name == "ffmpeg":
        dest = output / "ffmpeg"
        dest.mkdir(exist_ok=True)
        with tarfile.open(archive) as bundle:
            # The verified archive has lib/ and bin/ under a single release directory.
            # Copy regular library payloads first, then safe local symlinks.
            members = [m for m in bundle.getmembers() if len(Path(m.name).parts) == 3
                       and (Path(m.name).parts[-2] == "lib" or Path(m.name).name in ("ffmpeg", "ffprobe"))]
            for member in members:
                if member.isfile():
                    with bundle.extractfile(member) as source, (dest / Path(member.name).name).open("wb") as target:
                        shutil.copyfileobj(source, target)
                    (dest / Path(member.name).name).chmod(0o755)
            for member in members:
                if member.issym():
                    if Path(member.linkname).name != member.linkname:
                        raise SystemExit("Unexpected FFmpeg symlink")
                    link = dest / Path(member.name).name
                    link.unlink(missing_ok=True)
                    link.symlink_to(member.linkname)
    elif name == "deno":
        with zipfile.ZipFile(archive) as bundle, bundle.open("deno") as source, (output / "deno").open("wb") as target:
            shutil.copyfileobj(source, target)
        (output / "deno").chmod(0o755)
    else:
        shutil.copyfile(archive, output / name)
        (output / name).chmod(0o755)
    print(f"Verified {name}: {actual}")
