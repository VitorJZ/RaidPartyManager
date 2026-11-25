"""Bake the seven painted entrance props from their four transparent keys."""
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageChops

from AnimationCels import ART, new_meta, pack, save, sequence, split

SOURCE = ART.parents[2] / "Docs/References/BossEntranceProps"
OUTPUT = ART / "Enemies/Resources/BossEntranceProps"
TEMPLATE = OUTPUT.parent / "BossEntrances/EnemyAstralGovernor.png.meta"
BOSSES = ("BlockadeAdmiral", "DrownedLanternRay", "NamelessKing", "SepulchralMatron",
          "RoyalEngine", "AstralGovernor", "CrucibleRegent")


def keys(path):
    with Image.open(path) as source:
        if "A" not in source.getbands():
            raise ValueError(f"{path.name} requires transparent key art")
        cells = split(source.convert("RGBA"), 2, 2)
    for cell in cells:
        pixels = np.asarray(cell).copy()
        pixels[pixels[:, :, 3] <= 8] = 0
        image = Image.fromarray(pixels)
        image.thumbnail((312, 312), Image.Resampling.LANCZOS)
        padded = Image.new("RGBA", (320, 320))
        padded.paste(image, ((320 - image.width) // 2, (320 - image.height) // 2))
        yield padded


def suspended_censer(frames):
    # Extend the ring from the painted key up to the fly loft. Keep the censer at
    # its original scale in the bottom square so the camera can frame it separately.
    chain_link = frames[0].crop((150, 12, 176, 37)).resize((7, 10), Image.Resampling.LANCZOS)
    for cel in frames:
        tall = Image.new("RGBA", (320, 960))
        for y in range(-4, 657, 7):
            tall.alpha_composite(chain_link, (159, y))
        tall.alpha_composite(cel, (0, 640))
        yield tall


def coffin_layers(frames):
    # The king stands between the painted lid and the front rim. Follow the
    # perspective of the lip in each original key as the lid opens.
    xs = np.array([0, 21, 44, 88, 169, 248, 281, 300])
    lip_keys = np.array([[135, 132, 144, 149, 138, 129, 115, 110],
                         [135, 132, 146, 151, 140, 128, 115, 110],
                         [134, 141, 155, 155, 145, 135, 124, 120],
                         [136, 141, 156, 158, 148, 138, 125, 120]])
    back, front = [], []
    for i, cel in enumerate(frames):
        key, step = divmod(i, 5)
        ys = lip_keys[key] + (lip_keys[min(key + 1, 3)] - lip_keys[key]) * (step / 5)
        rim = Image.new("L", cel.size)
        edge = [(round(4 + x * 312 / 300), round(4 + y * 312 / 300)) for x, y in zip(xs, ys)]
        ImageDraw.Draw(rim).polygon([(0, edge[0][1]), *edge, (320,edge[-1][1]), (320,320), (0,320)], fill=255)
        for parts, mask in ((back, ImageChops.invert(rim)), (front, rim)):
            part = cel.copy()
            part.putalpha(ImageChops.multiply(cel.getchannel("A"), mask))
            parts.append(part)
    return back, front


if __name__ == "__main__":
    OUTPUT.mkdir(parents=True, exist_ok=True)
    folder_meta = Path(str(OUTPUT) + ".meta")
    if not folder_meta.exists():
        new_meta(folder_meta, OUTPUT.parent / "BossEntrances.meta")
    for boss in BOSSES:
        frames = sequence(list(keys(SOURCE / (boss + ".png"))))
        if boss == "NamelessKing":
            frames, rim = coffin_layers(frames)
            save(pack(rim, 4), OUTPUT / "EnemyNamelessKing-Rim.png", TEMPLATE)
        elif boss == "SepulchralMatron":
            frames = list(suspended_censer(frames))
        save(pack(frames, 4), OUTPUT / ("Enemy" + boss + ".png"), TEMPLATE)
