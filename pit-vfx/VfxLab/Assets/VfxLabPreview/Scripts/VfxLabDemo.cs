// Preview driver: one ball bounces around a 7-wide board between a few pit-art enemies and fires every effect of its
// element through the public VfxBank API, exactly as the game would:
//   every hit: Hit + Flash;  Ember: Status burn 2.5 s;  Rime: freeze 1.6 s, then shatter;
//   Spark: Chain to the two nearest enemies;  Ray: RowBeam across the hit enemy's row.
// Board convention as pit-unity: board = world XY, up out of the floor = -Z.
using System.Collections.Generic;
using RicochetVfx;
using UnityEngine;

public sealed class VfxLabDemo : MonoBehaviour
{
    public VfxElement element = VfxElement.Ember;
    [Tooltip("Play mode: switch element every cycleSeconds.")] public bool cycle = true;
    public float cycleSeconds = 7f;
    public Transform ball;
    public GameObject[] enemies;
    public float ballSpeed = 7.5f, ballRadius = 0.2f, ballHeight = 0.35f, enemyRadius = 0.42f;
    public float halfWidth = 3.5f, farY = 4f, nearY = -4.5f;
    public Vector2 launchFrom = new Vector2(0.4f, -4.2f);
    public float launchDeg = 68f;

    Vector2 pos, vel;
    VfxBall vis;
    float clock;
    int hits;
    float[] burnLeft, freezeLeft, punch;
    Vector3[] baseScale;

    void Start()
    {
        Restart(element);
    }

    void Update()
    {
        if (VfxBank.Instance != null && VfxBank.Instance.manualTick) return;    // the capture drives Step itself
        Step(Time.deltaTime);
        if (cycle && clock > cycleSeconds) Restart((VfxElement)(((int)element + 1) % VfxElements.Count));
    }

    public void Restart(VfxElement e)
    {
        VfxBank.Ensure();
        if (baseScale == null)
        {
            baseScale = new Vector3[enemies.Length];
            for (int i = 0; i < enemies.Length; i++) baseScale[i] = enemies[i].transform.localScale;
        }
        foreach (var en in enemies)
        {
            var hf = en.GetComponent<VfxHitFeedback>();
            if (hf != null) hf.ResetNow();
        }
        VfxBank.ReleaseAll();
        element = e;
        clock = 0;
        hits = 0;
        burnLeft = new float[enemies.Length];
        freezeLeft = new float[enemies.Length];
        punch = new float[enemies.Length];
        for (int i = 0; i < enemies.Length; i++) enemies[i].transform.localScale = baseScale[i];
        pos = launchFrom;
        float a = launchDeg * Mathf.Deg2Rad;
        vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ballSpeed;
        ball.position = World(pos, ballHeight);
        vis = VfxBank.Ball(VfxElements.BallId(e), ball);
    }

    Vector3 World(Vector2 p, float h) { return new Vector3(p.x, p.y, 0) + VfxBank.Up * h; }
    Vector3 Center(int i) { return enemies[i].transform.position + VfxBank.Up * 0.45f; }

    public void Step(float dt)
    {
        clock += dt;
        const int sub = 4;
        for (int s = 0; s < sub; s++) Move(dt / sub);
        ball.position = World(pos, ballHeight);

        string id = VfxElements.BallId(element);
        for (int i = 0; i < enemies.Length; i++)
        {
            if (burnLeft[i] > 0 && (burnLeft[i] -= dt) <= 0) VfxBank.Status(enemies[i], VfxElement.Ember, false);
            if (freezeLeft[i] > 0 && (freezeLeft[i] -= dt) <= 0) VfxBank.Status(enemies[i], VfxElement.Rime, false);
            punch[i] = Mathf.Max(0, punch[i] - dt / 0.15f);
            float k = Mathf.Ceil(punch[i] * 3) / 3;      // stepped squash, pixel-rate
            enemies[i].transform.localScale = Vector3.Scale(baseScale[i], new Vector3(1 + 0.1f * k, 1 + 0.1f * k, 1 - 0.12f * k));
        }
    }

    void Move(float dt)
    {
        pos += vel * dt;
        float r = ballRadius;
        if (pos.x < -halfWidth + r) { pos.x = -halfWidth + r; vel.x = Mathf.Abs(vel.x); }
        if (pos.x > halfWidth - r) { pos.x = halfWidth - r; vel.x = -Mathf.Abs(vel.x); }
        if (pos.y > farY - r) { pos.y = farY - r; vel.y = -Mathf.Abs(vel.y); }
        if (pos.y < nearY + r) { pos.y = nearY + r; vel.y = Mathf.Abs(vel.y); }
        for (int i = 0; i < enemies.Length; i++)
        {
            var c = (Vector2)enemies[i].transform.position;
            var d = pos - c;
            float rr = enemyRadius + r;
            if (d.sqrMagnitude >= rr * rr || Vector2.Dot(vel, d) >= 0) continue;
            var n = d.normalized;
            vel = Vector2.Reflect(vel, n);
            pos = c + n * rr;
            OnHit(i, c + n * enemyRadius, n);
        }
    }

    void OnHit(int i, Vector2 contact, Vector2 n)
    {
        hits++;
        string id = VfxElements.BallId(element);
        var en = enemies[i];
        VfxBank.Hit(id, World(contact, ballHeight), new Vector3(n.x, n.y, 0));
        VfxBank.Flash(en, id);
        punch[i] = 1;
        switch (element)
        {
            case VfxElement.Ember:
                if (burnLeft[i] <= 0) VfxBank.Status(en, VfxElement.Ember, true);
                burnLeft[i] = 2.5f;
                break;
            case VfxElement.Rime:
                if (freezeLeft[i] <= 0) { VfxBank.Status(en, VfxElement.Rime, true); freezeLeft[i] = 1.6f; }
                break;
            case VfxElement.Spark:
            {
                var order = new List<int>();
                for (int j = 0; j < enemies.Length; j++) if (j != i) order.Add(j);
                order.Sort((a, b) => Vector3.Distance(Center(i), Center(a)).CompareTo(Vector3.Distance(Center(i), Center(b))));
                var pts = new List<Vector3> { Center(i) };
                for (int k = 0; k < 2 && k < order.Count; k++)
                {
                    int j = order[k];
                    if (Vector3.Distance(pts[pts.Count - 1], Center(j)) > 3.2f) break;
                    pts.Add(Center(j));
                    var target = enemies[j];
                    int jj = j;
                    VfxBank.Delay(k * 0.07f, () => { VfxBank.Flash(target, "spark"); punch[jj] = 1; });
                }
                VfxBank.Chain(pts);
                break;
            }
            case VfxElement.Ray:
            {
                float rowY = en.transform.position.y;
                var targets = new List<Vector3>();
                var hitList = new List<int>();
                for (int j = 0; j < enemies.Length; j++)
                    if (Mathf.Abs(enemies[j].transform.position.y - rowY) < 0.5f) { targets.Add(Center(j)); hitList.Add(j); }
                VfxBank.RowBeam(rowY, -halfWidth, halfWidth, targets, contact.x);
                VfxBank.Delay(0.14f, () => { foreach (var j in hitList) { VfxBank.Flash(enemies[j], "ray"); punch[j] = 1; } });
                break;
            }
        }
    }
}
