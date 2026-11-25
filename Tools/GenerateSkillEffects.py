"""Validate the authored skill strips and optionally rebuild their animated overview."""

import argparse
from pathlib import Path
import re

from PIL import Image, ImageDraw, ImageFont


FRAMES = 16
ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / "Assets/RaidPartyManager/Art/SkillEffects"
PREVIEW = ROOT / "Docs/Previews/SkillEffects.gif"
FLIGHT_ATLASES = {"07-VolleyProjectile", "08-MeteorBoulder", "08-MeteorFragments"}


def read_strips():
    strips = {}
    for path in sorted(ART.glob("[0-9][0-9]-*.png")):
        if path.stem in FLIGHT_ATLASES:
            continue
        number = int(path.name[:2])
        with Image.open(path) as sheet:
            width, height = sheet.size
            if number in strips or sheet.mode != "RGBA" or width != height * FRAMES:
                raise ValueError(f"Expected sixteen square RGBA frames for {path}")
            strips[number] = [sheet.crop((i*height, 0, (i+1)*height, height)) for i in range(FRAMES)]

    catalog = ROOT / "Assets/RaidPartyManager/PrototypeData/SkillCatalog.asset"
    expected = {int(n) for n in re.findall(r"(?m)^  - number: (\d+)$", catalog.read_text())}
    if strips.keys() != expected:
        raise ValueError(f"Missing skills {sorted(expected - strips.keys())}; extra {sorted(strips.keys() - expected)}")
    return strips


def preview(strips):
    frames = []
    font = ImageFont.load_default()
    for i in range(FRAMES):
        board = Image.new("RGBA", (624, 10+123*((len(strips)+4)//5)), "#10151e")
        label = ImageDraw.Draw(board)
        for index, (number, cels) in enumerate(strips.items()):
            x, y = 10 + index % 5 * 122, 10 + index // 5 * 123
            label.rounded_rectangle((x, y, x+114, y+115), 4, fill="#202937", outline="#3c495a")
            art = cels[i].resize((96, 96), Image.Resampling.LANCZOS)
            board.alpha_composite(art, (x+9, y+4))
            label.text((x+56, y+103), f"{number:02d}", font=font, fill="#c8d0db", anchor="mt")
        frames.append(board.convert("RGB"))
    PREVIEW.parent.mkdir(parents=True, exist_ok=True)
    frames[0].save(PREVIEW, save_all=True, append_images=frames[1:], duration=55, loop=0, optimize=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--preview", action="store_true", help="rebuild the animated contact sheet")
    args = parser.parse_args()
    strips = read_strips()
    if args.preview:
        preview(strips)
