"""Author the twelve-frame, transparent combat VFX strips shown in Docs/SkillEffects.md."""

import argparse
from math import cos, pi, sin
from pathlib import Path
from random import Random

from PIL import Image, ImageDraw, ImageFont


CELL = 96
FRAMES = 12
ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / "Assets/RaidPartyManager/Art/SkillEffects"
PREVIEW = ROOT / "Docs/Previews/SkillEffects.gif"

# Shadow, saturated middle tone, highlight. The contour stays legible over bright arenas.
STYLES = (
    ("WarCry",         "#81331d", "#fd771f", "#ffe0a2"),
    ("BlazingFire",    "#742120", "#ff7229", "#fff1aa"),
    ("AbsoluteZero",  "#17456e", "#5abce7", "#dcfeff"),
    ("Sunshine",      "#86521b", "#ffc746", "#fff5bd"),
    ("StoneGuard",    "#41303a", "#af826b", "#f5c88e"),
    ("HealingHands",  "#18574b", "#56d5a1", "#dcffce"),
    ("ArrowVolley",   "#3a4752", "#a8c3bb", "#fff0b3"),
    ("Meteor",        "#50256e", "#a986ff", "#fce7ff"),
    ("ArmySweep",     "#4c3978", "#92a6ec", "#e7fdff"),
    ("LightningWhip", "#33347b", "#7a91ff", "#f2e4ff"),
    ("ToxicMist",     "#304421", "#a6d650", "#edff9b"),
    ("FleshFeast",    "#541430", "#cf4172", "#ffc5b7"),
    ("Sidestep",      "#1b5269", "#4fe1c8", "#fff2ae"),
    ("GoldenBell",    "#78481c", "#edaa3b", "#fff3ad"),
    ("FieryEyes",     "#811a27", "#ff654a", "#fff4a2"),
    ("CrimsonReap",   "#5f1739", "#f3496b", "#ffe4c8"),
    ("HeroicAnthem",  "#4c3b88", "#b5a4ff", "#fff4b6"),
    ("Intimidate",    "#4c335f", "#c583f3", "#ffe0a1"),
    ("Earthquake",    "#4a2a38", "#dc8242", "#ffe0a0"),
    ("CloudPiercer",  "#1b596d", "#65ddf5", "#f4ffff"),
)


class Frame:
    def __init__(self, palette):
        self.image = Image.new("RGBA", (CELL, CELL))
        self.draw = ImageDraw.Draw(self.image)
        self.shadow, self.mid, self.light = palette

    def line(self, points, width=2):
        self.draw.line(points, self.shadow, width + 4, joint="curve")
        self.draw.line(points, self.mid, width + 1, joint="curve")
        self.draw.line(points, self.light, max(1, width - 1), joint="curve")

    def arc(self, x, y, rx, ry, start=0, end=360, width=2):
        if min(rx, ry) < 1:
            return
        bounds = (x - rx, y - ry, x + rx, y + ry)
        self.draw.arc(bounds, start, end, self.shadow, width + 3)
        self.draw.arc(bounds, start, end, self.mid, width + 1)
        self.draw.arc(bounds, start, end, self.light, width)

    def gem(self, x, y, rx, ry):
        self.draw.polygon([(x, y-ry), (x+rx, y), (x, y+ry), (x-rx, y)], fill=self.mid)
        self.draw.polygon([(x, y-ry), (x, y+ry), (x-rx, y)], fill=self.light)

    def star(self, x, y, size):
        self.draw.line((x-size, y, x+size, y), self.mid, 1)
        self.draw.line((x, y-size, x, y+size), self.mid, 1)
        self.draw.rectangle((x-1, y-1, x+1, y+1), self.light)

    def sparks(self, rng, t, count=15, rise=0):
        for _ in range(count):
            angle = rng.uniform(0, 2*pi)
            distance = rng.uniform(13, 41) * (0.45 + 0.55*t)
            x = 48 + cos(angle) * distance
            y = 49 + sin(angle) * distance - rise*t
            size = rng.choice((1, 2, 2, 3))
            if rng.random() > t * 0.64:
                self.star(x, y, size + t)


def arrow(f, start, tip, wing=5):
    x, y = tip
    f.line((start, tip), 2)
    f.draw.polygon([(x+4, y-2), (x-wing, y+1), (x-1, y+wing), (x+1, y+3)], fill=f.light)
    f.draw.line((start, (start[0]+6, start[1]+2)), f.mid, 2)


def war_cry(f, t, _):
    f.arc(48, 51, 14+29*t, 10+19*t, 10, 350, 3)
    for side in (-1, 1):
        f.line([(48+side*5, 64), (48+side*14, 48), (48+side*17, 30),
                (48+side*(24+6*t), 20)], 4)
        f.gem(48+side*(26+7*t), 36, 3, 7)
    f.gem(48, 46-8*t, 4, 11)


def fire(f, t, rng):
    f.arc(48, 71, 16+22*t, 5+3*t, 5, 175, 3)
    for offset in (-21, -12, 0, 11, 20):
        x = 48+offset
        reach = (22+20*sin(pi*t)) * rng.uniform(.75, 1.25)
        drift = 9*sin(t*9+offset)
        y = 70-reach
        f.draw.polygon([(x-10, 72), (x-12, 53), (x+drift, y), (x+3, y+15),
                        (x+11, 48), (x+10, 70)], fill=f.shadow)
        f.draw.polygon([(x-6, 70), (x-8, 58), (x+drift, y+3), (x+1, y+18),
                        (x+9, 60), (x+6, 68)], fill=f.mid)
        f.draw.polygon([(x-4, 68), (x-2, 56), (x+1, y+14), (x+4, 60), (x+2, 70)],
                       fill=f.light)


def ice(f, t, _):
    for x, height, slope in ((24, 24, -7), (36, 42, -8), (49, 54, 4),
                             (61, 36, 7), (73, 22, 7)):
        y = 72-height*(0.3+0.7*sin(pi*min(.5, t+.1)))
        f.draw.polygon([(x-9, 75), (x+slope, y), (x+9, 70)], fill=f.shadow)
        f.draw.polygon([(x-5, 72), (x+slope, y), (x+6, 68)], fill=f.mid)
        f.draw.polygon([(x-5, 72), (x+slope, y), (x+1, 66)], fill=f.light)
    f.arc(48, 71, 13+24*t, 6+3*t, 185, 355)
    f.star(49, 23, 7)


def sunshine(f, t, _):
    ring_y = 27 + 8*t
    f.arc(48, ring_y, 14+8*t, 12+8*t, width=3)
    f.gem(48, ring_y, 8, 10)
    for i in range(8):
        a = i*pi/4
        f.line([(48+cos(a)*22, ring_y+sin(a)*21), (48+cos(a)*29, ring_y+sin(a)*27)], 2)
    for x in (30, 43, 54, 66):
        f.line([(x, 50), (x+(x-48)*.30, 60+19*t)], 1)
    f.arc(48, 72, 16+21*t, 5+5*t)


def stone_guard(f, t, _):
    for row in range(3):
        for col in range(3):
            x = 23 + col*17 + (row % 2)*5
            y = 26 + row*15 - 8*(1-t)
            f.draw.polygon([(x-2,y+3),(x+3,y-2),(x+15,y),(x+17,y+11),(x,y+12)], fill=f.shadow)
            f.draw.polygon([(x,y+3),(x+4,y),(x+13,y+2),(x+15,y+9),(x+2,y+10)], fill=f.mid)
            f.draw.line((x+4,y+1,x+13,y+2), f.light, 2)
    f.arc(48, 65, 21+12*t, 9+4*t, 0, 190)
    f.line([(52, 25), (43, 39), (51, 47), (39, 60)], 1)


def healing(f, t, _):
    y = 56-19*t
    f.line([(36, y), (61, y)], 4)
    f.line([(48, y-13), (48, y+14)], 4)
    f.arc(48, 58, 14+24*t, 8+7*t, 10, 350)
    for side in (-1, 1):
        f.line([(48, 67), (48+side*14, 57), (48+side*23, 45), (48+side*14, 46)], 2)
        f.gem(48+side*(12+16*t), 38+sin(t*pi)*14, 4, 7)


def volley(f, t, _):
    for i in range(5):
        x = 10+46*t+(i%3)*9
        y = 60-21*t+(i//3)*16-(i%3)*12
        arrow(f, (x-10, y+6), (x+13, y-6), 4)
    f.arc(72, 46, 9+10*t, 21-7*t, 274, 85)


def meteor(f, t, _):
    for offset, delay, radius in ((-17, .13, 6), (2, 0, 10), (16, .25, 7)):
        u = max(0, min(1, (t-delay)*1.55))
        x, y = 30+offset+21*u, 16+48*u
        f.line([(x-10,y-19), (x-6,y-11), (x,y)], 4)
        f.gem(x, y, radius, radius+2)
    f.arc(52, 70, 12+24*t, 7+4*t, 5, 175, 2)


def sweep(f, t, _):
    for offset, thickness in ((0,6), (-11,2), (10, 2)):
        f.arc(48, 48+offset, 28+13*t, 15+9*t, 194+65*t, 353+38*t, thickness)
    f.gem(27+49*t, 31+13*t, 7, 4)


def lightning(f, t, rng):
    flicker = .7+.3*cos(t*6*pi)
    pts = [(10, 53), (22, 34), (32, 50), (44, 30),
           (56, 49), (66, 41), (84, 62)]
    pts = [(x,y+rng.uniform(-8,8)*sin(13*t+x)) for x,y in pts]
    f.line(pts, 3+int(3*flicker))
    f.line([pts[2], (31, 62), (21, 73)], 1)
    f.line([pts[4], (66, 65), (61, 76)], 1)
    f.star(84, 61, 6+4*flicker)


def toxic(f, t, rng):
    for i in range(13):
        x = 48 + rng.uniform(-32,32)*(.48+.52*t)
        y = 58 + rng.uniform(-19,14)-12*t
        r = rng.uniform(4,10)+6*t
        f.draw.ellipse((x-r,y-r*.63,x+r,y+r*.63), fill=f.shadow)
        f.draw.arc((x-r,y-r*.63,x+r,y+r*.63), 178+i*4, 355, f.mid, 3)
        if i % 3 == 0:
            f.star(x+3,y-3,2)
    f.arc(48, 68, 21+17*t, 8, 10, 178, 2)


def feast(f, t, _):
    for i in range(3):
        a = 120*i + 85*t
        f.arc(48, 51, 37-15*t, 26-11*t, a, a+91, 4)
    for side in (-1, 1):
        x = 48+side*(19-10*t)
        f.draw.polygon([(x-5, 35), (x+5, 35), (x-side*6, 58), (x-4, 44)], fill=f.mid)
        f.draw.line((x+3, 37, x-side*6, 58), f.light, 2)
    f.gem(48, 53, 4+6*t, 7+5*t)


def sidestep(f, t, _):
    for i in range(4):
        x = 19+14*i+6*t
        f.line([(x-8, 65-i*4), (x+7, 60-i*4), (x+15, 50-i*4)], 1+i//2)
        if i < 3:
            f.line([(x+1, 35), (x+8, 42), (x+1, 49)], 2)
    f.arc(50, 66, 22+15*t, 8, 12, 151, 2)
    f.gem(66+8*t, 34, 6, 6)


def golden_bell(f, t, _):
    width = 22+8*t
    f.arc(48, 47, width, 25, 183, 357, 4)
    f.line([(48-width, 47), (48-width-5, 66), (48+width+5, 66), (48+width, 47)], 3)
    f.arc(48, 65, width+6, 8, 0, 181, 2)
    f.gem(48, 69, 4, 5)
    f.arc(48, 63, width+8, 17+5*t, 0, 160)


def fiery_eyes(f, t, _):
    f.arc(48, 42, 31, 13+3*t, 16, 165, 3)
    f.arc(48, 54, 31, 13+3*t, 196, 345, 3)
    f.gem(48, 48, 6+4*t, 12)
    d = 22+12*t
    f.line([(48-d,48),(48,48-d),(48+d,48),(48,48+d),(48-d,48)], 1)
    f.star(48, 48, 6)


def reaping(f, t, _):
    f.arc(46, 49, 35, 22+9*t, 170+60*t, 341+45*t, 7)
    f.arc(48, 51, 39, 27+9*t, 191+56*t, 295+56*t, 2)
    f.line([(69-15*t, 19), (47-7*t, 48), (28, 74)], 2)
    for i in range(3):
        f.gem(22+19*i+9*t, 68-36*t, 3, 5)


def anthem(f, t, _):
    f.arc(48, 65, 15+23*t, 7+7*t, 10, 355)
    for i, (x, y) in enumerate(((27, 54), (44, 43), (63, 53), (73, 35))):
        y -= (15-i*2)*t
        f.gem(x-3, y+5, 5, 4)
        f.line([(x, y+5),(x, y-11),(x+8,y-7)], 2)
    f.star(50, 25-5*t, 6)


def intimidate(f, t, _):
    f.arc(48, 42, 14+24*t, 8+12*t, 0, 360, 3)
    f.arc(48, 47, 11+18*t, 6+9*t, 5, 338, 2)
    for i in range(3):
        angle = 2*pi*i/3 + t*pi
        x = 48+cos(angle)*(20+9*t)
        y = 34+sin(angle)*12
        f.gem(x, y, 5, 7)
    f.line([(36, 67),(41,58),(47,66),(53,56),(59,65)], 2)


def earthquake(f, t, rng):
    f.arc(48, 70, 14+23*t, 6+5*t, width=2)
    f.line([(12, 74), (28, 66), (41, 73), (53, 59), (65, 63), (86, 48)], 4)
    f.line([(41, 73), (46, 83), (61, 82), (66, 88)], 2)
    for i in range(8):
        x = 48+rng.uniform(-30,30)*(0.6+t*.4)
        y = 67-rng.uniform(19,44)*sin(pi*t)
        f.gem(x, y, rng.uniform(2,6), rng.uniform(3,8))


def cloud_piercer(f, t, _):
    x = 18+57*min(1, t*1.22)
    y = 75-48*min(1, t*1.22)
    f.line([(x-25, y+15), (x-15, y+9), (x+3, y-1)], 5)
    arrow(f, (x-16,y+9), (x+9,y-6), 7)
    f.arc(49, 49, 12+22*t, 20+8*t, 270, 85, 2)
    f.star(x+2, y-3, 7)


DRAWINGS = (war_cry, fire, ice, sunshine, stone_guard, healing, volley, meteor, sweep, lightning,
            toxic, feast, sidestep, golden_bell, fiery_eyes, reaping, anthem, intimidate,
            earthquake, cloud_piercer)


def render_skill(number, palette, render):
    frames = []
    for i in range(FRAMES):
        t = i/(FRAMES-1)
        frame = Frame(palette)
        render(frame, t, Random(1000 + number))
        frame.sparks(Random(4000 + number), t, rise=10 if number in (2, 6, 11, 17) else 0)
        opacity = (.36 + .64*min(1, t/.20)) * min(1, (1.02-t)/.32)
        frame.image.putalpha(frame.image.getchannel("A").point(lambda alpha: int(alpha*opacity)))
        frames.append(frame.image)
    return frames


def preview(sets):
    frames = []
    font = ImageFont.load_default()
    for i in range(FRAMES):
        board = Image.new("RGBA", (624, 502), "#10151e")
        label = ImageDraw.Draw(board)
        for n, skill in enumerate(sets):
            x, y = 10 + (n%5)*122, 10 + (n//5)*123
            label.rounded_rectangle((x, y, x+114, y+115), 4, fill="#202937", outline="#3c495a")
            board.alpha_composite(skill[i], (x+9, y+4))
            label.text((x+56, y+103), f"{n+1:02d}", font=font, fill="#c8d0db", anchor="mt")
        frames.append(board.convert("RGB"))
    PREVIEW.parent.mkdir(parents=True, exist_ok=True)
    frames[0].save(PREVIEW, save_all=True, append_images=frames[1:], duration=54, loop=0, optimize=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--preview", action="store_true", help="also rebuild the animated contact sheet")
    args = parser.parse_args()
    ART.mkdir(parents=True, exist_ok=True)
    rendered = []
    for number, (style, draw) in enumerate(zip(STYLES, DRAWINGS, strict=True), 1):
        name, *palette = style
        frames = render_skill(number, palette, draw)
        sheet = Image.new("RGBA", (FRAMES*CELL, CELL))
        for i, frame in enumerate(frames):
            sheet.paste(frame, (i*CELL, 0))
        sheet.save(ART / f"{number:02d}-{name}.png", optimize=True)
        rendered.append(frames)
    if args.preview:
        preview(rendered)


if __name__ == "__main__":
    main()
