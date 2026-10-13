Work through the following in the open Unity project. Do every step through `ucp`, verify each
change before moving on, and keep your own reasoning short.

1. Orient yourself: confirm the editor and bridge are healthy, name the open scene, and get a
   top-level overview of its hierarchy and the console state.
2. Find the main camera. Report its world position, rotation, and the components on it.
3. Create an empty object `EvalProps` at the origin. Under it create two cubes `Crate_A` and
   `Crate_B` and a sphere `Orb`. Put the crates in a row along X, 2 units apart, both resting on
   the ground (grounded, not floating or sunk). Scale `Crate_B` to 1 x 2 x 1. Place `Orb`
   1.5 units above the top of `Crate_B`.
4. Make `Crate_A` face `Orb`. Add a Rigidbody to `Orb` with mass 3 and read it back to confirm.
5. Find a material asset in the project whose name contains "Stone" (any one will do) and assign
   it to both crates. Confirm the crates' renderers now use it.
6. Produce one framed image of `EvalProps` and one isolated render of `Orb`. Report both file paths.
7. Report the product name from the player settings, the scenes in the build list, and how many
   packages are installed directly (name three that are not built-in modules).
8. Enter play mode, wait about two seconds, read `Orb`'s position, pause, read it again, resume,
   then stop play mode. Report what happened to `Orb`.
9. Trigger a script compile and report whether it produced errors or warnings.
10. Save the scene. Then delete `EvalProps`, save again, and confirm the hierarchy is back to its
    original top level and the scene is clean (not dirty).

Finish with a short summary: what you did, the reported values, and anything that failed.
