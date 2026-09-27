using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace RallyX.VFX
{
    /// <summary>
    /// One car's tyre marks: a single world-space mesh holding a fixed number of quads, reused oldest first, so marks
    /// cost one draw call and no allocations after creation. Each wheel lays a continuous strip while it slides.
    /// </summary>
    internal sealed class SkidMarks
    {
        // Marks float this far above the ground; the material's depth offset does the rest.
        private const float Lift = 0.015f;

        private readonly Vector3[] vertices;
        private readonly Color32[] colors;
        private readonly Vector2[] uvs;
        private readonly Mesh mesh;
        private readonly GameObject owner;
        private readonly int capacity;
        private readonly Strip[] strips;
        private int next;
        private bool dirty;

        public SkidMarks(string name, int capacity, int wheelCount, Material material, Scene scene)
        {
            this.capacity = capacity;
            vertices = new Vector3[capacity * 4];
            colors = new Color32[capacity * 4];
            uvs = new Vector2[capacity * 4];
            strips = new Strip[wheelCount];

            var triangles = new int[capacity * 6];
            for (int q = 0; q < capacity; q++)
            {
                int v = q * 4;
                int t = q * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 2;
                triangles[t + 2] = v + 1;
                triangles[t + 3] = v + 1;
                triangles[t + 4] = v + 2;
                triangles[t + 5] = v + 3;
            }

            mesh = new Mesh { name = name, indexFormat = capacity * 4 > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.MarkDynamic();
            mesh.vertices = vertices;
            mesh.colors32 = colors;
            mesh.uv = uvs;
            mesh.triangles = triangles;

            // Marks can be anywhere on the track, so never cull them by bounds (and never recompute the bounds).
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);

            owner = new GameObject(name);
            SceneManager.MoveGameObjectToScene(owner, scene);
            owner.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = owner.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        /// <summary>Quads laid so far, up to the capacity.</summary>
        public int Count { get; private set; }

        /// <summary>
        /// Extends a wheel's mark to a new contact point. The first point of a strip only starts it; after that a quad
        /// is laid each time the wheel has moved <paramref name="segmentLength"/>.
        /// </summary>
        public void Add(int wheel, Vector3 point, Vector3 normal, Vector3 direction, float width, Color32 color, float segmentLength)
        {
            ref Strip strip = ref strips[wheel];
            Vector3 lifted = point + normal * Lift;
            Vector3 across = Vector3.Cross(normal, direction);
            if (across.sqrMagnitude < 1e-6f)
                return;
            across = across.normalized * (width * 0.5f);

            if (!strip.Active)
            {
                strip = new Strip { Active = true, Point = lifted, Left = lifted - across, Right = lifted + across, Color = color, V = 0f };
                return;
            }

            float length = Vector3.Distance(lifted, strip.Point);
            if (length < segmentLength)
                return;

            // A jump in position (a reset, a respawn) starts a new strip rather than drawing a streak across the track.
            if (length > segmentLength * 8f)
            {
                strip.Active = false;
                Add(wheel, point, normal, direction, width, color, segmentLength);
                return;
            }

            int v = next * 4;
            float endV = strip.V + length / width;
            vertices[v] = strip.Left;
            vertices[v + 1] = strip.Right;
            vertices[v + 2] = lifted - across;
            vertices[v + 3] = lifted + across;
            colors[v] = colors[v + 1] = strip.Color;
            colors[v + 2] = colors[v + 3] = color;
            uvs[v] = new Vector2(0f, strip.V);
            uvs[v + 1] = new Vector2(1f, strip.V);
            uvs[v + 2] = new Vector2(0f, endV);
            uvs[v + 3] = new Vector2(1f, endV);

            next = (next + 1) % capacity;
            Count = Mathf.Min(Count + 1, capacity);
            dirty = true;
            strip.Point = lifted;
            strip.Left = lifted - across;
            strip.Right = lifted + across;
            strip.Color = color;
            strip.V = Mathf.Repeat(endV, 64f);
        }

        /// <summary>Ends a wheel's current mark; the next point starts a new one.</summary>
        public void Break(int wheel) => strips[wheel].Active = false;

        /// <summary>Sends this frame's new marks to the GPU.</summary>
        public void Upload()
        {
            if (!dirty)
                return;

            const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
            mesh.SetVertices(vertices, 0, vertices.Length, flags);
            mesh.SetColors(colors, 0, colors.Length, flags);
            mesh.SetUVs(0, uvs, 0, uvs.Length, flags);
            dirty = false;
        }

        public void Destroy()
        {
            if (owner != null)
                Object.Destroy(owner);
            if (mesh != null)
                Object.Destroy(mesh);
        }

        private struct Strip
        {
            public bool Active;
            public Vector3 Point;
            public Vector3 Left;
            public Vector3 Right;
            public Color32 Color;
            public float V;
        }
    }
}
