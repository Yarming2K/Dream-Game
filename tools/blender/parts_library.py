# Dream Game part library: detailed meshes that replace the simple shapes of a block's model.
#
#   blender -b -P tools/blender/parts_library.py -- models/parts.glb [preview.png]
#
# Each part is one mesh, named as the block models tag it (see PartLibrary in src/Core/BlockModels.cs), built
# at a typical real size and centred on the origin. The game fits it to the simple shape it replaces, so sizes
# here only set the proportions. Up here is Blender's Z, which is the game's Y; a cylinder's axis is Z.
#
# Bevels and cut-out rims are tagged in the vertex colour's alpha (0 on a bevel, 1 on a face): that's where the
# game's weathering chips the paint first. Smoothing is by angle, so bevels catch the light.
import bpy, bmesh, sys, math
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else "parts.glb"
PREVIEW = argv[1] if len(argv) > 1 else None

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
FACE, EDGE = bpy.data.materials.new("face"), bpy.data.materials.new("bevel")

# ------------------------------------------------------------------ building blocks
def new_obj(name, bm):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me)
    scene.collection.objects.link(ob)
    ob.data.materials.append(FACE); ob.data.materials.append(EDGE)
    return ob

def cylinder(r, h, segs=32, z=0.0, r2=None):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=r, radius2=r if r2 is None else r2, depth=h,
                          matrix=Matrix.Translation((0, 0, z)))
    return bm

def box(sx, sy, sz, at=(0, 0, 0)):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation(at) @ Matrix.Diagonal((sx, sy, sz, 1)))
    return bm

def join(*bms):
    out = bmesh.new()
    for b in bms:
        me = bpy.data.meshes.new("tmp"); b.to_mesh(me); b.free()
        out.from_mesh(me); bpy.data.meshes.remove(me)
    return out

def activate(ob):
    for o in scene.objects: o.select_set(False)
    bpy.context.view_layer.objects.active = ob; ob.select_set(True)

def cut(ob, cutter_bm, name="cut"):
    """Boolean difference with a throw-away cutter."""
    c = new_obj(name, cutter_bm)
    m = ob.modifiers.new(name, "BOOLEAN"); m.operation = "DIFFERENCE"; m.solver = "EXACT"; m.object = c
    activate(ob); bpy.ops.object.modifier_apply(modifier=m.name)
    bpy.data.objects.remove(c, do_unlink=True)

def attach(ob, other_bm):
    """Adds separate pieces (bolts, tread blocks) to a part without a boolean: they just sit on its surface."""
    bm = bmesh.new(); bm.from_mesh(ob.data)
    me = bpy.data.meshes.new("tmp"); other_bm.to_mesh(me); other_bm.free()
    bm.from_mesh(me); bpy.data.meshes.remove(me)
    bm.to_mesh(ob.data); bm.free()

def union(ob, other_bm, name="add"):
    c = new_obj(name, other_bm)
    m = ob.modifiers.new(name, "BOOLEAN"); m.operation = "UNION"; m.solver = "EXACT"; m.object = c
    activate(ob); bpy.ops.object.modifier_apply(modifier=m.name)
    bpy.data.objects.remove(c, do_unlink=True)

def finish(ob, bevel=0.006, segments=2, angle=35):
    """Bevel the edges (tagged), smooth by angle, and write the edge tag into vertex colour alpha."""
    for p in ob.data.polygons: p.material_index = 0
    if bevel > 0:
        m = ob.modifiers.new("bevel", "BEVEL"); m.width = bevel; m.segments = segments
        m.limit_method = "ANGLE"; m.angle_limit = math.radians(angle); m.material = 1; m.harden_normals = False
        activate(ob); bpy.ops.object.modifier_apply(modifier="bevel")
    col = ob.data.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
    for poly in ob.data.polygons:
        a = 0.0 if poly.material_index == 1 else 1.0
        for li in poly.loop_indices: col.data[li].color = (1, 1, 1, a)
    ob.data.color_attributes.active_color = col
    ob.data.color_attributes.render_color_index = 0
    for p in ob.data.polygons: p.material_index = 0
    activate(ob); bpy.ops.object.shade_smooth_by_angle(angle=math.radians(32))
    # One material slot is enough; the game paints it.
    while len(ob.data.materials) > 1: ob.data.materials.pop()
    return ob

def bmesh_of(ob):
    """An object's mesh as a bmesh, removing the object."""
    bm = bmesh.new(); bm.from_mesh(ob.data)
    bpy.data.objects.remove(ob, do_unlink=True)
    return bm

def ring_of(n, radius, make, z=0.0, phase=0.0):
    """n copies round Z of a bmesh made at (radius, 0, z)."""
    parts = []
    for i in range(n):
        a = phase + i / n * math.tau
        b = make()
        bmesh.ops.transform(b, matrix=Matrix.Rotation(a, 4, "Z") @ Matrix.Translation((radius, 0, z)), verts=b.verts)
        parts.append(b)
    return join(*parts)

# ------------------------------------------------------------------ the parts
def tire():
    # A road tyre: rounded shoulders, a raised chevron tread round it.
    r, w = 0.30, 0.28
    ob = new_obj("tire", cylinder(r * 0.93, w, 48))
    def lug():
        b = box(0.024, 0.06, w * 0.4)
        bmesh.ops.transform(b, matrix=Matrix.Rotation(math.radians(28), 4, "X"), verts=b.verts)
        return b
    def lug2():
        b = box(0.024, 0.06, w * 0.4)
        bmesh.ops.transform(b, matrix=Matrix.Rotation(math.radians(-28), 4, "X"), verts=b.verts)
        return b
    # Sidewall dip where the rim sits.
    cut(ob, join(cylinder(r * 0.66, 0.04, 48, z=w / 2), cylinder(r * 0.66, 0.04, 48, z=-w / 2)))
    attach(ob, join(ring_of(26, r * 0.93 + 0.006, lug, z=w * 0.21), ring_of(26, r * 0.93 + 0.006, lug2, z=-w * 0.21, phase=math.pi / 26)))
    return finish(ob, 0.008, 2, 30)

def rim():
    # A pressed-steel wheel: a lip, a dished face with lightening holes, a centre boss.
    r, w = 0.19, 0.30
    ob = new_obj("rim", cylinder(r, w, 48))
    cut(ob, join(cylinder(r * 0.82, 0.08, 48, z=w / 2), cylinder(r * 0.82, 0.08, 48, z=-w / 2)))
    union(ob, cylinder(r * 0.45, w * 0.96, 32))
    for i in range(6):
        a = math.pi / 6 + i / 6 * math.tau
        hole = cylinder(r * 0.11, w * 1.3, 20)
        bmesh.ops.translate(hole, vec=(r * 0.62 * math.cos(a), r * 0.62 * math.sin(a), 0), verts=hole.verts)
        cut(ob, hole)
    return finish(ob, 0.006, 2, 35)

def hub():
    # The hub cap and its six wheel nuts.
    r, w = 0.085, 0.34
    ob = new_obj("hub", cylinder(r, w * 0.92, 24))
    attach(ob, cylinder(r * 0.55, w, 16))
    attach(ob, ring_of(6, r * 0.72, lambda: cylinder(0.011, w * 0.97, 6)))
    return finish(ob, 0.003, 1, 35)

def track_wheel():
    # A track's road wheel: rubber band, dished disc, hub with bolts.
    r, w = 0.26, 0.26
    ob = new_obj("track_wheel", cylinder(r, w, 48))
    cut(ob, join(cylinder(r * 0.8, 0.06, 48, z=w / 2), cylinder(r * 0.8, 0.06, 48, z=-w / 2)))
    cut(ob, cylinder(r * 0.93, w * 0.1, 48))  # the groove between the two rubber bands
    disc = new_obj("disc", cylinder(r * 0.86, w * 0.88, 48))
    cut(disc, ring_of(5, r * 0.55, lambda: cylinder(r * 0.12, w * 1.3, 16)))
    union(ob, bmesh_of(disc))
    attach(ob, cylinder(r * 0.3, w, 24))
    attach(ob, ring_of(8, r * 0.22, lambda: cylinder(0.012, w * 1.02, 6)))
    return finish(ob, 0.006, 2, 35)

def track_link():
    # A cast track shoe: two grousers on the outside (+Z), pin bosses at each end, a recessed middle.
    sx, sy, sz = 0.44, 0.225, 0.06  # width, pitch (Blender Y), thickness (up)
    ob = new_obj("track_link", box(sx, sy * 0.86, sz * 0.7))
    attach(ob, join(box(sx * 0.98, 0.03, 0.035, at=(0, -sy * 0.18, sz * 0.32)), box(sx * 0.98, 0.03, 0.035, at=(0, sy * 0.18, sz * 0.32))))
    pins = []
    for y in (-sy * 0.44, sy * 0.44):
        for x in (-sx * 0.3, sx * 0.3):
            b = cylinder(0.022, sx * 0.34, 12)
            bmesh.ops.transform(b, matrix=Matrix.Translation((x, y, -sz * 0.05)) @ Matrix.Rotation(math.pi / 2, 4, "Y"), verts=b.verts)
            pins.append(b)
    cut(ob, join(box(sx * 0.32, sy * 0.4, 0.03, at=(-sx * 0.25, 0, -sz * 0.33)), box(sx * 0.32, sy * 0.4, 0.03, at=(sx * 0.25, 0, -sz * 0.33))))
    attach(ob, join(*pins))
    return finish(ob, 0.005, 1, 35)

def muzzle_brake():
    # A double-baffle brake: a body with two big side ports each side, a cap at the front.
    r, h = 0.085, 0.28
    ob = new_obj("muzzle_brake", cylinder(r, h, 32))
    for z in (-h * 0.15, h * 0.18):
        cut(ob, box(r * 3, r * 1.2, h * 0.2, at=(0, 0, z)))
    cut(ob, cylinder(r * 0.42, h * 1.2, 24))  # the bore
    cap = new_obj("cap", cylinder(r * 1.0, h * 0.08, 32, z=h * 0.46))
    cut(cap, cylinder(r * 0.42, h * 0.3, 24, z=h * 0.46))
    attach(ob, bmesh_of(cap))
    return finish(ob, 0.004, 2, 35)

# Small fittings. Remember: the game's -Z (a block's front) is Blender's +Y.
def hatch():
    # A cast round hatch on a bolted ring: domed lid, central boss, three-knuckle hinge at the back, grab handle.
    ob = new_obj("hatch", cylinder(0.19, 0.03, 40, z=-0.0275))
    attach(ob, cylinder(0.17, 0.04, 40, z=0.0075))
    attach(ob, cylinder(0.17, 0.01, 40, z=0.0325, r2=0.14))
    attach(ob, cylinder(0.05, 0.02, 20, z=0.0325))
    for x in (-0.06, 0.0, 0.06):
        k = cylinder(0.022, 0.036, 12)
        bmesh.ops.transform(k, matrix=Matrix.Translation((x, -0.183, 0.008)) @ Matrix.Rotation(math.pi / 2, 4, "Y"), verts=k.verts)
        attach(ob, k)
    attach(ob, join(box(0.03, 0.06, 0.012, at=(-0.03, -0.15, 0.022)), box(0.03, 0.06, 0.012, at=(0.03, -0.15, 0.022))))
    attach(ob, join(box(0.08, 0.014, 0.01, at=(0, 0.1, 0.0425 - 0.005)), box(0.012, 0.012, 0.012, at=(-0.034, 0.1, 0.03)), box(0.012, 0.012, 0.012, at=(0.034, 0.1, 0.03))))
    attach(ob, ring_of(8, 0.18, lambda: cylinder(0.01, 0.012, 6), z=-0.008, phase=math.pi / 8))
    return finish(ob, 0.003, 1, 35)

def periscope():
    # A periscope head: armoured base, housing with a sloped front, window recess, rain hood over the glass.
    ob = new_obj("periscope", box(0.2, 0.16, 0.06, at=(0, 0, -0.0555)))
    up = new_obj("up", box(0.15, 0.12, 0.1, at=(0, 0, 0.0245)))
    cut(up, box(0.11, 0.02, 0.045, at=(0, 0.06, 0.0295)))
    attach(ob, bmesh_of(up))
    attach(ob, box(0.16, 0.05, 0.012, at=(0, 0.07, 0.0795)))
    attach(ob, join(box(0.012, 0.03, 0.03, at=(-0.07, 0.06, 0.07)), box(0.012, 0.03, 0.03, at=(0.07, 0.06, 0.07))))
    return finish(ob, 0.004, 1, 35)

def headlight():
    # A lamp bucket, wider at the front (Blender -Z), with a rim and the lens set in it.
    ob = new_obj("headlight", cylinder(0.075, 0.12, 32, r2=0.058))
    attach(ob, cylinder(0.08, 0.016, 32, z=-0.052))
    attach(ob, cylinder(0.05, 0.02, 20, z=0.06))
    return finish(ob, 0.003, 1, 35)

def toolbox():
    # A stowage bin: pressed body, a lid with a lip, two bands round it, hinges at the back.
    ob = new_obj("toolbox", box(0.38, 0.21, 0.13, at=(0, 0, -0.015)))
    cut(ob, box(0.28, 0.012, 0.07, at=(0, 0.105, -0.02)))
    attach(ob, box(0.42, 0.225, 0.03, at=(0, 0, 0.065)))
    attach(ob, join(box(0.02, 0.226, 0.15, at=(-0.17, 0, -0.005)), box(0.02, 0.226, 0.15, at=(0.17, 0, -0.005))))
    for x in (-0.12, 0.12):
        k = cylinder(0.01, 0.06, 10)
        bmesh.ops.transform(k, matrix=Matrix.Translation((x, -0.113, 0.05)) @ Matrix.Rotation(math.pi / 2, 4, "Y"), verts=k.verts)
        attach(ob, k)
    return finish(ob, 0.004, 1, 35)

def smoke_tube():
    # A smoke-grenade cup: a tube flared at the mouth (+Z), a clamp band, a base boss for the wiring.
    ob = new_obj("smoke_tube", cylinder(0.036, 0.17, 24, z=-0.005))
    m = new_obj("mouth", cylinder(0.036, 0.03, 24, z=0.085, r2=0.043))
    cut(m, cylinder(0.03, 0.06, 24, z=0.1))
    attach(ob, bmesh_of(m))
    attach(ob, cylinder(0.04, 0.012, 24, z=-0.04))
    attach(ob, cylinder(0.042, 0.015, 24, z=-0.0925))
    return finish(ob, 0.003, 1, 35)

def muffler():
    # An exhaust silencer: a drum with tapered ends, two clamp bands and a seam along it.
    ob = new_obj("muffler", cylinder(0.075, 0.28, 32))
    attach(ob, cylinder(0.075, 0.04, 32, z=0.16, r2=0.045))
    attach(ob, cylinder(0.045, 0.04, 32, z=-0.16, r2=0.075))
    attach(ob, join(cylinder(0.08, 0.02, 32, z=0.09), cylinder(0.08, 0.02, 32, z=-0.09)))
    attach(ob, box(0.012, 0.012, 0.26, at=(0, 0.074, 0)))
    return finish(ob, 0.003, 1, 35)

def prop_blade():
    # A propeller blade along +Z: round shank, airfoil section, tapering, twisted, with a rounded tip.
    bm = bmesh.new()
    rings, M, N = [], 20, 28
    for i in range(N + 1):
        t = i / N
        if t < 0.08:
            c, th = 0.06, 0.06  # the shank
        else:
            c = 0.17 * (1 - 0.38 * (t - 0.08))
            th = 0.035 * (1 - 0.55 * (t - 0.08))
        if t > 0.86:
            k = math.sqrt(max(0.0, 1 - ((t - 0.86) / 0.14) ** 2))
            c *= max(k, 0.08); th *= max(k, 0.15)
        twist = -0.12 * t
        ring = []
        for j in range(M):
            phi = j / M * math.tau
            x = 0.5 * c * math.cos(phi)
            y = 0.5 * th * math.sin(phi) + 0.12 * th * (1 - (2 * x / c) ** 2 if c > 0 else 0)
            xr, yr = x * math.cos(twist) - y * math.sin(twist), x * math.sin(twist) + y * math.cos(twist)
            ring.append(bm.verts.new((xr, yr, t)))
        rings.append(ring)
    for i in range(N):
        for j in range(M):
            a, b = rings[i][j], rings[i][(j + 1) % M]
            c2, d = rings[i + 1][(j + 1) % M], rings[i + 1][j]
            bm.faces.new((a, b, c2, d))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = new_obj("prop_blade", bm)
    return finish(ob, 0.0)

PARTS = [tire, rim, hub, track_wheel, track_link, muzzle_brake, hatch, periscope, headlight, toolbox, smoke_tube, muffler, prop_blade]
made = [f() for f in PARTS]

# Spread out for the preview only; reset before export.
if PREVIEW:
    for i, ob in enumerate(made):
        row, col = divmod(i, 7)
        ob.location = ((col - 3) * 0.7, row * 0.9, 0.35)
    mat = bpy.data.materials.new("show"); mat.use_nodes = True
    nt = mat.node_tree; bsdf = nt.nodes["Principled BSDF"]
    attr = nt.nodes.new("ShaderNodeVertexColor"); attr.layer_name = "Col"
    mixn = nt.nodes.new("ShaderNodeMix"); mixn.data_type = "RGBA"
    mixn.inputs[6].default_value = (0.75, 0.35, 0.2, 1); mixn.inputs[7].default_value = (0.32, 0.36, 0.22, 1)
    nt.links.new(attr.outputs["Alpha"], mixn.inputs["Factor"]); nt.links.new(mixn.outputs[2], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.5
    for ob in made: ob.data.materials[0] = mat
    world = bpy.data.worlds.new("w"); scene.world = world; world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.5, 0.55, 0.6, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.6
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); scene.collection.objects.link(sun)
    sun.data.energy = 3; sun.rotation_euler = (math.radians(50), 0, math.radians(30))
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scene.collection.objects.link(cam)
    cam.location = (0.0, -3.2, 3.4); cam.data.lens = 24
    cam.rotation_euler = (Vector((0, 0, 0.3)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    scene.render.engine = "CYCLES"; scene.cycles.samples = 32; scene.cycles.use_denoising = True
    scene.render.resolution_x, scene.render.resolution_y = 1600, 700
    scene.render.filepath = PREVIEW
    bpy.ops.render.render(write_still=True)
    for ob in made:
        ob.location = (0, 0, 0); ob.data.materials[0] = FACE

for o in scene.objects: o.select_set(o in made)
kw = dict(filepath=OUT, export_format="GLB", use_selection=True, export_apply=True, export_normals=True, export_materials="NONE")
try:
    bpy.ops.export_scene.gltf(export_vertex_color="ACTIVE", **kw)
except TypeError:
    bpy.ops.export_scene.gltf(export_colors=True, **kw)
for ob in made:
    print(f"part {ob.name}: {len(ob.data.polygons)} faces, size {tuple(round(d, 3) for d in ob.dimensions)}")
