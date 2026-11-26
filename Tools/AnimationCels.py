"""Bake 16-cel animation atlases from an immutable directory of original keyframes.

Run from the Unity project with: python3 Tools/AnimationCels.py /path/to/original/Art
Add --characters to rebake only the combat, attack, special and walk atlases of heroes and enemies.
Requires Pillow, NumPy and OpenCV. The original directory must be separate from the output.
"""
import argparse
import re
import uuid
from concurrent.futures import ThreadPoolExecutor
from functools import lru_cache
from pathlib import Path

import cv2
import numpy as np
from PIL import Image

ART = Path(__file__).resolve().parents[1] / "Assets/RaidPartyManager/Art"
COUNT = 16
cv2.setNumThreads(1)


def split(sheet, cols, rows):
    w, h = sheet.size
    return [sheet.crop((x*w//cols, y*h//rows, (x+1)*w//cols, (y+1)*h//rows))
            for y in range(rows) for x in range(cols)]


def pack(cels, cols):
    w, h = cels[0].size
    sheet = Image.new("RGBA", (cols*w, len(cels)//cols*h))
    for i, cel in enumerate(cels):
        sheet.paste(cel, (i%cols*w, i//cols*h))
    return sheet


@lru_cache(maxsize=48)
def grid(w, h):
    return np.stack(np.meshgrid(np.arange(w, dtype=np.float32),
                                np.arange(h, dtype=np.float32)), axis=-1)


def premultiplied(image):
    pixels = np.asarray(image.convert("RGBA")).astype(np.float32)
    pixels[:, :, :3] *= pixels[:, :, 3:] / 255
    return pixels


def straight(pixels):
    alpha = pixels[:, :, 3:]
    pixels[:, :, :3] *= 255 / np.maximum(alpha, 1)
    pixels[alpha[:, :, 0] < 2] = 0
    return Image.fromarray(np.rint(pixels).clip(0, 255).astype(np.uint8))


def motion(a, b):
    # Estimate displacement on a neutral background so transparent fringes carry no weight.
    h, w = a.shape[:2]
    size = (min(w, 256), max(12, round(h*min(1, 256/w))))
    def gray(p):
        rgb = p[:, :, :3] + (255-p[:, :, 3:])*.15
        return cv2.resize(cv2.cvtColor(rgb.astype(np.uint8), cv2.COLOR_RGB2GRAY), size)
    g, k = gray(a), gray(b)
    dis = cv2.DISOpticalFlow_create(cv2.DISOPTICAL_FLOW_PRESET_FAST)
    def estimate(start, end):
        f = cv2.resize(dis.calc(start, end, None), (w, h))
        f *= (w/size[0], h/size[1])
        return f
    return estimate(g, k), estimate(k, g)


def breathe(p, angle):
    h, w = p.shape[:2]
    xy = grid(w, h).copy()
    upper = np.clip((h*.97-xy[:, :, 1])/h, 0, 1)
    xy[:, :, 0] -= np.sin(angle)*w*.005*upper
    xy[:, :, 1] += (1-np.cos(angle))*h*.005*upper
    return cv2.remap(p, xy, None, cv2.INTER_LINEAR)


# A single warp may stand in for an in-between only while it reproduces the next silhouette this closely.
TWEEN_TOLERANCE = .16
# Foot-line corrections larger than this fraction of the cel are deliberate hops, hovers or falls.
GROUND_TOLERANCE = .025


def foot_line(p):
    rows = np.nonzero(p[:, :, 3].max(1) > 40)[0]
    return int(rows.max()) if len(rows) else None


def shifted(p, dx, dy):
    h, w = p.shape[:2]
    return cv2.warpAffine(p, np.float32([[1, 0, dx], [0, 1, dy]]), (w, h), flags=cv2.INTER_LINEAR)


def stance_offset(reference, p):
    """Horizontal drift of the planted lower body relative to the reference key."""
    h, w = reference.shape[:2]
    foot = foot_line(reference)
    if foot is None or foot_line(p) is None:
        return 0.
    band = slice(max(0, int(foot-h*.30)), foot+1)
    a = np.ascontiguousarray(reference[band, :, 3])
    b = np.ascontiguousarray(p[band, :, 3])
    (dx, _), response = cv2.phaseCorrelate(b, a)
    return dx if response > .08 and abs(dx) < w*.08 else 0.


def settle(keys, align, ground):
    """Put generated keys on one foot line, and keep an idle stance from sliding sideways."""
    h = keys[0].shape[0]
    base = ground if ground is not None else foot_line(keys[0])
    feet = [foot_line(k) for k in keys]
    planted = base is not None and None not in feet and \
        max(abs(f-base) for f in feet) <= max(2, h*GROUND_TOLERANCE)
    result = []
    for i, key in enumerate(keys):
        dy = base-feet[i] if planted else 0
        dx = stance_offset(keys[0], key) if align and i else 0.
        result.append(shifted(key, dx, dy) if dx or dy else key)
    return result


def tween(a, b):
    """Optical flow both ways, plus how far a one-sided warp of a misses b's silhouette."""
    forward, backward = motion(a, b)
    h, w = a.shape[:2]
    reached = cv2.remap(a, grid(w, h)-forward, None, cv2.INTER_LINEAR)[:, :, 3] > 60
    target = b[:, :, 3] > 60
    miss = np.logical_xor(reached, target).sum()/max(1, target.sum())
    return forward, backward, miss


def sequence(keys, loop=False, secondary=False, align=False, ground=None):
    """Spread keys over COUNT cels without ever blending two poses into one cel.

    Cel i shows key floor(i*n/COUNT), matching the runtime's per-key timing tables. The cels between
    two keys either warp the nearer key alone, when its flow reproduces the other silhouette, or hold
    the key so a large change of pose lands as a crisp snap instead of a cross-faded double image.
    """
    keys = settle([premultiplied(k) for k in keys], align, ground)
    h, w = keys[0].shape[:2]
    flows = {}
    cels = []
    for i in range(COUNT):
        at = i*len(keys)/COUNT
        index = int(at)
        t = at-index
        following = (index+1) % len(keys) if loop else min(index+1, len(keys)-1)
        a, b = keys[index], keys[following]
        if t == 0 or following == index or np.array_equal(a, b):
            p = a.copy()
        else:
            if index not in flows:
                flows[index] = tween(a, b)
            forward, backward, miss = flows[index]
            xy = grid(w, h)
            if miss > TWEEN_TOLERANCE:
                p = a.copy()
            elif t <= .5:
                p = cv2.remap(a, xy-forward*t, None, cv2.INTER_LINEAR)
            else:
                p = cv2.remap(b, xy-backward*(1-t), None, cv2.INTER_LINEAR)
        if secondary:
            p = breathe(p, 2*np.pi*i/COUNT)
        cels.append(straight(p))
    return cels


def save(sheet, path, template=None):
    sheet.save(path, compress_level=4)
    meta = Path(str(path)+".meta")
    if not meta.exists():
        new_meta(meta, template)
    s = meta.read_text()
    limit = max(2048, 2**(max(sheet.size)-1).bit_length())
    s = re.sub(r'maxTextureSize: (\d+)', lambda m: 'maxTextureSize: '+str(max(int(m[1]), limit)), s)
    if s != meta.read_text():
        meta.write_text(s)


def new_meta(meta, template):
    guid = uuid.uuid5(uuid.NAMESPACE_URL, 'RaidPartyManager/Art/'+str(meta.relative_to(ART))).hex
    meta.write_text(re.sub(r'(?m)^guid: \w+', 'guid: '+guid, template.read_text()))


def convert(path, source):
    target = ART/path.relative_to(source)
    name = path.stem
    with Image.open(path) as im:
        image = im.convert("RGBA")
    if "WhisperingGraveyard" in path.parts and name.endswith("-Walk"):
        from BuildWhisperingGraveyard import pose_frames
        source_grid = path.parent / "Sources" / (name[:-len("-Walk")] + "-Source.png")
        with Image.open(source_grid) as original:
            walk = pose_frames(original)[1]
        save(pack(sequence(walk, True, True), 4), target)
        return
    if name.endswith(("-Combat", "-Expanded")):
        cells = split(image, 8, 4)
        poses = []
        # Every row stands on the idle foot line; the idle stance is also held in place sideways.
        ground = foot_line(premultiplied(cells[0]))
        for row in range(4):
            keys = cells[row*8:(row+1)*8]
            if row == 2 and not keys[-1].getchannel("A").getbbox():
                keys = keys[:4]
            poses.extend(sequence(keys, row == 0, row == 0, align=row == 0, ground=ground))
        save(pack(poses, COUNT), target)
        return
    if name.endswith(("SpecialFX", "FinaleFX", "TrueStrikeFX")):
        # The cemetery FX generator renders its particle paths natively at 16 samples.
        if 'WhisperingGraveyard' in path.parts:
            return
        save(pack(sequence(split(image, 8, 1)), COUNT), target)
        return
    if path.parent.name == "Seated":
        key = image.resize((448, 448), Image.Resampling.LANCZOS)
        save(pack(sequence([key], True, True), 4), target)
        return
    if name == "BrokerPortraits":
        # Four independently looping expressions, each occupies two rows in the 8x8 atlas.
        poses = []
        for face in split(image, 2, 2):
            face = face.resize((448, 490), Image.Resampling.LANCZOS)
            poses.extend(sequence([face], True, True))
        save(pack(poses, 8), target)
        return
    if name == "BrokerIdle":
        keys = [image.crop((165+(i%3)*415,(i//3)*512,575+(i%3)*415,(i//3+1)*512)) for i in range(6)]
        loop, secondary = True, True
    elif name.endswith(("-Attack", "-Special")):
        keys, loop, secondary = split(image, 4, 2), False, False
    elif (name.endswith("Walk") and ("Enemies" in path.parts or "CombatSprites" in path.parts)) or "RecruitmentFront" in path.parts:
        keys, loop, secondary = split(image, 3, 2), True, True
    elif name in ("IronCase", "HerbalCase", "RattleCase"):
        keys, loop, secondary = split(image, 2, 1), False, False
    elif path.parent.name == "StoryEvents" and name != "FoundCoins":
        keys, loop, secondary = split(image, 3, 1), False, False
    elif name == 'GraveChronicler':
        keys, loop, secondary = split(image, 4, 1), False, False
    else:
        keys = split(image, 4, 1)
        loop, secondary = True, True
    save(pack(sequence(keys, loop, secondary), 4), target)


def add_missing_walks(source):
    sheets = {}
    for p in source.rglob("*.png"):
        if p.stem.endswith(("-Combat", "-Expanded")):
            guid = re.search(r'guid: (\w+)', Path(str(p)+".meta").read_text())[1]
            sheets[guid] = p
    for asset in (ART.parent/"PrototypeData").rglob("*.asset"):
        s = asset.read_text()
        match = re.search(r'(?m)^  battleSpriteSheet: .*?guid: (\w+).*$', s)
        if not match or match[1] not in sheets:
            continue
        sheet = sheets[match[1]]
        file = ART/sheet.parent.relative_to(source)/(sheet.stem.rsplit("-", 1)[0]+"-Walk.png")
        walk = re.search(r'(?m)^  battleWalkSheet: .*?guid: (\w+)', s)
        meta = Path(str(file)+".meta")
        if walk and (not meta.exists() or "guid: "+walk[1] not in meta.read_text()):
            continue
        if (source/file.relative_to(ART)).exists():
            continue
        # Preserve the stage-facing silhouette and use the combat idle keys for the upper body.
        poses = sequence(split(Image.open(sheet), 8, 4)[:8], True)
        role = int(re.search(r'(?m)^  profession: (\d+)', s)[1])
        stride, lift = {1:(.078,.052), 2:(.105,.074), 3:(.068,.048)}.get(role,(.084,.062))
        direction = -1 if "Enemies" in sheet.parts else 1
        frames = []
        for i, pose in enumerate(poses):
            w,h = pose.size
            xy = grid(w,h).copy()
            knee = np.clip((xy[:,:,1]/h-.53)/.38, 0, 1)
            knee = knee*knee*(3-2*knee)
            lane = xy[:,:,0]/w if direction == 1 else 1-xy[:,:,0]/w
            leg = np.clip((lane-.35)/.27, 0, 1)
            leg = 2*leg*leg*(3-2*leg)-1
            phase = 2*np.pi*i/COUNT
            xy[:,:,0] -= direction*w*leg*np.cos(phase)*stride*knee
            xy[:,:,1] += h*(np.maximum(0,leg*np.sin(phase))*lift*knee+.025*abs(np.sin(phase))*(1-knee))
            frames.append(straight(cv2.remap(premultiplied(pose),xy,None,cv2.INTER_LINEAR)))
        save(pack(frames,4),file,Path(str(sheet)+".meta"))
        guid = re.search(r'guid: (\w+)', Path(str(file)+".meta").read_text())[1]
        line = "  battleWalkSheet: {fileID: 2800000, guid: "+guid+", type: 3}"
        if "  battleWalkSheet:" in s:
            new = re.sub(r'(?m)^  battleWalkSheet:.*$',line,s)
        else:
            new = s[:match.end()]+"\n"+line+s[match.end():]
        if s != new:
            asset.write_text(new)


def flight_cels(image, kind):
    """Light, rotate and flex the painted projectile in local coordinates. The tip stays on its flight path."""
    image.thumbnail((288, 288), Image.Resampling.LANCZOS)
    rest = premultiplied(image)
    h, w = rest.shape[:2]
    xy = grid(w, h)
    u, v = xy[:, :, 0] / max(1, w-1), xy[:, :, 1] / max(1, h-1)
    cels = []
    for index in range(COUNT):
        angle = 2 * np.pi * index / COUNT
        if kind == "shaft":
            sample = xy.copy()
            sample[:, :, 1] += (1-u)**2 * (h*.048*np.sin(angle+u*3.0))
            pixels = cv2.remap(rest, sample, None, cv2.INTER_LINEAR)
            band = np.exp(-((u+.10*(v-.5) - (.04+.90*index/(COUNT-1))) / .095)**2)
            tint = (29, 26, 19)
        else:
            if kind == "rock":
                rotation, zoom, tint = 4.0, .990, (45, 21, 53)
            elif kind == "shard":
                rotation, zoom, tint = 12.0, .970, (52, 23, 58)
            else:
                rotation, zoom, tint = 10.0, .955, (33, 46, 54)
            matrix = cv2.getRotationMatrix2D(((w-1)*.5, (h-1)*.5), rotation*np.sin(angle),
                                               zoom + (1-zoom)*np.cos(angle))
            pixels = cv2.warpAffine(rest, matrix, (w, h), flags=cv2.INTER_LINEAR)
            band = (.5+.5*np.cos(angle + 6*u - 3*v))**4
        # Illuminate existing paint through its alpha; the surrounding transparent space stays clear.
        alpha = pixels[:, :, 3:4] / 255
        luminance = pixels[:, :, :3].mean(axis=2, keepdims=True) / np.maximum(alpha*255, 1)
        pixels[:, :, :3] += alpha * (.25 + .75*luminance) * band[:, :, None] * tint
        cels.append(straight(pixels))
    return cels


def projectiles(source):
    groups = {
        "shaft": ["CombatSprites/Arrow", "CombatSprites/TianGong-PiercingArrow",
                  "Enemies/ThreadRifleman-Projectile", "Enemies/ForkFisher-Projectile",
                  "Enemies/DeckSailor-Projectile", "Enemies/BlockadeAdmiral-Projectile",
                  "SkillEffects/07-VolleyProjectile"],
        "spark": ["CombatSprites/MagicOrb", "CombatSprites/TianGong-ArrowParticle"],
        "rock": ["SkillEffects/08-MeteorBoulder"],
    }
    for kind, paths in groups.items():
        for name in paths:
            path = Path(name + ".png")
            with Image.open(source/path) as original:
                save(pack(flight_cels(original.convert("RGBA"), kind), 4), ART/path)
    path = Path("SkillEffects/08-MeteorFragments.png")
    cels = []
    with Image.open(source/path) as original:
        for shard in split(original.convert("RGBA"), 2, 2):
            shard.thumbnail((160, 160), Image.Resampling.LANCZOS)
            cels.extend(flight_cels(shard, "shard"))
    save(pack(cels, 8), ART/path)


def scenery(path, source):
    # Painted backcloth cels differ in atmosphere and light; preserve their full resolution.
    fmt = path.parent.parent
    frames = [Image.open(fmt/str(i)/path.name).convert("RGB") for i in range(6)]
    for i in range(COUNT):
        at = i*6/COUNT
        folder = ART/fmt.relative_to(source)/str(i)
        out = folder/path.name
        if at.is_integer():
            out.write_bytes((fmt/str(int(at))/path.name).read_bytes())
        else:
            a = int(at)
            Image.blend(frames[a], frames[(a+1)%6], at-a).save(out, quality=91)
        meta = Path(str(out)+".meta")
        if not meta.exists():
            new_meta(meta, Path(str(path)+".meta"))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("--characters", action="store_true",
                        help="Only rebake hero and enemy combat, attack, special and walk atlases")
    args = parser.parse_args()
    src = args.source.resolve()
    if src == ART or not (src/"CombatSprites/CampGuard-Expanded.png").exists():
        parser.error("Pass a separate original Art directory.")
    with Image.open(src/"CombatSprites/CampGuard-Expanded.png") as original:
        if original.size != (1792, 896):
            parser.error("The source must contain the original eight-column combat atlases.")
    files = set()
    for folder in [src/"CombatSprites", src/"Enemies",src/"Enemies/WhisperingGraveyard"]:
        files.update(p for p in folder.glob("*.png") if p.stem.endswith(("-Combat","-Expanded","-Attack","-Special","Walk","FX")))
    if args.characters:
        # The cemetery cast is rebuilt from its own pose sources by Tools/BuildWhisperingGraveyard.py.
        actors = sorted(p for p in files if not p.stem.endswith("FX") and "WhisperingGraveyard" not in p.parts
                        and (ART/p.relative_to(src)).exists())
        with ThreadPoolExecutor(max_workers=5) as pool:
            list(pool.map(lambda p: convert(p, src), actors))
        add_missing_walks(src)
        print(f"Rebaked {len(actors)} character atlases")
        return
    files.update((src/"Shop/Resources/RecruitmentFront").glob("*.png"))
    files.update((src/"Camp/Seated").glob("*.png"))
    story = src/"DockPlayhouse/Resources/StoryEvents"
    files.update((story/"Cast").glob("*.png"))
    files.update(p for p in story.glob("*.png") if Image.open(p).size in [(1152,384),(896,448)] or p.stem == "FoundCoins")
    files.update((src/"ServiceStages").glob("*-Motion.png"))
    files.add(src/"ServiceStages/PaperHearth-Cels.png")
    # Create shared frame directories and Unity GUIDs before parallel image work.
    backdrops = list((src/"DockPlayhouse/Resources/SceneryCels").glob("*/0/*.jpg"))
    for fmt in {p.parent.parent for p in backdrops}:
        for i in range(6,COUNT):
            folder=ART/fmt.relative_to(src)/str(i)
            folder.mkdir(exist_ok=True)
            meta=Path(str(folder)+".meta")
            if not meta.exists(): new_meta(meta, Path(str(fmt/"0")+".meta"))
    with ThreadPoolExecutor(max_workers=5) as pool:
        list(pool.map(lambda p: convert(p, src), sorted(files)))
        list(pool.map(lambda p: scenery(p, src), backdrops))
    add_missing_walks(src)
    projectiles(src)
    event = ART/"DockPlayhouse/Resources/StoryEvents/ChestOpening.png"
    chest = src/"Events"
    keys = [Image.open(chest/("HarbourChest-"+state+".png")) for state in ("Closed","Ajar","Open")]
    save(pack(sequence(keys),4),event,Path(str(story/"Auction.png")+".meta"))


if __name__ == "__main__":
    main()
