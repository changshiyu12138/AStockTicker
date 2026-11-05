# -*- coding: utf-8 -*-
"""Generate the application icon (multi-size .ico + preview png)."""
import os
from PIL import Image, ImageDraw, ImageFont

OUT_DIR = os.path.dirname(os.path.abspath(__file__))
S = 512  # supersampled canvas


def lerp(a, b, t):
    return int(round(a + (b - a) * t))


def main():
    top = (26, 42, 68)
    bottom = (8, 13, 22)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    px = img.load()
    for y in range(S):
        t = y / (S - 1)
        c = (lerp(top[0], bottom[0], t), lerp(top[1], bottom[1], t),
             lerp(top[2], bottom[2], t), 255)
        for x in range(S):
            px[x, y] = c

    mask = Image.new("L", (S, S), 0)
    md = ImageDraw.Draw(mask)
    md.rounded_rectangle([8, 8, S - 8, S - 8], radius=112, fill=255)
    img.putalpha(mask)

    d = ImageDraw.Draw(img)
    # faint grid lines
    for i in range(3):
        y = 330 + i * 52
        d.line([70, y, S - 70, y], fill=(255, 255, 255, 16), width=3)
    # border
    d.rounded_rectangle([8, 8, S - 8, S - 8], radius=112,
                        outline=(62, 92, 134, 255), width=6)

    font_path = r"C:\Windows\Fonts\msyhbd.ttc"
    if not os.path.exists(font_path):
        font_path = r"C:\Windows\Fonts\arialbd.ttf"
    font = ImageFont.truetype(font_path, 320)
    text = "A"
    box = d.textbbox((0, 0), text, font=font)
    tw, th = box[2] - box[0], box[3] - box[1]
    tx = (S - tw) / 2 - box[0]
    ty = 232 - th / 2 - box[1]
    d.text((tx, ty), text, font=font, fill=(244, 248, 255, 255))

    # red rising line + arrow head
    pts = [(92, 400), (170, 362), (238, 378), (330, 292), (416, 220)]
    d.line(pts, fill=(255, 77, 79, 255), width=20, joint="curve")
    d.polygon([(416, 196), (438, 250), (386, 238)], fill=(255, 77, 79, 255))
    # small green baseline
    d.line([92, 432, 200, 432], fill=(0, 196, 140, 255), width=14)

    # downscale for the .ico
    base = img.resize((256, 256), Image.LANCZOS)
    base.save(os.path.join(OUT_DIR, "app.png"))
    base.save(os.path.join(OUT_DIR, "app.ico"),
              sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40),
                     (48, 48), (64, 64), (128, 128), (256, 256)])
    print("written", os.path.join(OUT_DIR, "app.ico"))


if __name__ == "__main__":
    main()
