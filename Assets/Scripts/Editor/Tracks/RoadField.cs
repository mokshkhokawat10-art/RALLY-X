using System.Collections.Generic;
using UnityEngine;

namespace RallyX.EditorTools
{
    /// <summary>
    /// For each point of a square grid over the map: distance to the nearest road centreline and that road's
    /// height, half-width and bridge state. Only points within a range of a road are filled in.
    /// </summary>
    internal sealed class RoadField
    {
        private readonly int resolution;
        private readonly float cellSize;
        private readonly float origin;

        private RoadField(int resolution, float cellSize, float origin)
        {
            this.resolution = resolution;
            this.cellSize = cellSize;
            this.origin = origin;
            Distance = new float[resolution, resolution];
            RoadHeight = new float[resolution, resolution];
            HalfWidth = new float[resolution, resolution];
            OnBridge = new bool[resolution, resolution];
            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                    Distance[z, x] = float.PositiveInfinity;
            }
        }

        /// <summary>Indexed [z, x] to match TerrainData height arrays.</summary>
        public float[,] Distance { get; }
        public float[,] RoadHeight { get; }
        public float[,] HalfWidth { get; }
        public bool[,] OnBridge { get; }

        /// <summary>World coordinate of grid index i along either axis.</summary>
        public float WorldCoordinate(int index) => origin + index * cellSize;

        public static RoadField Compute(IEnumerable<RoadPath> roads, int resolution, float cellSize, float origin, float range)
        {
            var field = new RoadField(resolution, cellSize, origin);
            foreach (var road in roads)
            {
                int segments = road.Closed ? road.Count : road.Count - 1;
                for (int k = 0; k < segments; k++)
                    field.Stamp(road.Get(k), road.Get(k + 1), range);
            }

            return field;
        }

        /// <summary>Distance to the nearest road's edge at a world position (nearest grid point), or infinity when far away.</summary>
        public float EdgeDistanceAt(float worldX, float worldZ)
        {
            int z = Index(worldZ);
            int x = Index(worldX);
            return Distance[z, x] - HalfWidth[z, x];
        }

        private int Index(float world) => Mathf.Clamp(Mathf.RoundToInt((world - origin) / cellSize), 0, resolution - 1);

        private void Stamp(TrackSample a, TrackSample b, float range)
        {
            float reach = range + Mathf.Max(a.HalfWidth, b.HalfWidth);
            int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.Position.x, b.Position.x) - reach - origin) / cellSize));
            int x1 = Mathf.Min(resolution - 1, Mathf.CeilToInt((Mathf.Max(a.Position.x, b.Position.x) + reach - origin) / cellSize));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.Position.z, b.Position.z) - reach - origin) / cellSize));
            int z1 = Mathf.Min(resolution - 1, Mathf.CeilToInt((Mathf.Max(a.Position.z, b.Position.z) + reach - origin) / cellSize));

            Vector2 start = TrackLayout.Flat(a.Position);
            Vector2 segment = TrackLayout.Flat(b.Position) - start;
            float lengthSqr = Mathf.Max(segment.sqrMagnitude, 1e-6f);

            // A segment counts as bridge only when both ends are on the deck, so abutments are carved like road.
            bool bridge = a.IsBridge && b.IsBridge;

            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var point = new Vector2(WorldCoordinate(x), WorldCoordinate(z));
                    float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSqr);
                    float distance = Vector2.Distance(point, start + segment * t);
                    if (distance >= Distance[z, x] || distance > reach)
                        continue;

                    Distance[z, x] = distance;
                    RoadHeight[z, x] = Mathf.Lerp(a.Position.y, b.Position.y, t);
                    HalfWidth[z, x] = Mathf.Lerp(a.HalfWidth, b.HalfWidth, t);
                    OnBridge[z, x] = bridge;
                }
            }
        }
    }
}
