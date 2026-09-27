using System.Collections.Generic;
using NUnit.Framework;
using RallyX.EditorTools;
using RallyX.Tracks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RallyX.Tests
{
    /// <summary>
    /// Each built track scene has its Phase 10 look: a colour grade (filmic tonemapping), baked reflections, the
    /// track's dust colours, the height-blended terrain material, trackside vegetation, road shoulders drawn as verge,
    /// and guard rails that collide as plain boxes but are drawn as rails on posts.
    /// </summary>
    public sealed class TrackVisualsTests
    {
        public static IEnumerable<string> TrackScenes()
        {
            foreach (var definition in TrackCatalog.LoadOrCreateAll())
                yield return definition.SceneName;
        }

        [Test]
        public void TrackScene_HasItsLookWithoutNewColliders([ValueSource(nameof(TrackScenes))] string sceneName)
        {
            string path = $"{TrackSceneBuilder.TracksFolder}/{sceneName}.unity";
            Assert.IsTrue(System.IO.File.Exists(path), $"Missing {path}. Build the tracks first.");
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var roots = scene.GetRootGameObjects();
                var volume = Find<Volume>(roots);
                Assert.IsNotNull(volume, "The track needs its colour grade.");
                Assert.IsTrue(volume.isGlobal);
                Assert.IsTrue(volume.sharedProfile.TryGet(out Tonemapping tonemapping) && tonemapping.mode.value == TonemappingMode.ACES,
                    "Tracks use filmic tonemapping.");

                var probe = Find<ReflectionProbe>(roots);
                Assert.IsNotNull(probe, "The track needs baked reflections.");
                Assert.IsNotNull(probe.customBakedTexture, "The reflections were not baked.");

                var palette = Find<SurfacePalette>(roots);
                Assert.IsNotNull(palette, "The track needs its dust colours.");

                var terrain = Find<Terrain>(roots);
                Assert.IsNotNull(terrain);
                Assert.AreEqual("Universal Render Pipeline/Terrain/Lit", terrain.materialTemplate.shader.name);
                Assert.IsTrue(terrain.materialTemplate.IsKeywordEnabled("_TERRAIN_BLEND_HEIGHT"), "Ground layers should blend by height.");
                foreach (var layer in terrain.terrainData.terrainLayers)
                {
                    Assert.IsNotNull(layer.normalMapTexture, $"{layer.name} has no normal map.");
                    Assert.IsNotNull(layer.maskMapTexture, $"{layer.name} has no mask map.");
                }

                var data = terrain.terrainData;
                Assert.AreEqual(2, data.detailPrototypes.Length, "Grass and bushes.");
                int tufts = CountDetails(data, 0);
                Assert.Greater(tufts, 1000, "There should be grass along the roads.");

                var road = FindByName(roots, "Road");
                Assert.IsNotNull(road);
                foreach (Transform piece in road.transform)
                {
                    var renderer = piece.GetComponent<MeshRenderer>();
                    bool bridge = piece.name.Contains("Bridge");
                    Assert.AreEqual(bridge ? 1 : 2, renderer.sharedMaterials.Length, $"{piece.name}: road and verge, or one deck.");
                    Assert.AreEqual(piece.GetComponent<MeshFilter>().sharedMesh.subMeshCount, renderer.sharedMaterials.Length);
                }

                var guardRails = FindByName(roots, "GuardRails");
                if (guardRails != null && guardRails.transform.childCount > 0)
                {
                    foreach (Transform rail in guardRails.transform)
                    {
                        if (rail.name != "Rail")
                            continue;
                        Assert.IsNotNull(rail.GetComponent<BoxCollider>(), "Guard rails still collide as boxes.");
                        Assert.IsNull(rail.GetComponent<MeshRenderer>(), "The collision box is not drawn.");
                    }

                    Assert.IsNotNull(guardRails.transform.Find("GuardRailVisuals"), "The rails are drawn on posts.");
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static int CountDetails(TerrainData data, int layer)
        {
            int total = 0;
            foreach (int count in data.GetDetailLayer(0, 0, data.detailWidth, data.detailHeight, layer))
                total += count;
            return total;
        }

        private static T Find<T>(GameObject[] roots) where T : Component
        {
            foreach (var root in roots)
            {
                var found = root.GetComponentInChildren<T>(true);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static GameObject FindByName(GameObject[] roots, string name)
        {
            foreach (var root in roots)
            {
                if (root.name == name)
                    return root;
                var child = FindChild(root.transform, name);
                if (child != null)
                    return child.gameObject;
            }

            return null;
        }

        private static Transform FindChild(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name)
                    return child;
                var deeper = FindChild(child, name);
                if (deeper != null)
                    return deeper;
            }

            return null;
        }
    }
}
