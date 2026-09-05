"""Read-only schema/connection smoke test. --exercise only touches reviewed overlay options.

No buttons, economy, movement, progression, unlocks or achievements are executed.
The optional overlay exercise restores settings in finally and returns nonzero on failure.
"""
import argparse
import json
import socket
import sys

PORT = 47821
REVERSIBLE = {
    "How to Fish": {"esp.creatures", "esp.players", "esp.items", "esp.range"},
}


class Floppy:
    def __init__(self, port=PORT):
        self.sock = socket.create_connection(("127.0.0.1", port), timeout=5)
        self.stream = self.sock.makefile("rw", encoding="utf-8", newline="\n")

    def send(self, **payload):
        self.stream.write(json.dumps(payload, allow_nan=False) + "\n")
        self.stream.flush()
        line = self.stream.readline()
        if not line:
            raise ConnectionError("Verbindung vom Spiel geschlossen")
        response = json.loads(line)
        if not response.get("ok"):
            raise RuntimeError(response.get("error") or "Anfrage fehlgeschlagen")
        return response

    def close(self):
        self.stream.close()
        self.sock.close()


def inspect(floppy, exercise=False):
    schema = floppy.send(cmd="schema")
    if not isinstance(schema.get("categories"), list) or not isinstance(schema.get("values"), dict):
        raise ValueError("Ungültiges Schema: Kategorien/Werte fehlen")
    options = [o for category in schema["categories"] for o in category["options"]]
    ids = [o["id"] for o in options]
    if len(ids) != len(set(ids)):
        raise ValueError("Doppelte Optionskennungen im Schema")
    print(f"Spiel: {schema.get('game')} – {len(options)} Optionen; bereit: {schema.get('ready', False)}")
    if not exercise:
        print("Schema geprüft. Keine Spieleinstellung geändert.")
        return
    allowed = REVERSIBLE.get(schema.get("game"), set())
    tested = 0
    for option in options:
        oid = option["id"]
        if oid not in allowed:
            continue
        before = schema["values"].get(oid, {})
        if not before.get("available", True):
            continue
        kind = option["kind"]
        if kind == "Toggle" and isinstance(before.get("bool"), bool):
            field, original, target = "bool", before["bool"], not before["bool"]
        elif kind == "Slider" and isinstance(before.get("number"), (int, float)):
            field, original = "number", before["number"]
            target = option["min"] if original != option["min"] else option["max"]
        else:
            raise ValueError(f"Nicht reversibler oder unvollständiger Optionswert: {oid}")
        try:
            floppy.send(cmd="set", id=oid, **{field: target})
        finally:
            # Also attempt restoration when a response is lost after the change was applied.
            floppy.send(cmd="set", id=oid, **{field: original})
        tested += 1
    print(f"{tested} freigegebene Overlay-Optionen geprüft und zurückgesetzt.")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--exercise", action="store_true", help="Zusätzlich ausschließlich freigegebene Overlay-Optionen kurz umschalten")
    parser.add_argument("--port", type=int, default=PORT)
    args = parser.parse_args(argv)
    floppy = None
    try:
        floppy = Floppy(args.port)
        inspect(floppy, args.exercise)
        return 0
    except (OSError, ValueError, KeyError, TypeError, RuntimeError) as error:
        print(f"FEHLER: {error}", file=sys.stderr)
        return 1
    finally:
        if floppy is not None:
            floppy.close()


if __name__ == "__main__":
    sys.exit(main())
