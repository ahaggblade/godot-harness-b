# Cutscene Demo Sample

Build a short cutscene or scripted camera flythrough with:

- a deterministic starting point
- a clear midpoint and ending frame
- optional `AgentHooks.load_fixture("intro_cutscene")`

Success looks like:

- the agent can load the fixture
- wait for known beats
- capture start and finish frames
- inspect enough semantic state to confirm the cutscene completed

