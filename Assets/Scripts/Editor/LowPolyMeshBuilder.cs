using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RallyX.EditorTools
{
    /// <summary>
    /// One cross-section of a lofted car body or cabin at a point along the car: a rounded box between the bottom and top
    /// heights, with sides that lean in from the bottom width to the top width (tumblehome).
    /// </summary>
    public readonly struct BodyStation
    {
        public readonly float Z;
        public readonly float Bottom;
        public readonly float Top;
        public readonly float HalfWidthBottom;
        public readonly float HalfWidthTop;
        public readonly float Round;

        public BodyStation(float z, float bottom, float top, float halfWidthBottom, float halfWidthTop, float round)
        {
            Z = z;
            Bottom = bottom;
            Top = top;
            HalfWidthBottom = halfWidthBottom;
            HalfWidthTop = halfWidthTop;
            Round = round;
        }

        public BodyStation WithZ(float z) => new BodyStation(z, Bottom, Top, HalfWidthBottom, HalfWidthTop, Round);

        public static BodyStation Lerp(BodyStation a, BodyStation b, float t) => new BodyStation(
            Mathf.Lerp(a.Z, b.Z, t), Mathf.Lerp(a.Bottom, b.Bottom, t), Mathf.Lerp(a.Top, b.Top, t),
            Mathf.Lerp(a.HalfWidthBottom, b.HalfWidthBottom, t), Mathf.Lerp(a.HalfWidthTop, b.HalfWidthTop, t), Mathf.Lerp(a.Round, b.Round, t));

        /// <summary>Half width of the side at a height.</summary>
        public float HalfWidthAt(float y) => Mathf.Lerp(HalfWidthBottom, HalfWidthTop, Mathf.InverseLerp(Bottom, Top, y));
    }

    /// <summary>
    /// Builds flat-shaded low-poly meshes for placeholder cars and track props: every face gets its own vertices and is turned to face
    /// outwards automatically, so shapes can be described by their outline without caring about winding.
    /// </summary>
    public sealed class LowPolyMeshBuilder
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> triangles = new List<int>();

        public bool IsEmpty => triangles.Count == 0;

        /// <summary>A triangle facing away from <paramref name="inside"/>. Degenerate triangles are skipped.</summary>
        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 inside)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 1e-12f)
                return;

            if (Vector3.Dot(normal, (a + b + c) / 3f - inside) < 0f)
                normal = -normal;
            AddTriangle(a, b, c, normal.normalized);
        }

        /// <summary>
        /// A four-sided face (corners in order around it) facing away from <paramref name="inside"/>. Both halves share one
        /// normal, so a slightly twisted panel shades evenly instead of showing a crease along its diagonal.
        /// </summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 inside)
        {
            Vector3 normal = Vector3.Cross(c - a, d - b);
            if (normal.sqrMagnitude < 1e-12f)
            {
                Triangle(a, b, c, inside);
                Triangle(a, c, d, inside);
                return;
            }

            if (Vector3.Dot(normal, (a + b + c + d) * 0.25f - inside) < 0f)
                normal = -normal;
            normal.Normalize();
            AddTriangle(a, b, c, normal);
            AddTriangle(a, c, d, normal);
        }

        /// <summary>Adds a triangle wound to face along <paramref name="normal"/>, shaded flat with that normal.</summary>
        private void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
        {
            Vector3 winding = Vector3.Cross(b - a, c - a);
            if (winding.sqrMagnitude < 1e-12f)
                return;
            if (Vector3.Dot(winding, normal) < 0f)
                (b, c) = (c, b);

            int index = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
            uvs.Add(Planar(a, normal));
            uvs.Add(Planar(b, normal));
            uvs.Add(Planar(c, normal));
            triangles.Add(index);
            triangles.Add(index + 1);
            triangles.Add(index + 2);
        }

        /// <summary>
        /// A body lofted through rounded cross-sections, with the ends capped. Stations must be in order along the car.
        /// </summary>
        public void Loft(IReadOnlyList<BodyStation> stations, bool capEnds = true)
        {
            var rings = new Vector3[stations.Count][];
            for (int i = 0; i < stations.Count; i++)
                rings[i] = Section(stations[i]);

            for (int i = 0; i < rings.Length - 1; i++)
            {
                Vector3 inside = (Center(stations[i]) + Center(stations[i + 1])) * 0.5f;
                var a = rings[i];
                var b = rings[i + 1];
                for (int j = 0; j < a.Length; j++)
                {
                    int k = (j + 1) % a.Length;

                    // A step between two stations at the same point (the end of a wheel arch) faces into the opening: forwards
                    // where the underside rises, backwards where it drops again.
                    Vector3 faceInside = inside;
                    if (Mathf.Approximately(stations[i].Z, stations[i + 1].Z))
                    {
                        float facing = stations[i].Bottom < stations[i + 1].Bottom ? 1f : -1f;
                        faceInside = FaceCentre(a[j], a[k], b[k], b[j]) - Vector3.forward * (facing * 0.05f);
                    }

                    Quad(a[j], a[k], b[k], b[j], faceInside);
                }
            }

            if (!capEnds)
                return;

            Cap(rings[0], Center(stations[0]), -Direction(stations));
            Cap(rings[rings.Length - 1], Center(stations[stations.Count - 1]), Direction(stations));
        }

        /// <summary>
        /// A body loft with rectangular openings over the wheels: across each arch the underside is raised to the arch top.
        /// </summary>
        public void LoftWithArches(IReadOnlyList<BodyStation> stations, IReadOnlyList<float> archCentres, float archHalfLength, float archTop)
        {
            var sorted = new List<BodyStation>(stations);
            sorted.Sort((x, y) => x.Z.CompareTo(y.Z));

            // Every place the outline changes: the stations and both ends of each arch.
            var cuts = new List<float>();
            foreach (var station in sorted)
                cuts.Add(station.Z);
            foreach (float centre in archCentres)
            {
                cuts.Add(centre - archHalfLength);
                cuts.Add(centre + archHalfLength);
            }

            cuts.Sort();
            var result = new List<BodyStation>();
            float previous = float.NegativeInfinity;
            foreach (float z in cuts)
            {
                if (z - previous < 1e-4f)
                    continue;
                previous = z;

                // Raising the underside keeps the side where it was at that height, so the arch edges are flush.
                var station = SampleStation(sorted, z);
                float raisedBottom = Mathf.Max(station.Bottom, archTop);
                var raised = new BodyStation(z, raisedBottom, station.Top, station.HalfWidthAt(raisedBottom), station.HalfWidthTop, station.Round);
                bool entering = false;
                bool leaving = false;
                bool inside = false;
                foreach (float centre in archCentres)
                {
                    entering |= Mathf.Abs(z - (centre - archHalfLength)) < 1e-4f;
                    leaving |= Mathf.Abs(z - (centre + archHalfLength)) < 1e-4f;
                    inside |= z > centre - archHalfLength + 1e-4f && z < centre + archHalfLength - 1e-4f;
                }

                // Going along the car, an arch starts with a step up and ends with a step down.
                if (entering)
                {
                    result.Add(station);
                    result.Add(raised);
                }
                else if (leaving)
                {
                    result.Add(raised);
                    result.Add(station);
                }
                else
                {
                    result.Add(inside ? raised : station);
                }
            }

            Loft(result);
        }

        /// <summary>A box of the given size, centred on a point and turned by a rotation.</summary>
        public void Box(Vector3 centre, Vector3 size, Quaternion rotation)
        {
            Vector3 half = size * 0.5f;
            var corners = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                var local = new Vector3((i & 1) == 0 ? -half.x : half.x, (i & 2) == 0 ? -half.y : half.y, (i & 4) == 0 ? -half.z : half.z);
                corners[i] = centre + rotation * local;
            }

            Quad(corners[0], corners[1], corners[3], corners[2], centre);
            Quad(corners[4], corners[5], corners[7], corners[6], centre);
            Quad(corners[0], corners[1], corners[5], corners[4], centre);
            Quad(corners[2], corners[3], corners[7], corners[6], centre);
            Quad(corners[0], corners[2], corners[6], corners[4], centre);
            Quad(corners[1], corners[3], corners[7], corners[5], centre);
        }

        public void Box(Vector3 centre, Vector3 size) => Box(centre, size, Quaternion.identity);

        /// <summary>A bar of rectangular section between two points; <paramref name="up"/> orients its width.</summary>
        public void Beam(Vector3 from, Vector3 to, float width, float depth, Vector3 up)
        {
            Vector3 along = to - from;
            if (along.sqrMagnitude < 1e-6f)
                return;
            Box((from + to) * 0.5f, new Vector3(width, depth, along.magnitude), Quaternion.LookRotation(along, up));
        }

        /// <summary>
        /// A solid of revolution around an axis through <paramref name="centre"/>: the profile's points are (distance along
        /// the axis, radius), in order; the outline is closed back to its first point.
        /// </summary>
        public void Lathe(Vector3 centre, Vector3 axis, IReadOnlyList<Vector2> profile, int segments)
        {
            axis.Normalize();
            Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.forward).normalized;
            Vector3 other = Vector3.Cross(axis, side);
            Vector2 profileCentre = Vector2.zero;
            foreach (var point in profile)
                profileCentre += point;
            profileCentre /= profile.Count;

            for (int s = 0; s < segments; s++)
            {
                float a0 = s * Mathf.PI * 2f / segments;
                float a1 = (s + 1) * Mathf.PI * 2f / segments;
                Vector3 d0 = side * Mathf.Cos(a0) + other * Mathf.Sin(a0);
                Vector3 d1 = side * Mathf.Cos(a1) + other * Mathf.Sin(a1);
                for (int p = 0; p < profile.Count; p++)
                {
                    int q = (p + 1) % profile.Count;
                    Vector2 pp = profile[p];
                    Vector2 pq = profile[q];
                    Vector3 v0 = centre + axis * pp.x + d0 * pp.y;
                    Vector3 v1 = centre + axis * pq.x + d0 * pq.y;
                    Vector3 v2 = centre + axis * pq.x + d1 * pq.y;
                    Vector3 v3 = centre + axis * pp.x + d1 * pp.y;

                    // Each face points away from the middle of the profile it was swept from.
                    Vector2 edge = pq - pp;
                    var outwards = new Vector2(edge.y, -edge.x);
                    if (Vector2.Dot(outwards, (pp + pq) * 0.5f - profileCentre) < 0f)
                        outwards = -outwards;
                    Vector3 radial = (d0 + d1).normalized;
                    Vector3 mid = (v0 + v1 + v2 + v3) * 0.25f;
                    Quad(v0, v1, v2, v3, mid - (axis * outwards.x + radial * outwards.y).normalized * 0.05f);
                }
            }
        }

        /// <summary>A closed cylinder around an axis through <paramref name="centre"/>.</summary>
        public void Cylinder(Vector3 centre, Vector3 axis, float radius, float length, int segments)
        {
            float half = length * 0.5f;
            Lathe(centre, axis, new[] { new Vector2(-half, 0f), new Vector2(-half, radius), new Vector2(half, radius), new Vector2(half, 0f) }, segments);
        }

        /// <summary>
        /// A wheel-arch flare: part of a ring around an axis along X through <paramref name="centre"/>, from
        /// <paramref name="startDegrees"/> to <paramref name="endDegrees"/> (0 = forwards, 90 = up), between two radii,
        /// reaching from <paramref name="innerX"/> out to <paramref name="outerX"/>.
        /// </summary>
        public void Arch(Vector3 centre, float innerRadius, float outerRadius, float innerX, float outerX, float startDegrees, float endDegrees,
            int segments)
        {
            for (int s = 0; s < segments; s++)
            {
                float a0 = Mathf.Lerp(startDegrees, endDegrees, s / (float)segments) * Mathf.Deg2Rad;
                float a1 = Mathf.Lerp(startDegrees, endDegrees, (s + 1) / (float)segments) * Mathf.Deg2Rad;
                var d0 = new Vector3(0f, Mathf.Sin(a0), Mathf.Cos(a0));
                var d1 = new Vector3(0f, Mathf.Sin(a1), Mathf.Cos(a1));
                Vector3 inner = new Vector3(innerX, centre.y, centre.z);
                Vector3 outer = new Vector3(outerX, centre.y, centre.z);
                Vector3 axisCentre = new Vector3((innerX + outerX) * 0.5f, centre.y, centre.z);

                // Outer curved face, inner curved face (towards the wheel) and the face looking out sideways.
                Quad(outer + d0 * outerRadius, outer + d1 * outerRadius, inner + d1 * outerRadius, inner + d0 * outerRadius, axisCentre);
                Quad(outer + d0 * innerRadius, outer + d1 * innerRadius, inner + d1 * innerRadius, inner + d0 * innerRadius,
                    axisCentre + (d0 + d1) * outerRadius);
                Quad(outer + d0 * innerRadius, outer + d1 * innerRadius, outer + d1 * outerRadius, outer + d0 * outerRadius,
                    new Vector3(innerX, centre.y, centre.z) + (d0 + d1) * ((innerRadius + outerRadius) * 0.25f));
            }
        }

        /// <summary>Adds the geometry, moved by an offset, to a mesh being assembled from several builders (one submesh each).</summary>
        public void AppendTo(List<Vector3> allVertices, List<Vector3> allNormals, List<Vector2> allUvs, List<int> subTriangles, Vector3 offset)
        {
            int start = allVertices.Count;
            foreach (var vertex in vertices)
                allVertices.Add(vertex + offset);
            allNormals.AddRange(normals);
            allUvs.AddRange(uvs);
            foreach (int index in triangles)
                subTriangles.Add(start + index);
        }

        /// <summary>This builder's geometry as a mesh, optionally with texture coordinates worked out from each vertex's position.</summary>
        public Mesh ToMesh(string name, System.Func<Vector3, Vector2> uv = null)
        {
            var mesh = Combine(name, Vector3.zero, this);
            if (uv == null)
                return mesh;

            var positions = mesh.vertices;
            var coordinates = new Vector2[positions.Length];
            for (int i = 0; i < positions.Length; i++)
                coordinates[i] = uv(positions[i]);
            mesh.uv = coordinates;
            return mesh;
        }

        /// <summary>A mesh with one submesh per builder (empty builders give empty submeshes).</summary>
        public static Mesh Combine(string name, Vector3 offset, params LowPolyMeshBuilder[] parts)
        {
            var allVertices = new List<Vector3>();
            var allNormals = new List<Vector3>();
            var allUvs = new List<Vector2>();
            var submeshes = new List<List<int>>();
            foreach (var part in parts)
            {
                var subTriangles = new List<int>();
                part.AppendTo(allVertices, allNormals, allUvs, subTriangles, offset);
                submeshes.Add(subTriangles);
            }

            var mesh = new Mesh { name = name };
            if (allVertices.Count > 65000)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(allVertices);
            mesh.SetNormals(allNormals);
            mesh.SetUVs(0, allUvs);
            mesh.subMeshCount = submeshes.Count;
            for (int i = 0; i < submeshes.Count; i++)
                mesh.SetTriangles(submeshes[i], i);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>The cross-section outline: a box with a chamfered bottom and rounded top corners, going round.</summary>
        public static Vector3[] Section(BodyStation s)
        {
            float height = Mathf.Max(0.01f, s.Top - s.Bottom);
            float top = Mathf.Min(s.Round, height * 0.45f);
            float bottom = Mathf.Min(s.Round * 0.35f, height * 0.25f);
            float corner = top * 0.293f;
            float z = s.Z;
            return new[]
            {
                new Vector3(-s.HalfWidthBottom + bottom, s.Bottom, z),
                new Vector3(s.HalfWidthBottom - bottom, s.Bottom, z),
                new Vector3(s.HalfWidthBottom, s.Bottom + bottom, z),
                new Vector3(s.HalfWidthTop, s.Top - top, z),
                new Vector3(s.HalfWidthTop - corner, s.Top - corner, z),
                new Vector3(s.HalfWidthTop - top, s.Top, z),
                new Vector3(-s.HalfWidthTop + top, s.Top, z),
                new Vector3(-s.HalfWidthTop + corner, s.Top - corner, z),
                new Vector3(-s.HalfWidthTop, s.Top - top, z),
                new Vector3(-s.HalfWidthBottom, s.Bottom + bottom, z),
            };
        }

        /// <summary>The station at a point along the car, between the given stations (sorted along the car).</summary>
        public static BodyStation SampleStation(IReadOnlyList<BodyStation> sorted, float z)
        {
            if (z <= sorted[0].Z)
                return sorted[0].WithZ(z);
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                if (z <= sorted[i + 1].Z)
                    return BodyStation.Lerp(sorted[i], sorted[i + 1], Mathf.InverseLerp(sorted[i].Z, sorted[i + 1].Z, z)).WithZ(z);
            }

            return sorted[sorted.Count - 1].WithZ(z);
        }

        private void Cap(Vector3[] ring, Vector3 centre, Vector3 outwards)
        {
            for (int j = 0; j < ring.Length; j++)
                Triangle(centre, ring[j], ring[(j + 1) % ring.Length], centre - outwards);
        }

        private static Vector3 Center(BodyStation s) => new Vector3(0f, (s.Bottom + s.Top) * 0.5f, s.Z);

        private static Vector3 FaceCentre(Vector3 a, Vector3 b, Vector3 c, Vector3 d) => (a + b + c + d) * 0.25f;

        private static Vector3 Direction(IReadOnlyList<BodyStation> stations) =>
            stations[stations.Count - 1].Z >= stations[0].Z ? Vector3.forward : Vector3.back;

        // Box-projected texture coordinates, a metre per unit, for materials with fine detail such as tread or grain.
        private static Vector2 Planar(Vector3 p, Vector3 normal)
        {
            Vector3 n = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
            if (n.x >= n.y && n.x >= n.z)
                return new Vector2(p.z, p.y);
            return n.y >= n.z ? new Vector2(p.x, p.z) : new Vector2(p.x, p.y);
        }
    }
}
