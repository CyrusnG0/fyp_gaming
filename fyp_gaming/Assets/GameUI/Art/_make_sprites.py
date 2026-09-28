"""Draw simple old-China-style 2D sprites for the LuanShi MVP demo."""
import random
from PIL import Image, ImageDraw, ImageFilter

random.seed(42)
OUT = "fyp_gaming/Assets/GameUI/Art"


def ink_blur(img, r=1.2):
    return img.filter(ImageFilter.GaussianBlur(r))


def make_plains():
    img = Image.new("RGB", (512, 512), (124, 146, 90))
    d = ImageDraw.Draw(img, "RGBA")
    for _ in range(60):
        x, y = random.randint(0, 512), random.randint(0, 512)
        r = random.randint(20, 70)
        c = random.choice([(104, 128, 74, 60), (144, 166, 108, 60), (116, 138, 84, 60)])
        d.ellipse([x - r, y - r, x + r, y + r], fill=c)
    img = img.filter(ImageFilter.GaussianBlur(18))
    d = ImageDraw.Draw(img, "RGBA")
    for _ in range(160):  # grass strokes
        x, y = random.randint(0, 500), random.randint(0, 500)
        h = random.randint(6, 14)
        d.line([x, y, x + random.randint(-3, 3), y - h], fill=(86, 108, 62, 90), width=2)
    img.convert("RGBA").save(f"{OUT}/Tile_Plains.png")


def make_water():
    img = Image.new("RGB", (512, 512), (88, 118, 138))
    d = ImageDraw.Draw(img, "RGBA")
    for _ in range(40):
        x, y = random.randint(0, 512), random.randint(0, 512)
        r = random.randint(30, 90)
        c = random.choice([(78, 106, 126, 70), (102, 132, 150, 70)])
        d.ellipse([x - r, y - r, x + r, y + r], fill=c)
    img = img.filter(ImageFilter.GaussianBlur(16))
    d = ImageDraw.Draw(img, "RGBA")
    for _ in range(36):  # wave strokes
        x, y = random.randint(0, 460), random.randint(0, 500)
        w = random.randint(30, 70)
        d.arc([x, y, x + w, y + 14], 200, 340, fill=(168, 192, 200, 110), width=3)
    img.convert("RGBA").save(f"{OUT}/Tile_Water.png")


def draw_tree(d, x, y, s):
    d.polygon([(x - 4 * s, y), (x + 4 * s, y), (x + 3 * s, y - 18 * s), (x - 3 * s, y - 18 * s)], fill=(96, 74, 52, 255))
    for i, (w, h, off) in enumerate([(30, 22, 14), (24, 20, 28), (16, 16, 40)]):
        g = [(52, 84, 58, 255), (60, 94, 66, 255), (70, 104, 74, 255)][i]
        d.polygon([(x - w * s, y - off * s), (x + w * s, y - off * s), (x, y - (off + h) * s)], fill=g)
        d.line([(x - w * s, y - off * s), (x, y - (off + h) * s), (x + w * s, y - off * s)], fill=(34, 54, 38, 200), width=max(1, int(2 * s)))


def make_forest():
    img = Image.new("RGBA", (512, 512), (0, 0, 0, 0))
    d = ImageDraw.Draw(img, "RGBA")
    d.ellipse([86, 400, 426, 470], fill=(40, 60, 42, 90))  # ground shadow
    for (x, y, s) in [(150, 420, 3.2), (256, 432, 3.8), (360, 416, 3.0), (210, 380, 2.4), (310, 372, 2.2)]:
        draw_tree(d, x, y, s)
    ink_blur(img, 0.8).save(f"{OUT}/Prop_Forest.png")


def make_mountain():
    img = Image.new("RGBA", (512, 512), (0, 0, 0, 0))
    d = ImageDraw.Draw(img, "RGBA")
    d.ellipse([60, 420, 452, 480], fill=(60, 60, 60, 70))  # mist/shadow
    peaks = [[(60, 440), (200, 130), (340, 440)], [(220, 440), (370, 190), (480, 440)]]
    greys = [(118, 116, 108, 255), (96, 96, 92, 255)]
    for poly, g in zip(peaks, greys):
        d.polygon(poly, fill=g)
        d.line(poly + [poly[0]], fill=(52, 52, 50, 220), width=5, joint="curve")
    # snow / light caps
    d.polygon([(172, 190), (200, 130), (228, 190), (210, 176), (200, 196), (188, 174)], fill=(214, 212, 200, 255))
    d.polygon([(346, 240), (370, 190), (394, 240), (378, 226), (368, 246), (360, 224)], fill=(206, 204, 194, 255))
    # ink texture strokes
    for _ in range(26):
        x = random.randint(120, 420)
        y = random.randint(230, 420)
        d.line([x, y, x + random.randint(-8, 8), y + random.randint(14, 34)], fill=(70, 70, 66, 120), width=2)
    ink_blur(img, 1.0).save(f"{OUT}/Prop_Mountain.png")


def make_city():
    img = Image.new("RGBA", (512, 512), (0, 0, 0, 0))
    d = ImageDraw.Draw(img, "RGBA")
    d.ellipse([66, 420, 446, 484], fill=(50, 50, 46, 80))  # ground shadow
    # pagoda (behind wall)
    tiers = [(186, 326, 140, 44), (206, 264, 100, 40), (224, 210, 64, 36)]
    body = (158, 92, 70, 255)
    roof = (64, 58, 62, 255)
    for (cx, ty, w, hh) in tiers:
        d.rectangle([256 - w // 3, ty, 256 + w // 3, ty + 46], fill=body)
        d.polygon([(cx - w // 2 - 18, ty), (cx + w // 2 + 18, ty), (256, ty - hh)], fill=roof)
        d.line([(cx - w // 2 - 18, ty), (256, ty - hh), (cx + w // 2 + 18, ty)], fill=(36, 32, 34, 220), width=4)
    d.line([256, 166, 256, 196], fill=(36, 32, 34, 255), width=5)
    d.ellipse([250, 152, 262, 168], fill=(196, 160, 84, 255))
    # wall
    d.rectangle([96, 360, 416, 452], fill=(152, 142, 124, 255))
    d.rectangle([96, 360, 416, 452], outline=(86, 78, 66, 255), width=5)
    for x in range(104, 408, 26):  # crenellations
        d.rectangle([x, 344, x + 14, 362], fill=(152, 142, 124, 255), outline=(86, 78, 66, 255), width=3)
    # gate
    d.pieslice([226, 380, 286, 452], 180, 360, fill=(70, 52, 40, 255))
    d.rectangle([226, 416, 286, 452], fill=(70, 52, 40, 255))
    ink_blur(img, 0.8).save(f"{OUT}/Prop_City.png")


def make_banner():
    img = Image.new("RGBA", (256, 512), (0, 0, 0, 0))
    d = ImageDraw.Draw(img, "RGBA")
    d.ellipse([86, 460, 170, 492], fill=(50, 50, 46, 90))  # shadow
    d.line([118, 60, 118, 474], fill=(88, 66, 44, 255), width=10)  # pole
    d.ellipse([110, 44, 126, 64], fill=(196, 160, 84, 255))  # finial
    # cloth (light neutral so Unity tint shows faction color)
    cloth = [(122, 70), (238, 84), (232, 240), (196, 258), (150, 238), (122, 250)]
    d.polygon(cloth, fill=(222, 202, 168, 255))
    d.line(cloth + [cloth[0]], fill=(92, 72, 52, 255), width=5, joint="curve")
    d.ellipse([152, 130, 200, 178], outline=(92, 72, 52, 220), width=6)  # emblem ring
    d.line([176, 146, 176, 162], fill=(92, 72, 52, 220), width=6)
    ink_blur(img, 0.7).save(f"{OUT}/Prop_ArmyBanner.png")


make_plains()
make_water()
make_forest()
make_mountain()
make_city()
make_banner()
print("done")
