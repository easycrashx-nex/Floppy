extends Node

# Floppy für Project P.I.T.T. (Godot 4.7)
#
# Godot lädt genau ein .pck neben der Exe - ein Einhängen von außen wie BepInEx bei
# Unity gibt es nicht. Dieses Skript liegt deshalb im Paket und ist als Autostart
# eingetragen; damit läuft es ab dem Spielstart mit.
#
# Es bedient dasselbe Protokoll wie die Unity-Module: zeilenweise JSON über
# 127.0.0.1:47821. Dadurch funktioniert die Floppy-Desktop-App hier unverändert,
# obwohl eine völlig andere Engine darunter liegt.
#
# Das Menü zeichnet ausschließlich die Desktop-App. Dieses Backend gibt ihr nur
# vorübergehend die Eingabe frei; der Fenstermodus des Spiels bleibt unverändert.

const PORT := 47821
const OVERLAY_LEASE_MS := 3000
const BERICHT := "user://floppy_bericht.txt"

## Was sich herbeirufen lässt, nach Rubriken sortiert.
##
## Die Kennungen sind die Pool-Namen aus ObjectPoolManager.POOL_CONFIG - keine
## geratenen Namen, sondern die, mit denen das Spiel selbst seine Objekte anlegt.
const SPAWN := {
	"Produkte": [
		["duck", "Ente"],
		["duck_luck", "Ente (Glück)"],
		["flat_duck", "Platte Ente"],
		["flat_duck_luck", "Platte Ente (Glück)"],
		["pinata", "Piñata"],
		["pinata_luck", "Piñata (Glück)"],
		["candy", "Bonbon"],
		["candy_luck", "Bonbon (Glück)"],
		["cash_register", "Registrierkasse"],
		["cash_register_luck", "Registrierkasse (Glück)"],
		["anomaly", "Anomalie"],
		["anomaly_luck", "Anomalie (Glück)"],
	],
	"Erze": [
		["ore_duck", "Enten-Erz"],
		["ore_pinata", "Piñata-Erz"],
		["ore_register", "Kassen-Erz"],
		["ore_anomaly", "Anomalie-Erz"],
	],
	"Werkzeuge": [
		["tool_fan", "Ventilator"],
		["tool_fan_2", "Ventilator II"],
		["tool_fan_3", "Ventilator III"],
		["tool_panel", "Platte"],
		["tool_panel_2", "Platte II"],
		["tool_panel_3", "Platte III"],
		["tool_panel_4", "Platte IV"],
		["tool_panel_5", "Platte V"],
		["tool_panel_6", "Platte VI"],
		["tool_panel_7", "Platte VII"],
		["tool_piston", "Kolben"],
		["tool_piston_2", "Kolben II"],
		["tool_piston_3", "Kolben III"],
		["tool_broom", "Besen"],
		["tool_broom_2", "Besen II"],
		["tool_broom_3", "Besen III"],
		["tool_cannon", "Kanone"],
		["tool_cannon_2", "Kanone II"],
		["tool_magnet", "Magnet"],
		["tool_magnet_2", "Magnet II"],
		["tool_vacuum", "Sauger"],
		["tool_vacuum_2", "Sauger II"],
		["tool_spring_pad", "Sprungfeld"],
		["tool_spring_pad_2", "Sprungfeld II"],
		["tool_box", "Kiste"],
		["tool_box_2", "Kiste II"],
		["tool_box_3", "Kiste III"],
		["tool_box_4", "Kiste IV"],
		["tool_sponge", "Schwamm"],
		["tool_portal", "Portal"],
		["tool_remote", "Fernbedienung"],
		["tool_qa_arm", "Prüfarm"],
		["tool_orbit_tool", "Kreiselwerkzeug"],
		["tool_tractor_beam", "Traktorstrahl"],
		["tool_gravity_inverter", "Schwerkraftwender"],
		["tool_scheduler", "Schichtplaner"],
		["tool_auto_crafter", "Selbstbauer"],
	],
	"Spielzeug": [
		["toy_basketball_hoop", "Basketballkorb"],
		["toy_bumper", "Prellbock"],
		["toy_disco_ball", "Discokugel"],
		["toy_golf_club", "Golfschläger"],
		["toy_golden_duck_statue", "Goldene Entenstatue"],
		["toy_speed_radar", "Tempomesser"],
		["toy_stat_sign", "Anzeigetafel"],
		["toy_stat_sign_big", "Große Anzeigetafel"],
		["toy_feed_camera", "Kamera"],
		["toy_feed_screen", "Bildschirm"],
		["toy_feed_screen_big", "Großer Bildschirm"],
	],
	"Kisten": [
		["toy_gambling_wodden_crate", "Holzkiste"],
		["toy_gambling_metal_crate", "Metallkiste"],
		["toy_gambling_plush_crate", "Plüschkiste"],
		["toy_gambling_anomaly_crate", "Anomaliekiste"],
	],
	"Sonstiges": [
		["keycard", "Schlüsselkarte"],
		["recycle_cube", "Recyclingwürfel"],
		["debris_chunk", "Trümmerstück"],
		["debris_chunk_large", "Großes Trümmerstück"],
	],
}

var _server: TCPServer
var _clients: Array[StreamPeerTCP] = []
var _puffer: Dictionary = {}          # Client -> angefangene Zeile

var _auswahl: Dictionary = {}         # Cheat-Id -> gewählter Eintrag (Auswahlfelder)
var _spawn_anzahl := 1
var _produktfaktor := 1.0
var _produktwert_basis := {}
var _letzter_faktor := 1.0
var _produktuhr := 0.0
var _session_id := "%d-%s-%d" % [OS.get_process_id(), str(Time.get_unix_time_from_system()), Time.get_ticks_usec()]
var _overlay_owner: StreamPeerTCP
var _overlay_until := 0
var _mouse_before := Input.MOUSE_MODE_VISIBLE
var _paused_by_us := false
var _input_before: Array[Dictionary] = []

var _cheats: Array = []               # Liste von Cheat-Beschreibungen
var _nach_id: Dictionary = {}
var _kategorien: Array = []


# ---------------------------------------------------------------- Start

func _ready() -> void:
	name = "Floppy"
	process_mode = Node.PROCESS_MODE_ALWAYS      # auch im Pausemenü weiterlaufen

	_baue_cheats()

	_server = TCPServer.new()
	var fehler := _server.listen(PORT, "127.0.0.1")
	if fehler == OK:
		print("[Floppy] bereit auf 127.0.0.1:%d - %d Cheats" % [PORT, _cheats.size()])
	else:
		print("[Floppy] Port %d nicht verfügbar (%d)" % [PORT, fehler])


func _process(delta: float) -> void:
	_check_overlay_lease()
	if _overlay_owner != null:
		_hold_overlay_input()
	# Der Faktor auf die Produktwerte muss nachgehalten werden: Beim Rundenstart setzt
	# das Spiel die Werte neu, und unser Aufschlag wäre sonst weg.
	if _produktfaktor != 1.0:
		_produktuhr += delta
		if _produktuhr > 1.0:
			_produktuhr = 0.0
			_setze_produktfaktor(_produktfaktor)

	if _server == null:
		return

	while _server.is_connection_available():
		var c := _server.take_connection()
		_clients.append(c)
		_puffer[c] = ""

	var weg: Array[StreamPeerTCP] = []
	for c in _clients:
		c.poll()
		if c.get_status() != StreamPeerTCP.STATUS_CONNECTED:
			weg.append(c)
			continue

		var da := c.get_available_bytes()
		if da > 0:
			_puffer[c] = _puffer[c] + c.get_utf8_string(da)
			_verarbeite(c)

	for c in weg:
		if c == _overlay_owner:
			_release_overlay()
		_clients.erase(c)
		_puffer.erase(c)


func _verarbeite(c: StreamPeerTCP) -> void:
	while true:
		var text: String = _puffer[c]
		var pos := text.find("\n")
		if pos < 0:
			return

		var zeile := text.substr(0, pos).strip_edges()
		_puffer[c] = text.substr(pos + 1)

		if zeile.is_empty():
			continue

		var antwort := _behandle(zeile, c)
		c.put_data((antwort + "\n").to_utf8_buffer())


# ---------------------------------------------------------------- Protokoll

func _behandle(zeile: String, client: StreamPeerTCP) -> String:
	var anfrage = JSON.parse_string(zeile)
	if typeof(anfrage) != TYPE_DICTIONARY:
		return _fehler("Anfrage nicht lesbar")

	match str(anfrage.get("cmd", "")).to_lower():
		"ping":
			return JSON.stringify({"ok": true, "pong": true})
		"schema":
			return _schema()
		"state":
			return _zustand()
		"overlay":
			return _set_overlay(anfrage, client)
		"set":
			return _setzen(anfrage)
		"invoke":
			return _ausloesen(str(anfrage.get("id", "")))
		"dump":
			return _dump(str(anfrage.get("objekt", "")))
		_:
			return _fehler("Unbekannter Befehl")


func _schema() -> String:
	var kategorien := []

	for kat in _kategorien:
		var optionen := []
		for c in kat["cheats"]:
			optionen.append({
				"id": c["id"],
				"label": c["label"],
				"description": c.get("description", ""),
				"kind": c["kind"],
				"scope": c.get("scope", "OnlyMe"),
				"min": c.get("min", 0.0),
				"max": c.get("max", 100.0),
				"step": c.get("step", 1.0),
				"choices": c["auswahlen"].call() if c.has("auswahlen") else [],
			})
		kategorien.append({"name": kat["name"], "options": optionen})

	var bereit := _im_spiel()
	return JSON.stringify({
		"ok": true,
		"game": "Project P.I.T.T.",
		"gameId": "Project P.I.T.T.",
		"sessionId": _session_id,
		"schemaVersion": 1,
		"processId": OS.get_process_id(),
		"externalOverlay": true,
		"overlayOpen": _overlay_owner != null,
		"categories": kategorien,
		"ready": bereit,
		"status": "Bereit" if bereit else "Warte auf Spielstart",
		"values": _werte(),
	})


func _zustand() -> String:
	var bereit := _im_spiel()
	return JSON.stringify({
		"ok": true,
		"gameId": "Project P.I.T.T.",
		"sessionId": _session_id,
		"schemaVersion": 1,
		"processId": OS.get_process_id(),
		"externalOverlay": true,
		"overlayOpen": _overlay_owner != null,
		"ready": bereit,
		"status": "Bereit" if bereit else "Warte auf Spielstart",
		"values": _werte(),
	})


# ---------------------------------------------------------------- Externes Menü: befristete Eingabefreigabe

func _set_overlay(request: Dictionary, client: StreamPeerTCP) -> String:
	_check_overlay_lease()
	if typeof(request.get("sessionId")) != TYPE_STRING or request["sessionId"] != _session_id:
		return _fehler("Overlay-Anfrage gehört nicht zur aktuellen Spielsitzung")
	if typeof(request.get("open")) != TYPE_BOOL:
		return _fehler("Overlay benötigt open als booleschen Wert")
	if client == null or client.get_status() != StreamPeerTCP.STATUS_CONNECTED:
		return _fehler("Overlay benötigt eine aktive Verbindung")
	if _overlay_owner != null and _overlay_owner != client:
		return _fehler("Overlay wird bereits von einer anderen Verbindung bedient")
	if request["open"]:
		if _overlay_owner == null:
			_mouse_before = Input.get_mouse_mode()
			_overlay_owner = client
		_overlay_until = Time.get_ticks_msec() + OVERLAY_LEASE_MS
		_hold_overlay_input()
	else:
		_release_overlay()
	return JSON.stringify({"ok": true, "overlayOpen": _overlay_owner != null,
		"sessionId": _session_id, "leaseMs": OVERLAY_LEASE_MS})


func _check_overlay_lease() -> void:
	if _overlay_owner != null and Time.get_ticks_msec() >= _overlay_until:
		_release_overlay()


func _hold_overlay_input() -> void:
	# Das Spiel darf während der Bedienung weder laufen noch die Maus erneut fangen.
	# Eine vorhandene Pause bleibt erhalten; nur eigene Änderungen werden rückgängig.
	if _im_spiel() and not get_tree().paused:
		get_tree().paused = true
		_paused_by_us = true
	_remember_input_flag(get_node_or_null("/root/MenuManager"), "game_paused")
	_remember_input_flag(get_tree().get_first_node_in_group("player"), "movement_blocked")
	if Input.get_mouse_mode() != Input.MOUSE_MODE_VISIBLE:
		Input.set_mouse_mode(Input.MOUSE_MODE_VISIBLE)


func _remember_input_flag(node: Node, property: String) -> void:
	if not is_instance_valid(node):
		return
	for saved in _input_before:
		if saved["node"].get_ref() == node and saved["property"] == property:
			node.set(property, true)
			return
	# Prüfen statt unbekannte Eigenschaften auf fremden Spielknoten zu erzeugen.
	for entry in node.get_property_list():
		if entry["name"] == property and entry["type"] == TYPE_BOOL:
			if node.get(property) == false:
				_input_before.append({"node": weakref(node), "property": property, "value": false})
				node.set(property, true)
			return


func _release_overlay() -> void:
	if _overlay_owner == null:
		return
	_overlay_owner = null
	_overlay_until = 0
	for saved in _input_before:
		var node = saved["node"].get_ref()
		if is_instance_valid(node) and node.get(saved["property"]) == true:
			node.set(saved["property"], saved["value"])
	_input_before.clear()
	if _paused_by_us:
		get_tree().paused = false
		_paused_by_us = false
	if Input.get_mouse_mode() == Input.MOUSE_MODE_VISIBLE:
		Input.set_mouse_mode(_mouse_before)


func _input(_event: InputEvent) -> void:
	if _overlay_owner != null:
		get_viewport().set_input_as_handled()


func _exit_tree() -> void:
	_release_overlay()
	for client in _clients:
		client.disconnect_from_host()
	_clients.clear()
	_puffer.clear()
	if _server != null:
		_server.stop()


func _werte() -> Dictionary:
	var d := {}
	for c in _cheats:
		var zahl := 0.0
		var ja := false
		var text := ""

		if c.has("lies"):
			var v = c["lies"].call()
			match typeof(v):
				TYPE_BOOL: ja = v
				TYPE_INT, TYPE_FLOAT: zahl = float(v)
				TYPE_STRING: text = v

		var auswahlen: Array = c["auswahlen"].call() if c.has("auswahlen") else []

		d[c["id"]] = {
			"bool": ja,
			"number": zahl,
			"text": text,
			"choice": _auswahl.get(c["id"], 0),
			"choiceCount": auswahlen.size(),
			# Die Liste selbst mitschicken: bei abhängigen Listen ändert sich oft nur
			# der Inhalt, nicht die Länge - an der Anzahl allein merkt es der Client nicht.
			"choices": auswahlen,
			"share": false,
			"available": _im_spiel(),
		}
	return d


func _setzen(anfrage: Dictionary) -> String:
	var id := str(anfrage.get("id", ""))
	if not _nach_id.has(id):
		return _fehler("Diesen Cheat gibt es nicht: " + id)

	var c: Dictionary = _nach_id[id]

	if c.has("schreibe"):
		if anfrage.has("bool"):
			c["schreibe"].call(bool(anfrage["bool"]))
		elif anfrage.has("number"):
			c["schreibe"].call(float(anfrage["number"]))
		elif anfrage.has("text"):
			c["schreibe"].call(str(anfrage["text"]))

	if anfrage.has("choice"):
		var index := int(anfrage["choice"])
		_auswahl[id] = index
		if c.has("gewaehlt"):
			c["gewaehlt"].call(index)

	return JSON.stringify({"ok": true, "values": _werte()})


func _ausloesen(id: String) -> String:
	if not _nach_id.has(id):
		return _fehler("Diesen Cheat gibt es nicht: " + id)

	var c: Dictionary = _nach_id[id]
	var meldung := ""

	if c.has("tue"):
		var r = c["tue"].call()
		if typeof(r) == TYPE_STRING:
			meldung = r

	var antwort := {"ok": true, "values": _werte()}
	if not meldung.is_empty():
		antwort["message"] = meldung
	return JSON.stringify(antwort)


## Zeigt, was im Spiel überhaupt da ist.
##
## Ohne so etwas rät man Feldnamen - und rät falsch. Ohne Objektnamen liefert die
## Antwort die Liste der Autostart-Objekte, mit Namen deren Felder und Methoden.
func _dump(objekt: String) -> String:
	if objekt.is_empty():
		var namen := []
		for kind in get_tree().root.get_children():
			namen.append(kind.name)
		return JSON.stringify({"ok": true, "objekte": namen})

	var n := _zustand_knoten(objekt)
	if n == null:
		return _fehler("Kein Objekt namens " + objekt)

	var felder := {}
	for eintrag in n.get_property_list():
		if not (eintrag.usage & PROPERTY_USAGE_SCRIPT_VARIABLE):
			continue

		var wert = n.get(eintrag.name)
		if typeof(wert) == TYPE_OBJECT and wert != null:
			var beschreibung := "<%s>" % wert.get_class()
			if wert.has_method("to_plain_string"):
				beschreibung += " " + str(wert.to_plain_string())
			felder[eintrag.name] = beschreibung
		else:
			felder[eintrag.name] = str(wert).left(80)

	var methoden := []
	for eintrag in n.get_method_list():
		var mname := str(eintrag["name"])
		if mname.begins_with("_") or mname.begins_with("@"):
			continue
		methoden.append("%s(%d)" % [mname, (eintrag["args"] as Array).size()])

	return JSON.stringify({
		"ok": true, "objekt": objekt, "klasse": n.get_class(),
		"felder": felder, "methoden": methoden,
	})


func _fehler(text: String) -> String:
	return JSON.stringify({"ok": false, "error": text})


# ---------------------------------------------------------------- Zugänge

func _zustand_knoten(pfad: String) -> Node:
	return get_node_or_null("/root/" + pfad)


func _im_spiel() -> bool:
	var gs := _zustand_knoten("GameState")
	return gs != null and gs.get("is_game_started") == true


## Liest eine Eigenschaft eines Autostart-Objekts.
func _lies(objekt: String, feld: String, ersatz = 0.0):
	var n := _zustand_knoten(objekt)
	if n == null:
		return ersatz
	var v = n.get(feld)
	return ersatz if v == null else v


## Schreibt eine Eigenschaft eines Autostart-Objekts.
func _schreibe(objekt: String, feld: String, wert) -> void:
	var n := _zustand_knoten(objekt)
	if n != null:
		n.set(feld, wert)


## Macht aus einem beliebigen Spielwert eine Zahl.
##
## Das Geld liegt in P.I.T.T. nicht als Zahl, sondern als BigInt-Objekt vor -
## sonst wäre bei ein paar Milliarden Schluss. Ein schlichtes float() darauf
## scheitert, deshalb dieser Umweg.
func _zahlwert(v) -> float:
	if v == null:
		return 0.0
	if typeof(v) == TYPE_OBJECT:
		if v.has_method("to_float"):
			return float(v.to_float())
		return 0.0
	if typeof(v) == TYPE_BOOL:
		return 1.0 if v else 0.0
	return float(v)


## Ruft eine Methode des Spiels auf und füllt fehlende Argumente selbst auf.
##
## Die Signaturen kennen wir nur aus dem übersetzten Skript - ein falsch gezähltes
## Argument wäre ein Laufzeitfehler. Deshalb fragen wir die Methodenliste.
func _rufe(objekt: String, methode: String, args: Array = []):
	var n := _zustand_knoten(objekt)
	if n == null or not n.has_method(methode):
		return null

	for eintrag in n.get_method_list():
		if eintrag["name"] != methode:
			continue

		var erwartet: Array = eintrag["args"]
		var noetig: int = erwartet.size() - (eintrag["default_args"] as Array).size()

		var liste := args.duplicate()
		while liste.size() < noetig:
			liste.append(type_convert(null, erwartet[liste.size()]["type"]))
		if liste.size() > erwartet.size():
			liste.resize(erwartet.size())

		return n.callv(methode, liste)

	return n.callv(methode, args)


# ---------------------------------------------------------------- Geld

func _geld() -> float:
	return _zahlwert(_lies("GameState", "money", 0.0))


func _geld_text() -> String:
	var v = _lies("GameState", "money", 0.0)
	if typeof(v) == TYPE_OBJECT and v.has_method("to_plain_string"):
		return str(v.to_plain_string())
	return _zahl_text(_zahlwert(v))


## Setzt das Geld auf einen Zielwert.
##
## Direkt schreiben geht nicht: money ist ein BigInt, und die Anzeige hängt am
## Signal money_updated. Über add_money/spend bleibt beides stimmig.
func _setze_geld(ziel: float) -> void:
	var unterschied := ziel - _geld()
	if absf(unterschied) < 1.0:
		return

	if unterschied > 0.0:
		_rufe("GameState", "add_money", [unterschied])
	else:
		_rufe("GameState", "spend", [-unterschied])


# ---------------------------------------------------------------- Produktwerte

## Was ein Produkt einbringt, steht nicht als eine Zahl da.
##
## money_per_product ist ein Wörterbuch mit einem eigenen Wert je Produktsorte - und
## jeder davon wieder ein BigInt. Ein einzelner Regler wäre also falsch; stattdessen
## nehmen wir einen Faktor auf die ursprünglichen Werte.
func _produktwerte() -> Dictionary:
	var d = _lies("GameState", "money_per_product", {})
	return d if d is Dictionary else {}


## Setzt den Faktor auf die Ausgangswerte an.
##
## Der Ausgangswert muss mitwandern: Im Hauptmenü stehen alle Produktwerte auf 0, und
## erst der Rundenstart füllt sie. Hätten wir sie einmal gemerkt, bliebe der Faktor
## für immer eine Multiplikation mit null. Deshalb prüfen wir bei jedem Anwenden, ob
## der Wert noch der ist, den wir zuletzt hingeschrieben haben - wenn nicht, hat das
## Spiel ihn selbst gesetzt und wir nehmen ihn als neuen Ausgangswert.
func _setze_produktfaktor(faktor: float) -> void:
	_produktfaktor = maxf(0.0, faktor)

	var werte := _produktwerte()

	for sorte in werte:
		var alt = werte[sorte]
		if typeof(alt) != TYPE_OBJECT or not alt.has_method("from_int"):
			continue

		var jetzt := _zahlwert(alt)
		var erwartet: float = float(_produktwert_basis.get(sorte, -1.0)) * _letzter_faktor

		if not _produktwert_basis.has(sorte) or absf(jetzt - erwartet) > 0.5:
			_produktwert_basis[sorte] = jetzt

		var ausgang: float = float(_produktwert_basis[sorte])
		werte[sorte] = alt.from_int(int(maxf(0.0, ausgang * _produktfaktor)))

	_letzter_faktor = _produktfaktor


func _produktwert_text() -> String:
	var werte := _produktwerte()
	if werte.is_empty():
		return "noch nichts geladen"

	var teile := []
	for sorte in werte:
		teile.append("%s %s" % [sorte, _zahl_text(_zahlwert(werte[sorte]))])
		if teile.size() >= 3:
			break

	return ", ".join(teile)


# ---------------------------------------------------------------- Stufen

## Hebt alles in einem Wörterbuch auf die höchste Stufe.
##
## Werkzeuge, Ausbauten, Spielzeug und Kombos sind gleich aufgebaut: je Eintrag eine
## Stufe, eine Preisliste und ein Schalter. Die Länge der Preisliste ist die
## Höchststufe. Produkte haben dieselbe Struktur noch einmal eine Ebene tiefer.
func _stufe_hoch(eintrag: Dictionary) -> int:
	var geaendert := 0

	if eintrag.has("prices") and eintrag.has("level"):
		var hoechste: int = (eintrag["prices"] as Array).size()
		if int(eintrag["level"]) < hoechste:
			eintrag["level"] = hoechste
			geaendert += 1

	if eintrag.has("enabled") and eintrag["enabled"] != true:
		eintrag["enabled"] = true
		geaendert += 1

	for schluessel in eintrag:
		if eintrag[schluessel] is Dictionary:
			geaendert += _stufe_hoch(eintrag[schluessel])

	return geaendert


func _alles_hoch(feld: String) -> String:
	var d = _lies("GameState", feld, {})
	if not (d is Dictionary):
		return "nichts gefunden"

	var geaendert := 0
	for schluessel in d:
		if d[schluessel] is Dictionary:
			geaendert += _stufe_hoch(d[schluessel])

	return "%d Stufen angehoben" % geaendert


# ---------------------------------------------------------------- Herbeirufen

func _rubriken() -> Array:
	return SPAWN.keys()


func _rubrik_name() -> String:
	var namen: Array = _rubriken()
	var i: int = int(_auswahl.get("spawn.rubrik", 0))
	return str(namen[clampi(i, 0, namen.size() - 1)]) if not namen.is_empty() else ""


func _rubrik_eintraege() -> Array:
	return SPAWN.get(_rubrik_name(), [])


func _spawn_namen() -> Array:
	var namen := []
	for eintrag in _rubrik_eintraege():
		namen.append(eintrag[1])
	return namen


## Wohin das Ding fällt: knapp vor die Kamera, leicht erhöht.
##
## Vor die Kamera und nicht vor den Körper, weil sich beim Umsehen nur die Kamera
## dreht - sonst landet alles hinter einem.
func _spawn_ort() -> Vector3:
	var baum := get_tree()

	for gruppe in ["main_camera", "player"]:
		var n = baum.get_first_node_in_group(gruppe)
		if n is Node3D:
			var t: Transform3D = (n as Node3D).global_transform
			return t.origin - t.basis.z * 2.5 + Vector3(0.0, 0.5, 0.0)

	return Vector3.ZERO


func _spawne(anzahl: int) -> String:
	var eintraege: Array = _rubrik_eintraege()
	if eintraege.is_empty():
		return "Nichts ausgewählt"

	var i: int = int(_auswahl.get("spawn.was", 0))
	var eintrag: Array = eintraege[clampi(i, 0, eintraege.size() - 1)]

	var ort := _spawn_ort()
	var gemacht := 0

	for n in range(maxi(1, anzahl)):
		# Etwas versetzen, sonst stecken sie ineinander und schießen auseinander
		var versatz := Vector3(randf_range(-0.4, 0.4), 0.35 * n, randf_range(-0.4, 0.4))
		if _rufe("ObjectPoolManager", "spawn", [eintrag[0], ort + versatz]) != null:
			gemacht += 1

	if gemacht == 0:
		return "%s: Der Vorrat des Spiels ist gerade leer" % eintrag[1]
	return "%dx %s da" % [gemacht, eintrag[1]]


# ---------------------------------------------------------------- Cheats

func _zahl(id: String, label: String, objekt: String, feld: String,
		   min_wert: float, max_wert: float, schritt: float, ganz := false,
		   beschreibung := "") -> Dictionary:
	return {
		"id": id, "label": label, "kind": "Slider", "description": beschreibung,
		"min": min_wert, "max": max_wert, "step": schritt,
		"lies": func(): return float(_lies(objekt, feld, 0.0)),
		"schreibe": func(v): _schreibe(objekt, feld, int(v) if ganz else float(v)),
	}


## Zahlenregler, der nicht an einem Feld hängt, sondern an eigenem Lesen/Schreiben.
func _zahl_frei(id: String, label: String, lies: Callable, schreibe: Callable,
				min_wert: float, max_wert: float, schritt: float,
				beschreibung := "") -> Dictionary:
	return {
		"id": id, "label": label, "kind": "Slider", "description": beschreibung,
		"min": min_wert, "max": max_wert, "step": schritt,
		"lies": lies, "schreibe": schreibe,
	}


func _schalter(id: String, label: String, objekt: String, feld: String,
			   beschreibung := "") -> Dictionary:
	return {
		"id": id, "label": label, "kind": "Toggle", "description": beschreibung,
		"lies": func(): return _lies(objekt, feld, false) == true,
		"schreibe": func(v): _schreibe(objekt, feld, bool(v)),
	}


## Zahlenfeld zum Eintippen.
##
## Für große Bereiche ist ein Regler unbrauchbar: Bei 0 bis 10 Millionen entscheidet
## ein Pixel über hunderttausend Dollar. Hier tippt man den Betrag einfach ein.
func _eingabe(id: String, label: String, lies: Callable, schreibe: Callable,
			  min_wert: float, max_wert: float, beschreibung := "") -> Dictionary:
	return {
		"id": id, "label": label, "kind": "Number", "description": beschreibung,
		"min": min_wert, "max": max_wert, "step": 1.0,
		"lies": lies, "schreibe": schreibe,
	}


func _auswahlfeld(id: String, label: String, auswahlen: Callable,
				  beschreibung := "") -> Dictionary:
	return {
		"id": id, "label": label, "kind": "Choice", "description": beschreibung,
		"auswahlen": auswahlen,
	}


func _anzeige(id: String, label: String, quelle: Callable) -> Dictionary:
	return {"id": id, "label": label, "kind": "Info", "lies": quelle}


func _knopf(id: String, label: String, tue: Callable, beschreibung := "") -> Dictionary:
	return {"id": id, "label": label, "kind": "Button",
			"description": beschreibung, "tue": tue}


func _baue_cheats() -> void:
	_kategorien = [
		{"name": "Geld", "cheats": [
			_anzeige("money.info", "Aktueller Stand", func():
				return "%s$   |   Bank %s   |   Mine %s   |   Produkte %s" % [
					_geld_text(),
					_zahl_text(_lies("GameState", "bank", 0.0)),
					_zahl_text(_lies("GameState", "mining_bank", 0)),
					_zahl_text(_lies("GameState", "total_products", 0.0))]),

			_eingabe("money.amount", "Geld",
				func(): return _geld(),
				func(v): _setze_geld(float(v)),
				0.0, 1000000000000.0,
				"Betrag eintippen und Eingabetaste drücken - genau das, was im Spiel " +
				"oben rechts steht."),

			_knopf("money.plus1k", "+ 1.000", func():
				_rufe("GameState", "add_money", [1000.0])
				return _geld_text() + "$"),

			_knopf("money.plus10k", "+ 10.000", func():
				_rufe("GameState", "add_money", [10000.0])
				return _geld_text() + "$"),

			_knopf("money.million", "+ 1 Million", func():
				_rufe("GameState", "add_money", [1000000.0])
				return _geld_text() + "$"),

			_anzeige("money.produktwert", "Wert je Produkt", func():
				return _produktwert_text()),

			_zahl_frei("money.faktor", "Produktwert-Faktor",
				func(): return _produktfaktor,
				func(v): _setze_produktfaktor(float(v)),
				1.0, 1000.0, 1.0,
				"Vervielfacht, was jede Produktsorte einbringt. 1 ist der Ausgangswert."),

			_eingabe("money.bank", "Bank",
				func(): return _zahlwert(_lies("GameState", "bank", 0.0)),
				func(v): _schreibe("GameState", "bank", float(v)),
				0.0, 1000000000000.0,
				"Das eingezahlte Guthaben - nicht das Geld in der Hand."),

			_eingabe("money.mining", "Minen-Guthaben",
				func(): return _zahlwert(_lies("GameState", "mining_bank", 0)),
				func(v): _schreibe("GameState", "mining_bank", int(v)),
				0.0, 1000000000.0),

			_eingabe("money.products", "Produkte gesamt",
				func(): return _zahlwert(_lies("GameState", "total_products", 0.0)),
				func(v): _schreibe("GameState", "total_products", float(v)),
				0.0, 1000000000.0),
		]},

		{"name": "Freischalten", "cheats": [
			_schalter("unlock.sandbox", "Sandkasten-Modus", "GameState", "sandbox_mode",
					  "Der freie Modus des Spiels."),
			_schalter("unlock.uv", "UV-Lampe", "GameState", "uv_unlocked"),
			_schalter("unlock.torch", "Schweißbrenner", "GameState", "torch_unlocked"),
			_schalter("unlock.pickaxe", "Spitzhacke gefunden", "GameState", "pickaxe_found"),
			_zahl("unlock.pickaxetier", "Stufe der Spitzhacke", "GameState", "pickaxe_tier",
				  1.0, 10.0, 1.0, true),
			_schalter("unlock.elevator", "Aufzug geknackt", "GameState", "elevator_hacked"),
			_schalter("unlock.keypad", "Zahlenfeld sichtbar", "GameState", "keypad_revealed"),
			_schalter("unlock.uvlamp", "UV-Lampe ausgegeben", "GameState", "uv_lamp_dispensed"),

			_anzeige("unlock.code", "Fluchtcode", func():
				return str(_lies("GameState", "escape_code", "?"))),

			_anzeige("unlock.count", "Erreichte Meilensteine", func():
				var l = _lies("UnlockManager", "unlocked_milestones", [])
				return str((l as Array).size()) if l is Array else "?"),

			_knopf("unlock.all", "Alles freischalten", func():
				_rufe("UnlockManager", "force_unlock_all")
				var l = _lies("UnlockManager", "unlocked_milestones", [])
				return "Freigeschaltet: %d" % ((l as Array).size() if l is Array else 0),
				"Schaltet jeden Meilenstein des Spiels frei - Werkbänke, Werkzeuge, Ausbauten."),

			_knopf("unlock.werkzeuge", "Werkzeuge auf Höchststufe", func():
				return _alles_hoch("tools")),

			_knopf("unlock.ausbauten", "Ausbauten auf Höchststufe", func():
				return _alles_hoch("upgrades")),

			_knopf("unlock.spielzeug", "Spielzeug auf Höchststufe", func():
				return _alles_hoch("toys")),

			_knopf("unlock.kombos", "Kombos auf Höchststufe", func():
				return _alles_hoch("combos")),

			_knopf("unlock.produkte", "Produkte auf Höchststufe", func():
				return _alles_hoch("products"),
				"Bautempo und Wert jeder Produktsorte auf das Maximum."),

			_knopf("unlock.check", "Fortschritt neu prüfen", func():
				_rufe("UnlockManager", "check_milestones")
				return "Geprüft"),
		]},

		{"name": "Werkzeuge", "cheats": [
			_zahl("tool.channels", "Fernbedienung: Kanäle", "GameState",
				  "remote_max_channels", 1.0, 32.0, 1.0, true),
			_zahl("tool.slots", "Fernbedienung: Plätze je Kanal", "GameState",
				  "remote_slots_per_channel", 1.0, 32.0, 1.0, true),
			_schalter("tool.pulse", "Impuls freigeschaltet", "GameState", "remote_pulse_enabled"),
			_zahl("tool.toggle", "Gleichzeitige Werkzeuge", "GameState",
				  "max_simultaneous_toggle_tools", 0.0, 32.0, 1.0, true),
			_zahl("tool.feeds", "Kameras auf Distanz", "GameState",
				  "long_range_feeds", 0.0, 32.0, 1.0, true),
			_zahl("tool.ricochet", "Abpraller-Kameras", "GameState",
				  "panel_ricochet_feeds", 0.0, 32.0, 1.0, true),

			_anzeige("tool.rack", "Regal-Kapazität", func():
				var n = _rufe("GameState", "panel_rack_capacity")
				return str(n) if n != null else "?"),

			_zahl_frei("tool.tier", "Fernbedienungs-Stufe",
				func(): return _zahlwert(_lies("GameState", "remote_max_channels", 1)),
				func(v): _rufe("GameState", "apply_remote_tier", [int(v)]),
				1.0, 3.0, 1.0,
				"Setzt Kanäle und Plätze auf einen Rutsch - so wie das Spiel es beim Ausbau tut."),

		]},

		{"name": "Kombo", "cheats": [
			_anzeige("combo.info", "Aktuelle Kombo", func():
				return "%d fach   |   Mega %s   |   Kette %d" % [
					int(_lies("ComboSystem", "current_combo", 0)),
					str(_lies("ComboSystem", "mega_combo_multiplier", 1.0)).left(5),
					int(_lies("ComboSystem", "current_chain_level", 0))]),

			_zahl("combo.current", "Kombo", "ComboSystem", "current_combo",
				  0.0, 10000.0, 10.0, true),
			_zahl("combo.mega", "Mega-Multiplikator", "ComboSystem",
				  "mega_combo_multiplier", 1.0, 100.0, 0.5),
			_zahl("combo.chain", "Ketten-Bonus", "ComboSystem",
				  "chain_combo_bonus", 0.0, 100.0, 0.5),
			_zahl("combo.bowling", "Bowling-Multiplikator", "ComboSystem",
				  "bowling_multiplier", 1.0, 50.0, 0.5),
			_zahl("combo.diversity", "Vielfalt-Multiplikator", "ComboSystem",
				  "diversity_multiplier", 1.0, 50.0, 0.5),
			_schalter("combo.mega.on", "Mega-Kombo aktiv", "ComboSystem", "mega_combo_active"),

			_anzeige("combo.buffs", "Aktive Boni", func():
				var b = _lies("GameState", "active_buffs", {})
				if not (b is Dictionary) or (b as Dictionary).is_empty():
					return "keine"
				return ", ".join((b as Dictionary).keys())),

			_knopf("buff.value", "Bonus: Produktwert x10", func():
				_rufe("GameState", "activate_buff", ["product_value", 10.0, 600.0])
				return "Läuft 10 Minuten"),

			_knopf("buff.combo", "Bonus: Kombowert x10", func():
				_rufe("GameState", "activate_buff", ["combo_value", 10.0, 600.0])
				return "Läuft 10 Minuten"),

			_knopf("buff.window", "Bonus: Kombofenster x10", func():
				_rufe("GameState", "activate_buff", ["combo_window", 10.0, 600.0])
				return "Läuft 10 Minuten"),

			_knopf("buff.craft", "Bonus: Bautempo x10", func():
				_rufe("GameState", "activate_buff", ["craft_speed", 10.0, 600.0])
				return "Läuft 10 Minuten"),
		]},

		{"name": "Fortschritt", "cheats": [
			_anzeige("prog.info", "Stand", func():
				return "Phase %d   |   Spielzeit %d min   |   Verstöße %d" % [
					int(_lies("GameProgression", "current_phase", 0)),
					int(float(_lies("GameState", "total_play_time", 0.0)) / 60.0),
					int(_lies("GameState", "safety_violations", 0))]),

			_zahl("prog.phase", "Phase", "GameProgression", "current_phase",
				  0.0, 10.0, 1.0, true,
				  "Der Abschnitt der Handlung. Sprünge können Ereignisse überspringen."),
			_zahl("prog.violations", "Sicherheitsverstöße", "GameState",
				  "safety_violations", 0.0, 100.0, 1.0, true),
			_schalter("prog.secret", "Geheimes Ende gesehen", "GameState", "secret_ending_seen"),
			_schalter("prog.elevator", "Aufzug verlassen", "GameProgression", "elevator_exited",
					  "Der Schalter, an dem das Spiel erkennt, dass die Schicht läuft."),
			_schalter("save.lock", "Speichern sperren", "SaveManager", "save_locked",
					  "Verhindert, dass das Spiel überschreibt - praktisch beim Ausprobieren."),
		]},

		{"name": "Herbeirufen", "cheats": [
			_auswahlfeld("spawn.rubrik", "Rubrik", func(): return _rubriken()),

			_auswahlfeld("spawn.was", "Gegenstand", func(): return _spawn_namen()),

			_eingabe("spawn.anzahl", "Anzahl",
				func(): return float(_spawn_anzahl),
				func(v): _spawn_anzahl = clampi(int(v), 1, 50),
				1.0, 50.0),

			_knopf("spawn.los", "Herbeirufen", func():
				return _spawne(_spawn_anzahl),
				"Legt den Gegenstand direkt vor dich."),

			_anzeige("spawn.info", "Herumliegend", func():
				var n = _rufe("ObjectPoolManager", "count_loose_items")
				return str(n) if n != null else "?"),

			_knopf("spawn.purge", "Herumliegendes einsammeln", func():
				var n = _rufe("ObjectPoolManager", "purge_loose_items")
				return "Eingesammelt: %s" % str(n if n != null else 0),
				"Räumt lose Gegenstände weg, wenn es zu voll wird."),
		]},

		{"name": "Werkzeugkasten", "cheats": [
			_knopf("dev.report", "Bestandsaufnahme schreiben", func():
				_schreibe_bericht()
				return "Bericht in " + ProjectSettings.globalize_path(BERICHT),
				"Schreibt alle Objekte und Werte des Spiels in eine Textdatei - " +
				"damit lassen sich neue Cheats finden."),
		]},
	]

	_cheats.clear()
	_nach_id.clear()
	for kat in _kategorien:
		for c in kat["cheats"]:
			_cheats.append(c)
			_nach_id[c["id"]] = c


func _zahl_text(wert) -> String:
	var f := float(wert)
	if f >= 1000000000.0:
		return "%.2f Mrd" % (f / 1000000000.0)
	if f >= 1000000.0:
		return "%.2f Mio" % (f / 1000000.0)
	if f >= 1000.0:
		return "%.1f Tsd" % (f / 1000.0)
	return str(int(f))


# ---------------------------------------------------------------- Bestandsaufnahme

func _schreibe_bericht() -> void:
	var datei := FileAccess.open(BERICHT, FileAccess.WRITE)
	if datei == null:
		return

	datei.store_line("Floppy - Bestandsaufnahme")
	datei.store_line("Godot %s" % Engine.get_version_info().string)
	datei.store_line("")

	for kind in get_tree().root.get_children():
		if kind == self:
			continue

		datei.store_line("=== %s   (%s) ===" % [kind.name, kind.get_class()])

		for eintrag in kind.get_property_list():
			if not (eintrag.usage & PROPERTY_USAGE_SCRIPT_VARIABLE):
				continue
			var wert = kind.get(eintrag.name)

			# Objekte nicht überspringen: das Geld liegt hier als BigInt, und genau
			# das hat die erste Fassung dieses Berichts verschluckt.
			if eintrag.type == TYPE_OBJECT and wert != null:
				var text := "<%s>" % wert.get_class()
				if wert.has_method("to_plain_string"):
					text += " " + str(wert.to_plain_string())
				datei.store_line("    %-34s %-8s = %s"
					% [eintrag.name, "Object", text])
				continue

			if not (eintrag.type in [TYPE_BOOL, TYPE_INT, TYPE_FLOAT, TYPE_STRING]):
				continue

			datei.store_line("    %-34s %-8s = %s"
				% [eintrag.name, type_string(eintrag.type), str(wert).left(60)])

		datei.store_line("")

	datei.close()
