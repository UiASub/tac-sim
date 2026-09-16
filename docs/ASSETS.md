# Reference assets for the next fidelity pass

The current environment is an original procedural training basin. Its fixtures, practice hoops, and docking platform are approximations, not a surveyed TAC venue. No third-party art or paid asset packages were downloaded for this pass.

| Priority | Useful input | What it improves |
| --- | --- | --- |
| 1 | Malstrøm front/camera orientation confirmation, measured mass/buoyancy and thruster configuration | The full and simplified CAD meshes are now integrated; remaining data calibrates the dynamics and sensor placement |
| 2 | Organizer docking station and valve/inspection structure CAD with dimensions | Accurate task apparatus and future docking/manipulation tolerances |
| 3 | Pool/venue photos, approximate layout and dimensions | Recognizable surroundings, lighting positions, launch/recovery area |
| 4 | Raw underwater footage with depth, camera settings, and lighting conditions | Matching colour, contrast loss, backscatter, and real camera viewpoints |
| 5 | Material close-ups with a ruler or known scale | Ceramic, painted steel, weathering, and facility-specific surfaces |

Meshes in `.blend`, FBX, or OBJ are useful. STEP is also useful as a CAD source, but requires a tessellation/export step before Blender/Unity import. Include units, axis conventions, reuse permission, and the model's source where available. Keep high-detail source files separately from simplified render meshes and collision geometry.

Do not buy anything yet. ROV and organizer CAD provide more useful fidelity than a generic underwater art pack. Real camera/venue footage is especially useful for tuning filters without making the scene merely more cinematic.

Current generated assets live under `Unity/Assets/TacSim/Art/`; their generator is `PoolEnvironment.cs`. The ceramic texture is procedural; the hoop mesh, water surface, caustics, and particle shaders are original project assets. Regenerating the training scene updates these generated assets, so preserve hand-authored replacements separately.
