"""Zeichnet das Floppy-Symbol: eine Diskette in der Farbwelt der App.

Alles wird vierfach vergrößert gezeichnet und dann heruntergerechnet - das ergibt
weiche Kanten, ohne dass ein Zeichenprogramm nötig wäre.

Der Aufbau in zwei Schritten: erst die Silhouette der Diskette als Maske, dann die
Farbflächen darauf. Dadurch kann nichts über den Rand hinauslaufen, egal wie grob
die einzelnen Formen gesetzt sind.

Aufruf:  python tools/icon.py src/Floppy.App/floppy.ico
"""

import sys
from pathlib import Path
from PIL import Image, ImageDraw

# Dieselben Töne wie in App.xaml
AKZENT = (58, 166, 255)
AKZENT_DUNKEL = (34, 106, 168)
PANEL = (28, 32, 41)
HELL = (226, 230, 238)
GRAU = (139, 147, 163)

SKALA = 4
GROESSEN = [256, 128, 64, 48, 32, 16]


def silhouette(kante: int, e: float) -> Image.Image:
    """Der Umriss der Diskette: abgerundet, mit abgeschrägter Ecke oben rechts."""
    rand = 3.5 * e
    ecke = 7 * e
    radius = 2.2 * e

    maske = Image.new("L", (kante, kante), 0)
    stift = ImageDraw.Draw(maske)
    stift.rounded_rectangle([rand, rand, kante - rand, kante - rand], radius=radius, fill=255)

    # Die Ecke wegschneiden - wie bei einer echten 3,5-Zoll-Diskette
    stift.polygon(
        [
            (kante - rand - ecke, rand - e),
            (kante - rand + e, rand - e),
            (kante - rand + e, rand + ecke),
        ],
        fill=0,
    )
    return maske


def zeichne(kante: int) -> Image.Image:
    e = kante / 32.0  # eine Einheit = 1/32 der Kante
    rand = 3.5 * e

    # Farbflächen ohne Rücksicht auf den Rand - die Maske schneidet später zu
    flaeche = Image.new("RGB", (kante, kante), AKZENT)
    stift = ImageDraw.Draw(flaeche)

    # Dunklere Kante unten für etwas Tiefe
    stift.rectangle([0, kante - rand - 2.2 * e, kante, kante], fill=AKZENT_DUNKEL)

    # Metallschieber oben - die Aussparung, durch die der Lesekopf greift
    stift.rounded_rectangle([9 * e, rand + 1.2 * e, 20.5 * e, 13.5 * e],
                            radius=0.8 * e, fill=HELL)

    # Fenster im Schieber
    stift.rounded_rectangle([16.2 * e, rand + 2.8 * e, 18.8 * e, 11.6 * e],
                            radius=0.4 * e, fill=PANEL)

    # Etikett unten - die weiße Fläche zum Beschriften
    stift.rounded_rectangle([7 * e, 17.5 * e, kante - 7 * e, kante - rand - 3.2 * e],
                            radius=0.9 * e, fill=HELL)

    # Beschriftungslinien, aber nur wo genug Platz ist
    if kante >= 96:
        for i in range(3):
            y = 19.8 * e + i * 2.4 * e
            stift.rounded_rectangle([9 * e, y, kante - 9 * e, y + 0.9 * e],
                                    radius=0.45 * e, fill=GRAU)

    bild = flaeche.convert("RGBA")
    bild.putalpha(silhouette(kante, e))
    return bild


def main(ziel: Path) -> int:
    ebenen = [
        zeichne(groesse * SKALA).resize((groesse, groesse), Image.LANCZOS)
        for groesse in GROESSEN
    ]

    ziel.parent.mkdir(parents=True, exist_ok=True)
    ebenen[0].save(ziel, format="ICO", sizes=[(g, g) for g in GROESSEN])

    # Vorschau zum Draufschauen
    vorschau = ziel.with_suffix(".preview.png")
    ebenen[0].save(vorschau)

    print(f"geschrieben: {ziel}  ({ziel.stat().st_size} Bytes, {len(GROESSEN)} Größen)")
    return 0


if __name__ == "__main__":
    sys.exit(main(Path(sys.argv[1] if len(sys.argv) > 1 else "floppy.ico")))
