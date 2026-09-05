"""Liest und schreibt Godot-4-Pakete (.pck) und deren Projekteinstellungen.

Godot kennt kein Einhängen von außen wie BepInEx: Es lädt genau eine .pck neben der
Exe, und alles muss darin stehen. Um eigenen Code auszuführen, tragen wir ihn deshalb
als Autostart in die Projekteinstellungen ein und packen ihn mit ins Paket.

Beide Formate sind schlicht genug, um sie hier selbst zu behandeln:

    .pck            "GDPC", Kopf, Datenblock, am Ende das Inhaltsverzeichnis
    project.binary  "ECFG", Anzahl, dann Schlüssel/Wert-Paare als Godot-Variant

Aufruf:
    python tools/godot_pck.py list   <pck>
    python tools/godot_pck.py inject <pck> <name> <datei> <autoload-name>
"""

import hashlib
import os
import shutil
import struct
import sys

KOPF_GROESSE = 112          # Datenbeginn im Originalpaket
FLAG_RELATIV = 2            # Offsets zählen ab Datenbeginn


def ist_altes_floppy_menue(name):
    return name.removeprefix('res://') in {
        'floppy_overlay.gd', 'floppy_overlay.gdc',
        'floppy_overlay.gd.remap', 'floppy_overlay.gd.uid',
    }


class Eintrag:
    def __init__(self, name, offset, groesse, md5, flags):
        self.name = name
        self.offset = offset
        self.groesse = groesse
        self.md5 = md5
        self.flags = flags


def lies_paket(pfad):
    """Gibt (kopf, eintraege, datenbasis) zurück."""
    f = open(pfad, 'rb')
    kopf = f.read(48)

    if kopf[:4] != b'GDPC':
        raise ValueError('kein Godot-Paket')

    format_version = struct.unpack_from('<i', kopf, 4)[0]
    basis = struct.unpack_from('<Q', kopf, 24)[0]
    verzeichnis = struct.unpack_from('<Q', kopf, 32)[0]

    f.seek(verzeichnis)
    anzahl, = struct.unpack('<I', f.read(4))

    eintraege = []
    for _ in range(anzahl):
        ln, = struct.unpack('<I', f.read(4))
        name = f.read(ln).rstrip(b'\0').decode('utf-8')
        offset, groesse = struct.unpack('<QQ', f.read(16))
        md5 = f.read(16)
        flags, = struct.unpack('<I', f.read(4))
        eintraege.append(Eintrag(name, offset, groesse, md5, flags))

    f.close()
    return format_version, basis, eintraege


def lies_datei(pfad, basis, eintrag):
    with open(pfad, 'rb') as f:
        f.seek(basis + eintrag.offset)
        return f.read(eintrag.groesse)


# ---------------------------------------------------------------- Projekteinstellungen

def lies_einstellungen(roh):
    """ECFG -> Liste von (schlüssel, wertbytes)."""
    if roh[:4] != b'ECFG':
        raise ValueError('keine Godot-Einstellungen')

    pos = 4
    anzahl, = struct.unpack_from('<I', roh, pos); pos += 4

    paare = []
    for _ in range(anzahl):
        kl, = struct.unpack_from('<I', roh, pos); pos += 4
        key = roh[pos:pos + kl].decode('utf-8'); pos += kl
        vl, = struct.unpack_from('<I', roh, pos); pos += 4
        val = roh[pos:pos + vl]; pos += vl
        paare.append((key, val))

    return paare


def schreibe_einstellungen(paare):
    teile = [b'ECFG', struct.pack('<I', len(paare))]

    for key, val in paare:
        rohkey = key.encode('utf-8')
        teile.append(struct.pack('<I', len(rohkey)))
        teile.append(rohkey)
        teile.append(struct.pack('<I', len(val)))
        teile.append(val)

    return b''.join(teile)


def variant_string(text):
    """Godot-Variant vom Typ String (4). Länge zählt das Nullbyte nicht mit,
    die Daten werden aber auf vier Bytes aufgefüllt."""
    roh = text.encode('utf-8')
    gefuellt = roh + b'\0' * ((4 - len(roh) % 4) % 4)
    return struct.pack('<II', 4, len(roh)) + gefuellt


# ---------------------------------------------------------------- Schreiben

def schreibe_paket(ziel, quelle, format_version, basis, eintraege, ersetzungen, zusaetze):
    """Baut das Paket neu. `ersetzungen` ist Name -> Bytes, `zusaetze` eine Liste
    von (Name, Bytes)."""
    daten = []          # (name, bytes, flags)

    for e in eintraege:
        inhalt = ersetzungen.get(e.name)
        if inhalt is None:
            inhalt = lies_datei(quelle, basis, e)
        daten.append((e.name, inhalt, e.flags))

    for name, inhalt in zusaetze:
        daten.append((name, inhalt, 0))

    with open(quelle, 'rb') as f:
        kopf = bytearray(f.read(KOPF_GROESSE))
    if len(kopf) != KOPF_GROESSE or kopf[:4] != b'GDPC':
        raise ValueError('unvollständiger Godot-Paketkopf')
    struct.pack_into('<Q', kopf, 24, KOPF_GROESSE)
    platz_verzeichnis = 32
    struct.pack_into('<Q', kopf, platz_verzeichnis, 0)
    with open(ziel, 'wb') as f:
        # Engine version and reserved fields belong to the game, not this tool.
        f.write(kopf)

        # Daten
        offsets = []
        for name, inhalt, flags in daten:
            aktuell = f.tell() - KOPF_GROESSE
            offsets.append(aktuell)
            f.write(inhalt)

            # Godot richtet die Einträge an 16 Bytes aus
            rest = (16 - (f.tell() % 16)) % 16
            if rest:
                f.write(b'\0' * rest)

        # Verzeichnis
        verzeichnis = f.tell()
        f.write(struct.pack('<I', len(daten)))

        for (name, inhalt, flags), offset in zip(daten, offsets):
            rohname = name.encode('utf-8')
            fuellung = (4 - len(rohname) % 4) % 4
            rohname += b'\0' * fuellung

            f.write(struct.pack('<I', len(rohname)))
            f.write(rohname)
            f.write(struct.pack('<QQ', offset, len(inhalt)))
            f.write(hashlib.md5(inhalt).digest())
            f.write(struct.pack('<I', flags))

        f.seek(platz_verzeichnis)
        f.write(struct.pack('<Q', verzeichnis))

    return len(daten)


# ---------------------------------------------------------------- Befehle

def befehl_list(pck):
    fv, basis, eintraege = lies_paket(pck)
    print(f"Format {fv}, Datenbeginn {basis}, {len(eintraege)} Dateien")
    for e in eintraege[:40]:
        print(f"  {e.groesse:>9}  {e.name}")


def befehl_inject(pck, res_name, quelldatei, autoload_name, weitere=()):
    """`weitere` ist eine flache Liste res-Name, Datei, res-Name, Datei, ... -
    Dateien, die mit ins Paket sollen, aber keinen Autostart bekommen."""
    fv, basis, eintraege = lies_paket(pck)

    # Floppy's menu now runs in the desktop app. Keep this optional preparation
    # tool consistent with the installer when upgrading a previously patched PCK.
    if autoload_name == 'Floppy':
        eintraege = [e for e in eintraege if not ist_altes_floppy_menue(e.name)]

    # Projekteinstellungen holen und Autostart ergänzen
    projekt = next(e for e in eintraege if e.name.endswith('project.binary'))
    paare = lies_einstellungen(lies_datei(pck, basis, projekt))

    schluessel = 'autoload/' + autoload_name
    paare = [(k, v) for k, v in paare if k != schluessel]
    if autoload_name == 'Floppy':
        paare = [(k, v) for k, v in paare if k != 'autoload/FloppyOverlay']
    paare.append((schluessel, variant_string('*' + res_name)))

    neu_projekt = schreibe_einstellungen(paare)

    with open(quelldatei, 'rb') as f:
        skript = f.read()

    vorhanden = {e.name for e in eintraege}
    ersetzungen = {projekt.name: neu_projekt}
    zusaetze = []

    dateien = [(res_name, skript)]
    for i in range(0, len(weitere) - 1, 2):
        if autoload_name == 'Floppy' and ist_altes_floppy_menue(weitere[i]):
            raise ValueError('Das Floppy-Menü wird ausschließlich von der Desktop-App angezeigt.')
        with open(weitere[i + 1], 'rb') as f:
            dateien.append((weitere[i], f.read()))

    for name, inhalt in dateien:
        if name in vorhanden:
            ersetzungen[name] = inhalt
        else:
            zusaetze.append((name, inhalt))

    ziel = pck + '.neu'
    anzahl = schreibe_paket(ziel, pck, fv, basis, eintraege, ersetzungen, zusaetze)

    # Keep the exact pre-install package. The desktop installer additionally tracks
    # owned changes and can restore them without overwriting a later game update.
    backup = pck + '.floppy-original'
    if not os.path.exists(backup):
        shutil.copy2(pck, backup)
    os.replace(ziel, pck)
    print(f"{anzahl} Dateien geschrieben, Autostart '{autoload_name}' -> {res_name}")


if __name__ == '__main__':
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(1)

    if sys.argv[1] == 'list':
        befehl_list(sys.argv[2])
    elif sys.argv[1] == 'inject':
        befehl_inject(sys.argv[2], sys.argv[3], sys.argv[4], sys.argv[5],
                      sys.argv[6:])
    else:
        print(__doc__)
        sys.exit(1)
