"""Rebuild the seven boss sets from the paintings in Docs/References/BossRooms."""
import math
import re
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageOps

import BuildWhisperingGraveyard as unity

ROOT = unity.ROOT
ART = ROOT / "Assets/RaidPartyManager/Art"
SOURCE = ROOT / "Docs/References/BossRooms"
TARGET = ART / "BossRooms"
PANORAMA = ART / "DockPlayhouse/Panoramas/Harbour.jpg.meta"
PROP = ART / "Enemies/SaltCrab-Combat.png.meta"
SCRIPT = ROOT / "Assets/RaidPartyManager/Presentation/BossRoomArt.cs"

# Left and right floor marks (across, upstage, height), floor repeat in metres,
# front bar, rim, low haze, haze height and scenic loop duration.
ROOMS = [
    ("BlockadeAdmiral", "封锁线总炮台", (.20,.65,2.00),(.80,.81,3.15),(4.2,1.7),
     (1.17,.91,.71,-5),(.76,.83,.96,-.6),(.46,.45,.41,.053),1.1,3.4),
    ("DrownedLanternRay", "沉灯船腹", (.16,.77,3.55),(.82,.63,2.55),(3.0,1.8),
     (.72,1.02,1.18,4.3),(.36,.95,1.24,-1),(.13,.42,.51,.110),1.9,4.6),
    ("NamelessKing", "削名王座", (.22,.82,3.35),(.85,.66,2.55),(4.0,3.6),
     (1.10,1.03,.83,4.5),(.63,.69,.87,-1),(.30,.29,.33,.066),.8,5.0),
    ("SepulchralMatron", "守夜圣龛", (.19,.65,2.85),(.78,.82,2.35),(3.1,3.1),
     (.83,1.07,1.06,-3.6),(.90,.73,1.05,-.8),(.36,.43,.46,.069),1.3,3.8),
    ("RoyalEngine", "御用终点站", (.17,.81,3.45),(.84,.68,2.75),(4.8,2.3),
     (1.18,1.01,.79,-4.4),(.70,.90,.90,-.6),(.43,.41,.35,.048),1.5,3.7),
    ("AstralGovernor", "子午星仪厅", (.20,.70,2.62),(.83,.84,3.2),(4.9,4.9),
     (.87,.91,1.18,3.8),(.87,.72,1.22,-1.4),(.25,.28,.47,.048),1.5,5.2),
    ("CrucibleRegent", "熔心御铸厅", (.20,.81,2.62),(.83,.66,3.3),(3.5,3.5),
     (1.16,.87,.70,-4),(.97,.63,.45,-.8),(.41,.29,.23,.063),1.0,3.2)
]


def material_import(path, template, repeat=False):
    meta = Path(str(path) + ".meta")
    if not meta.exists():
        body = re.sub(r'(?m)^guid: \w+', 'guid: ' + unity.guid(path), template.read_text())
        if repeat:
            body = re.sub(r'wrap([UVW]): 1', r'wrap\1: 0', body)
        meta.write_text(body)


def cells(path, columns, rows):
    image = Image.open(path).convert("RGBA")
    w, h = image.size
    return [image.crop((x*w//columns,y*h//rows,(x+1)*w//columns,(y+1)*h//rows))
            for y in range(rows) for x in range(columns)]


def cutout(image):
    # Minor specks and adjacent-cell tips must not enlarge a scenic flat's ground footprint.
    rgba = np.array(image)
    alpha = rgba[:,:,3]
    count, labels, stats, _ = cv2.connectedComponentsWithStats((alpha>10).astype("uint8"))
    areas = stats[1:, cv2.CC_STAT_AREA]
    primary = areas.argmax()+1
    x,y,w,h = stats[primary,:4]
    keep = np.zeros_like(alpha)
    for i in range(1,count):
        xx, yy, ww, hh, area = stats[i]
        if area >= max(16,areas.max()*.001) and x-18 <= xx+ww/2 <= x+w+18 and y-18 <= yy+hh/2 <= y+h+18:
            keep[labels==i] = 255
    rgba[:,:,3] = np.minimum(alpha,cv2.dilate(keep,np.ones((5,5),np.uint8)))
    out = Image.fromarray(rgba)
    out = out.crop(out.getchannel("A").getbbox())
    out.thumbnail((640,640), Image.Resampling.LANCZOS)
    return ImageOps.expand(out,border=3,fill=(0,0,0,0))


def animate(paint, index):
    # The central painting faces the playing mark; its dark continuations wrap the 180° cyclorama.
    core = np.array(ImageOps.fit(paint.convert("RGB"),(956,464),
                                method=Image.Resampling.LANCZOS,centering=(.5,.43)),dtype=np.float32)
    h,w = core.shape[:2]
    yy,xx = np.mgrid[0:h,0:w].astype(np.float32)
    x,y = xx/w, yy/h
    rng = np.random.default_rng(741+index)
    dust = [(rng.uniform(.10,.90),rng.uniform(.12,.88),rng.uniform(1,2.4),
             rng.uniform(0,2*math.pi)) for _ in range(32)]
    luminance = core.mean(axis=2)/255
    warm = np.clip((core[:,:,0]-core[:,:,2])/95,0,1)
    for frame in range(16):
        phase = 2*math.pi*frame/16
        if index == 0: # Signal lamp sweeps both stone towers.
            beam = np.exp(-((x-(.46+.30*math.sin(phase))-.14*y)/.10)**2)
            # As the reflector turns away, its returning beam is dimmer.
            light = beam*(1-y)*(.24+.76*luminance)*(12+4*math.cos(phase))
            tint = (1,.91,.71)
        elif index == 1: # Crossing caustics beneath the lightship.
            ray = (.5+.5*np.sin(32*x+7*y+1.6*np.sin(13*y-phase)+phase))**7
            light = ray*(.68+.32*np.cos(27*x-2*phase))*(1-.65*y)*21
            tint = (.30,.89,1)
        elif index == 2:
            light = np.exp(-((x-.54+.16*y)/.11)**2)*(1-y)*(1+.5*math.sin(phase))*9
            tint = (1,.92,.62)
        elif index == 3:
            light = luminance**2*(.70+.30*np.sin(phase*2+29*x))*(1+.65*np.sin(phase-39*x))*17
            tint = (.82,1,.91)
        elif index == 4: # Steam drifts along the rails under the vaulted roof.
            light = np.exp(-((y-.64-.05*np.sin(13*x+phase))/.09)**2)*(1+np.sin(16*x-phase)) * 6
            tint = (.97,.96,.82)
        elif index == 5:
            angle = np.arctan2(y-.43,x-.53)
            light = (.5+.5*np.cos(3*angle-phase))**5*luminance*(1-y)*15
            tint = (.82,.87,1)
        else:
            light = warm*(.65+.35*np.sin(38*x+2*phase))*(1+.62*math.sin(phase))*23
            tint = (1,.43,.13)
        pixels = core + light[:,:,None]*np.array(tint)
        img=Image.fromarray(np.uint8(np.clip(pixels,0,255)))
        if index in (2,5,6):
            overlay = Image.new('RGBA',img.size)
            draw = ImageDraw.Draw(overlay)
            for dx,dy,size,seed in dust:
                intensity = (.5+.5*math.sin(phase+seed))**2
                px=(dx+.004*math.sin(phase+seed))*w
                py=(dy+(.015 if index==6 else .004)*math.cos(phase+seed))*h
                color = [(219,193,133),(189,216,255),(255,144,51)][(2,5,6).index(index)]
                draw.ellipse((px,py,px+size,py+size),fill=(*color,round(150*intensity)))
            img=Image.alpha_composite(img.convert('RGBA'),overlay).convert('RGB')
        a=np.array(img)
        wide=np.pad(a,((0,0),(736,736),(0,0)),mode='reflect')
        edge=np.maximum(0,abs(np.arange(wide.shape[1])-(wide.shape[1]-1)/2)-w/2)
        wide=(wide*(1-.23*np.minimum(1,edge/580))[:,None]).astype(np.uint8)
        yield Image.fromarray(wide)


def vector(v):
    return "{"+", ".join(f"{k}: {n}" for k,n in zip("xyzw",v))+"}"


def build():
    props=cells(SOURCE/"propsA.png",2,4)+cells(SOURCE/"propsB.png",2,3)
    tiles=cells(SOURCE/"tiles.png",4,2)
    library=TARGET/"Resources/BossRooms"
    unity.folder(library)
    for i,(boss,name,left,right,tile,key,rim,mist,height,seconds) in enumerate(ROOMS):
        folder=TARGET/boss
        unity.folder(folder)
        refs=[]
        for frame, cel in enumerate(animate(Image.open(SOURCE/(boss+".png")),i)):
            path=folder/f"Panorama{frame:02}.jpg"
            cel.save(path,quality=89,subsampling=1)
            material_import(path,PANORAMA)
            refs.append(unity.ref(path))
        floor=folder/"Floor.jpg"
        tile_image=tiles[i]
        tile_image=tile_image.crop((5,5,tile_image.width-5,tile_image.height-5)).convert('RGB')
        tile_image.save(floor,quality=94)
        material_import(floor,PANORAMA,True)
        wings=[folder/'Left.png',folder/'Right.png']
        for wing,sprite in zip(wings,props[2*i:2*i+2]):
            cutout(sprite).save(wing)
            material_import(wing,PROP)
        asset=library/("Enemy"+boss+".asset")
        header=(ART/"BattleStage/BattleStageArt.asset").read_text().split("  m_Script:")[0]
        asset.write_text(header+"  m_Script: "+unity.ref(SCRIPT,11500000,3)+"\n  m_Name: Enemy"+boss+
            "\n  m_EditorClassIdentifier: \n  roomName: "+name+"\n  backdrop:\n"+
            "".join("  - "+v+"\n" for v in refs)+"  floor: "+unity.ref(floor)+"\n  leftWing: "+unity.ref(wings[0])+
            "\n  rightWing: "+unity.ref(wings[1])+"\n  leftMark: "+vector(left)+"\n  rightMark: "+vector(right)+
            "\n  floorTile: "+vector(tile)+"\n  keyLight: "+vector(key)+"\n  rimLight: "+vector(rim)+
            "\n  mist: "+vector(mist)+f"\n  mistHeight: {height}\n  loopSeconds: {seconds}\n")
        unity.meta(asset,"asset")


if __name__ == "__main__":
    build()
