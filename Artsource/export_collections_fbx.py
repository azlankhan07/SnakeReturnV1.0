# SnakeReturns - export each top-level collection as its own FBX for Unity.
# Works on GamePieces.blend and Phone.blend alike.
#
# Run from Blender's Scripting tab (Alt+P), or:
#   blender <file>.blend --background --python export_collections_fbx.py
#
# Non-destructive: objects are rotated/moved, exported, then restored exactly.
# Nothing about the .blend is permanently changed.

import bpy, os
from math import radians
from mathutils import Vector, Matrix

# ---------------------------------------------------------------- settings
OUT_DIR = r"D:\MyGames\SnakeReturns\SnakeReturns\Assets\_Project\Art\Models"

# Both .blend files are authored on Blender's XY ground with +Z up. Unity's
# board is the XY plane with the camera on -Z (GDD s1), so the art has to be
# stood upright on the way out. This does it at export time only.
#
# If the first import faces AWAY from the camera, flip this to +90.
ORIENT_X_DEG = -90

# Where each collection's own 0,0,0 ends up. Internal alignment within a
# collection is ALWAYS preserved exactly; this only moves the group as a whole.
#   "GROUND" - centred on the footprint and sitting ON the plane, never sinking
#   "XY"     - centred on the footprint, vertical axis left as authored
#   "WORLD"  - keep true Blender world coordinates
PIVOT = "GROUND"

# Collections that should pivot on a specific object's centre instead.
# The phone pivots on its LCD quad, so dropping it at the point GameCam frames
# lines the physical screen up with the board with no fudge factors.
PIVOT_OBJECT = {"Collection": "Screen_LED", "Phone": "Screen_LED"}

# Rename the output file for collections whose name isn't what you want.
NAME_MAP = {"Collection": "Phone"}

ONLY = None          # e.g. {"SnakeHead"} to export a single collection
TRIANGULATE = True   # deterministic triangulation of any ngons
# -------------------------------------------------------------------------

os.makedirs(OUT_DIR, exist_ok=True)
scene = bpy.context.scene
R = Matrix.Rotation(radians(ORIENT_X_DEG), 4, 'X')

if bpy.context.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')


def bounds(objs):
    pts = []
    for ob in objs:
        pts += [ob.matrix_world @ Vector(c) for c in ob.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi


exported = []
for col in scene.collection.children:
    if ONLY and col.name not in ONLY:
        continue

    meshes = [o for o in col.objects if o.type == 'MESH']
    if not meshes:
        print("SKIP %s - no meshes" % col.name)
        continue

    # Only unparented objects get moved; children ride along with their parent.
    inside = set(col.objects)
    roots = [o for o in meshes if o.parent is None or o.parent not in inside]
    saved = [(o, o.matrix_world.copy()) for o in roots]

    try:
        # 1. stand the art upright for Unity
        for o in roots:
            o.matrix_world = R @ o.matrix_world
        bpy.context.view_layer.update()

        # 2. decide this collection's origin, measured AFTER the rotation
        pivot_name = PIVOT_OBJECT.get(col.name)
        if pivot_name and pivot_name in bpy.data.objects:
            plo, phi = bounds([bpy.data.objects[pivot_name]])
            off = -(plo + phi) / 2.0
        elif PIVOT == "WORLD":
            off = Vector((0.0, 0.0, 0.0))
        else:
            lo, hi = bounds(meshes)
            ctr = (lo + hi) / 2.0
            off = Vector((-ctr.x, -ctr.y, -lo.z if PIVOT == "GROUND" else 0.0))

        for o in roots:
            m = o.matrix_world.copy()
            m.translation += off
            o.matrix_world = m
        bpy.context.view_layer.update()

        # 3. export
        bpy.ops.object.select_all(action='DESELECT')
        for o in meshes:                      # lights/empties deliberately excluded
            o.select_set(True)
        bpy.context.view_layer.objects.active = meshes[0]

        stem = NAME_MAP.get(col.name, col.name)
        path = os.path.join(OUT_DIR, stem + ".fbx")
        bpy.ops.export_scene.fbx(
            filepath=path,
            use_selection=True,
            object_types={'MESH'},
            # --- scale: 1 Blender metre == 1 Unity unit, Scale Factor reads 1 ---
            global_scale=1.0,
            apply_unit_scale=True,
            apply_scale_options='FBX_SCALE_NONE',
            # --- axes: Blender default. Pair with "Bake Axis Conversion" in Unity ---
            axis_forward='-Z',
            axis_up='Y',
            use_space_transform=True,
            bake_space_transform=False,   # experimental + breaks parented hierarchies
            # --- geometry ---
            use_mesh_modifiers=True,
            mesh_smooth_type='FACE',      # 'OFF' makes Unity warn about smoothing groups
            use_triangles=TRIANGULATE,
            colors_type='NONE',
            prioritize_active_color=False,
            # --- nothing rigged or animated in either file ---
            add_leaf_bones=False,
            bake_anim=False,
            # Packed images have no path on disk, which is why the first
            # export carried no textures at all. COPY + embed makes the
            # FBX self-contained so Unity cannot miss them.
            path_mode='COPY',
            embed_textures=True,
        )
        lo, hi = bounds(meshes)
        exported.append((stem, len(meshes), lo, hi))
        print("OK   %-12s -> %s" % (stem, path))
    finally:
        for o, m in saved:
            o.matrix_world = m
        bpy.context.view_layer.update()

print("\n--- as written to FBX, in Blender axes (Blender Z becomes Unity Y) ---")
for name, n, lo, hi in exported:
    print("%-12s meshes=%d  x[%.3f..%.3f]  y[%.3f..%.3f]  z[%.3f..%.3f]"
          % (name, n, lo.x, hi.x, lo.y, hi.y, lo.z, hi.z))
