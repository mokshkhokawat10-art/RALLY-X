using NUnit.Framework;
using RallyX.AI;
using RallyX.EditorTools;
using UnityEngine;

namespace RallyX.Tests
{
    /// <summary>The difficulty tuning, in new AI settings and in the project's AI settings asset.</summary>
    public sealed class AIDifficultyTests
    {
        [Test]
        public void NewSettings_GetFasterFromEasyToHardWithNormalAtEachDriversOwnPace()
        {
            var settings = ScriptableObject.CreateInstance<AISettings>();
            try
            {
                var normal = settings.GetDifficulty(AIDifficulty.Normal);
                Assert.AreEqual(1f, normal.Cornering);
                Assert.AreEqual(1f, normal.Braking);
                Assert.AreEqual(1f, normal.TopSpeed);
                AssertFasterFromEasyToHard(settings);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void ProjectSettings_GetFasterFromEasyToHard()
        {
            var settings = AIContentBuilder.LoadSettings();
            Assert.IsNotNull(settings, $"Missing {AIContentBuilder.SettingsPath}. Run RALLY X > Build > Create Missing AI Content.");
            AssertFasterFromEasyToHard(settings);
        }

        private static void AssertFasterFromEasyToHard(AISettings settings)
        {
            var easy = settings.GetDifficulty(AIDifficulty.Easy);
            var normal = settings.GetDifficulty(AIDifficulty.Normal);
            var hard = settings.GetDifficulty(AIDifficulty.Hard);
            TestContext.WriteLine($"Cornering {easy.Cornering} / {normal.Cornering} / {hard.Cornering}, braking {easy.Braking} / {normal.Braking} / {hard.Braking}, " +
                                  $"top speed {easy.TopSpeed} / {normal.TopSpeed} / {hard.TopSpeed} (easy / normal / hard)");

            Assert.Less(easy.Cornering, normal.Cornering, "Easy drivers should corner slower than Normal.");
            Assert.Less(normal.Cornering, hard.Cornering, "Hard drivers should corner faster than Normal.");
            Assert.LessOrEqual(easy.Braking, normal.Braking);
            Assert.LessOrEqual(normal.Braking, hard.Braking);
            Assert.LessOrEqual(easy.TopSpeed, normal.TopSpeed);
            Assert.LessOrEqual(normal.TopSpeed, hard.TopSpeed);
        }
    }
}
