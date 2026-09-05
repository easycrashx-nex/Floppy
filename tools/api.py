"""Schält aus einer dekompilierten IL2CPP-Hülle die reine Schnittstelle heraus.

Die von BepInEx erzeugten Zwischen-Assemblies enthalten viel Beiwerk: Zeiger auf
native Felder, Aufrufhilfen, Marshalling. Die Namen sind aber vollständig - und in
den Zeigernamen steckt sogar die Signatur:

    NativeMethodInfoPtr_get_MaxEnergy_Public_get_Single_0
                       ^Name          ^Sichtbarkeit ^Rückgabe ^Parameterzahl

Aufruf:  python tools/api.py <ordner> <Klasse> [<Klasse> ...]
         python tools/api.py <ordner> --suche geld money coin
"""

import re
import sys
from pathlib import Path

FELD = re.compile(r'NativeFieldInfoPtr_(\w+)')
METHODE = re.compile(r'NativeMethodInfoPtr_(\w+)')


def aufraeumen(name: str) -> str:
    """Backing-Feld-Namen lesbar machen."""
    name = name.replace('_k__BackingField', '')
    return name.lstrip('_')


def schnittstelle(pfad: Path):
    text = pfad.read_text(encoding='utf-8', errors='replace')

    felder = sorted({aufraeumen(m) for m in FELD.findall(text)})

    methoden = {}
    for roh in METHODE.findall(text):
        # Hinten hängen Sichtbarkeit, Rückgabetyp und Parameterzahl
        teile = roh.split('_')
        for i, t in enumerate(teile):
            if t in ('Public', 'Private', 'Protected', 'Internal'):
                name = '_'.join(teile[:i])
                rest = ' '.join(teile[i:])
                methoden.setdefault(name, rest)
                break
        else:
            methoden.setdefault(roh, '')

    return felder, methoden


def zeige(pfad: Path):
    felder, methoden = schnittstelle(pfad)

    print(f"\n{'=' * 60}\n{pfad.stem}\n{'=' * 60}")

    if felder:
        print("Felder:")
        for f in felder:
            print(f"   {f}")

    if methoden:
        print("Methoden:")
        for name in sorted(methoden):
            if name.startswith(('get_', 'set_')):
                continue  # Eigenschaften getrennt
            print(f"   {name}()   [{methoden[name]}]")

        eigenschaften = sorted({n[4:] for n in methoden if n.startswith(('get_', 'set_'))})
        if eigenschaften:
            print("Eigenschaften:")
            for e in eigenschaften:
                schreibbar = ('set_' + e) in methoden
                print(f"   {e}{'  (schreibbar)' if schreibbar else '  (nur lesen)'}")


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 1

    ordner = Path(sys.argv[1])

    if sys.argv[2] == '--suche':
        begriffe = [b.lower() for b in sys.argv[3:]]
        for datei in sorted(ordner.rglob('*.cs')):
            text = datei.read_text(encoding='utf-8', errors='replace')
            namen = set(FELD.findall(text)) | set(METHODE.findall(text))
            treffer = {n for n in namen if any(b in n.lower() for b in begriffe)}
            if treffer:
                print(f"{datei.stem}:")
                for t in sorted(treffer)[:8]:
                    print(f"   {aufraeumen(t)}")
        return 0

    for name in sys.argv[2:]:
        treffer = list(ordner.rglob(name + '.cs'))
        if treffer:
            zeige(treffer[0])
        else:
            print(f"nicht gefunden: {name}")

    return 0


if __name__ == '__main__':
    sys.exit(main())
