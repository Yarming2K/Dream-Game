# Dream Game scenery: trees, bushes and rocks.
#
#   blender -b -P tools/blender/env_library.py -- models/env.glb [preview.png]
#
# One mesh each, named as the game asks for them (pine, broadleaf, bush, rock, boulder), at a typical real size,
# standing on the origin (Blender's Z is the game's Y). The colour is in the vertex colours (no textures). The
# vertex colour's alpha is how much a point sways in the wind (0 for trunks and rocks). Leaves are cards: their
# UVs run 0..1 across each card, and the game's shader cuts leaf clusters out of them. Everything else has UV -1.
# Leaf cards' normals point out from the middle of the crown, so a tree is lit like a soft round mass.
import bpy, bmesh, sys, math, random
from mathutils import Vector, Matrix, noise

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else "env.glb"
PREVIEW = argv[1] if len(argv) > 1 else None

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
rng = random.Random(4)

class Builder:
    """Collects verts, faces, colours, UVs and custom normals, then makes one mesh."""
    def __init__(self):
        self.v, self.f, self.col, self.uv, self.nrm = [], [], [], [], []

    def face(self, pts, color, sway, uvs=None, normals=None):
        i0 = len(self.v)
        for k, p in enumerate(pts):
            self.v.append(Vector(p))
            self.col.append((*color, sway if not callable(sway) else sway(Vector(p))))
            self.uv.append(uvs[k] if uvs else (-1.0, -1.0))
            self.nrm.append(normals[k] if normals else None)
        self.f.append(list(range(i0, i0 + len(pts))))

    def cone(self, base, top, r0, r1, color, segs=10, sway=0.0):
        base, top = Vector(base), Vector(top)
        axis = (top - base).normalized()
        side = axis.orthogonal().normalized(); up = axis.cross(side)
        for j in range(segs):
            a0, a1 = j / segs * math.tau, (j + 1) / segs * math.tau
            d0 = side * math.cos(a0) + up * math.sin(a0); d1 = side * math.cos(a1) + up * math.sin(a1)
            pts = [base + d0 * r0, base + d1 * r0, top + d1 * r1, top + d0 * r1]
            self.face(pts, color, sway, normals=[d0, d1, d1, d0])
        if r1 > 0.001:
            self.face([top + (side * math.cos(j / segs * math.tau) + up * math.sin(j / segs * math.tau)) * r1 for j in range(segs)], color, sway, normals=[axis] * segs)

    def card(self, center, u, v, color, sway, crown):
        """A leaf card: a quad round center spanning ±u, ±v, its normals pointing away from the crown's middle."""
        c = Vector(center)
        pts = [c - u - v, c + u - v, c + u + v, c - u + v]
        nr = [((p - Vector(crown)).normalized() * 0.8 + Vector((0, 0, 0.35))).normalized() for p in pts]
        self.face(pts, color, sway, uvs=[(0, 0), (1, 0), (1, 1), (0, 1)], normals=nr)

    def build(self, name):
        me = bpy.data.meshes.new(name)
        me.from_pydata([tuple(p) for p in self.v], [], self.f)
        me.update()
        col = me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
        for i, c in enumerate(self.col): col.data[i].color = c
        me.color_attributes.active_color = col
        uvl = me.uv_layers.new(name="UV")
        for poly in me.polygons:
            for li in poly.loop_indices:
                uvl.data[li].uv = self.uv[me.loops[li].vertex_index]
        loop_n = []
        for poly in me.polygons:
            for li in poly.loop_indices:
                n = self.nrm[me.loops[li].vertex_index]
                loop_n.append(tuple(n) if n is not None else tuple(poly.normal))
        me.normals_split_custom_set(loop_n)
        ob = bpy.data.objects.new(name, me)
        scene.collection.objects.link(ob)
        return ob

def jitter(c, amount):
    return tuple(max(0.0, min(1.0, x * (1 + rng.uniform(-amount, amount)))) for x in c)

BARK = (0.24, 0.19, 0.14)

def pine():
    b = Builder()
    h = 12.0
    b.cone((0, 0, -0.3), (0, 0, h), 0.24, 0.04, BARK, segs=9)
    tiers = 8
    for t in range(tiers):
        z = 2.6 + t * (h - 3.2) / (tiers - 1)
        r = 3.1 * (1 - t / tiers) + 0.5
        n = 9 if t < 5 else 6
        for k in range(n):
            a = k / n * math.tau + rng.uniform(-0.25, 0.25) + t * 0.6
            d = Vector((math.cos(a), math.sin(a), 0))
            mid = d * r * 0.55 + Vector((0, 0, z - r * 0.18))
            along = (d * r * 0.55 + Vector((0, 0, -r * 0.18)))
            across = d.cross(Vector((0, 0, 1))) * r * 0.42
            tilt = Matrix.Rotation(rng.uniform(-0.4, 0.4), 3, d)
            col = jitter((0.10, 0.16, 0.08), 0.18)
            b.card(mid, along, tilt @ across, col, lambda p: min(1.0, p.z / h) * min(1.0, p.xy.length / 2.0), (0, 0, z + 0.5))
    return b.build("pine")

def broadleaf():
    b = Builder()
    b.cone((0, 0, -0.3), (0, 0, 4.6), 0.3, 0.17, BARK, segs=10)
    crown = Vector((0, 0, 7.0))
    for k in range(4):
        a = k / 4 * math.tau + 0.4
        tip = Vector((math.cos(a) * 2.0, math.sin(a) * 2.0, 6.6 + rng.uniform(-0.5, 0.6)))
        b.cone((0, 0, 4.2), tip, 0.13, 0.05, BARK, segs=7)
    for k in range(26):
        # Clusters in a squashed ball round the crown, each three crossed cards.
        d = Vector((rng.gauss(0, 1), rng.gauss(0, 1), rng.gauss(0, 0.7))).normalized()
        c = crown + Vector((d.x * 3.1, d.y * 3.1, d.z * 2.3)) * rng.uniform(0.45, 1.0)
        s = rng.uniform(1.1, 1.6)
        col = jitter((0.15, 0.23, 0.09), 0.2)
        for q in range(3):
            m = Matrix.Rotation(rng.uniform(0, math.pi), 3, "Z") @ Matrix.Rotation(rng.uniform(-0.6, 0.6), 3, "X")
            b.card(c, m @ Vector((s, 0, 0)), m @ Vector((0, 0, s * 0.8)), col, lambda p: min(1.0, max(0.0, (p.z - 3.5) / 6.0)), crown)
    return b.build("broadleaf")

def bush():
    b = Builder()
    mid = Vector((0, 0, 0.55))
    for k in range(9):
        d = Vector((rng.gauss(0, 1), rng.gauss(0, 1), abs(rng.gauss(0, 0.6)))).normalized()
        c = mid + Vector((d.x * 0.8, d.y * 0.8, d.z * 0.45))
        s = rng.uniform(0.55, 0.8)
        col = jitter((0.17, 0.22, 0.1), 0.2)
        for q in range(2):
            m = Matrix.Rotation(rng.uniform(0, math.pi), 3, "Z") @ Matrix.Rotation(rng.uniform(-0.5, 0.5), 3, "X")
            b.card(c, m @ Vector((s, 0, 0)), m @ Vector((0, 0, s * 0.75)), col, lambda p: min(1.0, p.z / 1.4) * 0.6, mid)
    return b.build("bush")

def rock(name, radius, squash, subdiv, seed):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=radius)
    off = Vector((seed * 3.1, seed * 1.7, seed * 2.3))
    for v in bm.verts:
        p = v.co
        k = 1 + 0.28 * noise.noise(p * 1.3 / radius + off) + 0.1 * noise.noise(p * 3.7 / radius + off)
        v.co = Vector((p.x * squash[0], p.y * squash[1], p.z * squash[2])) * k
        if v.co.z < -0.15 * radius: v.co.z = -0.15 * radius + (v.co.z + 0.15 * radius) * 0.2  # a flat-ish underside, sunk in
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me); scene.collection.objects.link(ob)
    col = me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
    for i, v in enumerate(me.vertices):
        t = 0.5 + 0.5 * noise.noise(v.co * 2.5 / radius + off)
        g = 0.33 + 0.16 * t
        lichen = max(0.0, noise.noise(v.co * 1.4 / radius - off)) * (0.6 if v.normal.z > 0.3 else 0.1)
        c = (g * (1 - lichen * 0.3), g * (1 + lichen * 0.15), g * (0.93 - lichen * 0.35))
        col.data[i].color = (*c, 0.0)
    me.color_attributes.active_color = col
    uvl = me.uv_layers.new(name="UV")
    for d in uvl.data: d.uv = (-1.0, -1.0)
    for p in me.polygons: p.use_smooth = True
    return ob

made = [pine(), broadleaf(), bush(), rock("rock", 0.7, (1.2, 0.95, 0.65), 2, 1), rock("boulder", 1.4, (1.3, 1.0, 0.8), 3, 2)]

if PREVIEW:
    for i, ob in enumerate(made): ob.location = ((i - 2) * 6.5, 0, 0)
    mat = bpy.data.materials.new("show"); mat.use_nodes = True
    nt = mat.node_tree; bsdf = nt.nodes["Principled BSDF"]
    attr = nt.nodes.new("ShaderNodeVertexColor"); attr.layer_name = "Col"
    nt.links.new(attr.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.9
    # Leaf cards: cut out blotchy clusters by UV (roughly what the game's shader does).
    uvn = nt.nodes.new("ShaderNodeUVMap"); uvn.uv_map = "UV"
    sep = nt.nodes.new("ShaderNodeSeparateXYZ"); nt.links.new(uvn.outputs[0], sep.inputs[0])
    vn = nt.nodes.new("ShaderNodeTexVoronoi"); vn.inputs["Scale"].default_value = 7
    nt.links.new(uvn.outputs[0], vn.inputs[0])
    d = nt.nodes.new("ShaderNodeVectorMath"); d.operation = "DISTANCE"; d.inputs[1].default_value = (0.5, 0.5, 0)
    nt.links.new(uvn.outputs[0], d.inputs[0])
    m1 = nt.nodes.new("ShaderNodeMath"); m1.operation = "ADD"
    nt.links.new(vn.outputs["Distance"], m1.inputs[0]); nt.links.new(d.outputs["Value"], m1.inputs[1])
    lt = nt.nodes.new("ShaderNodeMath"); lt.operation = "LESS_THAN"; lt.inputs[1].default_value = 0.62
    nt.links.new(m1.outputs[0], lt.inputs[0])
    neg = nt.nodes.new("ShaderNodeMath"); neg.operation = "LESS_THAN"; neg.inputs[1].default_value = -0.5
    nt.links.new(sep.outputs["X"], neg.inputs[0])
    mx = nt.nodes.new("ShaderNodeMath"); mx.operation = "MAXIMUM"
    nt.links.new(lt.outputs[0], mx.inputs[0]); nt.links.new(neg.outputs[0], mx.inputs[1])
    nt.links.new(mx.outputs[0], bsdf.inputs["Alpha"])
    mat.blend_method = "CLIP" if hasattr(mat, "blend_method") else None
    for ob in made: ob.data.materials.append(mat)
    g = bpy.data.meshes.new("ground"); bmg = bmesh.new(); bmesh.ops.create_grid(bmg, x_segments=1, y_segments=1, size=60); bmg.to_mesh(g)
    go = bpy.data.objects.new("ground", g); scene.collection.objects.link(go)
    gm = bpy.data.materials.new("g"); gm.use_nodes = True; gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.25, 0.3, 0.15, 1)
    g.materials.append(gm)
    world = bpy.data.worlds.new("w"); scene.world = world; world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.65, 0.75, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.7
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); scene.collection.objects.link(sun)
    sun.data.energy = 3.5; sun.rotation_euler = (math.radians(50), 0, math.radians(35))
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scene.collection.objects.link(cam)
    cam.location = (0, -26, 7); cam.data.lens = 30
    cam.rotation_euler = (Vector((0, 0, 4)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    scene.render.engine = "CYCLES"; scene.cycles.samples = 32; scene.cycles.use_denoising = True
    scene.render.resolution_x, scene.render.resolution_y = 1600, 700
    scene.render.filepath = PREVIEW
    bpy.ops.render.render(write_still=True)
    for ob in made:
        ob.location = (0, 0, 0); ob.data.materials.clear()

for o in scene.objects: o.select_set(o in made)
kw = dict(filepath=OUT, export_format="GLB", use_selection=True, export_apply=True, export_normals=True, export_materials="NONE",
          export_texcoords=True)
try:
    bpy.ops.export_scene.gltf(export_vertex_color="ACTIVE", **kw)
except TypeError:
    bpy.ops.export_scene.gltf(export_colors=True, **kw)
for ob in made:
    print(f"env {ob.name}: {len(ob.data.polygons)} faces, size {tuple(round(d, 2) for d in ob.dimensions)}")
