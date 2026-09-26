using System;
using RallyX.Tracks;
using UnityEngine;

namespace RallyX.EditorTools
{
    /// <summary>
    /// Authoring data for a generated track: the road layout as a loop of control points plus features,
    /// terrain shape and scenery density. Edit it, then rebuild the track scene.
    /// </summary>
    [CreateAssetMenu(menuName = "RALLY X/Track Definition", fileName = "TrackDefinition")]
    public sealed class TrackDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string displayName = "Track";
        [Tooltip("Scene file name, without extension, created in Assets/Scenes/Tracks.")]
        [SerializeField] private string sceneName = "Track";

        [Header("Layout")]
        [Tooltip("Control points in driving order forming a closed loop. Node 0 is the start/finish line.")]
        [SerializeField] private TrackNode[] nodes = Array.Empty<TrackNode>();
        [Tooltip("Distance between baked centreline samples, metres.")]
        [SerializeField, Min(0.5f)] private float sampleSpacing = 2f;
        [SerializeField] private TrackJump[] jumps = Array.Empty<TrackJump>();
        [SerializeField] private TrackBridge[] bridges = Array.Empty<TrackBridge>();
        [SerializeField] private TrackShortcut[] shortcuts = Array.Empty<TrackShortcut>();

        [Header("Road Cross-Section")]
        [Tooltip("Width of the sloped strip either side of the road surface, metres.")]
        [SerializeField, Min(0f)] private float roadBevelWidth = 1f;
        [Tooltip("How far the bevel drops to meet the ground, metres.")]
        [SerializeField, Min(0f)] private float roadBevelDrop = 0.3f;

        [Header("Terrain")]
        [SerializeField, Min(100f)] private float terrainSize = 640f;
        [SerializeField, Min(10f)] private float terrainMaxHeight = 100f;
        [Tooltip("Must be 2^n + 1 (257, 513, 1025).")]
        [SerializeField] private int heightmapResolution = 513;
        [SerializeField] private int alphamapResolution = 512;
        [SerializeField, Min(0f)] private float baseHeight = 6f;
        [SerializeField, Min(0f)] private float noiseAmplitude = 14f;
        [SerializeField, Min(1f)] private float noiseScale = 170f;
        [Tooltip("Height of the valley rim at the map edge, metres. Keeps cars inside the map.")]
        [SerializeField, Min(0f)] private float rimHeight = 50f;
        [Tooltip("Normalised distance from the map centre (0..1) where the rim starts rising.")]
        [SerializeField, Range(0f, 1f)] private float rimStart = 0.8f;
        [Tooltip("Width of the slope blending the road into the surrounding hills, metres.")]
        [SerializeField, Min(1f)] private float embankmentWidth = 22f;
        [SerializeField] private int seed = 1234;

        [Header("Scenery")]
        [Tooltip("Average spacing between trees, metres. Lower = denser forest.")]
        [SerializeField, Min(2f)] private float treeSpacing = 7.5f;
        [Tooltip("Minimum gap between the road edge and a tree trunk, metres.")]
        [SerializeField, Min(1f)] private float treeClearance = 7f;
        [Tooltip("Fraction of the forest left as clearings (0..1).")]
        [SerializeField, Range(0f, 1f)] private float clearingAmount = 0.25f;
        [Tooltip("Chance of a roadside rock every 30 m.")]
        [SerializeField, Range(0f, 1f)] private float roadsideRockChance = 0.35f;
        [SerializeField, Min(0)] private int forestBoulderCount = 45;

        [Header("Race")]
        [Tooltip("Approximate distance between checkpoint gates, metres. The start/finish line is always a gate.")]
        [SerializeField, Min(20f)] private float checkpointSpacing = 120f;
        [Tooltip("How far each gate reaches beyond the road edges, metres, so cars running wide still pass through.")]
        [SerializeField, Min(0f)] private float checkpointMargin = 6f;
        [Tooltip("Height of the gates, metres. Tall enough for cars in the air over jumps.")]
        [SerializeField, Min(2f)] private float checkpointHeight = 12f;
        [Tooltip("Gates keep at least this far from where a shortcut leaves or rejoins the main road, metres.")]
        [SerializeField, Min(0f)] private float checkpointJunctionClearance = 20f;

        public string DisplayName => displayName;
        public string SceneName => sceneName;
        public TrackNode[] Nodes => nodes;
        public float SampleSpacing => sampleSpacing;
        public TrackJump[] Jumps => jumps;
        public TrackBridge[] Bridges => bridges;
        public TrackShortcut[] Shortcuts => shortcuts;
        public float RoadBevelWidth => roadBevelWidth;
        public float RoadBevelDrop => roadBevelDrop;
        public float TerrainSize => terrainSize;
        public float TerrainMaxHeight => terrainMaxHeight;
        public int HeightmapResolution => heightmapResolution;
        public int AlphamapResolution => alphamapResolution;
        public float BaseHeight => baseHeight;
        public float NoiseAmplitude => noiseAmplitude;
        public float NoiseScale => noiseScale;
        public float RimHeight => rimHeight;
        public float RimStart => rimStart;
        public float EmbankmentWidth => embankmentWidth;
        public int Seed => seed;
        public float TreeSpacing => treeSpacing;
        public float TreeClearance => treeClearance;
        public float ClearingAmount => clearingAmount;
        public float RoadsideRockChance => roadsideRockChance;
        public int ForestBoulderCount => forestBoulderCount;
        public float CheckpointSpacing => checkpointSpacing;
        public float CheckpointMargin => checkpointMargin;
        public float CheckpointHeight => checkpointHeight;
        public float CheckpointJunctionClearance => checkpointJunctionClearance;

        /// <summary>Replaces the authored content. Used by the builder that creates the default definition.</summary>
        public void SetLayout(string newDisplayName, string newSceneName, TrackNode[] newNodes, TrackJump[] newJumps,
            TrackBridge[] newBridges, TrackShortcut[] newShortcuts)
        {
            displayName = newDisplayName;
            sceneName = newSceneName;
            nodes = newNodes;
            jumps = newJumps;
            bridges = newBridges;
            shortcuts = newShortcuts;
        }
    }

    /// <summary>A point on the road's centreline.</summary>
    [Serializable]
    public struct TrackNode
    {
        [Tooltip("Map position (x, z), metres.")]
        public Vector2 position;
        [Tooltip("Road surface height, metres above the bottom of the terrain (y = 0).")]
        public float elevation;
        [Min(4f)] public float width;
        [Tooltip("Surface from this node until the next node with a different surface.")]
        public SurfaceType surface;

        public TrackNode(float x, float z, float elevation, float width, SurfaceType surface)
        {
            position = new Vector2(x, z);
            this.elevation = elevation;
            this.width = width;
            this.surface = surface;
        }
    }

    /// <summary>A place on the loop: between node <see cref="node"/> and the next, at fraction <see cref="t"/>.</summary>
    [Serializable]
    public struct TrackAnchor
    {
        public int node;
        [Range(0f, 1f)] public float t;

        public TrackAnchor(int node, float t)
        {
            this.node = node;
            this.t = t;
        }
    }

    /// <summary>A crest jump: the road ramps up to a lip, then falls away into a landing slope.</summary>
    [Serializable]
    public struct TrackJump
    {
        [Tooltip("Where the ramp starts.")]
        public TrackAnchor start;
        [Min(0f)] public float height;
        [Min(1f)] public float rampLength;
        [Min(1f)] public float landingLength;
    }

    /// <summary>A bridge over a creek that crosses the road.</summary>
    [Serializable]
    public struct TrackBridge
    {
        [Tooltip("Middle of the bridge.")]
        public TrackAnchor center;
        [Min(4f)] public float length;
        [Tooltip("Depth of the creek bed below the deck, metres.")]
        [Min(1f)] public float creekDepth;
    }

    /// <summary>An optional alternative route that leaves and rejoins the main loop.</summary>
    [Serializable]
    public struct TrackShortcut
    {
        public TrackAnchor from;
        public TrackAnchor to;
        [Tooltip("Map positions (x, z) between the two ends.")]
        public Vector2[] waypoints;
        [Min(3f)] public float width;
        public SurfaceType surface;
    }
}
