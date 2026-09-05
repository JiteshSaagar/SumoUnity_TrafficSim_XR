// ==============================
// LaneMarkingMesh.cs
// ==============================
// Lane paint as one child mesh per lane boundary, replacing the URP
// DecalProjector that used to be spawned every 3 m.
//
// The trap with drawing paint as geometry is that a 0.12 m line viewed across
// the road is thinner than one pixel past ~10 m. The rasteriser then catches
// some pixel centres and misses others, and a solid line reads as crawling
// dashes. A quad exactly as wide as the paint cannot avoid that.
//
// So the mesh built here is NOT the paint. It is an oversized "carrier" ribbon,
// markingWidth + 2 * CarrierMargin across, which the vertex shader widens
// further so it always covers a couple of pixels on screen. The paint itself is
// drawn analytically inside the carrier by the fragment shader, from the true
// world distance to the line centre, so coverage is computed rather than
// sampled and a line a tenth of a pixel wide contributes a tenth of a pixel of
// white instead of flickering.
//
// Dashes work the same way. The ribbon stays continuous and the dash pattern is
// a filtered function of distance along the lane; cutting real gaps into the
// geometry would just reintroduce the same aliasing along the road.
//
// Vertex contract, read by Sumo2Unity/Road Marking:
//   TANGENT   : xyz = lateral unit direction, w = signed distance from centre
//   TEXCOORD0 : y = metres along the lane, driving the dash pattern
//   COLOR     : r = this line is broken
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A run of lane boundary that lies outside every junction polygon, and so is
/// eligible to be painted. Serializable so a rebuilt marking can be re-dashed
/// (solid &lt;-&gt; broken) without re-parsing the SUMO network.
/// </summary>
[Serializable]
public class MarkingSpan
{
    public Vector3[] points;

    public MarkingSpan() { }
    public MarkingSpan(Vector3[] pts) { points = pts; }
}

public static class LaneMarkingMesh
{
    public const float DefaultWidth = 0.12f;
    public const float DefaultDashLength = 1.5f;
    public const float DefaultGapLength = 1.5f;

    /// <summary>
    /// Extra half-width built into the ribbon beyond the paint, giving the
    /// fragment shader room for its antialiasing ramp at close range, where the
    /// vertex stage does no widening. Also the headroom for raising
    /// _MarkingWidth on the material without the paint being clipped.
    /// </summary>
    public const float CarrierMargin = 0.15f;

    /// <summary>Spans shorter than this are not worth painting.</summary>
    private const float MinSpanLength = 0.30f;

    /// <summary>Clamp on miter extension, so hairpin vertices cannot spike.</summary>
    private const float MaxMiterScale = 4f;

    /// <summary>Points closer together than this are treated as coincident.</summary>
    private const float Epsilon = 1e-4f;

    // ─────────────────────────────────────────────────────────────────────
    // Mesh construction
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds one continuous carrier ribbon covering every span.
    /// <paramref name="broken"/> only sets a vertex-colour flag; the dash
    /// pattern itself is drawn by the shader.
    /// </summary>
    public static Mesh Build(List<MarkingSpan> spans, bool broken, float markingWidth, string meshName)
    {
        if (spans == null || spans.Count == 0) return null;

        float carrierHalf = Mathf.Max(markingWidth, 0.01f) * 0.5f + CarrierMargin;

        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tangents = new List<Vector4>();
        var tris = new List<int>();

        foreach (MarkingSpan span in spans)
        {
            if (span?.points == null || span.points.Length < 2) continue;

            float[] cum = Cumulative(span.points);
            if (cum[cum.Length - 1] < MinSpanLength) continue;

            AppendRibbon(span.points, carrierHalf, verts, normals, uvs, tangents, tris);
        }

        if (tris.Count == 0) return null;

        var mesh = new Mesh { name = meshName };
        if (verts.Count > 65534) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTangents(tangents);
        mesh.SetTriangles(tris, 0);
        SetBrokenFlag(mesh, broken);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// Writes the solid/broken flag into vertex colours. Toggling a line is only
    /// this, never a geometry rebuild, so each marking can differ while every
    /// marking still shares one material.
    /// </summary>
    public static void SetBrokenFlag(Mesh mesh, bool broken)
    {
        if (mesh == null) return;
        var colors = new Color[mesh.vertexCount];
        var flag = new Color(broken ? 1f : 0f, 0f, 0f, 1f);
        for (int i = 0; i < colors.Length; i++) colors[i] = flag;
        mesh.colors = colors;
    }

    /// <summary>
    /// Emits a quad strip that follows <paramref name="pts"/>. Interior
    /// vertices are mitered so the ribbon stays a constant width around curves
    /// without gaps or overlap at the joints.
    /// </summary>
    private static void AppendRibbon(
        Vector3[] pts,
        float halfWidth,
        List<Vector3> verts,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<Vector4> tangents,
        List<int> tris)
    {
        int n = pts.Length;
        if (n < 2) return;

        // Per-segment lateral direction, flat in XZ.
        var segNormal = new Vector3[n - 1];
        for (int i = 0; i < n - 1; i++)
        {
            Vector3 dir = pts[i + 1] - pts[i];
            dir.y = 0f;
            if (dir.sqrMagnitude < Epsilon * Epsilon)
            {
                segNormal[i] = i > 0 ? segNormal[i - 1] : Vector3.right;
                continue;
            }
            dir.Normalize();
            segNormal[i] = new Vector3(-dir.z, 0f, dir.x);
        }

        int baseIndex = verts.Count;
        float arc = 0f;

        for (int i = 0; i < n; i++)
        {
            Vector3 lateral;
            if (i == 0)
            {
                lateral = segNormal[0];
            }
            else if (i == n - 1)
            {
                lateral = segNormal[n - 2];
            }
            else
            {
                Vector3 averaged = segNormal[i - 1] + segNormal[i];
                float cos = averaged.sqrMagnitude < Epsilon * Epsilon
                    ? 0f
                    : Vector3.Dot(averaged.normalized, segNormal[i]);

                if (cos <= Epsilon)
                {
                    // Doubling back on itself; a miter here would invert the
                    // ribbon, so square the joint off instead.
                    lateral = segNormal[i];
                }
                else
                {
                    // 1/cos(theta/2) keeps the outer edge continuous through the bend.
                    lateral = averaged.normalized * Mathf.Clamp(1f / cos, 1f, MaxMiterScale);
                }
            }

            if (i > 0) arc += Vector3.Distance(pts[i - 1], pts[i]);

            Vector3 offset = lateral * halfWidth;

            // Miter joints make |offset| exceed halfWidth, so the signed distance
            // handed to the shader comes from the offset actually applied.
            float mag = offset.magnitude;
            Vector3 dir = mag > Epsilon ? offset / mag : Vector3.right;

            verts.Add(pts[i] + offset);
            verts.Add(pts[i] - offset);

            tangents.Add(new Vector4(dir.x, dir.y, dir.z, mag));
            tangents.Add(new Vector4(dir.x, dir.y, dir.z, -mag));

            normals.Add(Vector3.up);
            normals.Add(Vector3.up);

            // y carries metres along the lane, which drives the dash pattern.
            uvs.Add(new Vector2(0f, arc));
            uvs.Add(new Vector2(1f, arc));
        }

        // Winding matches CreateLaneMesh so the faces point +Y.
        for (int i = 0; i < n - 1; i++)
        {
            int l0 = baseIndex + i * 2;
            int r0 = l0 + 1;
            int l1 = l0 + 2;
            int r1 = l0 + 3;

            tris.Add(l0); tris.Add(l1); tris.Add(r0);
            tris.Add(l1); tris.Add(r1); tris.Add(r0);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Junction clipping
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Splits a lane boundary into the runs that lie outside every junction, so
    /// paint stops at the intersection instead of being drawn across it.
    /// Coarse sampling locates each crossing, then a bisection refines it to
    /// roughly a millimetre — the old code left span ends on whatever 0.25 m
    /// sample happened to land first.
    /// </summary>
    public static List<MarkingSpan> ClipToOutside(Vector3[] boundary, Func<Vector3, bool> isInside)
    {
        var result = new List<MarkingSpan>();
        if (boundary == null || boundary.Length < 2) return result;

        float[] cum = Cumulative(boundary);
        float total = cum[cum.Length - 1];
        if (total < MinSpanLength) return result;

        if (isInside == null)
        {
            result.Add(new MarkingSpan(boundary));
            return result;
        }

        const float sampleStep = 0.25f;

        bool prevInside = isInside(PointAt(boundary, cum, 0f));
        float prevDist = 0f;
        float spanStart = prevInside ? -1f : 0f;

        for (float d = sampleStep; d <= total + sampleStep; d += sampleStep)
        {
            float dist = Mathf.Min(d, total);
            bool inside = isInside(PointAt(boundary, cum, dist));

            if (inside != prevInside)
            {
                float crossing = RefineCrossing(boundary, cum, prevDist, dist, prevInside, isInside);

                if (inside)
                {
                    // Leaving open road: close the current span.
                    if (spanStart >= 0f)
                    {
                        AddSpan(result, boundary, cum, spanStart, crossing);
                        spanStart = -1f;
                    }
                }
                else
                {
                    spanStart = crossing;
                }

                prevInside = inside;
            }

            prevDist = dist;
            if (dist >= total) break;
        }

        if (spanStart >= 0f) AddSpan(result, boundary, cum, spanStart, total);
        return result;
    }

    /// <summary>
    /// Bisects [lo, hi] — known to straddle an inside/outside transition — down
    /// to about a millimetre. Returns the arc distance of the boundary.
    /// </summary>
    private static float RefineCrossing(
        Vector3[] pts,
        float[] cum,
        float lo,
        float hi,
        bool loInside,
        Func<Vector3, bool> isInside)
    {
        for (int i = 0; i < 10 && hi - lo > 0.001f; i++)
        {
            float mid = (lo + hi) * 0.5f;
            if (isInside(PointAt(pts, cum, mid)) == loInside) lo = mid;
            else hi = mid;
        }
        return (lo + hi) * 0.5f;
    }

    private static void AddSpan(List<MarkingSpan> result, Vector3[] pts, float[] cum, float start, float end)
    {
        if (end - start < MinSpanLength) return;
        Vector3[] piece = SubPolyline(pts, cum, start, end);
        if (piece != null && piece.Length >= 2) result.Add(new MarkingSpan(piece));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Polyline helpers
    // ─────────────────────────────────────────────────────────────────────

    public static float[] Cumulative(Vector3[] pts)
    {
        var cum = new float[pts.Length];
        cum[0] = 0f;
        for (int i = 1; i < pts.Length; i++)
            cum[i] = cum[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
        return cum;
    }

    /// <summary>Position at an arc distance along the polyline.</summary>
    public static Vector3 PointAt(Vector3[] pts, float[] cum, float dist)
    {
        float total = cum[cum.Length - 1];
        if (dist <= 0f) return pts[0];
        if (dist >= total) return pts[pts.Length - 1];

        int idx = SegmentIndex(cum, dist);
        float segStart = cum[idx];
        float segLen = cum[idx + 1] - segStart;
        float t = segLen <= Epsilon ? 0f : (dist - segStart) / segLen;
        return Vector3.Lerp(pts[idx], pts[idx + 1], t);
    }

    /// <summary>Extracts the portion of a polyline between two arc distances.</summary>
    private static Vector3[] SubPolyline(Vector3[] pts, float[] cum, float startDist, float endDist)
    {
        float total = cum[cum.Length - 1];
        startDist = Mathf.Clamp(startDist, 0f, total);
        endDist = Mathf.Clamp(endDist, 0f, total);
        if (endDist - startDist < Epsilon) return null;

        var piece = new List<Vector3> { PointAt(pts, cum, startDist) };

        for (int i = 0; i < pts.Length; i++)
        {
            if (cum[i] <= startDist + Epsilon) continue;
            if (cum[i] >= endDist - Epsilon) break;
            if (Vector3.Distance(pts[i], piece[piece.Count - 1]) > Epsilon)
                piece.Add(pts[i]);
        }

        Vector3 last = PointAt(pts, cum, endDist);
        if (Vector3.Distance(last, piece[piece.Count - 1]) > Epsilon) piece.Add(last);

        return piece.Count >= 2 ? piece.ToArray() : null;
    }

    private static int SegmentIndex(float[] cum, float dist)
    {
        // Binary search: lane boundaries out of SUMO can carry many vertices.
        int lo = 0, hi = cum.Length - 1;
        while (lo < hi - 1)
        {
            int mid = (lo + hi) / 2;
            if (cum[mid] <= dist) lo = mid;
            else hi = mid;
        }
        return lo;
    }
}
