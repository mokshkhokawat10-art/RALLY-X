using NUnit.Framework;
using RallyX.EditorTools;
using UnityEditor;
using UnityEngine;

namespace RallyX.Tests
{
    /// <summary>
    /// Each track's look: Pine Valley keeps the original forest (the theme's defaults, the shared art folders and Unity's
    /// sky), and Red Canyon has its own art, sky, dry washes and sandy ground off the road.
    /// </summary>
    public sealed class TrackThemeTests
    {
        [Test]
        public void PineValley_KeepsTheOriginalForestLook()
        {
            var theme = LoadDefinition(PineValleyTrack.DefinitionPath).Theme;
            Assert.IsNotNull(theme, "Pine Valley should have the forest theme.");
            var forest = TrackTheme.CreateForest();
            try
            {
                Assert.AreEqual(string.Empty, theme.ArtFolder, "The forest's art is the shared art Pine Valley was built with.");
                Assert.IsFalse(theme.CustomSky);
                Assert.IsTrue(theme.CreekWater);
                Assert.IsNull(theme.OffRoadSurface, "Off the road is grass.");
                Assert.AreEqual(forest.SunRotation, theme.SunRotation);
                Assert.AreEqual(forest.SunIntensity, theme.SunIntensity);
                Assert.AreEqual(forest.SunColor, theme.SunColor);
                Assert.AreEqual(forest.FogColor, theme.FogColor);
                Assert.AreEqual(forest.FogDensity, theme.FogDensity);
                Assert.AreEqual(forest.RockSlope, theme.RockSlope);
                Assert.AreEqual(forest.Grass.light, theme.Grass.light);
                CollectionAssert.AreEqual(new[] { "PineTall", "PineBushy" }, System.Array.ConvertAll(theme.Trees, tree => tree.name));
            }
            finally
            {
                Object.DestroyImmediate(forest);
            }
        }

        [Test]
        public void RedCanyon_IsADryCanyonWithItsOwnArt()
        {
            var definition = LoadDefinition(RedCanyonTrack.DefinitionPath);
            var theme = definition.Theme;
            Assert.IsNotNull(theme);
            Assert.AreEqual("RedCanyon", theme.ArtFolder);
            Assert.IsTrue(theme.CustomSky);
            Assert.IsFalse(theme.CreekWater, "The canyon's washes are dry.");
            Assert.IsNotNull(theme.OffRoadSurface);
            Assert.AreEqual("Sand", theme.OffRoadSurface.DisplayName);
            Assert.Greater(definition.TerraceHeight, 0f, "Canyon walls rise in steps.");
            Assert.GreaterOrEqual(definition.Bridges.Length, 1);
            Assert.GreaterOrEqual(definition.Jumps.Length, 1);
            Assert.GreaterOrEqual(definition.Shortcuts.Length, 1);

            // Its art was generated into its own folders and the scene uses it.
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Environment/RedCanyon/Rock.mat"));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Environment/RedCanyon/Sky.mat"));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Environment/RedCanyon/Juniper.prefab"));
            var terrain = AssetDatabase.LoadAssetAtPath<TerrainData>("Assets/Scenes/Tracks/RedCanyon/Terrain.asset");
            Assert.IsNotNull(terrain, "Build Red Canyon first.");
            StringAssert.StartsWith("Assets/Materials/Environment/RedCanyon/", AssetDatabase.GetAssetPath(terrain.terrainLayers[0]));
            StringAssert.StartsWith("Assets/Prefabs/Environment/RedCanyon/", AssetDatabase.GetAssetPath(terrain.treePrototypes[0].prefab));
        }

        [Test]
        public void EveryTrack_HasATheme()
        {
            foreach (var definition in TrackCatalog.LoadOrCreateAll())
                Assert.IsNotNull(definition.Theme, $"{definition.DisplayName} has no theme.");
        }

        private static TrackDefinition LoadDefinition(string path)
        {
            var definition = AssetDatabase.LoadAssetAtPath<TrackDefinition>(path);
            Assert.IsNotNull(definition, $"Missing {path}. Build the tracks first.");
            return definition;
        }
    }
}
