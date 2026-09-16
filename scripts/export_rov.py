"""blender --background source.blend --python scripts/export_rov.py -- output.fbx"""
import bpy
import json
from pathlib import Path
import sys

output = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
output.parent.mkdir(parents=True, exist_ok=True)
root = bpy.data.objects["Malstrøm Assembly"]
bpy.ops.object.select_all(action="DESELECT")
objects = [root, *root.children_recursive]
for obj in objects:
    obj.hide_set(False)
    obj.select_set(True)
bpy.context.view_layer.objects.active = root
bpy.ops.export_scene.fbx(filepath=str(output), use_selection=True,
    object_types={"EMPTY", "MESH"}, global_scale=1, apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
    bake_anim=False, use_mesh_modifiers=True, mesh_smooth_type="OFF",
    use_custom_props=False, add_leaf_bones=False)
triangles = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objects if o.type == "MESH")
print(f"TAC_EXPORT: {output.name}: {triangles} triangles, {len(objects)} objects")
# Small material palette stays in Git; geometry lives on Drive. Colors are linear.
palette = []
for material in bpy.data.materials:
    principled = next((n for n in material.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None) if material.use_nodes else None
    palette.append({"name": material.name,
        "color": list(principled.inputs["Base Color"].default_value if principled else material.diffuse_color),
        "metallic": float(principled.inputs["Metallic"].default_value) if principled else 0,
        "smoothness": 1 - float(principled.inputs["Roughness"].default_value) if principled else 0.4})
palette_path = Path(__file__).resolve().parents[1] / "assets/rov-materials.json"
palette_path.write_text(json.dumps({"materials": palette}, indent=2) + "\n")
