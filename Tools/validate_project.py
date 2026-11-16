"""Static checks only. This does not import or compile a Unity project."""
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
guids = {}
for path in sorted((ROOT / "Assets").rglob("*")):
    if path.suffix == ".meta":
        continue
    meta = Path(str(path) + ".meta")
    assert meta.exists(), f"Missing meta: {path}"
    match = re.search(r"^guid: ([0-9a-f]{32})$", meta.read_text(), re.MULTILINE)
    assert match, f"Invalid GUID: {meta}"
    guid = match.group(1)
    assert guid not in guids, f"Duplicate GUID in {meta}"
    guids[guid] = path
    if path.suffix == ".asmdef":
        json.loads(path.read_text())

scene = ROOT / "Assets/Scenes/SharedWorld.unity"
scene_text = scene.read_text()
script_guid = re.search(r"m_Script: \{fileID: 11500000, guid: ([0-9a-f]{32})", scene_text).group(1)
assert guids[script_guid] == ROOT / "Assets/Kjs/Runtime/KjsDemo.cs"
scene_guid = re.search(r"^guid: ([0-9a-f]{32})", Path(str(scene) + ".meta").read_text(), re.MULTILINE).group(1)
build_settings = (ROOT / "ProjectSettings/EditorBuildSettings.asset").read_text()
assert "enabled: 1" in build_settings and scene_guid in build_settings
assert "Assets/Scenes/SharedWorld.unity" in build_settings
version = (ROOT / "ProjectSettings/ProjectVersion.txt").read_text()
assert "6000.6.9f1" in version and "8c45277fb9c5" in version
manifest = json.loads((ROOT / "Packages/manifest.json").read_text())
assert manifest["dependencies"]["com.unity.modules.jsonserialize"] == "1.0.0"
assert "m_DefaultBehaviorMode: 0" in (ROOT / "ProjectSettings/EditorSettings.asset").read_text()
assert "m_Name: Horizontal" in (ROOT / "ProjectSettings/InputManager.asset").read_text()
assert "m_Name: Vertical" in (ROOT / "ProjectSettings/InputManager.asset").read_text()
print(json.dumps({"passed": True, "assets_and_folders": len(guids),
                  "checks": ["meta_files", "unique_guids", "scene_script_reference", "enabled_build_scene",
                             "3d_mode", "editor_version", "input_axes", "json_manifests"],
                  "scope": "static structure only; Unity import/build/rendering not verified"}, indent=2))
