#!/usr/bin/env python3
"""Run on the phone. Inspect only the Rambutan session identified in its log.

No screenshots, credentials, stream URLs, other player sessions or sink metadata.
Requires the system's python3-dbus package; does not install anything.
"""
import argparse
import json
import os
from datetime import datetime, timezone
from pathlib import Path

import dbus

parser = argparse.ArgumentParser()
parser.add_argument("session", type=int)
parser.add_argument("pid", type=int)
args = parser.parse_args()
proc = Path(f"/proc/{args.pid}")
if proc.joinpath("comm").read_text().strip() != "Rambutan.App":
    raise SystemExit("The specified PID is not Rambutan.App")
os.environ.setdefault("DBUS_SESSION_BUS_ADDRESS", f"unix:path=/run/user/{os.getuid()}/bus")
bus = dbus.SessionBus()
service = "com.lomiri.MediaHub.Service"
path = f"/com/lomiri/MediaHub/Service/sessions/{args.session}"
player = dbus.Interface(bus.get_object(service, path), "org.freedesktop.DBus.Properties")
values = player.GetAll("org.mpris.MediaPlayer2.Player")
tracks = dbus.Interface(bus.get_object(service, path + "/TrackList"), "org.freedesktop.DBus.Properties")
state = next(s for s in proc.joinpath("status").read_text().splitlines() if s.startswith("State:"))
backlight = Path("/sys/class/backlight/panel0-backlight")
print(json.dumps({
    "utc": datetime.now(timezone.utc).isoformat(),
    "pid": args.pid,
    "process_state": state.split(":", 1)[1].strip(),
    "profile": proc.joinpath("attr/current").read_text().strip(),
    "brightness": int(backlight.joinpath("brightness").read_text()),
    "bl_power": int(backlight.joinpath("bl_power").read_text()),
    "session": path,
    "status": str(values["PlaybackStatus"]),
    "position_seconds": int(values["Position"]) / 1_000_000_000,
    "duration_seconds": int(values["Duration"]) / 1_000_000_000,
    "track_id": str(values["Metadata"].get("mpris:trackid", "")),
    "native_tracks": len(tracks.Get("org.mpris.MediaPlayer2.TrackList", "Tracks")),
    "audio": bool(values["IsAudioSource"]),
    "video": bool(values["IsVideoSource"]),
    "volume": float(values["Volume"]),
}, indent=2))
