# Made by Claude Ai & Relic Hunter
import sys
from PIL import Image, ImageDraw

S = 1024


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def grad(size, c0, c1, c2):
    img = Image.new("RGB", (size, size))
    px = img.load()
    for y in range(size):
        for x in range(size):
            t = (x + y) / (2 * (size - 1))
            px[x, y] = lerp(c0, c1, t / 0.55) if t < 0.55 else lerp(c1, c2, (t - 0.55) / 0.45)
    return img


def pts(raw):
    return [(x * S / 64, y * S / 64) for x, y in raw]


def main(out):
    gold = grad(S, (0xF7, 0xDC, 0x9C), (0xE3, 0xB7, 0x5C), (0x9C, 0x6D, 0x22))
    red = grad(S, (0xFF, 0x6B, 0x7A), (0xC8, 0x10, 0x2E), (0x5C, 0x06, 0x12))
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    hexa = pts([(32, 2), (58.5, 17), (58.5, 47), (32, 62), (5.5, 47), (5.5, 17)])
    inner = pts([(32, 5.5), (55.5, 19), (55.5, 45), (32, 58.5), (8.5, 45), (8.5, 19)])
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).polygon(hexa, fill=255)
    img.paste(gold, (0, 0), mask)
    ImageDraw.Draw(img).polygon(inner, fill=(0x14, 0x0C, 0x0C, 255))
    dia_o = pts([(32, 12), (48, 32), (32, 52), (16, 32)])
    dia_i = pts([(32, 15.6), (45.1, 32), (32, 48.4), (18.9, 32)])
    m2 = Image.new("L", (S, S), 0)
    ImageDraw.Draw(m2).polygon(dia_o, fill=255)
    ImageDraw.Draw(m2).polygon(dia_i, fill=0)
    img.paste(gold, (0, 0), m2)
    core = pts([(32, 21), (41, 32), (32, 43), (23, 32)])
    m3 = Image.new("L", (S, S), 0)
    ImageDraw.Draw(m3).polygon(core, fill=255)
    img.paste(red, (0, 0), m3)
    ImageDraw.Draw(img).line(core + [core[0]], fill=(0xF6, 0xD6, 0x90, 255), width=10)
    img.save(out, sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    img.resize((256, 256), Image.LANCZOS).save(out.replace(".ico", "-256.png"))


if __name__ == "__main__":
    main(sys.argv[1])
