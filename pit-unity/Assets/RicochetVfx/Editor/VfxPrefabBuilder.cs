// Builds every Ricochet VFX asset from code: the atlas, materials, quad meshes, prefabs and the VfxBank asset.
// Menu "Ricochet VFX/Build All", or batch mode: -executeMethod RicochetVfx.EditorTools.VfxPrefabBuilder.BuildAll
// Particle budget (max alive per instance) is noted next to each system; all systems are world-space, pooled, and
// emit through bursts or from code, so the same prefabs run under manual capture ticks.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using MMC = UnityEngine.ParticleSystem.MinMaxCurve;

namespace RicochetVfx.EditorTools
{
    public static class VfxPrefabBuilder
    {
        const string Root = "Assets/RicochetVfx";
        static readonly string[] Names = { "Ember", "Rime", "Spark", "Ray" };
        static Texture2D atlas;

        [MenuItem("Ricochet VFX/Build All")]
        public static void BuildAll()
        {
            foreach (var d in new[] { "Materials", "Prefabs", "Meshes", "Resources/RicochetVfx", "Textures" })
                Directory.CreateDirectory(Path.Combine(Root, d));
            VfxAtlasBaker.Bake();
            atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(VfxAtlasBaker.Path);

            var bank = AssetDatabase.LoadAssetAtPath<VfxBankAsset>(Root + "/Resources/RicochetVfx/VfxBank.asset");
            if (bank == null)
            {
                bank = ScriptableObject.CreateInstance<VfxBankAsset>();
                AssetDatabase.CreateAsset(bank, Root + "/Resources/RicochetVfx/VfxBank.asset");
            }
            bank.elements = new VfxBankAsset.ElementSet[4];
            for (int e = 0; e < 4; e++)
            {
                var el = (VfxElement)e;
                bank.elements[e] = new VfxBankAsset.ElementSet { element = el, ball = BuildBall(el), hit = BuildHit(el) };
            }
            bank.elements[0].status = BuildBurn();
            bank.elements[1].status = BuildFreeze();
            bank.elements[1].statusEnd = BuildShatter();
            bank.chainBolt = BuildBolt();
            bank.rowBeam = BuildBeam();
            bank.rowHit = BuildRowHit();
            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();
            Debug.Log("RicochetVfx: built atlas, materials, prefabs and the bank.");
        }

        // ------------------------------------------------------------------ materials and meshes

        static Material Mat(string name, string shader, int ramp, params (string, float)[] floats)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find(shader);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            m.SetTexture("_MainTex", atlas);
            m.SetFloat("_Ramp", ramp);
            foreach (var f in floats) m.SetFloat(f.Item1, f.Item2);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Particle(int e) { return Mat("M_" + Names[e] + "_Particle", "RicochetVfx/Particle", e, ("_Boost", 2.6f), ("_Steps", 8), ("_Flicker", e == 2 ? 0.3f : 0)); }

        static Mesh Quad(string name, float u0, float v0, float u1, float v1)
        {
            string path = Root + "/Meshes/" + name + ".asset";
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (m == null) { m = new Mesh(); AssetDatabase.CreateAsset(m, path); }
            m.Clear();
            m.name = name;
            m.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f) };
            m.uv = new[] { new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u0, v1), new Vector2(u1, v1) };
            m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            m.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 2);    // billboarded in the shader
            EditorUtility.SetDirty(m);
            return m;
        }

        static GameObject Child(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        static void Renderer(GameObject go, Mesh mesh, Material mat)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        static VfxRibbon Ribbon(GameObject parent, string name, Material mat)
        {
            var go = Child(parent, name);
            go.AddComponent<MeshFilter>();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.AddComponent<VfxRibbon>();
        }

        static GameObject Save(GameObject go, string name)
        {
            var p = PrefabUtility.SaveAsPrefabAsset(go, Root + "/Prefabs/" + name + ".prefab");
            Object.DestroyImmediate(go);
            return p;
        }

        // ------------------------------------------------------------------ particle helpers

        static ParticleSystem PS(GameObject parent, string name, Material mat, float life0, float life1, float size0, float size1, int max, bool loop = false)
        {
            var go = Child(parent, name);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main;
            m.duration = 1;
            m.loop = loop;
            m.playOnAwake = false;
            m.startLifetime = new MMC(life0, life1);
            m.startSpeed = 0;
            m.startSize = new MMC(size0, size1);
            m.startColor = Color.white;
            m.maxParticles = max;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var em = ps.emission; em.enabled = false; em.rateOverTime = 0;
            var sh = ps.shape; sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        static void Burst(ParticleSystem ps, int n, float t = 0)
        {
            var em = ps.emission;
            em.enabled = true;
            em.rateOverTime = 0;
            em.SetBursts(new[] { new ParticleSystem.Burst(t, (short)n) });
        }

        static void Rate(ParticleSystem ps, float perSecond)
        {
            var em = ps.emission; em.enabled = true; em.rateOverTime = perSecond;
            var m = ps.main; m.loop = true;
        }

        /// <summary>Atlas cell(s) on one row. frames > 1: a flipbook over the lifetime, or one random frame per particle.</summary>
        static void Sheet(ParticleSystem ps, int row, int col, int frames = 1, bool random = false)
        {
            var t = ps.textureSheetAnimation;
            t.enabled = true;
            t.mode = ParticleSystemAnimationMode.Grid;
            t.numTilesX = 16; t.numTilesY = 16;
            t.animation = ParticleSystemAnimationType.SingleRow;
            t.rowMode = ParticleSystemAnimationRowMode.Custom;
            t.rowIndex = row;
            t.cycleCount = 1;
            if (frames == 1) t.frameOverTime = new MMC((col + 0.5f) / 16f);
            else if (random) t.frameOverTime = new MMC((col + 0.05f) / 16f, (col + frames - 0.05f) / 16f);
            else t.frameOverTime = new MMC(1f, AnimationCurve.Linear(0, (col + 0.05f) / 16f, 1, (col + frames - 0.05f) / 16f));
        }

        /// <summary>Colour over lifetime: red = ramp heat from h0 to h1, alpha fades out (aMid at 60 %).</summary>
        static void Heat(ParticleSystem ps, float h0, float h1, float aMid = 1f, float aStart = 1f)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(h0, 0, 0), 0), new GradientColorKey(new Color(h1, 0, 0), 1) },
                      new[] { new GradientAlphaKey(aStart, 0), new GradientAlphaKey(aMid, 0.6f), new GradientAlphaKey(0, 1) });
            var c = ps.colorOverLifetime; c.enabled = true; c.color = new ParticleSystem.MinMaxGradient(g);
        }

        static void SizeCurve(ParticleSystem ps, params Keyframe[] keys)
        {
            var s = ps.sizeOverLifetime; s.enabled = true; s.size = new MMC(1f, new AnimationCurve(keys));
        }

        static void Cone(ParticleSystem ps, float angle, float radius, float speed0, float speed1)
        {
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = angle; sh.radius = radius;
            var m = ps.main; m.startSpeed = new MMC(speed0, speed1);
        }

        static void Sphere(ParticleSystem ps, float radius, float speed0, float speed1)
        {
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = radius;
            var m = ps.main; m.startSpeed = new MMC(speed0, speed1);
        }

        static void Box(ParticleSystem ps, float speed0 = 0, float speed1 = 0, bool randomDir = false)
        {
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = Vector3.one * 0.5f;
            sh.randomDirectionAmount = randomDir ? 1 : 0;
            var m = ps.main; m.startSpeed = new MMC(speed0, speed1);
        }

        static void Rise(ParticleSystem ps, float v0, float v1)
        {
            var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.Local;
            v.x = new MMC(0, 0); v.y = new MMC(v0, v1); v.z = new MMC(0, 0);
        }

        static void Gravity(ParticleSystem ps, float g) { ps.gameObject.AddComponent<VfxGravity>().g = g; }

        static void Drag(ParticleSystem ps, float drag)
        {
            var l = ps.limitVelocityOverLifetime; l.enabled = true; l.limit = 100; l.drag = drag;
        }

        static void Spin(ParticleSystem ps, float degPerSec)
        {
            var m = ps.main; m.startRotation = new MMC(0, Mathf.PI * 2);
            var r = ps.rotationOverLifetime; r.enabled = true; r.z = new MMC(-degPerSec * Mathf.Deg2Rad, degPerSec * Mathf.Deg2Rad);
        }

        static void Stretch(ParticleSystem ps, float velocityScale, float length = 1)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = velocityScale; r.lengthScale = length;
        }

        static void Noise(ParticleSystem ps, float strength, float freq)
        {
            var n = ps.noise; n.enabled = true; n.strength = strength; n.frequency = freq; n.scrollSpeed = 0.6f; n.quality = ParticleSystemNoiseQuality.Low;
        }

        // ------------------------------------------------------------------ balls

        static GameObject BuildBall(VfxElement el)
        {
            int e = (int)el;
            const float s = 1f / 16f;
            var ballMesh = Quad("Q_Ball_" + Names[e], e * s, 1 - s, (e + 1) * s, 1);
            var haloMesh = Quad("Q_Halo", 0.5f, 1 - 2 * s, 0.625f, 1);

            var root = new GameObject("Ball_" + Names[e]);
            var b = root.AddComponent<VfxBall>();
            b.element = el;
            b.life = 0;
            var trail = Mat("M_" + Names[e] + "_Trail", "RicochetVfx/Ribbon", e,
                ("_Boost", 2.4f), ("_Steps", 8),
                ("_Core", new[] { 0.45f, 0.5f, 0.5f, 0.5f }[e]), ("_Glow", new[] { 1f, 0.8f, 0.7f, 0.6f }[e]),
                ("_Flicker", new[] { 0.35f, 0.1f, 0.25f, 0.04f }[e]), ("_Fps", e == 2 ? 15 : 12), ("_Segments", e == 0 ? 10 : 14), ("_TailFade", 0));
            b.ribbon = Ribbon(root, "Trail", trail);

            var fxMat = Particle(e);
            ParticleSystem fx;
            switch (el)
            {
                case VfxElement.Ember:     // rising sparks, ≤ 40
                    fx = PS(root, "TrailFx", fxMat, 0.3f, 0.55f, 0.16f, 0.24f, 40);
                    Sheet(fx, VfxAtlasBaker.RowFire, 4); Heat(fx, 1f, 0.35f); Gravity(fx, -1.5f); Drag(fx, 1.5f);
                    b.trailTime = 0.3f; b.trailWidth = 0.46f; b.emitSpacing = 0.12f;
                    break;
                case VfxElement.Rime:      // hanging frost flakes, ≤ 30
                    fx = PS(root, "TrailFx", fxMat, 0.45f, 0.7f, 0.16f, 0.26f, 30);
                    Sheet(fx, VfxAtlasBaker.RowIce, 2, 2, true); Heat(fx, 0.95f, 0.55f, 0.8f); Spin(fx, 90); Drag(fx, 2f);
                    b.trailTime = 0.45f; b.trailWidth = 0.22f; b.emitSpacing = 0.2f;
                    break;
                case VfxElement.Spark:     // zaps, ≤ 20
                    fx = PS(root, "TrailFx", fxMat, 0.1f, 0.18f, 0.25f, 0.35f, 20);
                    Sheet(fx, VfxAtlasBaker.RowZap, 1, 4, true); Heat(fx, 1f, 0.6f); Spin(fx, 0);
                    b.trailTime = 0.28f; b.trailWidth = 0.14f; b.emitSpacing = 0.3f;
                    break;
                default:                   // lens glints, ≤ 12
                    fx = PS(root, "TrailFx", fxMat, 0.25f, 0.35f, 0.3f, 0.3f, 12);
                    Sheet(fx, VfxAtlasBaker.RowRay, 0); Heat(fx, 1f, 0.7f); SizeCurve(fx, new Keyframe(0, 1), new Keyframe(1, 0));
                    b.trailTime = 0.24f; b.trailWidth = 0.26f; b.emitSpacing = 0.45f;
                    break;
            }
            b.trailFx = fx;

            var bankSize = 0.5f;
            var halo = Child(root, "Halo");
            Renderer(halo, haloMesh, Mat("M_" + Names[e] + "_Halo", "RicochetVfx/Particle", e, ("_Billboard", 1), ("_Boost", 1.5f), ("_Steps", 6), ("_Flicker", 0)));
            halo.transform.localScale = Vector3.one * bankSize * 2.4f;
            b.halo = halo.transform;
            var sprite = Child(root, "Sprite");
            Renderer(sprite, ballMesh, Mat("M_" + Names[e] + "_Ball", "RicochetVfx/BallSprite", e, ("_Boost", 2.2f), ("_Flicker", e == 0 || e == 2 ? 1 : 0)));
            sprite.transform.localScale = Vector3.one * bankSize;
            b.sprite = sprite.transform;
            return Save(root, "Ball_" + Names[e]);
        }

        // ------------------------------------------------------------------ hits

        static GameObject BuildHit(VfxElement el)
        {
            int e = (int)el;
            var mat = Particle(e);
            GameObject root;
            switch (el)
            {
                case VfxElement.Ember:
                {
                    root = new GameObject("Hit_Ember");
                    root.AddComponent<VfxOneShot>().life = 0.9f;
                    var flash = PS(root, "Flash", mat, 0.14f, 0.14f, 0.9f, 0.9f, 1);       // 1
                    Burst(flash, 1); Sheet(flash, VfxAtlasBaker.RowFlash, 0, 3); Heat(flash, 1f, 0.7f, 1f);
                    var sparks = PS(root, "Sparks", mat, 0.25f, 0.45f, 0.14f, 0.2f, 12); // 12
                    Burst(sparks, 12); Cone(sparks, 70, 0.05f, 3f, 6f); Sheet(sparks, VfxAtlasBaker.RowFire, 4);
                    Heat(sparks, 1f, 0.3f); Stretch(sparks, 0.04f); Gravity(sparks, 9f); Drag(sparks, 1f);
                    var embers = PS(root, "Embers", mat, 0.35f, 0.5f, 0.3f, 0.45f, 4);   // 4
                    Burst(embers, 4); Cone(embers, 60, 0.1f, 0.6f, 1.2f); Sheet(embers, VfxAtlasBaker.RowFire, 0, 4);
                    Heat(embers, 1f, 0.4f); Rise(embers, 0.8f, 1.4f);
                    break;
                }
                case VfxElement.Rime:
                {
                    root = new GameObject("Hit_Rime");
                    root.AddComponent<VfxOneShot>().life = 0.9f;
                    var ring = PS(root, "Ring", mat, 0.2f, 0.2f, 1.1f, 1.1f, 1);          // 1
                    Burst(ring, 1); Sheet(ring, VfxAtlasBaker.RowIce, 4); Heat(ring, 1f, 0.5f, 0.8f);
                    SizeCurve(ring, new Keyframe(0, 0.3f), new Keyframe(1, 1));
                    var shards = PS(root, "Shards", mat, 0.35f, 0.55f, 0.22f, 0.34f, 8);  // 8
                    Burst(shards, 8); Cone(shards, 75, 0.05f, 2.5f, 4.5f); Sheet(shards, VfxAtlasBaker.RowIce, 0, 2, true);
                    Heat(shards, 0.95f, 0.55f); Spin(shards, 540); Gravity(shards, 7f); Drag(shards, 1f);
                    var dust = PS(root, "Dust", mat, 0.3f, 0.5f, 0.2f, 0.3f, 6);          // 6
                    Burst(dust, 6); Sphere(dust, 0.15f, 0.3f, 0.9f); Sheet(dust, VfxAtlasBaker.RowIce, 5); Heat(dust, 1f, 0.6f);
                    break;
                }
                case VfxElement.Spark:
                {
                    root = new GameObject("Hit_Spark");
                    var c = root.AddComponent<VfxCrackle>();
                    c.life = 0.5f;
                    var bolt = Mat("M_Spark_Bolt", "RicochetVfx/Ribbon", 2, ("_Boost", 3f), ("_Steps", 8), ("_Core", 0.3f), ("_Glow", 0.75f),
                        ("_Flicker", 0.15f), ("_Fps", 20), ("_Segments", 20), ("_TailFade", 0));
                    c.arcs = new VfxRibbon[4];
                    for (int i = 0; i < 4; i++) c.arcs[i] = Ribbon(root, "Arc" + i, bolt);
                    var flash = PS(root, "Flash", mat, 0.12f, 0.12f, 1f, 1f, 1);          // 1
                    Burst(flash, 1); Sheet(flash, VfxAtlasBaker.RowZap, 5); Heat(flash, 1f, 0.8f);
                    var zaps = PS(root, "Zaps", mat, 0.12f, 0.22f, 0.12f, 0.18f, 6);      // 6
                    Burst(zaps, 6); Cone(zaps, 85, 0.05f, 3f, 5f); Sheet(zaps, VfxAtlasBaker.RowZap, 0); Heat(zaps, 1f, 0.6f); Stretch(zaps, 0.05f);
                    break;
                }
                default:
                {
                    root = new GameObject("Hit_Ray");
                    root.AddComponent<VfxOneShot>().life = 0.7f;
                    RayBurst(root, mat, 1.3f, 5);
                    var ring = PS(root, "Ring", mat, 0.22f, 0.22f, 1.2f, 1.2f, 1);        // 1
                    Burst(ring, 1); Sheet(ring, VfxAtlasBaker.RowRay, 1); Heat(ring, 0.9f, 0.4f, 0.8f);
                    SizeCurve(ring, new Keyframe(0, 0.2f), new Keyframe(1, 1));
                    break;
                }
            }
            return Save(root, "Hit_" + Names[e]);
        }

        /// <summary>Laser charge flare (flipbook star → ring) plus a few glints: the Ray hit and the per-enemy row spark.</summary>
        static void RayBurst(GameObject root, Material mat, float flareSize, int glints)
        {
            var flare = PS(root, "Flare", mat, 0.3f, 0.3f, flareSize, flareSize, 1);   // 1
            Burst(flare, 1); Sheet(flare, VfxAtlasBaker.RowRay, 3, 3); Heat(flare, 1f, 0.8f, 1f);
            SizeCurve(flare, new Keyframe(0, 0.5f), new Keyframe(0.3f, 1f), new Keyframe(1, 0.9f));
            var gl = PS(root, "Glints", mat, 0.25f, 0.4f, 0.14f, 0.2f, glints);       // ≤ 5
            Burst(gl, glints); Cone(gl, 80, 0.05f, 1.5f, 3f); Sheet(gl, VfxAtlasBaker.RowRay, 2); Heat(gl, 1f, 0.5f); Drag(gl, 3f);
        }

        static GameObject BuildRowHit()
        {
            var root = new GameObject("RowHit_Ray");
            root.AddComponent<VfxOneShot>().life = 0.6f;
            RayBurst(root, Particle(3), 0.9f, 4);
            return Save(root, "RowHit_Ray");
        }

        // ------------------------------------------------------------------ statuses

        static GameObject BuildBurn()
        {
            var mat = Particle(0);
            var root = new GameObject("Status_Burn");
            root.AddComponent<VfxStatus>().life = 0;
            var flames = PS(root, "Flames", mat, 0.35f, 0.55f, 0.42f, 0.62f, 18, true);  // ≤ 18
            Rate(flames, 18); Box(flames); Sheet(flames, VfxAtlasBaker.RowFire, 0, 4);
            Heat(flames, 1f, 0.45f, 1f, 0.6f); Rise(flames, 0.8f, 1.3f); Noise(flames, 0.3f, 1.5f);
            var embers = PS(root, "Embers", mat, 0.5f, 0.8f, 0.12f, 0.16f, 6, true);     // ≤ 6
            Rate(embers, 5); Box(embers); Sheet(embers, VfxAtlasBaker.RowFire, 4);
            Heat(embers, 1f, 0.3f); Rise(embers, 1.2f, 1.8f); Noise(embers, 0.6f, 2f);
            return Save(root, "Status_Burn");
        }

        static GameObject BuildFreeze()
        {
            var mat = Particle(1);
            var root = new GameObject("Status_Freeze");
            var shell = root.AddComponent<VfxIceShell>();
            shell.life = 0;
            shell.shellMaterial = Mat("M_Rime_IceShell", "RicochetVfx/IceShell", 1, ("_Boost", 2.2f), ("_Opacity", 0.55f), ("_Amount", 1));
            var gl = PS(root, "Glints", mat, 0.2f, 0.35f, 0.18f, 0.26f, 4, true);        // ≤ 4
            Rate(gl, 4); Box(gl); Sheet(gl, VfxAtlasBaker.RowIce, 5); Heat(gl, 1f, 0.8f);
            return Save(root, "Status_Freeze");
        }

        static GameObject BuildShatter()
        {
            var mat = Particle(1);
            var root = new GameObject("Shatter_Rime");
            root.AddComponent<VfxOneShot>().life = 1f;
            var shards = PS(root, "Shards", mat, 0.45f, 0.7f, 0.25f, 0.4f, 16);          // 16
            Burst(shards, 16); Box(shards, 2f, 4.5f, true); Sheet(shards, VfxAtlasBaker.RowIce, 0, 2, true);
            Heat(shards, 1f, 0.5f); Spin(shards, 720); Gravity(shards, 9f); Drag(shards, 0.5f);
            var puff = PS(root, "Puff", mat, 0.22f, 0.22f, 1.8f, 1.8f, 1);               // 1
            Burst(puff, 1); Sheet(puff, VfxAtlasBaker.RowIce, 4); Heat(puff, 1f, 0.5f, 0.7f);
            SizeCurve(puff, new Keyframe(0, 0.4f), new Keyframe(1, 1));
            var dust = PS(root, "Dust", mat, 0.4f, 0.7f, 0.18f, 0.28f, 10);              // 10
            Burst(dust, 10); Box(dust, 0.4f, 1.2f, true); Sheet(dust, VfxAtlasBaker.RowIce, 2, 2, true); Heat(dust, 1f, 0.6f); Spin(dust, 120);
            return Save(root, "Shatter_Rime");
        }

        // ------------------------------------------------------------------ chain and beam

        static GameObject BuildBolt()
        {
            var mat = Particle(2);
            var bolt = Mat("M_Spark_Bolt", "RicochetVfx/Ribbon", 2, ("_Boost", 3f), ("_Steps", 8), ("_Core", 0.3f), ("_Glow", 0.75f),
                ("_Flicker", 0.15f), ("_Fps", 20), ("_Segments", 20), ("_TailFade", 0));
            var root = new GameObject("Chain_Bolt");
            var b = root.AddComponent<VfxBolt>();
            b.life = 0.32f;
            b.main = Ribbon(root, "Main", bolt);
            b.fork = Ribbon(root, "Fork", bolt);
            var flash = PS(root, "EndFlash", mat, 0.14f, 0.14f, 0.9f, 0.9f, 1);           // 1
            Burst(flash, 1); Sheet(flash, VfxAtlasBaker.RowZap, 5); Heat(flash, 1f, 0.7f);
            var zaps = PS(root, "EndZaps", mat, 0.12f, 0.2f, 0.12f, 0.18f, 5);            // 5
            Burst(zaps, 5); Sphere(zaps, 0.1f, 2f, 4f); Sheet(zaps, VfxAtlasBaker.RowZap, 0); Heat(zaps, 1f, 0.6f); Stretch(zaps, 0.05f);
            return Save(root, "Chain_Bolt");
        }

        static GameObject BuildBeam()
        {
            var mat = Particle(3);
            var root = new GameObject("Row_Beam");
            var b = root.AddComponent<VfxRowBeam>();
            b.life = 1.2f;
            b.glow = Ribbon(root, "Glow", Mat("M_Ray_BeamGlow", "RicochetVfx/Ribbon", 3, ("_Boost", 1.8f), ("_Steps", 6), ("_Core", 0f), ("_Glow", 1f),
                ("_Flicker", 0.05f), ("_Fps", 24), ("_Segments", 30), ("_TailFade", 0)));
            b.core = Ribbon(root, "Core", Mat("M_Ray_BeamCore", "RicochetVfx/Ribbon", 3, ("_Boost", 3.2f), ("_Steps", 8), ("_Core", 0.55f), ("_Glow", 0.6f),
                ("_Flicker", 0.08f), ("_Fps", 24), ("_Segments", 40), ("_TailFade", 0)));
            var charge = PS(root, "Charge", mat, 0.15f, 0.17f, 0.14f, 0.2f, 10);          // 10
            Burst(charge, 10); Sphere(charge, 0.9f, -5.5f, -5.5f); Sheet(charge, VfxAtlasBaker.RowRay, 2);
            Heat(charge, 0.7f, 1f, 1f, 0.3f); Stretch(charge, 0.04f);
            var flare = PS(root, "Flare", mat, 0.45f, 0.45f, 1.6f, 1.6f, 1);              // 1
            Burst(flare, 1); Sheet(flare, VfxAtlasBaker.RowRay, 3, 3); Heat(flare, 0.8f, 0.6f, 1f);
            SizeCurve(flare, new Keyframe(0, 0.15f), new Keyframe(0.3f, 0.6f), new Keyframe(0.4f, 1f), new Keyframe(1, 0.5f));
            var sparkle = PS(root, "Sparkle", mat, 0.35f, 0.6f, 0.2f, 0.3f, 20);          // ≤ 20, emitted along the beam
            Sheet(sparkle, VfxAtlasBaker.RowRay, 0); Heat(sparkle, 1f, 0.6f); SizeCurve(sparkle, new Keyframe(0, 1), new Keyframe(1, 0)); Drag(sparkle, 2f);
            var caps = PS(root, "Caps", mat, 0.18f, 0.18f, 0.8f, 0.8f, 4);                // ≤ 4, both board edges
            Sheet(caps, VfxAtlasBaker.RowFlash, 0, 3); Heat(caps, 1f, 0.7f);
            b.sparkle = sparkle;
            b.caps = caps;
            return Save(root, "Row_Beam");
        }
    }
}
