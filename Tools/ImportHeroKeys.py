"""Fit ImageGen hero keyframe sheets to an existing hero's atlas cells, then bake the runtime atlases.

The ImageGen sheets follow the design reference layout: an 8x4 main sheet (idle, attack, hit in the first four
cells, death) and an optional 3x2 walk. Each key keeps its position inside its generated cell. The whole set is
scaled and shifted once, so the first idle key matches the height, foot line and centre of the hero's current
idle cel. The fitted keys are saved in the original-source layout under Docs/References/HeroKeys, and
Tools/AnimationCels.py bakes them into Combat (16x4), Attack (4x4 wide) and Walk (4x4) atlases.

Run from the Unity project: python3 Tools/ImportHeroKeys.py Hero /path/Hero-Combat.png [/path/Hero-Walk.png]
Without a walk sheet the hero is carried by its idle keys, as for heroes who ride a wave.
"""
import argparse
import sys
from pathlib import Path

import cv2
import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
import AnimationCels as anim

ROOT = Path(__file__).resolve().parents[1]
SPRITES = anim.ART / "CombatSprites"
KEYS = ROOT / "Docs/References/HeroKeys"
# ImageGen paints faint noise into transparent areas and leaves bodies a few steps short of opaque.
NOISE, SOLID = 14, 240


def clean(cel):
    pixels = np.asarray(cel.convert("RGBA")).astype(np.float32)
    alpha = np.clip((pixels[:, :, 3] - NOISE) * 255 / (SOLID - NOISE), 0, 255)
    # A weapon reaching across the grid leaves a sliver in the neighbouring cell; drop small edge islands.
    count, labels, stats, _ = cv2.connectedComponentsWithStats((alpha > 0).astype(np.uint8), 8)
    if count > 2:
        largest = stats[1:, cv2.CC_STAT_AREA].max()
        h, w = alpha.shape
        for i in range(1, count):
            x, y, bw, bh, area = stats[i]
            if area < largest*.06 and (x == 0 or y == 0 or x+bw == w or y+bh == h):
                alpha[labels == i] = 0
    pixels[:, :, 3] = alpha
    pixels[alpha == 0] = 0
    return Image.fromarray(np.rint(pixels).astype(np.uint8))


def bounds(cel):
    box = cel.getchannel("A").point(lambda v: 255 if v > 40 else 0).getbbox()
    if box is None:
        raise SystemExit("The first idle key is empty")
    return box


def fit(cels, source_box, target_box, size, scale):
    """Place generated cels on target canvases with one shared scale and offset."""
    fitted = []
    for cel in cels:
        scaled = cel.resize((round(cel.width*scale), round(cel.height*scale)), Image.Resampling.LANCZOS)
        canvas = Image.new("RGBA", size)
        # Idle key 0: centre and foot of the generated figure land on the current figure's centre and foot.
        x = (target_box[0]+target_box[2])/2 - (source_box[0]+source_box[2])/2*scale
        y = target_box[3] - source_box[3]*scale
        # Wider attack cels keep the figure at the same offset from the cel centre.
        x += (size[0]-REFERENCE_WIDTH)/2
        canvas.alpha_composite(scaled, (round(x), round(y)))
        fitted.append(canvas)
    return fitted


def cells(path, cols, rows):
    with Image.open(path) as im:
        return [clean(c) for c in anim.split(im.convert("RGBA"), cols, rows)]


def main():
    global REFERENCE_WIDTH
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("hero")
    parser.add_argument("combat", type=Path)
    parser.add_argument("walk", type=Path, nargs="?")
    args = parser.parse_args()

    with Image.open(SPRITES/f"{args.hero}-Combat.png") as current:
        idle = anim.split(current.convert("RGBA"), anim.COUNT, 4)[0]
    with Image.open(SPRITES/f"{args.hero}-Attack.png") as current:
        attack_size = anim.split(current, 4, 4)[0].size
    with Image.open(SPRITES/f"{args.hero}-Walk.png") as current:
        walk_size = anim.split(current, 4, 4)[0].size
    size = idle.size
    REFERENCE_WIDTH = size[0]
    target = bounds(idle)

    keys = cells(args.combat, 8, 4)
    hit = keys[16:24]
    for i in range(4, 8):
        # The hit reaction uses four keys; anything left in the spare cells is generation noise.
        hit[i] = Image.new("RGBA", hit[i].size)
    keys[16:24] = hit
    generated = bounds(keys[0])
    scale = (target[3]-target[1]) / (generated[3]-generated[1])
    combat = fit(keys, generated, target, size, scale)
    attack = fit(keys[8:16], generated, target, attack_size, scale)

    if args.walk:
        walk_keys = cells(args.walk, 3, 2)
        stride = bounds(walk_keys[0])
        walk_scale = (target[3]-target[1]) / (stride[3]-stride[1])
        walk = fit(walk_keys, stride, target, walk_size, walk_scale)
    else:
        walk = fit(keys[:6], generated, target, walk_size, scale)

    folder = KEYS/"CombatSprites"
    folder.mkdir(parents=True, exist_ok=True)
    outputs = {"Combat": (combat, 8), "Attack": (attack, 4), "Walk": (walk, 3)}
    for kind, (frames, cols) in outputs.items():
        path = folder/f"{args.hero}-{kind}.png"
        anim.pack(frames, cols).save(path, compress_level=6)
        anim.convert(path, KEYS)
        print("baked", SPRITES/path.name)


if __name__ == "__main__":
    main()
