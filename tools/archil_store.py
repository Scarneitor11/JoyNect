#!/usr/bin/env python3
"""Keeps JoyNect's generated artifacts on an Archil disk so they survive between runs.

The screen captures and the app icon sizes are drawn by the tooling here rather than edited by hand,
and reviewing a visual change means comparing them across runs. An Archil disk holds those files, so
a capture run on one machine can be pulled for review on another instead of being redone.

Usage:
    python tools/archil_store.py push [path ...]   # default: the artifact set below
    python tools/archil_store.py pull [path ...]
    python tools/archil_store.py status

Run it from the repository's virtual environment with the archil package installed (see
.env.example for the key):

    py -m venv .venv && .venv/Scripts/python.exe -m pip install archil
    .venv/Scripts/python.exe tools/archil_store.py push

ARCHIL_API_KEY is read from the environment and must be an account API key (`key-...`), not a disk
token. ARCHIL_DISK_ID pins an existing disk; otherwise the `joynect-artifacts` disk is found by name
and created on first use.
"""

from __future__ import annotations

import os
import sys
from collections.abc import Iterator
from pathlib import Path

import archil

ROOT = Path(__file__).resolve().parent.parent
DISK_NAME = "joynect-artifacts"

# What the tooling writes and a visual review reads: one PNG per screen in each theme, plus the icon
# sizes and the tray resources beside them.
DEFAULT_PATHS = ("captures", "src/JoyNect.App/assets")

CONTENT_TYPES = {
    ".png": "image/png",
    ".ico": "image/x-icon",
    ".xaml": "application/xml",
}


def _env(name: str) -> str:
    return os.environ.get(name, "").strip()


def open_disk() -> archil.Disk:
    """Opens the artifact disk, creating it the first time a key is used."""
    key = _env("ARCHIL_API_KEY")
    if not key:
        raise SystemExit(
            "ARCHIL_API_KEY is not set. Create an API key in the Archil dashboard, export it in your "
            "shell (see .env.example), and run this again."
        )
    if not key.startswith("key-"):
        raise SystemExit(
            "ARCHIL_API_KEY does not look like an account API key (it starts with 'key-'). A disk "
            "token mounts one disk and cannot manage them; use the API key instead."
        )

    # The SDK takes the region from ARCHIL_REGION when it is set, so only the key is passed here.
    archil.configure(api_key=key)

    disk_id = _env("ARCHIL_DISK_ID")
    if disk_id:
        return archil.get_disk(disk_id)

    existing = archil.list_disks(name=DISK_NAME)
    if existing:
        return existing[0]

    created = archil.create_disk(name=DISK_NAME)
    print(f"created disk {DISK_NAME} ({created.disk.id})")
    return created.disk


def artifacts(paths: tuple[str, ...]) -> Iterator[Path]:
    """Every file under the requested repository paths, skipping ones not generated yet."""
    for entry in paths:
        path = ROOT / entry
        if not path.exists():
            print(f"skipping {entry}: nothing generated there yet")
            continue
        if path.is_file():
            yield path
            continue
        for file in sorted(path.rglob("*")):
            if file.is_file():
                yield file


def push(disk: archil.Disk, paths: tuple[str, ...]) -> int:
    stored = 0
    for file in artifacts(paths):
        key = file.relative_to(ROOT).as_posix()
        disk.put_object(key, file.read_bytes(), CONTENT_TYPES.get(file.suffix.lower()))
        stored += 1

    print(f"stored {stored} file(s) on {disk.name} ({disk.id})")
    return stored


def remote_files(disk: archil.Disk, paths: tuple[str, ...]) -> list[str]:
    keys: list[str] = []
    for entry in paths:
        entry = entry.strip("/")
        for obj in disk.list_objects(entry, recursive=True).objects:
            if obj.key == entry or obj.key.startswith(entry + "/"):
                keys.append(obj.key)
    return keys


def pull(disk: archil.Disk, paths: tuple[str, ...]) -> int:
    keys = remote_files(disk, paths)
    if not keys:
        print(f"no artifacts on {disk.name} yet; run `push` after a capture run")
        return 0

    for key in keys:
        target = ROOT / key
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(disk.get_object(key))

    print(f"restored {len(keys)} file(s) from {disk.name} ({disk.id})")
    return len(keys)


def status(disk: archil.Disk) -> int:
    keys = remote_files(disk, DEFAULT_PATHS)
    total = 0
    for key in keys:
        meta = disk.head_object(key)
        total += meta.size if meta else 0

    print(f"disk    {disk.name} ({disk.id})")
    print(f"region  {disk.region}")
    print(f"state   {disk.status}")
    print(f"files   {len(keys)} tracked artifact(s), {total / 1024:.0f} KiB")
    return 0


def main(argv: list[str]) -> int:
    command = argv[0] if argv else "status"
    if command not in {"push", "pull", "status"}:
        print(f"unknown command {command!r}; expected push, pull or status")
        return 2

    disk = open_disk()
    paths = tuple(argv[1:]) or DEFAULT_PATHS

    if command == "push":
        push(disk, paths)
        return 0
    if command == "pull":
        pull(disk, paths)
        return 0
    return status(disk)


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
