# Benchmark Games And Success Criteria

## 1. 2D Greybox Controller

Goal: verify movement, jump behavior, and camera framing in a minimal platformer.

Acceptance checks:

- deterministic hook resets the world to a known spawn
- agent presses movement and jump actions
- runtime inspection confirms player state and position changed as expected
- screenshot artifact confirms the player remains on-screen and camera framing is correct
- a regression such as broken input mapping or wrong spawn position should fail either semantic or visual validation

## 2. UI Menu Demo

Goal: verify focus order, button activation, hover state, and visible menu layout.

Acceptance checks:

- focus starts on the expected control
- agent navigates with keyboard and mouse
- `inspect.focus` and `inspect.hover` confirm UI ownership changes
- screenshot shows the correct menu, highlighted control, and no overlapping layers
- a regression such as invisible text, focus trap, or wrong z-order should be caught

## 3. 3D Camera / Cutscene Demo

Goal: verify a short non-interactive or lightly interactive cutscene path.

Acceptance checks:

- deterministic hook loads the cutscene start
- scenario waits at known timestamps and captures frames
- runtime state confirms cutscene progress and completion
- screenshots confirm framing, target visibility, and expected end pose
- a regression such as wrong camera target, missing actor, or incomplete cutscene should fail

## Overall Success

The system is successful when an agent can:

1. edit the sample project
2. run or restart it
3. capture a screenshot artifact
4. inject input
5. inspect runtime state
6. run deterministic gdUnit checks
7. decide pass or fail without a human opening the editor

