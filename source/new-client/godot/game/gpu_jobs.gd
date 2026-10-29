class_name GpuJobs
extends Node
## GPU steps of background work, run on the main thread (owner 2026-10-28: the client froze after /tc - "Not
## Responding", no CPU - right after a zone load; godot.log: "Found open draw list at the end of the frame" and
## "Updating buffers is forbidden during creation of a draw list" with the zone workers' stacks).
##
## With the separate render thread (thread_model 2) Godot 4.7 makes some render-server objects straight on whatever
## thread asks, into the RenderingDevice the render thread is drawing with: a material's RID and first uniform buffer
## (Material.get_rid() -> material_create_from_shader) made by a worker while a draw list was open was refused (its
## data missing - later "uniform_set is null"), and a worker reading a mesh back (get_faces, create_trimesh_shape)
## stalled the device and ended the render thread's frame under it. Meshes, shaders, parameters and nodes a worker
## makes are queued for the render thread (safe); textures are made directly by design. The main thread never met
## either problem (its calls are ordered with the render thread's frames).
##
## So the workers keep all their work - reading and parsing files, decoding images, building meshes, scenes and
## collision (from the parsed arrays, never read back) - and only these short steps come here:
##   ready_materials(mats)  each material's RID made (a few microseconds each) before a worker sets it on a mesh;
##   run(callable)          anything else that must be the main thread's (particle systems set their own materials).
## From the main thread both run at once. Queued steps run a few milliseconds a frame (at least one a frame), the
## worker waiting for its result. wait_task(task) replaces WorkerThreadPool.wait_for_task_completion on the main thread:
## it keeps running queued steps while it waits, so a worker waiting on one can always finish (a zone change / quit
## while a zone builds).

const BUDGET_USEC := 4000                      # main-thread time a frame for queued steps (at least one runs)
const MATERIALS_A_STEP := 4

static var _mutex := Mutex.new()
static var _queue: Array = []                  # [[Callable, Array result_box, Array done_box], ...]
static var _hooked := false
static var _closing := false                   # the game is quitting: workers run their own steps
static var jobs_run := 0                       # (counters for tests/tele_probe: steps run, the longest frame's share)
static var max_pump_usec := 0
static var trace := false                      # (tests: print slow steps)
static var slowest := {}                      # tests: label -> the longest one of its steps (usec)


static func on_main_thread() -> bool:
	return OS.get_thread_caller_id() == OS.get_main_thread_id()


## The callable's result, run on the main thread (now, when called from it).
static func run(c: Callable, label := "") -> Variant:
	if on_main_thread() or _closing:
		return c.call()
	var result := [null]
	var done := [false]
	_mutex.lock()
	_queue.append([c, result, done, label])
	var hook := not _hooked
	_hooked = true
	_mutex.unlock()
	if hook:
		_install.call_deferred()
	while true:
		_mutex.lock()
		var finished: bool = done[0]
		_mutex.unlock()
		if finished:
			break
		if _closing:                                # (quitting: nobody will pump - run it here, if still queued)
			_mutex.lock()
			var mine := -1
			for i in _queue.size():
				if is_same((_queue[i] as Array)[2], done):
					mine = i
					break
			if mine >= 0:
				_queue.remove_at(mine)
			_mutex.unlock()
			if mine >= 0:
				return c.call()
		OS.delay_usec(250)
	return result[0]


## The materials' render-server materials made on the main thread - before a worker sets them on a mesh or a scene
## is generated with them. A Material makes its RID on the first get_rid() (ShaderMaterial / BaseMaterial3D
## _check_material_rid -> material_create_from_shader, carried out on the calling thread); everything else a worker
## does with it - parameters, shaders, meshes, nodes - is queued for the render thread or kept until then. A few
## microseconds each: the workers keep the heavy work.
static func ready_materials(mats: Array) -> void:
	if mats.is_empty():
		return
	# a StandardMaterial3D's shader compiled here, on the calling thread (BaseMaterial3D.get_shader_rid(): the shader
	# only - Godot's shader map is built for workers to do this): on the main thread each compile cost ~20 ms
	for m in mats:
		if m is BaseMaterial3D:
			(m as BaseMaterial3D).get_shader_rid()
	# a few a step, so the pump's per-frame budget spreads a zone's hundreds over frames (64 terrain patches and a
	# zone's model materials made in one step took 40 ms of a frame)
	var steps: Array = []
	for i in range(0, mats.size(), MATERIALS_A_STEP):
		var part := mats.slice(i, i + MATERIALS_A_STEP)
		steps.append(func():
			for m in part:
				if m is Material:
					var t := Time.get_ticks_usec()
					(m as Material).get_rid()
					if trace and Time.get_ticks_usec() - t > 5000:
						print("[gpujobs] slow material %.1f ms: %s %s" % [(Time.get_ticks_usec() - t) / 1000.0, m.get_class(),
								str(m.get_meta("spec", {}).get("slots", m.resource_name)) if m.has_meta("spec") else (m.resource_name + " " + str((m as ShaderMaterial).shader.resource_path if m is ShaderMaterial and (m as ShaderMaterial).shader else ""))]))
	run_all(steps, "materials")


## Several steps queued at once (each its own step for the per-frame budget); returns when all have run.
static func run_all(steps: Array, label := "") -> void:
	if steps.is_empty():
		return
	if on_main_thread() or _closing:
		for c in steps:
			(c as Callable).call()
		return
	var dones: Array = []
	_mutex.lock()
	for c in steps:
		var done := [false]
		dones.append(done)
		_queue.append([c, [null], done, label])
	var hook := not _hooked
	_hooked = true
	_mutex.unlock()
	if hook:
		_install.call_deferred()
	while true:
		_mutex.lock()
		var left: Array = []
		for i in steps.size():
			if not (dones[i] as Array)[0]:
				left.append(i)
		var mine: Array = []
		if _closing and not left.is_empty():             # (quitting: nobody will pump - run what is still queued)
			for i in left:
				for q in _queue.size():
					if is_same((_queue[q] as Array)[2], dones[i]):
						mine.append(i)
						_queue.remove_at(q)
						break
		_mutex.unlock()
		for i in mine:
			(steps[i] as Callable).call()
			(dones[i] as Array)[0] = true
		if left.size() == mine.size():
			return
		OS.delay_usec(250)


## Runs queued steps for up to budget_usec (at least one when any is queued). Main thread only.
static func pump(budget_usec := BUDGET_USEC) -> void:
	var t0 := Time.get_ticks_usec()
	while true:
		_mutex.lock()
		var job: Array = _queue.pop_front() if not _queue.is_empty() else []
		_mutex.unlock()
		if job.is_empty():
			return
		var t1 := Time.get_ticks_usec()
		var value: Variant = (job[0] as Callable).call()
		var label := String(job[3]) if job.size() > 3 else ""
		slowest[label] = maxi(int(slowest.get(label, 0)), Time.get_ticks_usec() - t1)
		_mutex.lock()
		(job[1] as Array)[0] = value
		(job[2] as Array)[0] = true
		_mutex.unlock()
		jobs_run += 1
		max_pump_usec = maxi(max_pump_usec, Time.get_ticks_usec() - t0)
		if Time.get_ticks_usec() - t0 >= budget_usec:
			return


## A worker task finished, on the main thread - queued GPU steps keep running meanwhile.
static func wait_task(task: int) -> void:
	while not WorkerThreadPool.is_task_completed(task):
		pump(1000000)
		OS.delay_usec(200)
	WorkerThreadPool.wait_for_task_completion(task)


## The pump: a node under the tree's root, every frame.
static func _install() -> void:
	var tree := Engine.get_main_loop() as SceneTree
	if tree == null or tree.root == null:
		_closing = true
		return
	var n := (load("res://game/gpu_jobs.gd") as GDScript).new() as Node
	n.name = "GpuJobs"
	n.process_mode = Node.PROCESS_MODE_ALWAYS
	tree.root.add_child(n)


func _process(_delta: float) -> void:
	pump()


func _exit_tree() -> void:                         # quitting: what is queued runs now, later steps on their workers
	_closing = true
	pump(1000000)
