# Stability regression checks

Run in a Unity Editor project with Location Tools, Valheim game assemblies, and Unity Pipeline installed:

```sh
unity command run_script --project-path /absolute/project/path \
  --file /absolute/path/to/location-tools/Tests~/StabilityChecks.cs \
  --entry StabilityChecks.Run --timeout_ms 120000 --json
```

The checks create a temporary additive scene, invoke the Location Tools check command, and test numeric support and predicted failures. They close the test scene and restore the original active scene in a `finally` block. A failure throws an exception. Inspect the command's nested `result.success`, not just its outer transport status.

Coverage includes hierarchy order, mixed materials, single-side and two-point support, physics layers, optional ground, cancellation cleanup, external support pieces, `m_supports`, `m_noSupportWear`, collapse chains, floating cycles, and non-convex meshes. The mesh case compares its contact point with `FindSupportPoint` in the project's loaded game assembly.

## Model and limits

- Material values come from the loaded `WearNTear.GetMaterialProperties`, including materials added by newer game assemblies.
- Contact queries use the target's physics scene and the game's named layers. Connected pieces outside the target are included. Eligible static colliders provide full support, as in the game.
- Ground is not invented by default. The optional flat-ground height is in world coordinates. It creates a temporary collider that is always removed.
- Support starts at each material's maximum, then decreases until it settles. Failed pieces are removed from the calculation, and support is calculated again. Scene pieces are never destroyed.
- Pieces with support wear disabled retain maximum support. The game skips their structural wear update.
- Cancellation, invalid setup, and failure to settle produce an incomplete result. They never produce a stable result.
- This is a settled structural estimate. It does not simulate network ownership, update order and timing, initial health, damage protection, runtime mod patches, random location variants, or terrain changes during world placement. Final location validation still requires the deployed game's environment.

## Validation recorded for this change

Unity 6000.0.46f1, project `valheimRipCombatArms/valheimRipCombat`, using its loaded `Assets/Plugins/assembly_valheim.dll`:

- Before: the window reported one failure for two connected wood pieces, then no failures after changing only hierarchy order.
- After: all 18 checks passed, including both hierarchy orders. No new console warnings or errors were captured during the test window.
- Current `Interior`: 11,971 pieces; 70 calculation passes; 228 predicted failures using scene colliders; approximately 1.4 seconds. This is a diagnostic result, not live-game proof.
- No production or test-server deployment was performed. The plugin runs in the Editor, and live-game collapse behavior with the production mod set remains unverified.
