extends SceneTree

# Run only in an isolated temporary Godot project with a copy of floppy.gd.
# Fake game nodes and loopback sockets exercise the real backend; no game is loaded.
class Backend:
	extends "res://floppy.gd"
	func _ready() -> void:
		name = "Floppy"
		process_mode = Node.PROCESS_MODE_ALWAYS
	func _im_spiel() -> bool:
		return true

class MenuState:
	extends Node
	var game_paused := false

class PlayerState:
	extends Node
	var movement_blocked := false

var _checks := 0
var _failures := 0
var _received := {}

func _init() -> void:
	call_deferred("_run")

func check(ok: bool, message: String) -> void:
	_checks += 1
	if not ok:
		_failures += 1
		push_error("FAIL " + message)
	else:
		print("PASS " + message)

func ask(peer: StreamPeerTCP, data: Dictionary) -> Dictionary:
	peer.put_data((JSON.stringify(data) + "\n").to_utf8_buffer())
	var until := Time.get_ticks_msec() + 2000
	while Time.get_ticks_msec() < until:
		peer.poll()
		var available := peer.get_available_bytes()
		if available > 0:
			_received[peer] = str(_received.get(peer, "")) + peer.get_utf8_string(available)
		var text := str(_received.get(peer, ""))
		var newline := text.find("\n")
		if newline >= 0:
			_received[peer] = text.substr(newline + 1)
			return JSON.parse_string(text.substr(0, newline))
		await create_timer(0.005).timeout
	push_error("Response timeout: " + str(data))
	_failures += 1
	return {}

func connect_client(port: int) -> StreamPeerTCP:
	var peer := StreamPeerTCP.new()
	peer.connect_to_host("127.0.0.1", port)
	for attempt in range(100):
		peer.poll()
		if peer.get_status() == StreamPeerTCP.STATUS_CONNECTED:
			return peer
		await create_timer(0.005).timeout
	return peer

func _run() -> void:
	var menu := MenuState.new()
	menu.name = "MenuManager"
	root.add_child(menu)
	var player := PlayerState.new()
	root.add_child(player)
	player.add_to_group("player")
	var backend := Backend.new()
	root.add_child(backend)
	backend._server = TCPServer.new()
	check(backend._server.listen(0, "127.0.0.1") == OK, "isolated random loopback port")
	var first := await connect_client(backend._server.get_local_port())
	var second := await connect_client(backend._server.get_local_port())
	var schema := await ask(first, {"cmd": "schema"})
	var session: String = schema.get("sessionId", "")
	check(not session.is_empty() and schema.get("gameId") == "Project P.I.T.T." and schema.get("schemaVersion") == 1,
		"schema identifies game, session and revision")
	check(schema.get("processId") == OS.get_process_id() and schema.get("externalOverlay") == true
		and schema.get("overlayOpen") == false, "schema advertises external overlay without opening it")
	var f1 := InputEventKey.new()
	f1.keycode = KEY_F1
	f1.pressed = true
	backend._input(f1)
	check(backend._overlay_owner == null and backend.get_child_count() == 0, "backend creates no menu and does not handle F1")
	var bad := await ask(first, {"cmd": "overlay", "open": true, "sessionId": "old-session"})
	check(bad.get("ok") == false and not paused, "old session cannot change input")
	bad = await ask(first, {"cmd": "overlay", "open": 1, "sessionId": session})
	check(bad.get("ok") == false and not paused, "non-boolean open is rejected")
	Input.set_mouse_mode(Input.MOUSE_MODE_HIDDEN)
	var previous_mouse := Input.get_mouse_mode()
	var opened := await ask(first, {"cmd": "overlay", "open": true, "sessionId": session})
	check(opened.get("ok") == true and opened.get("overlayOpen") == true and opened.get("leaseMs") == 3000,
		"owner opens a three-second lease")
	check(paused and menu.game_paused and player.movement_blocked and Input.get_mouse_mode() == Input.MOUSE_MODE_VISIBLE,
		"open pauses gameplay, blocks movement and releases mouse")
	var deadline: int = backend._overlay_until
	await create_timer(0.025).timeout
	opened = await ask(first, {"cmd": "overlay", "open": true, "sessionId": session})
	check(opened.get("ok") == true and backend._overlay_until > deadline, "owner heartbeat renews lease")
	bad = await ask(second, {"cmd": "overlay", "open": true, "sessionId": session})
	check(bad.get("ok") == false and paused, "another connection cannot take ownership")
	bad = await ask(second, {"cmd": "overlay", "open": false, "sessionId": session})
	check(bad.get("ok") == false and paused, "another connection cannot release owner's input lock")
	var state := await ask(first, {"cmd": "state"})
	check(state.get("sessionId") == session and state.get("externalOverlay") == true
		and state.get("processId") == OS.get_process_id() and state.get("overlayOpen") == true,
		"state reports current overlay ownership and process metadata")
	var closed := await ask(first, {"cmd": "overlay", "open": false, "sessionId": session})
	check(closed.get("ok") == true and closed.get("overlayOpen") == false and not paused
		and not menu.game_paused and not player.movement_blocked and Input.get_mouse_mode() == previous_mouse,
		"close restores exact previous game and mouse state")
	closed = await ask(second, {"cmd": "overlay", "open": false, "sessionId": session})
	check(closed.get("ok") == true, "closing without an owner is idempotent")
	paused = true
	menu.game_paused = true
	player.movement_blocked = true
	await ask(first, {"cmd": "overlay", "open": true, "sessionId": session})
	await ask(first, {"cmd": "overlay", "open": false, "sessionId": session})
	check(paused and menu.game_paused and player.movement_blocked, "preexisting game pause and movement block remain intact")
	paused = false
	menu.game_paused = false
	player.movement_blocked = false
	await ask(first, {"cmd": "overlay", "open": true, "sessionId": session})
	player.queue_free()
	await process_frame
	player = PlayerState.new()
	root.add_child(player)
	player.add_to_group("player")
	await process_frame
	await process_frame
	check(player.movement_blocked, "replacement player is locked while overlay remains open")
	await create_timer(3.1).timeout
	check(backend._overlay_owner == null and not paused and not menu.game_paused and not player.movement_blocked,
		"expired lease releases input without a desktop close command")
	await ask(first, {"cmd": "overlay", "open": true, "sessionId": session})
	first.disconnect_from_host()
	await create_timer(0.05).timeout
	check(backend._overlay_owner == null and not paused and not player.movement_blocked,
		"owner disconnect releases input immediately")
	await ask(second, {"cmd": "overlay", "open": true, "sessionId": session})
	backend.queue_free()
	await process_frame
	check(not paused and not menu.game_paused and not player.movement_blocked, "backend exit releases input")
	second.disconnect_from_host()
	print("Pitt backend checks: %d, failures: %d" % [_checks, _failures])
	quit(0 if _failures == 0 else 1)
