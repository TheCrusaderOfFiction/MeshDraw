"""Checked, self-contained v1 interchange. No Blender dependency."""
import base64
import hashlib
import json
import re
from pathlib import Path

MAX_TOTAL = 256 * 1024 * 1024
MAX_FILE = 96 * 1024 * 1024
SAFE_NAME = re.compile(r"^[A-Za-z0-9_-]+\.(?:fbx|png|json|meshdraw|blend|obj)$")
CHANNELS = ("basecolor", "roughness", "metallic", "emission", "height")
REQUIRED = ("mesh.fbx", "project.meshdraw", "skeleton.json", "job.json", "front.png", "back.png", "left.png", "right.png", "top.png", "bottom.png")

def decode(document):
    if document.get("format") != "MeshDrawAsset" or document.get("version") != 1:
        raise ValueError("Unsupported MeshDraw asset format")
    if document.get("outputType") not in ("static", "skeletal"):
        raise ValueError("Invalid mesh type")
    entries = document.get("files")
    if not isinstance(entries, list) or not 1 <= len(entries) <= 128:
        raise ValueError("Invalid file manifest")
    files = {}; total = 0
    for entry in entries:
        name = entry.get("name", "")
        if not isinstance(name, str) or not SAFE_NAME.fullmatch(name) or name in files:
            raise ValueError("Unsafe or duplicate file name")
        data = entry.get("data", "")
        if not isinstance(data, str) or len(data) > MAX_FILE * 4 // 3 + 4:
            raise ValueError("Oversized file")
        payload = base64.b64decode(data, validate=True); total += len(payload)
        if total > MAX_TOTAL or len(payload) > MAX_FILE or len(payload) != entry.get("size") or hashlib.sha1(payload).hexdigest() != entry.get("sha1"):
            raise ValueError("Damaged payload: " + name)
        files[name] = payload
    if any(name not in files for name in REQUIRED):
        raise ValueError("Missing mesh or source drawing data")
    if (document.get("mesh"), document.get("project"), document.get("skeleton")) != ("mesh.fbx", "project.meshdraw", "skeleton.json"):
        raise ValueError("Invalid source references")
    textures = document.get("textures")
    if not isinstance(textures, dict) or any(k not in CHANNELS or n != f"mesh_{k}.png" or n not in files for k, n in textures.items()):
        raise ValueError("Invalid texture manifest")
    project = json.loads(files["project.meshdraw"])
    if project.get("Version") != 2 or project.get("OutputType") != (1 if document["outputType"] == "skeletal" else 0):
        raise ValueError("Source drawing and mesh type disagree")
    return files

def read(path):
    path = Path(path)
    if path.stat().st_size > MAX_TOTAL * 4 // 3 + 1024 * 1024:
        raise ValueError("Asset file is too large")
    document = json.loads(path.read_text(encoding="utf-8-sig"))
    return document, decode(document)

def make(document, files):
    result = {k: v for k, v in document.items() if k != "files"}
    result["files"] = [{"name": n, "size": len(p), "sha1": hashlib.sha1(p).hexdigest(), "data": base64.b64encode(p).decode("ascii")} for n, p in sorted(files.items())]
    decode(result)
    return result

def write(path, document, files):
    import os
    import tempfile
    path = Path(path)
    result = make(document, files)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=path.parent, prefix=".meshdraw-", suffix=".tmp", delete=False) as stream:
            temporary = Path(stream.name)
            json.dump(result, stream, separators=(",", ":"))
        os.replace(temporary, path)
    finally:
        if temporary and temporary.exists(): temporary.unlink()
