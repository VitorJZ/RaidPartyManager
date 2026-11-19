"""Assemble ImageGen pose grids into the runtime's existing atlas formats.

This performs layout/slicing only. Enemy drawings come from the retained ImageGen
sources, never from recoloring an existing enemy. Reruns preserve stable GUIDs.
Requires Python 3.10+ and Pillow. Run from any directory, optionally --allow-missing
while image generation is in progress. Final validation requires all 30 sources.
"""
from __future__ import annotations

import argparse
import json
import math
import re
import uuid
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "Docs/WhisperingGraveyard.json"
ART = ROOT / "Assets/RaidPartyManager/Art/Enemies/WhisperingGraveyard"
ASSETS = ROOT / "Assets/RaidPartyManager/PrototypeData/Enemies/WhisperingGraveyard"
PREVIEWS = ROOT / "Docs/Previews/WhisperingGraveyard"
TARGETS = "Front Rear LowestHealth HighestAttack LowestDefense Self WoundedAlly FrontTwo RearTwo AllEnemies AllAllies StrongestAlly".split()
STATUSES = "Vigor Swift Resolve Undying Greed Purity Ember Poison Exposed Weakened Thorns Stunned Injured Cursed Chill Frozen".split()
PROFESSIONS = "Warrior Mage Archer Support Monster".split()
DAMAGE = "Physical Magic Fire Lightning True Default Fixed".split()
KINDS = "Attack Status Heal Shield".split()
RANKS = ["Unspecified", "Normal", "Elite", "Boss"]
CELL = 192


def guid(path: Path) -> str:
    return uuid.uuid5(uuid.NAMESPACE_URL, "raid-party-manager/" + path.relative_to(ROOT).as_posix()).hex


def meta(path: Path, kind: str = "file") -> None:
    target = Path(str(path) + ".meta")
    if target.exists():
        return
    head = f"fileFormatVersion: 2\nguid: {guid(path)}\n"
    if kind == "texture":
        template = (ROOT / "Assets/RaidPartyManager/Art/Enemies/SaltCrab-Combat.png.meta").read_text(encoding="utf-8")
        head = re.sub(r"guid: [a-f0-9]{32}", "guid: " + guid(path), template, count=1)
    elif kind == "folder":
        head += "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n"
    elif kind == "asset":
        head += "NativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 11400000\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n"
    elif kind == "script":
        head += "MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n"
    else:
        head += "DefaultImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n"
    target.write_text("\n".join(line.rstrip() for line in head.splitlines()) + "\n", encoding="utf-8", newline="\n")


def folder(path: Path) -> None:
    path.mkdir(parents=True, exist_ok=True)
    if path.is_relative_to(ROOT / "Assets"):
        for p in [path, *path.parents]:
            if p == ROOT / "Assets":
                break
            meta(p, "folder")


def ref(path: Path, file_id=2800000, kind=3) -> str:
    existing = Path(str(path) + ".meta")
    identifier = re.search(r"guid: ([a-f0-9]{32})", existing.read_text(encoding="utf-8")).group(1) if existing.exists() else guid(path)
    return f"{{fileID: {file_id}, guid: {identifier}, type: {kind}}}"


def save_image(image: Image.Image, path: Path) -> None:
    image.save(path, optimize=True)
    meta(path, "texture")


def pack(frames: list[Image.Image], cols: int, rows: int, wide=False) -> Image.Image:
    width = 256 if wide else CELL
    atlas = Image.new("RGBA", (cols * width, rows * CELL))
    for i, frame in enumerate(frames):
        atlas.alpha_composite(frame, ((i % cols) * width + (width - CELL) // 2, (i // cols) * CELL))
    return atlas


def pose_frames(source: Image.Image) -> list[list[Image.Image]]:
    if source.mode != "RGBA" or source.getchannel("A").getextrema()[0] != 0:
        raise ValueError("ImageGen source must contain genuine transparency")
    result = []
    for row in range(6):
        frames = []
        for col in range(8):
            box = (round(col * source.width / 8), round(row * source.height / 6),
                   round((col + 1) * source.width / 8), round((row + 1) * source.height / 6))
            frame = source.crop(box)
            if not frame.getbbox():
                raise ValueError(f"Empty source pose row {row + 1}, column {col + 1}")
            # Fit the entire cell, preserving alpha and movement relative to its cell.
            frame.thumbnail((CELL - 12, CELL - 12), Image.Resampling.NEAREST)
            canvas = Image.new("RGBA", (CELL, CELL))
            canvas.alpha_composite(frame, ((CELL - frame.width) // 2, CELL - 6 - frame.height))
            frames.append(canvas)
        result.append(frames)
    return result


def special_fx(enemy: dict) -> Image.Image:
    action = enemy["actions"][1]
    color = (101, 220, 174) if action["kind"] in ("Heal", "Shield") else (
        (124, 198, 245) if action.get("status") == "Chill" else (166, 135, 211))
    strip = Image.new("RGBA", (1024, 128))
    for frame in range(8):
        image = Image.new("RGBA", (128, 128))
        d = ImageDraw.Draw(image)
        progress = frame / 7
        radius = 9 + int(progress * 44)
        alpha = int(220 * math.sin(math.pi * (frame + 1) / 9))
        d.ellipse((64-radius, 64-radius, 64+radius, 64+radius), outline=(*color, alpha), width=3)
        for mote in range(7):
            a = mote * math.tau / 7 + progress
            x, y = 64 + int(math.cos(a) * radius), 64 + int(math.sin(a) * radius)
            d.rectangle((x-2, y-2, x+2, y+2), fill=(*color, alpha))
        if action["kind"] == "Heal":
            d.rectangle((60, 38, 68, 84), fill=(*color, alpha))
            d.rectangle((43, 55, 85, 63), fill=(*color, alpha))
        strip.alpha_composite(image, (frame * 128, 0))
    return strip


def make_art(enemy: dict) -> Image.Image:
    prefix = enemy["id"]
    source_path = ART / "Sources" / (prefix + "-Source.png")
    meta(source_path, "texture")
    rows = pose_frames(Image.open(source_path))
    save_image(pack(rows[0] + rows[2] + rows[4] + rows[5], 8, 4), ART / (prefix + "-Combat.png"))
    save_image(pack(rows[2], 4, 2, wide=True), ART / (prefix + "-Attack.png"))
    save_image(pack([rows[1][i] for i in (0, 1, 2, 4, 5, 6)], 3, 2), ART / (prefix + "-Walk.png"))
    save_image(pack(rows[3], 4, 2, wide=True), ART / (prefix + "-Special.png"))
    save_image(special_fx(enemy), ART / (prefix + "-SpecialFX.png"))
    portrait = Image.new("RGBA", (256, 384))
    pose = rows[0][0]
    pose = pose.crop(pose.getbbox())
    pose.thumbnail((236, 354), Image.Resampling.NEAREST)
    # Upscale only as a display crop. Retain the ImageGen source for full pose detail.
    factor = min(236 / pose.width, 354 / pose.height)
    pose = pose.resize((round(pose.width * factor), round(pose.height * factor)), Image.Resampling.NEAREST)
    portrait.alpha_composite(pose, ((256 - pose.width) // 2, 374 - pose.height))
    save_image(portrait, ART / (prefix + "-Portrait.png"))
    sequence = []
    font = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 16)
    for row, label in enumerate(("待机", "移动", "普攻", "特技", "受击", "倒下")):
        for frame in rows[row]:
            preview = Image.new("RGB", (256, 242), (24, 27, 38))
            preview.paste(frame, (32, 30), frame)
            d = ImageDraw.Draw(preview)
            d.text((10, 6), f'{enemy["number"]} {enemy["name"]} / {label}', fill=(215, 218, 224), font=font)
            sequence.append(preview)
    sequence[0].save(PREVIEWS / (prefix + ".gif"), save_all=True, append_images=sequence[1:], duration=115, loop=0, disposal=2)
    return portrait


def make_definition(enemy: dict) -> None:
    prefix = enemy["id"]
    template = (ROOT / "Assets/RaidPartyManager/PrototypeData/Enemies/EnemyDrownedLanternRay.asset").read_text(encoding="utf-8")
    body = template.split("  m_Name:")[0] + f"  m_Name: Enemy{prefix}\n"
    body += "  m_EditorClassIdentifier: RaidPartyManager.Content::RaidPartyManager.Content.CharacterAsset\n"
    body += f'  id: Enemy{prefix}\n  displayName: {json.dumps(enemy["name"], ensure_ascii=False)}\n'
    for field, suffix in (("portrait", "Portrait"), ("battleSpriteSheet", "Combat"), ("battleAttackSheet", "Attack"),
                          ("battleWalkSheet", "Walk"), ("battleSpecialSheet", "Special"), ("battleSpecialEffect", "SpecialFX")):
        body += f"  {field}: {ref(ART / (prefix + '-' + suffix + '.png'))}\n"
    body += "  usePortraitInBattle: 0\n"
    body += f'  profession: {PROFESSIONS.index(enemy["profession"])}\n  enemyRank: {RANKS.index(enemy["rank"])}\n'
    body += f'  positionSlots: {enemy["slots"]}\n  rosterNumber: 0\n  stage: 1\n  reach: {1 if enemy["reach"] == "Melee" else 2}\n'
    summary = " / ".join(f'{a["name"]}（{a["weight"]}%）' for a in enemy["actions"])
    body += f'  fixedTrait: 0\n  traitPower: 0\n  traitText: {json.dumps(summary, ensure_ascii=False)}\n  personality: 1\n'
    for field in ("attack", "defense", "specialDefense", "health"):
        body += f"  {field}: {enemy[field]}\n"
    first = enemy["actions"][0]
    body += '  actionInterval: 10\n  basicAttack:\n    id: basic\n    displayName: 普攻\n    kind: 0\n    target: 0\n    power: 0\n    cooldown: 0\n'
    body += f'    damageType: {DAMAGE.index(first.get("damage", "Physical"))}\n  hasSkill: 0\n  enemyBehavior:\n    actions:\n'
    for action in enemy["actions"]:
        body += f'    - name: {json.dumps(action["name"], ensure_ascii=False)}\n      weight: {action["weight"]}\n'
        body += f'      kind: {KINDS.index(action["kind"])}\n      target: {TARGETS.index(action["target"])}\n      power: {action.get("power", 0)}\n'
        body += f'      damageType: {DAMAGE.index(action.get("damage", "Physical"))}\n      status: {STATUSES.index(action["status"]) if "status" in action else -1}\n'
        body += f'      layers: {action.get("layers", 0)}\n      requiresCharge: {int(action.get("charge", False))}\n      delayDecay: {int(action.get("delayDecay", False))}\n'
    body += f'    openingShield: {enemy.get("openingShield", 0)}\n    phaseThresholdPercent: {enemy.get("phaseThreshold", 0)}\n    phaseAttackBonus: {enemy.get("phasePower", 0)}\n'
    asset = ASSETS / ("Enemy" + prefix + ".asset")
    asset.write_text(body, encoding="utf-8", newline="\n")
    meta(asset, "asset")


def make_encounter(enemies: list[dict]) -> None:
    prototype = ROOT / "Assets/RaidPartyManager/PrototypeData/RoadsideEncounter.asset"
    destination = prototype.with_name("WhisperingGraveyardEncounter.asset")
    text = prototype.read_text(encoding="utf-8").replace("m_Name: RoadsideEncounter", "m_Name: WhisperingGraveyardEncounter")
    text = re.sub(r"  displayName:.*", "  displayName: 低语墓园演练", text, count=1)
    roster = "  enemyRoster:\n" + "".join("  - " + ref(ASSETS / ("Enemy" + e["id"] + ".asset"), 11400000, 2) + "\n" for e in enemies)
    text = re.sub(r"  enemyRoster:.*?(?=  statusIcons:)", lambda _: roster, text, flags=re.S)
    opening = "  enemies:\n"
    for i, enemy in enumerate([enemies[0], enemies[5], enemies[7], enemies[12]], 1):
        opening += f'  - instanceId: graveyard-{i}\n    character: {ref(ASSETS / ("Enemy" + enemy["id"] + ".asset"), 11400000, 2)}\n    position: {i}\n    startingHealth: -1\n'
    text = re.sub(r"  enemies:.*?(?=  additionalRecruits:)", lambda _: opening, text, flags=re.S)
    text = text.replace("  tickLimit: 200", "  tickLimit: 400")
    destination.write_text(text, encoding="utf-8", newline="\n")
    meta(destination, "asset")
    scene = ROOT / "Assets/Scenes/WhisperingGraveyard.unity"
    text = (ROOT / "Assets/Scenes/BattlePrototype.unity").read_text(encoding="utf-8")
    text = re.sub(r"  encounter:.*", "  encounter: " + ref(destination, 11400000, 2), text, count=1)
    background = ROOT / "Assets/RaidPartyManager/Art/BattleBackgrounds/04-WhisperingGraveyard.png"
    if not background.exists():
        raise FileNotFoundError(background)
    text = re.sub(r"  battleBackgrounds:.*?(?=  randomizeLineups:)", "  battleBackgrounds:\n  - " + ref(background) + "\n", text, flags=re.S)
    scene.write_text(text, encoding="utf-8", newline="\n")
    meta(scene)
    settings = ROOT / "ProjectSettings/EditorBuildSettings.asset"
    text = settings.read_text(encoding="utf-8")
    if "Assets/Scenes/WhisperingGraveyard.unity" not in text:
        text = text.replace("  m_configObjects:", "  - enabled: 1\n    path: Assets/Scenes/WhisperingGraveyard.unity\n    guid: " + guid(scene) + "\n  m_configObjects:")
        settings.write_text(text, encoding="utf-8", newline="\n")


def contact_sheet(enemies: list[dict], portraits: dict) -> None:
    font = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 18)
    sheet = Image.new("RGB", (1200, 5 * 252 + 50), (24, 27, 38))
    draw = ImageDraw.Draw(sheet)
    draw.text((18, 12), "低语墓园  23 普通 / 5 精英 / 2 Boss", font=font, fill=(210, 225, 221))
    for i, enemy in enumerate(enemies):
        if enemy["id"] not in portraits:
            continue
        image = portraits[enemy["id"]].copy()
        image.thumbnail((178, 210), Image.Resampling.NEAREST)
        x, y = i % 6 * 200, i // 6 * 252 + 50
        sheet.paste(image, (x+(200-image.width)//2, y), image)
        draw.text((x+10, y+212), f'{enemy["number"]} {enemy["name"]}', font=font, fill=(218, 217, 224))
    sheet.save(PREVIEWS / "Roster.jpg", quality=92)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--allow-missing", action="store_true")
    parser.add_argument("--data-only", action="store_true", help="Rebuild numeric definitions without re-slicing unchanged art")
    args = parser.parse_args()
    enemies = json.loads(DATA.read_text(encoding="utf-8"))["enemies"]
    assert len(enemies) == 30 and len({e["id"] for e in enemies}) == 30
    assert [sum(e["rank"] == rank for e in enemies) for rank in RANKS[1:]] == [23, 5, 2]
    assert all(sum(a["weight"] for a in e["actions"]) == 100 for e in enemies)
    for path in (ART, ART / "Sources", ASSETS, PREVIEWS):
        folder(path)
    portraits = {}
    missing = []
    for enemy in enemies:
        source = ART / "Sources" / (enemy["id"] + "-Source.png")
        if source.exists() and not args.data_only:
            portraits[enemy["id"]] = make_art(enemy)
        elif not source.exists():
            missing.append(enemy["id"])
        make_definition(enemy)
    make_encounter(enemies)
    for directory in ("Domain", "Content", "Battle"):
        for script in (ROOT / "Assets/RaidPartyManager" / directory).glob("*.cs"):
            meta(script, "script")
    if not args.data_only:
        contact_sheet(enemies, portraits)
    print(json.dumps({"definitions": len(enemies), "illustrated": len(portraits), "missing": missing}, ensure_ascii=False))
    if missing and not args.allow_missing:
        raise SystemExit("Final build requires all 30 ImageGen sources")


if __name__ == "__main__":
    main()
