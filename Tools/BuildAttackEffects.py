"""Bake the generated transparent attack paintings into 4x4 Unity animation atlases."""

from pathlib import Path
import re
import uuid

import cv2
import numpy as np
from PIL import Image

from AnimationCels import ART, COUNT, pack, premultiplied, split, straight


ROOT = ART.parents[2]
SOURCES = ROOT / "Docs/References/AttackEffects"
OUTPUT = ART / "BattleEffects/Resources/AttackEffects"
TEXTURE_META = ART / "SkillEffects/01-WarCry.png.meta"
SIZE = 128
# IDs map to the original paintings in row-major order. A dash skips an unused painting.
PAINTINGS = {
    "Elements-a": """
        CampGuard - HeavyWarrior GreenRanger
        SilverArcher StarMage StarWeaver DawnHealer
        EnemySeismoSpider RadiantMixer MistAlchemist GoldHunter
        ShieldCommander EnemyAnchorHermit GloomWatcher -
    """,
    "Elements-b": """
        EnemyMudEel CalamityChanter Chronomancer -
        AbyssKeeper Vanguard FateWeaver -
        EnemyInkScribe EnemySignalBeacon EnemyWaxAttendant DiSha
        - - - EnemyFogSignalGunner
    """,
    "Company-veterans": """
        KindleGuard TwilightKnight HeavyBulwark ArcaneApprentice
        VoidWeaver StarfireMage EmberBrawler ValiantSoldier
        Apocalypse ForestStalker StarChaser EclipseArcher
        TianGong DivinePriest FallenAcolyte GildedMerchant
    """,
    "Docks-company": """
        DeepSeaWhisper EnemyTideFollower EnemyWreckScavenger EnemyMooringWarden
        EnemyOathlineGuard EnemyRearHunter EnemyBellMagistrate EnemyShipCaulker
        EnemyForkFisher EnemyFogAngler EnemyLanternMoth EnemyCrackedPotter
        EnemyAshLanternAdept EnemyRoadBrute EnemyBlockadeAdmiral EnemyDrownedLanternRay
    """,
    "Graveyard-workers": """
        EnemyGraveDigger EnemyGraveShoveler EnemyTombShield EnemyRustSpear
        EnemyVigilCrossbow EnemyBoneLantern EnemyDirgeAcolyte EnemyStitchServant
        EnemyShroudWeaver EnemyGraveFern EnemyEpitaphGargoyle EnemyCarrionRaven
        EnemyBoneHyena EnemyPhosphorWisp EnemyFrostRevenant EnemyBoneNun
    """,
    "Graveyard-night": """
        EnemyCoffinHound EnemySplitCoffin EnemyRustChain EnemyFuneralDrummer
        EnemyMistWraith EnemyBonePeddler EnemyPallbearer EnemyGraveBell
        EnemySoulStitcher EnemyWeepingGargoyle EnemyGraveExecutioner EnemyNamelessKing
        EnemySepulchralMatron Chant RelicHunter IntelligenceOfficer
    """,
    "Capital-machines": """
        EnemyGatePunch EnemyMailSphere EnemyPorcelainConductor EnemyLensSurveyor
        EnemyRivetMantis EnemyShearCentipede EnemyWalkingKiln EnemyValveMedic
        EnemyFlueSweep EnemyLuggageCrane EnemyBogieHound EnemyFareInspector
        EnemyWeldFirefly EnemyCabinetMaw EnemyCokeBoar EnemyPhonoHerald
    """,
    "BrassQuack": "EnemyBrassQuack",
    "SaltCrab": "EnemySaltCrab",
    "WireDispatch": "EnemyWireDispatch",
    "Capital-night": """
        EnemyKilnTender EnemyDieBoxer EnemyGlassGardener EnemyCivicNurse
        EnemyPostmaster EnemyArsonExaminer EnemyFilingJustice EnemyPressureBishop
        EnemySwitchMarshal EnemyTurbineKnight EnemyAstralGovernor EnemyCrucibleRegent
        EnemyRoyalEngine EnemyDeckSailor EnemyThreadRifleman EnemyCorpseCrossbowman
    """,
}


def isolate(cell):
    pixels = np.asarray(cell, dtype=np.float32).copy()
    height, width = pixels.shape[:2]
    row, col = np.indices((height, width))
    edge = np.minimum.reduce((row, height - row - 1, col, width - col - 1))
    gutter = edge / (min(width, height) * .06)
    alpha = np.clip((pixels[:, :, 3] - 10) / 245, 0, 1)
    # Keep the painted glow and clear any low-alpha colour carried across a cell boundary.
    pixels[:, :, 3] = 255 * alpha * np.clip(gutter, 0, 1)
    pixels[pixels[:, :, 3] < 1] = 0
    yy, xx = np.where(pixels[:, :, 3] > 34)
    if len(xx) < 150:
        raise ValueError("Generated cel has insufficient foreground coverage")
    padding = round(min(width, height) * .045)
    bounds = (max(0, xx.min() - padding), max(0, yy.min() - padding),
              min(width, xx.max() + padding + 1), min(height, yy.max() + padding + 1))
    art = Image.fromarray(np.rint(pixels).astype(np.uint8)).crop(bounds)
    art.thumbnail((108, 108), Image.Resampling.LANCZOS)
    cel = Image.new("RGBA", (SIZE, SIZE))
    cel.paste(art, ((SIZE - art.width) // 2, (SIZE - art.height) // 2))
    return cel


def animate(cel):
    source = premultiplied(cel)
    row, col = np.indices((SIZE, SIZE), dtype=np.float32)
    centre = (SIZE - 1) / 2
    for frame in range(COUNT):
        time = frame / (COUNT - 1)
        grow = 1 - (1 - time) ** 3
        spread = .66 + .44 * grow
        shift = -8 + 16 * grow
        # The source painting remains the only artwork. The warp spreads its existing brushstrokes.
        x = (col - centre - shift) / spread + centre
        y = (row - centre) / spread + centre - time * 3 * np.sin(col / 15)
        paint = cv2.remap(source, x, y, cv2.INTER_LINEAR)
        fade = 1 - np.clip((time - .48) / .52, 0, 1) ** 1.4
        paint *= (.68 + .32 * min(1, time * 6)) * max(.015, fade)
        yield straight(paint)


def folder_meta(directory):
    guid = uuid.uuid5(uuid.NAMESPACE_URL, directory.relative_to(ART).as_posix()).hex
    meta = directory.with_suffix(".meta")
    if not meta.exists():
        meta.write_text(f"fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n")


def build():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    for directory in (OUTPUT, OUTPUT.parent, OUTPUT.parent.parent):
        folder_meta(directory)
    template = TEXTURE_META.read_text()
    characters = set()
    for name, ids in PAINTINGS.items():
        ids = ids.split()
        columns = 1 if len(ids) == 1 else 4
        if len(ids) != columns * columns:
            raise ValueError(f"{name} must map exactly {columns * columns} paintings")
        with Image.open(SOURCES / f"{name}.png") as artwork:
            if "A" not in artwork.getbands():
                raise ValueError(f"{name} must have an alpha channel")
            cells = split(artwork.convert("RGBA"), columns, columns)
        for character, cell in zip(ids, cells):
            if character == "-":
                continue
            if character in characters:
                raise ValueError(f"Duplicate attack art for {character}")
            characters.add(character)
            atlas = pack(list(animate(isolate(cell))), 4)
            png = OUTPUT / f"{character}.png"
            atlas.save(png, optimize=True)
            guid = uuid.uuid5(uuid.NAMESPACE_URL, png.relative_to(ART).as_posix()).hex
            settings = re.sub(r"(?m)^guid: .+", f"guid: {guid}", template)
            png.with_suffix(".png.meta").write_text(settings.replace("filterMode: 0", "filterMode: 1"))
    print(f"Baked {len(characters)} individual attack atlases into {OUTPUT}")


if __name__ == "__main__":
    build()
