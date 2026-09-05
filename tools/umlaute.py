"""Ersetzt die ASCII-Umschreibungen (ae/oe/ue/ss) durch echte Umlaute und ß.

Arbeitet bewusst mit einer festen Wortliste statt mit Mustern: ein blindes
"ue" -> "ü" würde sonst Bezeichner wie Value, Queue oder true zerstören.

Schreibt anschließend mit UTF-8-BOM, damit sowohl der C#-Compiler als auch
Windows PowerShell 5.1 die Dateien richtig lesen.
"""

import sys
import re
from pathlib import Path

# Nur Kleinschreibung eintragen - die Großschreibung wird daraus abgeleitet.
WORDS = {
    "abgestuerzt": "abgestürzt",
    "anbeisst": "anbeißt",
    "anfuehlen": "anfühlen",
    "auffuellen": "auffüllen",
    "aufschliessen": "aufschließen",
    "aufsaetze": "aufsätze",
    "ausdruecklich": "ausdrücklich",
    "ausfuehren": "ausführen",
    "ausgewaehltes": "ausgewähltes",
    "aussen": "außen",
    "ausser": "außer",
    "ausserdem": "außerdem",
    "befuellen": "befüllen",
    "beissen": "beißen",
    "beisst": "beißt",
    "beruehrungspunkte": "berührungspunkte",
    "daempfen": "dämpfen",
    "dafuer": "dafür",
    "darueber": "darüber",
    "druecken": "drücken",
    "eintraege": "einträge",
    "entfaellt": "entfällt",
    "erklaert": "erklärt",
    "faelschlich": "fälschlich",
    "faenge": "fänge",
    "flaechen": "flächen",
    "fuehrt": "führt",
    "fuellen": "füllen",
    "fuellt": "füllt",
    "fuer": "für",
    "gefuellt": "gefüllt",
    "gegenstaende": "gegenstände",
    "gehoeren": "gehören",
    "gehoert": "gehört",
    "genuegt": "genügt",
    "geprueft": "geprüft",
    "giesskanne": "gießkanne",
    "groesserer": "größerer",
    "haelt": "hält",
    "haengen": "hängen",
    "haengend": "hängend",
    "haengt": "hängt",
    "hauptmenue": "hauptmenü",
    "hinzufuegen": "hinzufügen",
    "hoechsten": "höchsten",
    "hoechster": "höchster",
    "hoeher": "höher",
    "hoeren": "hören",
    "kaeufe": "käufe",
    "kaufvorgaenge": "kaufvorgänge",
    "kernstueck": "kernstück",
    "knoepfen": "knöpfen",
    "koeder": "köder",
    "koennen": "können",
    "koennt": "könnt",
    "kraeftiges": "kräftiges",
    "kuerzere": "kürzere",
    "laenger": "länger",
    "laesst": "lässt",
    "laeuft": "läuft",
    "liess": "ließ",
    "liesse": "ließe",
    "loesen": "lösen",
    "loest": "löst",
    "menue": "menü",
    "mitfuehren": "mitführen",
    "muessen": "müssen",
    "nachtraeglich": "nachträglich",
    "naechste": "nächste",
    "naechsten": "nächsten",
    "naehe": "nähe",
    "oberflaeche": "oberfläche",
    "oeffnen": "öffnen",
    "oeffnet": "öffnet",
    "prueft": "prüft",
    "pruefsummen": "prüfsummen",
    "pruefung": "prüfung",
    "rueckgabe": "rückgabe",
    "rueckstoss": "rückstoß",
    "saettigung": "sättigung",
    "schliessen": "schließen",
    "schliesst": "schließt",
    "schraegstrich": "schrägstrich",
    "schuesse": "schüsse",
    "schuessen": "schüssen",
    "schuetzt": "schützt",
    "standardmaessig": "standardmäßig",
    "stoesse": "stöße",
    "toeten": "töten",
    "toetung": "tötung",
    "traeger": "träger",
    "traegt": "trägt",
    "ueber": "über",
    "ueberhaupt": "überhaupt",
    "uebers": "übers",
    "ueberspringen": "überspringen",
    "ueberstimmt": "überstimmt",
    "uebrigen": "übrigen",
    "unmoeglich": "unmöglich",
    "unterstuetztem": "unterstütztem",
    "unterstuetzten": "unterstützten",
    "unterstuetztes": "unterstütztes",
    "unveraendert": "unverändert",
    "unveraendertes": "unverändertes",
    "veraendern": "verändern",
    "verfuegbar": "verfügbar",
    "verfuegbare": "verfügbare",
    "vorraetig": "vorrätig",
    "waehle": "wähle",
    "waehrend": "während",
    "waende": "wände",
    "wertaenderung": "wertänderung",
    "wuerde": "würde",
    "wuerfelt": "würfelt",
    "zaehlt": "zählt",
    "zumuellt": "zumüllt",
    "zurueck": "zurück",
    "zuruecksetzen": "zurücksetzen",
    "zusaetzlich": "zusätzlich",
}

# Längste zuerst, damit "ueber" nicht in "ueberspringen" hineinfunkt.
PATTERN = re.compile(
    r"\b(" + "|".join(sorted(map(re.escape, WORDS), key=len, reverse=True)) + r")\b",
    re.IGNORECASE,
)

SUFFIXES = {".cs", ".xaml", ".ps1", ".md", ".json"}

# Diese Formate brauchen ein BOM: C# sonst nur bei ASCII sicher, PowerShell 5.1
# liest BOM-lose Dateien als ANSI und zeigt dann Kauderwelsch.
NEEDS_BOM = {".cs", ".xaml", ".ps1"}


def convert(match: re.Match) -> str:
    found = match.group(0)
    replacement = WORDS[found.lower()]
    # Großschreibung des Originals übernehmen
    return replacement[0].upper() + replacement[1:] if found[0].isupper() else replacement


def main(root: Path) -> int:
    changed = 0

    for path in root.rglob("*"):
        if path.suffix.lower() not in SUFFIXES:
            continue
        if any(part in {"bin", "obj", ".git"} for part in path.parts):
            continue

        original = path.read_text(encoding="utf-8-sig")
        updated = PATTERN.sub(convert, original)

        encoding = "utf-8-sig" if path.suffix.lower() in NEEDS_BOM else "utf-8"

        if updated != original:
            path.write_text(updated, encoding=encoding)
            changed += 1
            print(f"  {path.relative_to(root)}")
        else:
            # Auch unveränderte Dateien auf das richtige BOM bringen
            path.write_text(updated, encoding=encoding)

    print(f"{changed} Dateien angepasst")
    return 0


if __name__ == "__main__":
    sys.exit(main(Path(sys.argv[1] if len(sys.argv) > 1 else ".")))
