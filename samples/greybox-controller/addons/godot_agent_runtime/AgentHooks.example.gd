extends Node

# Optional project-side deterministic hooks. Rename to `AgentHooks.gd`,
# add as an autoload named `AgentHooks`, and implement only the methods you need.

func reset_world() -> Dictionary:
    return {"ok": true}

func seed_world(seed: int) -> Dictionary:
    seed(seed)
    return {"ok": true, "seed": seed}

func load_fixture(name: String) -> Dictionary:
    return {"ok": true, "fixture": name}

func get_semantic_state() -> Dictionary:
    return {"ok": true}

