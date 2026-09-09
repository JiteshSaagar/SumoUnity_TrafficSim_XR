using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One walkable surface footprint in world XZ, generated from the SUMO network.
/// </summary>
[System.Serializable]
public class WalkablePolygon
{
    public Vector2[] points;

    /// <summary>Sidewalk, walking area or crossing - what the surface is.</summary>
    public WalkableKind kind;

    public WalkablePolygon() { }

    public WalkablePolygon(Vector2[] pts, WalkableKind k)
    {
        points = pts;
        kind = k;
    }
}

public enum WalkableKind
{
    Sidewalk,
    WalkingArea,
    Crossing,
}

/// <summary>
/// The registry of every surface a pedestrian may stand on, baked by
/// <c>RoadNetworkBuilder</c> and read at runtime.
///
/// This exists for the Social Force Model (Phase 3). Garrido et al. found their
/// pedestrians drifting off the pavement into the carriageway while avoiding
/// each other, and fixed it by adding repulsive "walls" along the edges of every
/// walkable surface. That force needs boundary polygons in world space, and the
/// only place that knows them is the road builder - so they are captured here at
/// build time rather than re-derived from mesh colliders at runtime.
///
/// It lives on the generated RoadNetworkRoot, so a rebuild replaces it wholesale
/// and it can never go stale relative to the geometry.
/// </summary>
[DisallowMultipleComponent]
public class WalkableAreas : MonoBehaviour
{
    [Tooltip("Every sidewalk, walking area and crossing footprint, in world XZ.")]
    public List<WalkablePolygon> polygons = new List<WalkablePolygon>();

    [Tooltip("Height of the walkable surface above the carriageway, in metres. " +
             "Pedestrian controllers use this to sit agents on the kerb.")]
    public float surfaceHeight;

    /// <summary>
    /// True if the point is on any walkable surface. Phase 3 uses this both for
    /// the hard clamp that Garrido et al. needed as a last resort, and for
    /// deciding whether an agent has stepped into the road.
    /// </summary>
    public bool Contains(Vector2 worldXZ)
    {
        for (int i = 0; i < polygons.Count; i++)
        {
            if (PointInPolygon(worldXZ, polygons[i].points)) return true;
        }
        return false;
    }

    /// <summary>
    /// Nearest point on the nearest walkable boundary, and its distance. This is
    /// the input to the social-force wall term: an agent close to an edge gets
    /// pushed away from it.
    /// </summary>
    public bool TryGetNearestBoundary(Vector2 worldXZ, float searchRadius,
                                      out Vector2 nearest, out float distance)
    {
        nearest = worldXZ;
        distance = float.MaxValue;
        float r2 = searchRadius * searchRadius;

        for (int p = 0; p < polygons.Count; p++)
        {
            Vector2[] pts = polygons[p].points;
            if (pts == null || pts.Length < 2) continue;

            for (int i = 0; i < pts.Length; i++)
            {
                Vector2 a = pts[i];
                Vector2 b = pts[(i + 1) % pts.Length];

                // Cheap reject before the projection maths.
                if ((a - worldXZ).sqrMagnitude > r2 && (b - worldXZ).sqrMagnitude > r2) continue;

                Vector2 c = ClosestPointOnSegment(a, b, worldXZ);
                float d2 = (c - worldXZ).sqrMagnitude;
                if (d2 < distance)
                {
                    distance = d2;
                    nearest = c;
                }
            }
        }

        if (distance == float.MaxValue) return false;
        distance = Mathf.Sqrt(distance);
        return distance <= searchRadius;
    }

    private static Vector2 ClosestPointOnSegment(Vector2 a, Vector2 b, Vector2 p)
    {
        Vector2 ab = b - a;
        float len2 = ab.sqrMagnitude;
        if (len2 < 1e-6f) return a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
        return a + ab * t;
    }

    private static bool PointInPolygon(Vector2 p, Vector2[] poly)
    {
        if (poly == null || poly.Length < 3) return false;
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if (((poly[i].y > p.y) != (poly[j].y > p.y)) &&
                (p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x))
            {
                inside = !inside;
            }
        }
        return inside;
    }

    /// <summary>Counts by kind, for the build log and the inspector.</summary>
    public string Summary()
    {
        int s = 0, w = 0, c = 0;
        foreach (var p in polygons)
        {
            if (p.kind == WalkableKind.Sidewalk) s++;
            else if (p.kind == WalkableKind.WalkingArea) w++;
            else c++;
        }
        return $"{s} sidewalk, {w} walking area, {c} crossing ({polygons.Count} total)";
    }
}
