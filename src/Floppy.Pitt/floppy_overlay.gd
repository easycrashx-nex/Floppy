extends CanvasLayer

# Das Ingame-Menü von Floppy für Project P.I.T.T.
#
# Unitys IMGUI gibt es in Godot nicht - dort zeichnet man jeden Frame neu, hier baut
# man einen Knotenbaum. Das Aussehen ist trotzdem dasselbe wie im Client: dieselben
# Farben, dieselben Reiter, dieselben Zeilen. Nur die Technik darunter ist eine andere.
#
# Die Cheats kommen unverändert aus floppy.gd - dieselben Lese- und Schreibfunktionen,
# die auch der Client über die Verbindung bedient. Es gibt also keine zweite Wahrheit.

const FARBE_HINTERGRUND := Color("0f1115")
const FARBE_FLAECHE := Color("161920")
const FARBE_ZEILE := Color("1c2029")
const FARBE_ZEILE_HELL := Color("232834")
const FARBE_AKZENT := Color("3aa6ff")
const FARBE_AKZENT_MATT := Color("2b5f8f")
const FARBE_TEXT := Color("e2e6ee")
const FARBE_MATT := Color("8b93a3")
const FARBE_GUT := Color("5fd98b")
const FARBE_LINIE := Color("2a2f3b")

const BREITE := 1000
const HOEHE := 680
const MINDEST := Vector2(720, 420)
const GRIFF := 8                   # wie breit der Rand zum Ziehen ist
const FENSTER_DATEI := "user://floppy_fenster.cfg"

# Die Maße stammen aus dem Client, damit beide Oberflächen gleich wirken:
# feste Beschriftungsspalte, gleich hohe Zeilen, Bedienelemente in fester Breite.
const SPALTE_BESCHRIFTUNG := 200
const ZEILE_HOEHE := 34
const FELD_BREITE := 200
const KNOPF_BREITE := 240
const TAKT := 0.15                 # so oft werden die Werte nachgezogen

var _kern: Node

var _reiter_leiste: Container
var _liste: VBoxContainer
var _status: Label
var _hinweis: Label
var _hinweis_bis := 0.0

var _rubrik := 0
var _suche := ""
var _auffrischen: Array[Callable] = []
var _uhr := 0.0
var _offen := false
var _pausiert_von_uns := false

var _fenster: PanelContainer
var _griffe: Control
var _wurzel: Control
var _zone := ""                    # welche Kante gerade gezogen wird
var _start_rechteck := Rect2()
var _start_maus := Vector2()


# ---------------------------------------------------------------- Aufbau

func baue(kern: Node) -> void:
	_kern = kern


func _ready() -> void:
	layer = 128
	process_mode = Node.PROCESS_MODE_ALWAYS

	_erstelle()
	visible = false


## Ein Kasten mit runden Ecken - das Grundelement der ganzen Oberfläche.
func _kasten(farbe: Color, radius := 6, rand := 0,
			 quer := 12, hoch := 8) -> StyleBoxFlat:
	var k := StyleBoxFlat.new()
	k.bg_color = farbe
	k.corner_radius_top_left = radius
	k.corner_radius_top_right = radius
	k.corner_radius_bottom_left = radius
	k.corner_radius_bottom_right = radius

	if rand > 0:
		k.border_width_left = rand
		k.border_width_right = rand
		k.border_width_top = rand
		k.border_width_bottom = rand
		k.border_color = FARBE_LINIE

	k.content_margin_left = quer
	k.content_margin_right = quer
	k.content_margin_top = hoch
	k.content_margin_bottom = hoch
	return k


func _beschriftung(text: String, groesse := 14, farbe := FARBE_TEXT) -> Label:
	var l := Label.new()
	l.text = text
	l.add_theme_font_size_override("font_size", groesse)
	l.add_theme_color_override("font_color", farbe)
	l.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	return l


func _erstelle() -> void:
	# Ein Control über den ganzen Bildschirm: es schluckt alle Klicks, die nicht das
	# Fenster treffen - sonst würde man durch das Menü hindurch ins Spiel greifen.
	var wurzel := Control.new()
	wurzel.set_anchors_preset(Control.PRESET_FULL_RECT)
	wurzel.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(wurzel)
	_wurzel = wurzel

	# Das Fenster liegt frei darin - Größe und Ort setzen wir selbst, sonst könnte
	# man es nicht ziehen.
	_fenster = PanelContainer.new()
	_fenster.add_theme_stylebox_override("panel", _kasten(FARBE_HINTERGRUND, 10, 1))
	wurzel.add_child(_fenster)

	var rand := MarginContainer.new()
	for seite in ["left", "right", "top", "bottom"]:
		rand.add_theme_constant_override("margin_" + seite, 18)
	_fenster.add_child(rand)

	var spalte := VBoxContainer.new()
	spalte.add_theme_constant_override("separation", 12)
	rand.add_child(spalte)

	spalte.add_child(_kopf())

	# Reiter und Zeilen sitzen zusammen auf einer etwas helleren Fläche - im Client
	# ist das die rechte Hälfte des Fensters.
	var flaeche := PanelContainer.new()
	flaeche.size_flags_vertical = Control.SIZE_EXPAND_FILL
	flaeche.add_theme_stylebox_override("panel", _kasten(FARBE_FLAECHE, 10, 0, 14, 14))
	spalte.add_child(flaeche)

	var innen := VBoxContainer.new()
	innen.add_theme_constant_override("separation", 12)
	flaeche.add_child(innen)

	innen.add_child(_reiter())

	var suchfeld := LineEdit.new()
	suchfeld.placeholder_text = "Funktion in allen Rubriken suchen…"
	suchfeld.clear_button_enabled = true
	suchfeld.text_changed.connect(func(text: String):
		_suche = text.strip_edges().to_lower()
		_fuelle_liste())
	innen.add_child(suchfeld)

	var rollen := ScrollContainer.new()
	rollen.size_flags_vertical = Control.SIZE_EXPAND_FILL
	rollen.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	_schmale_leiste(rollen)
	innen.add_child(rollen)

	_liste = VBoxContainer.new()
	_liste.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_liste.add_theme_constant_override("separation", 10)
	rollen.add_child(_liste)

	_hinweis = _beschriftung("", 13, FARBE_GUT)
	spalte.add_child(_hinweis)

	_baue_griffe()
	_hole_fenster()

	_fuelle_reiter()
	_fuelle_liste()


# ---------------------------------------------------------------- Fenster ziehen

## Acht schmale Flächen auf dem Rand: vier Kanten, vier Ecken.
##
## Sie liegen über dem Inhalt, sind aber nur so breit wie der Rahmen - deshalb kommt
## man überall sonst weiter an die Knöpfe.
func _baue_griffe() -> void:
	# Die Griffe liegen neben dem Fenster, nicht darin.
	#
	# Als Kind des PanelContainers wären sie um dessen Innenabstand nach innen
	# verschoben - der Greifrand säße dann ein Stück neben der sichtbaren Kante, und
	# genau daneben greift man ins Leere. Als eigene Fläche über dem Fenster sitzt er
	# genau auf dem Rand; ihr Rechteck wird in _setze_fenster mitgeführt.
	_griffe = Control.new()
	_griffe.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_wurzel.add_child(_griffe)

	# Der Greifrand reicht ein Stück über die Kante hinaus - so trifft man ihn auch,
	# wenn man knapp danebenzielt.
	var aus := -GRIFF * 0.5
	var ein := GRIFF * 1.5

	var zonen := {
		"o":  [0.0, 0.0, 1.0, 0.0,  ein, aus, -ein, ein,  Control.CURSOR_VSIZE],
		"u":  [0.0, 1.0, 1.0, 1.0,  ein, -ein, -ein, -aus,  Control.CURSOR_VSIZE],
		"l":  [0.0, 0.0, 0.0, 1.0,  aus, ein, ein, -ein,  Control.CURSOR_HSIZE],
		"r":  [1.0, 0.0, 1.0, 1.0,  -ein, ein, -aus, -ein,  Control.CURSOR_HSIZE],
		"ol": [0.0, 0.0, 0.0, 0.0,  aus, aus, ein, ein,  Control.CURSOR_FDIAGSIZE],
		"or": [1.0, 0.0, 1.0, 0.0,  -ein, aus, -aus, ein,  Control.CURSOR_BDIAGSIZE],
		"ul": [0.0, 1.0, 0.0, 1.0,  aus, -ein, ein, -aus,  Control.CURSOR_BDIAGSIZE],
		"ur": [1.0, 1.0, 1.0, 1.0,  -ein, -ein, -aus, -aus,  Control.CURSOR_FDIAGSIZE],
	}

	for schluessel in zonen:
		# Ausdrücklich als String: beim Durchlaufen eines Dictionarys kennt GDScript
		# den Typ des Schlüssels nicht, und ":=" kann ihn dann nicht ableiten.
		var zone: String = schluessel
		var w: Array = zonen[zone]

		var griff := Control.new()
		griff.anchor_left = w[0]
		griff.anchor_top = w[1]
		griff.anchor_right = w[2]
		griff.anchor_bottom = w[3]
		griff.offset_left = w[4]
		griff.offset_top = w[5]
		griff.offset_right = w[6]
		griff.offset_bottom = w[7]
		griff.mouse_default_cursor_shape = w[8]
		griff.mouse_filter = Control.MOUSE_FILTER_STOP

		var name_der_zone: String = zone
		griff.gui_input.connect(func(e: InputEvent): _griff_eingabe(e, name_der_zone))
		_griffe.add_child(griff)


func _griff_eingabe(e: InputEvent, zone: String) -> void:
	if e is InputEventMouseButton and e.button_index == MOUSE_BUTTON_LEFT:
		if e.pressed:
			_zone = zone
			_start_rechteck = Rect2(_fenster.position, _fenster.size)
			_start_maus = _fenster.get_global_mouse_position()
		else:
			_zone = ""
			_merke_fenster()

	elif e is InputEventMouseMotion and _zone == zone:
		_ziehe(_fenster.get_global_mouse_position() - _start_maus)


## Aus Startgröße und Mausweg das neue Rechteck bauen.
##
## Beim Ziehen an der linken oder oberen Kante wandert auch die Ecke mit - deshalb
## wird dort die Position mitgerechnet und nicht nur die Größe.
func _ziehe(weg: Vector2) -> void:
	var r := Rect2(_start_rechteck)

	if _zone == "verschieben":
		r.position += weg
	else:
		if _zone.contains("l"):
			r.position.x += weg.x
			r.size.x -= weg.x
		if _zone.contains("r"):
			r.size.x += weg.x
		if _zone.contains("o"):
			r.position.y += weg.y
			r.size.y -= weg.y
		if _zone.contains("u"):
			r.size.y += weg.y

		# Unter die Mindestgröße nicht - und beim Ziehen an der linken oder oberen
		# Kante muss die Ecke dann stehen bleiben.
		if r.size.x < MINDEST.x:
			if _zone.contains("l"):
				r.position.x = _start_rechteck.end.x - MINDEST.x
			r.size.x = MINDEST.x
		if r.size.y < MINDEST.y:
			if _zone.contains("o"):
				r.position.y = _start_rechteck.end.y - MINDEST.y
			r.size.y = MINDEST.y

	_setze_fenster(r)


func _setze_fenster(r: Rect2) -> void:
	var schirm := get_viewport().get_visible_rect().size

	r.size.x = clampf(r.size.x, MINDEST.x, schirm.x)
	r.size.y = clampf(r.size.y, MINDEST.y, schirm.y)
	r.position.x = clampf(r.position.x, 0.0, schirm.x - r.size.x)
	r.position.y = clampf(r.position.y, 0.0, schirm.y - r.size.y)

	_fenster.position = r.position
	_fenster.size = r.size

	if _griffe != null:
		_griffe.position = r.position
		_griffe.size = r.size


## Größe und Ort merken, damit das Fenster beim nächsten Start wieder da ist.
func _merke_fenster() -> void:
	var datei := ConfigFile.new()
	datei.set_value("fenster", "x", _fenster.position.x)
	datei.set_value("fenster", "y", _fenster.position.y)
	datei.set_value("fenster", "breite", _fenster.size.x)
	datei.set_value("fenster", "hoehe", _fenster.size.y)
	datei.save(FENSTER_DATEI)


func _hole_fenster() -> void:
	var schirm := get_viewport().get_visible_rect().size
	var groesse := Vector2(BREITE, HOEHE)
	var ort := (schirm - groesse) * 0.5

	var datei := ConfigFile.new()
	if datei.load(FENSTER_DATEI) == OK:
		groesse = Vector2(
			float(datei.get_value("fenster", "breite", BREITE)),
			float(datei.get_value("fenster", "hoehe", HOEHE)))
		ort = Vector2(
			float(datei.get_value("fenster", "x", ort.x)),
			float(datei.get_value("fenster", "y", ort.y)))

	_setze_fenster(Rect2(ort, groesse))


## Godots Bildlaufleiste ist im Standardaussehen ein breiter grauer Balken.
## Der Client hat dort einen dünnen Strich - also bauen wir denselben.
func _schmale_leiste(rollen: ScrollContainer) -> void:
	var leiste := rollen.get_v_scroll_bar()
	leiste.custom_minimum_size = Vector2(6, 0)

	var rinne := _kasten(FARBE_HINTERGRUND, 3, 0, 0, 0)
	var griff := _kasten(FARBE_LINIE, 3, 0, 0, 0)
	var griff_hell := _kasten(FARBE_AKZENT_MATT, 3, 0, 0, 0)

	leiste.add_theme_stylebox_override("scroll", rinne)
	leiste.add_theme_stylebox_override("grabber", griff)
	leiste.add_theme_stylebox_override("grabber_highlight", griff_hell)
	leiste.add_theme_stylebox_override("grabber_pressed", griff_hell)


func _kopf() -> Control:
	var zeile := HBoxContainer.new()
	zeile.add_theme_constant_override("separation", 10)
	zeile.custom_minimum_size = Vector2(0, 34)

	# Am Kopf zieht man das Fenster durch die Gegend - wie an einer Titelleiste.
	zeile.mouse_filter = Control.MOUSE_FILTER_STOP
	zeile.mouse_default_cursor_shape = Control.CURSOR_MOVE
	zeile.gui_input.connect(func(e: InputEvent): _griff_eingabe(e, "verschieben"))

	zeile.add_child(_beschriftung("Floppy", 26, FARBE_AKZENT))

	var spiel := _beschriftung("Project P.I.T.T.", 15, FARBE_MATT)
	spiel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	zeile.add_child(spiel)

	_status = _beschriftung("", 13, FARBE_GUT)
	zeile.add_child(_status)

	zeile.add_child(_beschriftung("F1 schließt", 13, FARBE_MATT))
	return zeile


func _reiter() -> Control:
	# Ein umbrechender Container statt einer Leiste zum Schieben: macht man das
	# Fenster schmal, rutschen die Reiter in die nächste Zeile, statt hinter einer
	# Bildlaufleiste zu verschwinden.
	var flies := HFlowContainer.new()
	flies.add_theme_constant_override("h_separation", 6)
	flies.add_theme_constant_override("v_separation", 6)

	_reiter_leiste = flies
	return flies


# ---------------------------------------------------------------- Reiter

func _fuelle_reiter() -> void:
	for kind in _reiter_leiste.get_children():
		kind.queue_free()

	var kategorien: Array = _kern.kategorien()
	for i in range(kategorien.size()):
		var knopf := Button.new()
		knopf.text = str(kategorien[i]["name"])
		knopf.focus_mode = Control.FOCUS_NONE
		knopf.add_theme_font_size_override("font_size", 14)
		knopf.custom_minimum_size = Vector2(0, 36)
		_faerbe_reiter(knopf, i == _rubrik)

		var nummer := i
		knopf.pressed.connect(func(): _waehle_rubrik(nummer))
		_reiter_leiste.add_child(knopf)


func _faerbe_reiter(knopf: Button, aktiv: bool) -> void:
	var farbe := FARBE_AKZENT_MATT if aktiv else FARBE_ZEILE
	knopf.add_theme_stylebox_override("normal", _kasten(farbe, 6, 0, 16, 8))
	knopf.add_theme_stylebox_override("hover",
		_kasten(FARBE_AKZENT if aktiv else FARBE_ZEILE_HELL, 6, 0, 16, 8))
	knopf.add_theme_stylebox_override("pressed", _kasten(FARBE_AKZENT, 6, 0, 16, 8))
	knopf.add_theme_color_override("font_color", FARBE_TEXT)


func _waehle_rubrik(i: int) -> void:
	_rubrik = i
	_fuelle_reiter()
	_fuelle_liste()


# ---------------------------------------------------------------- Zeilen

func _fuelle_liste() -> void:
	_auffrischen.clear()
	for kind in _liste.get_children():
		kind.queue_free()

	var kategorien: Array = _kern.kategorien()
	if kategorien.is_empty():
		return

	var anzahl := 0
	for i in range(kategorien.size()):
		if _suche.is_empty() and i != clampi(_rubrik, 0, kategorien.size() - 1):
			continue
		for c in kategorien[i]["cheats"]:
			var text := (str(c.get("label", "")) + " " + str(c.get("description", ""))).to_lower()
			if not _suche.is_empty() and not text.contains(_suche):
				continue
			_liste.add_child(_zeile(c))
			anzahl += 1
	if anzahl == 0:
		_liste.add_child(_beschriftung("Keine passenden Funktionen gefunden", 14, FARBE_MATT))


## Eine Zeile: der graue Kasten mit Bezeichnung links und Bedienung rechts.
func _zeile(c: Dictionary) -> Control:
	var kasten := PanelContainer.new()
	kasten.add_theme_stylebox_override("panel", _kasten(FARBE_ZEILE, 8, 0, 16, 12))

	var innen := VBoxContainer.new()
	innen.add_theme_constant_override("separation", 4)
	kasten.add_child(innen)

	var zeile := HBoxContainer.new()
	zeile.add_theme_constant_override("separation", 12)
	zeile.custom_minimum_size = Vector2(0, ZEILE_HOEHE)
	innen.add_child(zeile)

	var art := str(c.get("kind", "Toggle"))

	if art != "Button":
		var titel := _beschriftung(str(c["label"]), 15)
		titel.custom_minimum_size = Vector2(SPALTE_BESCHRIFTUNG, ZEILE_HOEHE)
		zeile.add_child(titel)

	match art:
		"Toggle": _bau_schalter(zeile, c)
		"Slider": _bau_regler(zeile, c)
		"Number": _bau_eingabe(zeile, c)
		"Choice": _bau_auswahl(zeile, c)
		"Button": _bau_knopf(zeile, c)
		"Info": _bau_anzeige(zeile, c)

	var beschreibung := str(c.get("description", ""))
	if not beschreibung.is_empty():
		var text := _beschriftung(beschreibung, 12, FARBE_MATT)
		text.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		text.custom_minimum_size = Vector2(0, 18)
		innen.add_child(text)

	return kasten


func _bau_schalter(zeile: HBoxContainer, c: Dictionary) -> void:
	var schalter := CheckButton.new()
	schalter.focus_mode = Control.FOCUS_NONE
	schalter.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	schalter.alignment = HORIZONTAL_ALIGNMENT_RIGHT
	schalter.button_pressed = _lies_bool(c)
	schalter.toggled.connect(func(an: bool): _schreibe(c, an))
	zeile.add_child(schalter)

	_auffrischen.append(func():
		var wert := _lies_bool(c)
		if schalter.button_pressed != wert:
			schalter.set_pressed_no_signal(wert))


func _bau_regler(zeile: HBoxContainer, c: Dictionary) -> void:
	var regler := HSlider.new()
	regler.min_value = float(c.get("min", 0.0))
	regler.max_value = float(c.get("max", 100.0))
	regler.step = 0.01
	regler.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	regler.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	regler.value = _lies_zahl(c)
	zeile.add_child(regler)

	var wert := _beschriftung(_zeige(regler.value), 14, FARBE_AKZENT)
	wert.custom_minimum_size = Vector2(70, 0)
	wert.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	zeile.add_child(wert)

	regler.value_changed.connect(func(v: float):
		wert.text = _zeige(v)
		_schreibe(c, v))

	_auffrischen.append(func():
		if regler.has_focus():
			return
		var v := _lies_zahl(c)
		if absf(regler.value - v) > 0.001:
			regler.set_value_no_signal(v)
			wert.text = _zeige(v))


func _bau_eingabe(zeile: HBoxContainer, c: Dictionary) -> void:
	var feld := LineEdit.new()
	feld.custom_minimum_size = Vector2(FELD_BREITE, ZEILE_HOEHE)
	feld.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
	feld.alignment = HORIZONTAL_ALIGNMENT_LEFT
	feld.text = _zeige(_lies_zahl(c))
	feld.add_theme_stylebox_override("normal", _kasten(FARBE_ZEILE_HELL, 6, 0, 12, 6))
	feld.add_theme_stylebox_override("focus", _kasten(FARBE_AKZENT_MATT, 6, 0, 12, 6))
	feld.add_theme_color_override("font_color", FARBE_TEXT)
	zeile.add_child(feld)

	var abschicken := func(_t := ""):
		# Tausenderpunkte wegwerfen, Komma als Dezimaltrenner zulassen - aber nur,
		# wenn wirklich ein Komma da ist. Sonst wäre "12.5" plötzlich 125.
		var roh := feld.text.strip_edges()
		if roh.contains(","):
			roh = roh.replace(".", "").replace(",", ".")
		if not roh.is_valid_float():
			return
		var v := clampf(float(roh), float(c.get("min", 0.0)), float(c.get("max", 0.0)))
		_schreibe(c, v)

	# Ein Callable ruft man mit .call() auf - abschicken() wäre eine Funktion,
	# die es nicht gibt.
	feld.text_submitted.connect(func(t: String):
		abschicken.call(t)
		feld.release_focus())
	feld.focus_exited.connect(func(): abschicken.call(""))

	_auffrischen.append(func():
		if not feld.has_focus():
			feld.text = _zeige(_lies_zahl(c)))


func _bau_auswahl(zeile: HBoxContainer, c: Dictionary) -> void:
	var liste := OptionButton.new()
	liste.custom_minimum_size = Vector2(300, ZEILE_HOEHE)
	liste.focus_mode = Control.FOCUS_NONE
	liste.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
	liste.add_theme_stylebox_override("normal", _kasten(FARBE_ZEILE_HELL, 6, 0, 12, 6))
	liste.add_theme_stylebox_override("hover", _kasten(FARBE_AKZENT_MATT, 6, 0, 12, 6))
	liste.add_theme_stylebox_override("pressed", _kasten(FARBE_AKZENT_MATT, 6, 0, 12, 6))
	liste.add_theme_color_override("font_color", FARBE_TEXT)

	var eintraege: Array = c["auswahlen"].call() if c.has("auswahlen") else []
	for e in eintraege:
		liste.add_item(str(e))

	var gewaehlt: int = _kern.auswahl_index(str(c["id"]))
	if gewaehlt >= 0 and gewaehlt < liste.item_count:
		liste.select(gewaehlt)

	# Nach einer Auswahl kann sich eine abhängige Liste geändert haben
	# (Rubrik -> Gegenstand). Deshalb die Zeilen neu aufbauen.
	liste.item_selected.connect(func(i: int):
		_kern.setze_auswahl(str(c["id"]), i)
		call_deferred("_fuelle_liste"))

	zeile.add_child(liste)


func _bau_knopf(zeile: HBoxContainer, c: Dictionary) -> void:
	var knopf := Button.new()
	knopf.text = str(c["label"])
	knopf.focus_mode = Control.FOCUS_NONE
	knopf.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
	knopf.custom_minimum_size = Vector2(KNOPF_BREITE, ZEILE_HOEHE + 4)
	knopf.add_theme_font_size_override("font_size", 15)
	knopf.add_theme_stylebox_override("normal", _kasten(FARBE_ZEILE_HELL, 6, 0, 16, 8))
	knopf.add_theme_stylebox_override("hover", _kasten(FARBE_AKZENT_MATT, 6, 0, 16, 8))
	knopf.add_theme_stylebox_override("pressed", _kasten(FARBE_AKZENT, 6, 0, 16, 8))
	knopf.add_theme_color_override("font_color", FARBE_TEXT)

	knopf.pressed.connect(func():
		if not c.has("tue"):
			return
		var r = c["tue"].call()
		if typeof(r) == TYPE_STRING:
			_melde(str(r)))

	zeile.add_child(knopf)


func _bau_anzeige(zeile: HBoxContainer, c: Dictionary) -> void:
	var wert := _beschriftung("", 15, FARBE_AKZENT)
	wert.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	wert.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	zeile.add_child(wert)

	_auffrischen.append(func(): wert.text = _lies_text(c))


# ---------------------------------------------------------------- Werte

func _lies_bool(c: Dictionary) -> bool:
	if not c.has("lies"):
		return false
	return c["lies"].call() == true


func _lies_zahl(c: Dictionary) -> float:
	if not c.has("lies"):
		return 0.0
	var v = c["lies"].call()
	return float(v) if typeof(v) in [TYPE_INT, TYPE_FLOAT] else 0.0


func _lies_text(c: Dictionary) -> String:
	if not c.has("lies"):
		return ""
	return str(c["lies"].call())


func _schreibe(c: Dictionary, wert) -> void:
	if c.has("schreibe"):
		c["schreibe"].call(wert)


## Zahlen lesbar machen: keine Nachkommastellen, wo keine hingehören,
## und Tausenderpunkte, damit man 1000000 nicht abzählen muss.
func _zeige(wert: float) -> String:
	if absf(wert - roundf(wert)) > 0.005:
		return "%.2f" % wert

	var text := str(int(roundf(absf(wert))))
	var mit_punkten := ""
	var zaehler := 0

	for i in range(text.length() - 1, -1, -1):
		mit_punkten = text[i] + mit_punkten
		zaehler += 1
		if zaehler % 3 == 0 and i > 0:
			mit_punkten = "." + mit_punkten

	return ("-" if wert < 0 else "") + mit_punkten


func _melde(text: String) -> void:
	_hinweis.text = text
	_hinweis_bis = Time.get_ticks_msec() / 1000.0 + 4.0


# ---------------------------------------------------------------- Laufen

func _process(delta: float) -> void:
	if not _offen:
		return

	# Jeden Frame darauf bestehen, dass die Maus frei ist. Das Spiel fängt sie an
	# mehreren Stellen wieder ein - einmal setzen beim Öffnen reicht nicht.
	if Input.get_mouse_mode() != Input.MOUSE_MODE_VISIBLE:
		Input.set_mouse_mode(Input.MOUSE_MODE_VISIBLE)

	_uhr += delta
	if _uhr < TAKT:
		return
	_uhr = 0.0

	var bereit: bool = _kern.im_spiel()
	_status.text = "Bereit" if bereit else "Warte auf Spielstart"
	_status.add_theme_color_override("font_color", FARBE_GUT if bereit else FARBE_MATT)

	if not _hinweis.text.is_empty() and Time.get_ticks_msec() / 1000.0 > _hinweis_bis:
		_hinweis.text = ""

	for auffrischen in _auffrischen:
		auffrischen.call()


func _input(event: InputEvent) -> void:
	if not (event is InputEventKey) or not event.pressed or event.echo:
		return

	if event.keycode == KEY_F1:
		umschalten()
		get_viewport().set_input_as_handled()
	elif event.keycode == KEY_ESCAPE and _offen:
		umschalten()
		get_viewport().set_input_as_handled()


func umschalten() -> void:
	_offen = not _offen
	visible = _offen

	if _offen:
		# Falls das Spielfenster inzwischen kleiner geworden ist, wieder hineinschieben
		_setze_fenster(Rect2(_fenster.position, _fenster.size))
		_fuelle_reiter()
		_fuelle_liste()

	_sperre(_offen)


## Solange das Menü offen ist, soll die Maus nur das Menü bedienen - danach wieder
## ganz normal das Spiel.
##
## Den Zeiger nur freizugeben genügt nicht: Umsehen, Greifen und das Aufwärmen des
## Spielers setzen die Maus an mehreren Stellen selbst wieder auf "gefangen". Deshalb
## halten wir den Spielbaum an, solange das Menü offen ist - genau das tut das
## Pausemenü des Spiels auch. Unser eigener Knoten läuft weiter (PROCESS_MODE_ALWAYS),
## die Verbindung zum Client also ebenso.
func _sperre(an: bool) -> void:
	if an:
		# Im Hauptmenü nicht anhalten: dort ist unser Menü nur eine Anzeige, und ein
		# angehaltener Baum würde die Menüknöpfe des Spiels lahmlegen.
		# Nur anhalten, wenn nicht ohnehin schon pausiert - sonst würden wir beim
		# Schließen eine fremde Pause aufheben.
		if _kern.im_spiel() and not get_tree().paused:
			get_tree().paused = true
			_pausiert_von_uns = true
	elif _pausiert_von_uns:
		get_tree().paused = false
		_pausiert_von_uns = false

	if an:
		Input.set_mouse_mode(Input.MOUSE_MODE_VISIBLE)
	elif _kern.im_spiel():
		# Im Hauptmenü bleibt der Zeiger frei - sonst könnte man dort nichts mehr anklicken.
		Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)

	var menue := get_node_or_null("/root/MenuManager")
	if menue != null:
		menue.set("game_paused", an)

	var spieler = get_tree().get_first_node_in_group("player")
	if spieler != null:
		spieler.set("movement_blocked", an)
