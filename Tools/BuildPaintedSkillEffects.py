"""Compile transparent ImageGen key art into Unity's 16-frame horizontal skill strips."""

from pathlib import Path

import cv2
import numpy as np
from PIL import Image

from AnimationCels import ART, COUNT, pack, premultiplied, sequence, split, straight


SOURCES = ART.parents[2] / "Docs/References/SkillEffects"
OUTPUT = ART / "SkillEffects"
SIZE = 128
PACKS = {
    "Frontline": (3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18),
    "Specialist": (19, 20, 21, 22, 23, 24, 25, 26, 28, 29, 31, 33, 34, 35, 36, 37),
}
RISING = {4, 6, 11, 12, 17, 23, 26, 29, 34, 37}
SWEEPING = {7, 9, 10, 13, 16, 20, 21, 24, 31, 33, 35}
IMPACTS = {3, 8, 18, 19, 25, 28}
DISPLAY_NAMES = {
    7: "07-ArrowVolley.png",
    8: "08-Meteor.png",
    38: "38-VenomSurge.png",
}


def clear_gutter(cell):
    pixels = np.asarray(cell, dtype=np.float32).copy()
    height, width = pixels.shape[:2]
    row, col = np.indices((height, width))
    edge = np.minimum.reduce((row, height - row - 1, col, width - col - 1))
    gutter = np.clip(edge / (min(width, height) * .074), 0, 1)
    pixels[:, :, 3] = 255 * np.clip((pixels[:, :, 3] - 10) / 245, 0, 1) * gutter
    pixels[pixels[:, :, 3] < 1] = 0
    return Image.fromarray(np.rint(pixels).astype(np.uint8))


def fit(cell, zoom, crop):
    cleaned = clear_gutter(cell)
    if crop:
        alpha = cleaned.getchannel("A")
        bounds = alpha.point(lambda v: 255 if v >= 35 else 0).getbbox()
        if not bounds:
            raise ValueError("Painted key has no solid silhouette")
        x0, y0, x1, y1 = bounds
        padding = round(min(cleaned.size) * .045)
        cleaned = cleaned.crop((max(0, x0-padding), max(0, y0-padding),
                                min(cleaned.width, x1+padding), min(cleaned.height, y1+padding)))
    width, height = cleaned.size
    factor = zoom / max(width, height)
    cleaned = cleaned.resize((round(width*factor), round(height*factor)), Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", (SIZE, SIZE))
    canvas.paste(cleaned, ((SIZE-cleaned.width)//2, (SIZE-cleaned.height)//2))
    return canvas


def smooth(value):
    value = np.clip(value, 0, 1)
    return value * value * (3 - 2 * value)


def animate(image, skill):
    paint = premultiplied(image)
    row, col = np.indices((SIZE, SIZE), dtype=np.float32)
    centre = (SIZE - 1) * .5
    sweeping = skill in SWEEPING
    rising = skill in RISING
    impact = skill in IMPACTS
    for frame in range(COUNT):
        t = frame / (COUNT - 1)
        growth = 1 - (1 - t) ** 3
        grow_x = (.63 + .45 * growth) if not impact else (.44 + .66 * smooth(t / .64))
        grow_y = (.67 + .40 * growth) if not rising else (.58 + .49 * growth)
        offset_x = -13 + 24 * growth if sweeping else 0
        offset_y = (9 - 19 * t) if rising else (5 - 7 * growth) if impact else 0
        wave = np.sin(col * .061 - t * 8) * (1.8 if rising or sweeping else .8) * t
        x = ((col-centre-offset_x)/grow_x+centre).astype(np.float32)
        y = ((row-centre-offset_y)/grow_y+centre+wave).astype(np.float32)
        shape = cv2.remap(paint, x, y, cv2.INTER_LINEAR)
        if sweeping or impact:
            trail = cv2.remap(paint, x + (10 if sweeping else 0), y - (6 if impact else 1), cv2.INTER_LINEAR)
            trail *= .22 * smooth(t / .22) * (1 - smooth((t-.54)/.46))
            shape = shape + trail*(1 - shape[:, :, 3:] / 255)
        onset = (.30 + .70 * smooth(t / (.24 if impact else .34)))
        tail = .018 + .982 * (1 - smooth((t - (.44 if impact else .55)) / (.56 if impact else .45)))
        yield straight(shape * onset * tail)


def source_keys(path, cols, rows):
    with Image.open(path) as sheet:
        if "A" not in sheet.getbands():
            raise ValueError(f"{path.name} needs a transparent background")
        return split(sheet.convert("RGBA"), cols, rows)


def war_cry():
    keys = [fit(cel, 124, crop=False) for cel in source_keys(SOURCES/"WarCry.png", 3, 2)]
    frames = sequence(keys)
    for i, cel in enumerate(frames):
        t = i / (COUNT-1)
        opacity = (.48 + .52 * smooth(t/.23)) * (.045 + .955 * (1 - smooth((t-.72)/.28)))
        cel.putalpha(cel.getchannel("A").point([round(a*opacity) for a in range(256)]))
    return frames


def venom_surge():
    keys = [fit(cel, 124, crop=False) for cel in source_keys(SOURCES/"VenomSurgeKeys.png", 2, 2)]
    for i, cel in enumerate(sequence(keys)):
        fade = 1 - smooth((i / (COUNT - 1) - .66) / .34)
        cel.putalpha(cel.getchannel("A").point([round(alpha * fade) for alpha in range(256)]))
        padded = Image.new("RGBA", cel.size)
        padded.paste(cel.crop((2, 2, SIZE - 2, SIZE - 2)), (2, 2))
        yield padded


def save(skill, frames):
    filename = DISPLAY_NAMES.get(skill)
    path = OUTPUT/filename if filename else next(OUTPUT.glob(f"{skill:02d}-*.png"))
    pack(list(frames), COUNT).save(path, optimize=True)
    meta = path.with_suffix(".png.meta")
    meta.write_text(meta.read_text().replace("filterMode: 0", "filterMode: 1"))


def main():
    save(1, war_cry())
    save(38, venom_surge())
    for name, skills in PACKS.items():
        keys = source_keys(SOURCES/f"{name}.png", 4, 4)
        for skill, key in zip(skills, keys):
            save(skill, animate(fit(key, 108, crop=True), skill))


if __name__ == "__main__":
    main()
