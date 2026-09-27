using System.Collections.Generic;
using RallyX.Tracks;
using UnityEngine;

namespace RallyX.EditorTools
{
    /// <summary>A run of consecutive samples with the same surface and bridge state.</summary>
    public readonly struct RoadSection
    {
        public readonly TrackSample[] Samples;
        public readonly SurfaceType Surface;
        public readonly int BridgeIndex;

        public RoadSection(TrackSample[] samples, SurfaceType surface, int bridgeIndex)
        {
            Samples = samples;
            Surface = surface;
            BridgeIndex = bridgeIndex;
        }

        public bool IsBridge => BridgeIndex >= 0;
    }

    /// <summary>Builds road and bridge-deck meshes along centreline samples.</summary>
    public static class RoadMeshBuilder
    {
        private const float UvMetresPerTile = 8f;
        private const float DeckThickness = 0.6f;

        /// <summary>
        /// Splits a road into runs of the same surface and bridge state. Each run also includes the first sample
        /// of the next run so neighbouring meshes join without gaps.
        /// </summary>
        public static List<RoadSection> Split(RoadPath path)
        {
            var sections = new List<RoadSection>();
            int count = path.Count;
            int start = 0;
            if (path.Closed)
            {
                // Begin at a boundary so no section straddles the start of the sample array.
                for (int i = 0; i < count; i++)
                {
                    if (!SameSection(path.Get(i), path.Get(i - 1)))
                    {
                        start = i;
                        break;
                    }
                }
            }

            int end = path.Closed ? start + count : count - 1;
            int sectionStart = start;
            for (int i = start + 1; i <= end; i++)
            {
                bool boundary = i == end || !SameSection(path.Get(i), path.Get(sectionStart));
                if (!boundary)
                    continue;

                var samples = new TrackSample[i - sectionStart + 1];
                for (int k = 0; k < samples.Length; k++)
                    samples[k] = path.Get(sectionStart + k);
                var first = path.Get(sectionStart);
                sections.Add(new RoadSection(samples, first.Surface, first.BridgeIndex));
                sectionStart = i;
            }

            return sections;
        }

        /// <summary>
        /// Road surface with a sloped bevel either side, so wheels roll on and off it smoothly. The surface is submesh 0
        /// (texture across the road's width once); the bevels are submesh 1, the verge, textured in metres / 8 like the
        /// road's length so their texture has the same scale whatever the road's width.
        /// </summary>
        public static Mesh BuildRoad(TrackSample[] samples, float bevelWidth, float bevelDrop, string name)
        {
            int count = samples.Length;
            var outerLeft = new Vector3[count];
            var left = new Vector3[count];
            var right = new Vector3[count];
            var outerRight = new Vector3[count];
            for (int k = 0; k < count; k++)
            {
                var s = samples[k];
                left[k] = s.Position - s.Right * s.HalfWidth;
                right[k] = s.Position + s.Right * s.HalfWidth;
                outerLeft[k] = left[k] - s.Right * bevelWidth + Vector3.down * bevelDrop;
                outerRight[k] = right[k] + s.Right * bevelWidth + Vector3.down * bevelDrop;
            }

            float bevelU = bevelWidth / UvMetresPerTile;
            var builder = new StripMeshBuilder(AlongUv(samples));
            builder.AddStrip(outerLeft, left, -bevelU, 0f, submesh: 1);
            builder.AddStrip(left, right, 0f, 1f);
            builder.AddStrip(right, outerRight, 0f, bevelU, submesh: 1);
            return builder.Build(name);
        }

        /// <summary>Solid bridge deck: top surface, sides and underside.</summary>
        public static Mesh BuildDeck(TrackSample[] samples, string name)
        {
            int count = samples.Length;
            var topLeft = new Vector3[count];
            var topRight = new Vector3[count];
            var bottomLeft = new Vector3[count];
            var bottomRight = new Vector3[count];
            for (int k = 0; k < count; k++)
            {
                var s = samples[k];
                topLeft[k] = s.Position - s.Right * s.HalfWidth;
                topRight[k] = s.Position + s.Right * s.HalfWidth;
                bottomLeft[k] = topLeft[k] + Vector3.down * DeckThickness;
                bottomRight[k] = topRight[k] + Vector3.down * DeckThickness;
            }

            // Strips go clockwise around the cross-section seen from behind, so every face points outwards.
            var builder = new StripMeshBuilder(AlongUv(samples));
            builder.AddStrip(topLeft, topRight, 0f, 1f);
            builder.AddStrip(topRight, bottomRight, 0f, 0.1f);
            builder.AddStrip(bottomRight, bottomLeft, 0f, 1f);
            builder.AddStrip(bottomLeft, topLeft, 0f, 0.1f);
            return builder.Build(name);
        }

        private static bool SameSection(TrackSample a, TrackSample b) =>
            a.Surface == b.Surface && a.BridgeIndex == b.BridgeIndex;

        private static float[] AlongUv(TrackSample[] samples)
        {
            var v = new float[samples.Length];
            for (int k = 1; k < samples.Length; k++)
                v[k] = v[k - 1] + Vector3.Distance(samples[k - 1].Position, samples[k].Position) / UvMetresPerTile;
            return v;
        }

        /// <summary>
        /// Builds a mesh from strips running along the road. Each strip has its own vertices so edges between
        /// strips stay sharp. A strip's first edge must be on the viewer's left when facing the strip's front
        /// with the road running away from them.
        /// </summary>
        private sealed class StripMeshBuilder
        {
            private readonly float[] alongUv;
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector2> uvs = new List<Vector2>();
            private readonly List<List<int>> submeshes = new List<List<int>> { new List<int>() };

            public StripMeshBuilder(float[] alongUv) => this.alongUv = alongUv;

            public void AddStrip(Vector3[] firstEdge, Vector3[] secondEdge, float firstU, float secondU, int submesh = 0)
            {
                while (submeshes.Count <= submesh)
                    submeshes.Add(new List<int>());
                var triangles = submeshes[submesh];
                int baseIndex = vertices.Count;
                for (int k = 0; k < firstEdge.Length; k++)
                {
                    vertices.Add(firstEdge[k]);
                    vertices.Add(secondEdge[k]);
                    uvs.Add(new Vector2(firstU, alongUv[k]));
                    uvs.Add(new Vector2(secondU, alongUv[k]));
                }

                for (int k = 0; k < firstEdge.Length - 1; k++)
                {
                    int a = baseIndex + k * 2;
                    int b = a + 1;
                    int c = a + 2;
                    int d = a + 3;
                    triangles.Add(a);
                    triangles.Add(c);
                    triangles.Add(b);
                    triangles.Add(b);
                    triangles.Add(c);
                    triangles.Add(d);
                }
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                if (vertices.Count > 65000)
                    mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uvs);
                mesh.subMeshCount = submeshes.Count;
                for (int i = 0; i < submeshes.Count; i++)
                    mesh.SetTriangles(submeshes[i], i);
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
