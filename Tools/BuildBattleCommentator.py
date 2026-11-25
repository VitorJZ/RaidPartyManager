"""Bake the eight upstage commentary loops from the empty-handed expression keys."""
from pathlib import Path

import cv2
import numpy as np
from PIL import Image

from AnimationCels import ART, grid, motion, pack, premultiplied, save, split, straight

SOURCE = ART.parents[2] / "Docs/References/BattleCommentator"
DEST = ART / "DockPlayhouse/Resources/BattleCommentator"
SETS = {"Observe": ("Watch", "Explain"), "Cheer": ("Cheer", "Surprise"),
        "Warn": ("Warn", "Wince"), "Relief": ("Relief", "Rally")}
W, H = 186, 252


def align(images):
    poses = []
    marks = []
    for image in images:
        rgba = np.array(image.convert("RGBA"))
        # Suppress the low-opacity generation matte. Keep the connected hair and fingertips.
        mask = (rgba[:, :, 3] > 93).astype(np.uint8)
        count, labels, stats, _ = cv2.connectedComponentsWithStats(mask)
        body = 1 + np.argmax(stats[1:count, cv2.CC_STAT_AREA])
        near = cv2.dilate((labels == body).astype(np.uint8), np.ones((7, 7), np.uint8))
        a = np.clip((rgba[:, :, 3].astype(float)-36)*255/215, 0, 255) * near
        rgba[:, :, 3] = a.astype(np.uint8)
        ys, xs = np.where(labels == body)
        floor, crown = int(ys.max()), int(ys.min())
        height = floor - crown
        shins = xs[(ys > floor - height * .14) & (ys < floor - height * .055)]
        hips = xs[(ys > crown + height * .52) & (ys < crown + height * .58)]
        axis = .55 * np.median(shins) + .45 * np.median(hips)
        marks.append((float(axis), floor, height))
        poses.append(Image.fromarray(rgba))
    scale = (H - 12) / max(tall for _, _, tall in marks)
    aligned = []
    for image, (axis, floor, _) in zip(poses, marks):
        image = image.resize(tuple(round(n*scale) for n in image.size), Image.Resampling.LANCZOS)
        cel = Image.new("RGBA", (W, H))
        cel.paste(image, (round(W*.50 - axis*scale), round(H - 5 - floor*scale)))
        aligned.append(cel)
    return aligned


def bake(keys):
    frames = []
    for i, key in enumerate(keys):
        a, b = premultiplied(key), premultiplied(keys[(i+1) % len(keys)])
        forward, backward = motion(a, b)
        xy = grid(W, H)
        for sub in range(4):
            t = sub / 4
            if sub == 0:
                frames.append(key)
                continue
            # Reproject the nearer key so a fast pointing finger never dissolves into two hands.
            image, uv = (a, xy - t * forward) if sub < 3 else (b, xy - (1-t) * backward)
            frames.append(straight(cv2.remap(image, uv, None, cv2.INTER_LINEAR)))
    return pack(frames, 4)


if __name__ == "__main__":
    DEST.mkdir(parents=True, exist_ok=True)
    template = ART / "DockPlayhouse/Resources/StoryEvents/Cast/Ambush.png.meta"
    for source, moods in SETS.items():
        with Image.open(SOURCE / (source + ".png")) as sheet:
            keys = split(sheet, 4, 2)
        for row, mood in enumerate(moods):
            save(bake(align(keys[4*row:4*row+4])), DEST / (mood + ".png"), template)
