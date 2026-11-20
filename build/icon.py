# Draws the app icon: a crate held in a dock cradle. Run: python build/icon.py
from pathlib import Path

from PIL import Image, ImageDraw

S = 1024
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

d.rounded_rectangle([40, 40, S - 40, S - 40], radius=200, fill=(22, 34, 52, 255))

steel = (200, 214, 229, 255)
# the cradle: two arms and a floor beam
d.rounded_rectangle([170, 300, 270, 800], radius=30, fill=steel)
d.rounded_rectangle([S - 270, 300, S - 170, 800], radius=30, fill=steel)
d.rounded_rectangle([170, 730, S - 170, 820], radius=30, fill=steel)
# clamps reaching in from the arms
d.rounded_rectangle([250, 430, 340, 490], radius=18, fill=steel)
d.rounded_rectangle([S - 340, 430, S - 250, 490], radius=18, fill=steel)

orange = (240, 140, 40, 255)
dark = (170, 90, 20, 255)
d.rounded_rectangle([340, 330, S - 340, 700], radius=36, fill=orange)
d.rounded_rectangle([340, 330, S - 340, 400], radius=36, fill=dark)
d.rectangle([340, 370, S - 340, 400], fill=dark)
d.rectangle([494, 400, 530, 700], fill=dark)

out = Path(__file__).resolve().parent.parent / "src" / "Drydock.App" / "Assets"
out.mkdir(parents=True, exist_ok=True)
img.save(out / "Drydock.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
img.resize((256, 256), Image.LANCZOS).save(out / "Drydock.png")
print("wrote", out / "Drydock.ico")
