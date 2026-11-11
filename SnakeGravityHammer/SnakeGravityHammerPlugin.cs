using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SnakeGravityHammer
{
    public enum FitMode
    {
        CenterOfMass,
        Average
    }

    public enum KeyMode
    {
        Hold,
        Toggle
    }

    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class SnakeGravityHammerPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.snakesquad.snakegravityhammer";
        public const string PluginName = "SnakeGravityHammer";
        public const string PluginVersion = "1.3.1";

        internal static SnakeGravityHammerPlugin? Instance;

        internal static ConfigEntry<bool> Enabled = null!;
        internal static ConfigEntry<KeyboardShortcut> GravityKey = null!;
        internal static ConfigEntry<KeyMode> Mode = null!;
        internal static ConfigEntry<float> SinkDepth = null!;
        internal static ConfigEntry<float> MaxTilt = null!;
        internal static ConfigEntry<int> Samples = null!;
        internal static ConfigEntry<bool> RestOnPieces = null!;
        internal static ConfigEntry<FitMode> Fit = null!;
        internal static ConfigEntry<float> MaxStepUp = null!;
        internal static ConfigEntry<bool> LogFit = null!;

        private static bool _toggledOn;

        private void Awake()
        {
            Instance = this;

            Enabled = Config.Bind("1 - General", "Enabled", true,
                "Turns the whole mod on or off.");
            GravityKey = Config.Bind("1 - General", "Gravity Key", new KeyboardShortcut(KeyCode.LeftAlt),
                "While placing a piece with the hammer, this key makes the preview tilt and settle onto " +
                "whatever is underneath it (terrain, rocks, other pieces), as if it had been dropped there.");
            Mode = Config.Bind("1 - General", "Key Mode", KeyMode.Hold,
                "Hold: gravity placement only while the key is held down. " +
                "Toggle: press the key once to turn it on, again to turn it off.");

            SinkDepth = Config.Bind("2 - Settling", "Sink Depth", 0.1f,
                new ConfigDescription(
                    "How far (in metres) the piece may sink into the ground or objects below it. A little " +
                    "overlap hides the gaps under a piece on bumpy ground. 0 = rest exactly on the highest " +
                    "point underneath.",
                    new AcceptableValueRange<float>(0f, 1f)));
            MaxTilt = Config.Bind("2 - Settling", "Max Tilt", 45f,
                new ConfigDescription(
                    "Steepest tilt (in degrees) a piece will take. On steeper slopes it tilts this far and no more.",
                    new AcceptableValueRange<float>(0f, 89f)));
            Samples = Config.Bind("2 - Settling", "Samples Per Side", 5,
                new ConfigDescription(
                    "How many points along each side of the piece's base are checked for ground (5 = a 5x5 " +
                    "grid). More fits uneven ground more closely.",
                    new AcceptableValueRange<int>(2, 7)));
            Fit = Config.Bind("2 - Settling", "Fit", FitMode.CenterOfMass,
                "CenterOfMass: the piece settles the way a real object would, tipping over its middle until " +
                "it rests on the ground points that hold it up (it can rock onto a bump rather than follow " +
                "the average slope). Average: tilt to the average slope under the piece, then rest on the " +
                "highest point.");
            MaxStepUp = Config.Bind("2 - Settling", "Max Step Up", 0.5f,
                new ConfigDescription(
                    "How far (in metres) above where the game itself would put the piece it may rise to rest " +
                    "on something. Stops the top of a neighbouring piece next to it from lifting it into the " +
                    "air. To stack onto a piece, aim the cursor at that piece.",
                    new AcceptableValueRange<float>(0f, 3f)));
            RestOnPieces = Config.Bind("2 - Settling", "Rest On Pieces", true,
                "Pieces can rest on other built pieces, not just terrain and rocks.");

            LogFit = Config.Bind("9 - Debug", "Log Fit", false,
                "Log the fitted tilt and height each time a piece is placed with gravity placement.");

            new Harmony(PluginGuid).PatchAll(typeof(SnakeGravityHammerPlugin).Assembly);
        }

        private void Update()
        {
            if (Mode.Value == KeyMode.Toggle && GravityKey.Value.IsDown() && CanTakeInput())
            {
                _toggledOn = !_toggledOn;
                var player = Player.m_localPlayer;
                if (player != null)
                    player.Message(MessageHud.MessageType.TopLeft,
                        _toggledOn ? "Gravity placement on" : "Gravity placement off");
            }
        }

        private static bool CanTakeInput()
        {
            var player = Player.m_localPlayer;
            return player != null && player.InPlaceMode() && player.TakeInput();
        }

        internal static bool Active()
        {
            if (!Enabled.Value)
                return false;
            if (Mode.Value == KeyMode.Toggle)
                return _toggledOn;
            var key = GravityKey.Value;
            if (key.MainKey == KeyCode.None)
                return false;
            if (!Input.GetKey(key.MainKey))
                return false;
            foreach (var mod in key.Modifiers)
                if (!Input.GetKey(mod))
                    return false;
            return true;
        }

        internal static void Log(string text) => Instance?.Logger.LogInfo(text);
    }

    /// <summary>
    /// Runs after vanilla (and after Valheim Plus / other building mods) has put the placement
    /// ghost where it would go. Keeps that spot and facing, then tilts and lowers the ghost so it
    /// rests on what is underneath. Placing uses the ghost's pose, so the piece lands as shown.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.UpdatePlacementGhost))]
    [HarmonyAfter("org.bepinex.plugins.valheim_plus", "com.codepeople.smartbuild")]
    internal static class UpdatePlacementGhostPatch
    {
        // What we set last frame, so we can tell if nobody moved the ghost since (some building
        // modes leave the ghost alone between frames) and start from the original pose again
        // instead of tilting an already tilted ghost.
        private static GameObject? _lastGhost;
        private static Vector3 _setPos;
        private static Quaternion _setRot;
        private static Vector3 _basePos;
        private static Quaternion _baseRot;

        // Ghost base shape, cached per ghost.
        private static GameObject? _boundsGhost;
        private static Bounds _localBounds;
        private static bool _hasBounds;

        private static readonly List<Vector3> SampleLocal = new List<Vector3>();
        private static readonly List<Vector3> HitLocal = new List<Vector3>();

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer)
                return;
            var ghost = __instance.m_placementGhost;
            if (ghost == null || !ghost.activeSelf)
            {
                _lastGhost = null;
                return;
            }

            var t = ghost.transform;
            if (_lastGhost == ghost && t.position == _setPos && t.rotation == _setRot)
            {
                // Untouched since our last change (some building modes and the frame after placing
                // don't reset the ghost): start again from the pose before our tilt.
                t.position = _basePos;
                t.rotation = _baseRot;
            }
            else
            {
                _basePos = t.position;
                _baseRot = t.rotation;
            }
            // From here the ghost is at its untilted pose; anything that returns early leaves it so.
            _lastGhost = null;

            if (!SnakeGravityHammerPlugin.Active())
                return;

            var piece = ghost.GetComponent<Piece>();
            if (piece == null || !Suitable(piece, ghost))
                return;

            // Snapped onto another piece's snap point: the snap decides the pose, so stand aside.
            if (IsSnapped(ghost))
                return;

            _lastGhost = ghost;
            try
            {
                if (Settle(ghost, out var pos, out var rot))
                {
                    t.position = pos;
                    t.rotation = rot;
                }
                else
                {
                    t.position = _basePos;
                    t.rotation = _baseRot;
                }
            }
            catch (Exception e)
            {
                SnakeGravityHammerPlugin.Log("Gravity placement failed: " + e);
                t.position = _basePos;
                t.rotation = _baseRot;
            }
            _setPos = t.position;
            _setRot = t.rotation;
        }

        private static readonly List<Transform> GhostSnaps = new List<Transform>();
        private static readonly List<Transform> OtherSnaps = new List<Transform>();
        private static readonly List<Piece> NearPieces = new List<Piece>();

        /// <summary>True when one of the ghost's snap points sits on a snap point of a placed piece.</summary>
        private static bool IsSnapped(GameObject ghost)
        {
            GhostSnaps.Clear();
            foreach (var tr in ghost.GetComponentsInChildren<Transform>())
                if (tr.CompareTag("snappoint"))
                    GhostSnaps.Add(tr);
            if (GhostSnaps.Count == 0)
                return false;

            float radius = 1f;
            foreach (var sp in GhostSnaps)
                radius = Mathf.Max(radius, Vector3.Distance(sp.position, ghost.transform.position) + 1f);
            NearPieces.Clear();
            Piece.GetAllPiecesInRadius(ghost.transform.position, radius, NearPieces);
            foreach (var other in NearPieces)
            {
                if (other == null || other.gameObject == ghost || other.transform.IsChildOf(ghost.transform))
                    continue;
                OtherSnaps.Clear();
                other.GetSnapPoints(OtherSnaps);
                foreach (var b in OtherSnaps)
                foreach (var a in GhostSnaps)
                    if ((a.position - b.position).sqrMagnitude < 0.0004f) // within 2 cm
                        return true;
            }
            return false;
        }

        private static bool Suitable(Piece piece, GameObject ghost)
        {
            // Hoe/cultivator terrain tools and water pieces keep vanilla behaviour.
            if (piece.m_waterPiece || piece.m_cultivatedGroundOnly)
                return false;
            if (ghost.GetComponentInChildren<TerrainOp>() != null || ghost.GetComponentInChildren<TerrainModifier>() != null)
                return false;
            return true;
        }

        private static bool Settle(GameObject ghost, out Vector3 pos, out Quaternion rot)
        {
            pos = _basePos;
            rot = _baseRot;

            if (_boundsGhost != ghost)
            {
                _boundsGhost = ghost;
                _hasBounds = LocalBounds(ghost, out _localBounds);
            }
            if (!_hasBounds)
                return false;

            // Keep the facing the player chose (yaw only); the tilt is worked out here.
            var fwd = _baseRot * Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f)
                fwd = _baseRot * Vector3.up;
            fwd.y = 0f;
            var yaw = fwd.sqrMagnitude < 1e-4f ? Quaternion.identity : Quaternion.LookRotation(fwd.normalized, Vector3.up);

            var b = _localBounds;
            var size = b.size;
            int n = SnakeGravityHammerPlugin.Samples.Value;
            float reach = Mathf.Max(size.x, size.y, size.z) + 3f;
            float stepUp = SnakeGravityHammerPlugin.MaxStepUp.Value;
            float baseBottom = (_basePos + _baseRot * b.center).y - Mathf.Abs((_baseRot * new Vector3(0f, b.extents.y, 0f)).y);

            // Sample a grid over the base of the piece, cast rays straight down, and collect the
            // ground heights in the piece's yaw frame (x right, z forward, y up from its pivot).
            int mask = RayMask();
            SampleLocal.Clear();
            HitLocal.Clear();
            var inv = Quaternion.Inverse(yaw);
            for (int i = 0; i < n; i++)
            {
                float fx = n == 1 ? 0.5f : i / (float)(n - 1);
                for (int j = 0; j < n; j++)
                {
                    float fz = n == 1 ? 0.5f : j / (float)(n - 1);
                    // Inset slightly so a neighbour merely touching the edge doesn't count as underneath.
                    float fxi = Mathf.Lerp(0.05f, 0.95f, fx);
                    float fzi = Mathf.Lerp(0.05f, 0.95f, fz);
                    var local = new Vector3(Mathf.Lerp(b.min.x, b.max.x, fxi), b.min.y, Mathf.Lerp(b.min.z, b.max.z, fzi));
                    SampleLocal.Add(local);
                    var world = _basePos + yaw * new Vector3(local.x, 0f, local.z);
                    // Start just above where the game put the base: surfaces higher than that (a
                    // neighbour's top) are ignored; a ray starting inside a collider doesn't hit it.
                    var from = new Vector3(world.x, baseBottom + stepUp, world.z);
                    if (CastDown(from, stepUp + reach, mask, ghost.transform, out var hitPoint))
                    {
                        var rel = inv * (hitPoint - _basePos);
                        HitLocal.Add(new Vector3(local.x, rel.y, local.z));
                    }
                }
            }

            if (HitLocal.Count == 0)
                return false;

            float maxTilt = SnakeGravityHammerPlugin.MaxTilt.Value;
            float cx = b.center.x, cz = b.center.z;

            // Center of mass: the lowest plane that lies on top of every ground point, measured
            // under the piece's middle. That's where a rigid piece comes to rest (the upper hull
            // facet under its center). Falls back to Average when no such plane is within Max Tilt.
            float a = 0f, c = 0f, d;
            if (SnakeGravityHammerPlugin.Fit.Value == FitMode.CenterOfMass && HitLocal.Count >= 3
                && RestingPlane(HitLocal, cx, cz, Mathf.Tan(maxTilt * Mathf.Deg2Rad), out var ra, out var rc, out var rd))
            {
                a = ra;
                c = rc;
                d = rd;
            }
            // Least-squares plane y = a*x + c*z + d through the ground heights.
            else if (HitLocal.Count >= 3 && FitPlane(HitLocal, out var fa, out var fc, out var fd))
            {
                a = fa;
                c = fc;
                d = fd;
            }
            else
            {
                d = 0f;
                foreach (var h in HitLocal) d += h.y;
                d /= HitLocal.Count;
            }

            // Limit the tilt.
            var normal = new Vector3(-a, 1f, -c).normalized;
            float tilt = Vector3.Angle(Vector3.up, normal);
            if (tilt > maxTilt && tilt > 0.01f)
            {
                normal = Vector3.Slerp(Vector3.up, normal, maxTilt / tilt).normalized;
                a = -normal.x / normal.y;
                c = -normal.z / normal.y;
            }

            // Rest on the highest ground point, then allow it to sink in a little to hide gaps.
            float lift = float.MinValue;
            foreach (var h in HitLocal)
                lift = Mathf.Max(lift, h.y - (a * h.x + c * h.z + d));
            d += lift - SnakeGravityHammerPlugin.SinkDepth.Value;

            var tiltRot = Quaternion.FromToRotation(Vector3.up, normal);
            rot = yaw * tiltRot;

            // Keep the centre of the base where it was across the ground, on the fitted plane.
            var baseCentre = new Vector3(cx, a * cx + c * cz + d, cz);
            var pivotLocal = baseCentre - tiltRot * new Vector3(cx, b.min.y, cz);
            pos = _basePos + yaw * pivotLocal;

            if (SnakeGravityHammerPlugin.LogFit.Value && Input.GetMouseButtonDown(0))
                SnakeGravityHammerPlugin.Log($"Gravity fit {ghost.name}: tilt {Vector3.Angle(Vector3.up, normal):F1} deg, " +
                                            $"{HitLocal.Count}/{SampleLocal.Count} ground points, base at {pos}");
            return true;
        }

        private static int _rayMask;
        private static bool _rayMaskOnPieces;

        private static int RayMask()
        {
            bool onPieces = SnakeGravityHammerPlugin.RestOnPieces.Value;
            if (_rayMask == 0 || _rayMaskOnPieces != onPieces)
            {
                _rayMaskOnPieces = onPieces;
                var layers = new List<string> { "Default", "static_solid", "Default_small", "terrain" };
                if (onPieces)
                {
                    layers.Add("piece");
                    layers.Add("piece_nonsolid");
                }
                _rayMask = LayerMask.GetMask(layers.ToArray());
            }
            return _rayMask;
        }

        private static readonly RaycastHit[] Hits = new RaycastHit[32];

        private static bool CastDown(Vector3 from, float distance, int mask, Transform ghost, out Vector3 point)
        {
            point = default;
            int count = Physics.RaycastNonAlloc(from, Vector3.down, Hits, distance, mask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var hit = Hits[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(ghost))
                    continue;
                if (hit.collider.attachedRigidbody != null && !hit.collider.attachedRigidbody.isKinematic)
                    continue; // loose items, creatures, carts
                if (hit.distance < best)
                {
                    best = hit.distance;
                    point = hit.point;
                    found = true;
                }
            }
            return found;
        }

        private static bool RestingPlane(List<Vector3> pts, float cx, float cz, float maxSlope,
            out float a, out float c, out float d)
        {
            a = c = d = 0f;
            const float eps = 0.002f;
            float best = float.MaxValue;
            bool found = false;
            int m = pts.Count;
            for (int i = 0; i < m - 2; i++)
            for (int j = i + 1; j < m - 1; j++)
            for (int k = j + 1; k < m; k++)
            {
                var p = pts[i];
                var u = pts[j] - p;
                var v = pts[k] - p;
                // Plane normal (x, z components over y); skip near-vertical / collinear triples.
                float det = u.x * v.z - u.z * v.x;
                if (Mathf.Abs(det) < 0.02f)
                    continue;
                float pa = (u.y * v.z - u.z * v.y) / det;
                float pc = (u.x * v.y - u.y * v.x) / det;
                if (pa * pa + pc * pc > maxSlope * maxSlope)
                    continue;
                float pd = p.y - pa * p.x - pc * p.z;
                float h = pa * cx + pc * cz + pd;
                if (h >= best)
                    continue;
                bool ok = true;
                for (int q = 0; q < m; q++)
                {
                    var r = pts[q];
                    if (r.y > pa * r.x + pc * r.z + pd + eps)
                    {
                        ok = false;
                        break;
                    }
                }
                if (!ok)
                    continue;
                best = h;
                a = pa;
                c = pc;
                d = pd;
                found = true;
            }
            return found;
        }

        private static bool FitPlane(List<Vector3> pts, out float a, out float c, out float d)
        {
            // Normal equations for y = a*x + c*z + d.
            double sxx = 0, sxz = 0, szz = 0, sx = 0, sz = 0, sxy = 0, szy = 0, sy = 0;
            int m = pts.Count;
            foreach (var p in pts)
            {
                sxx += p.x * p.x; sxz += p.x * p.z; szz += p.z * p.z;
                sx += p.x; sz += p.z;
                sxy += p.x * p.y; szy += p.z * p.y; sy += p.y;
            }
            // | sxx sxz sx | |a|   |sxy|
            // | sxz szz sz | |c| = |szy|
            // | sx  sz  m  | |d|   |sy |
            double det = Det(sxx, sxz, sx, sxz, szz, sz, sx, sz, m);
            a = c = d = 0f;
            if (Math.Abs(det) < 1e-6)
                return false; // points in a line (e.g. a thin pole): no tilt
            a = (float)(Det(sxy, sxz, sx, szy, szz, sz, sy, sz, m) / det);
            c = (float)(Det(sxx, sxy, sx, sxz, szy, sz, sx, sy, m) / det);
            d = (float)(Det(sxx, sxz, sxy, sxz, szz, szy, sx, sz, sy) / det);
            return true;
        }

        private static double Det(double a, double b, double c, double d, double e, double f, double g, double h, double i)
            => a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);

        /// <summary>
        /// The ghost's shape in its own space: solid colliders if it has any, else its meshes.
        /// </summary>
        private static bool LocalBounds(GameObject ghost, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            var toLocal = ghost.transform.worldToLocalMatrix;

            foreach (var col in ghost.GetComponentsInChildren<Collider>(true))
            {
                if (col.isTrigger)
                    continue;
                Bounds lb;
                switch (col)
                {
                    case BoxCollider box: lb = new Bounds(box.center, box.size); break;
                    case SphereCollider s: lb = new Bounds(s.center, Vector3.one * s.radius * 2f); break;
                    case CapsuleCollider cap:
                        var cs = Vector3.one * cap.radius * 2f;
                        cs[cap.direction] = Mathf.Max(cap.height, cap.radius * 2f);
                        lb = new Bounds(cap.center, cs);
                        break;
                    case MeshCollider mc when mc.sharedMesh != null: lb = mc.sharedMesh.bounds; break;
                    default: continue;
                }
                Encapsulate(ref bounds, ref any, lb, toLocal * col.transform.localToWorldMatrix);
            }

            if (!any)
            {
                foreach (var mf in ghost.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null)
                        continue;
                    var r = mf.GetComponent<Renderer>();
                    if (r == null)
                        continue;
                    Encapsulate(ref bounds, ref any, mf.sharedMesh.bounds, toLocal * mf.transform.localToWorldMatrix);
                }
            }
            return any && bounds.size.x > 0.01f && bounds.size.z > 0.01f;
        }

        private static void Encapsulate(ref Bounds bounds, ref bool any, Bounds lb, Matrix4x4 m)
        {
            var mn = lb.min;
            var mx = lb.max;
            for (int k = 0; k < 8; k++)
            {
                var corner = new Vector3((k & 1) == 0 ? mn.x : mx.x, (k & 2) == 0 ? mn.y : mx.y, (k & 4) == 0 ? mn.z : mx.z);
                var p = m.MultiplyPoint3x4(corner);
                if (!any)
                {
                    bounds = new Bounds(p, Vector3.zero);
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(p);
                }
            }
        }
    }
}
