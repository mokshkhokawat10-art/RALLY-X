using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RallyX.Core;
using RallyX.Player;
using RallyX.Racing;
using RallyX.Tracks;
using RallyX.Vehicle;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RallyX.Tests
{
    /// <summary>
    /// Loads the generated Pine Valley scene and checks that it is drivable: road under the whole lap, nothing in
    /// the space above it, trees standing back, a usable starting grid, and a full lap by a scripted driver in the
    /// real car. The race is switched off, so these check free driving. Rebuild the track
    /// (RALLY X > Build > Rebuild Pine Valley Track) before running these.
    /// </summary>
    public sealed class PineValleyTrackTests : VehicleTestBase
    {
        private const string SceneName = "PineValley";

        // Road probes cover this fraction of the half width, keeping clear of the edge where the bevel starts.
        private const float ProbeWidthFraction = 0.9f;
        private const float SurfaceHeightTolerance = 0.15f;

        // The space a car needs above the road surface.
        private const float CorridorBottom = 0.4f;
        private const float CorridorTop = 2.4f;

        private const float MinTreeGapFromRoadEdge = 5f;

        private const float LapTimeout = 300f;
        private const float StuckWindow = 10f;

        private Scene scene;
        private TrackPath path;
        private int probeMask;

        [UnitySetUp]
        public IEnumerator LoadTrack()
        {
            Assert.IsTrue(Application.CanStreamedLevelBeLoaded(SceneName),
                $"{SceneName} is not in the build scene list. Run RALLY X > Build > Rebuild Pine Valley Track.");
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Additive);
            scene = SceneManager.GetSceneByName(SceneName);

            path = Object.FindFirstObjectByType<TrackPath>();
            Assert.IsNotNull(path, "The track scene has no TrackPath.");
            Assert.Greater(path.Count, 100, "The TrackPath has no baked centreline.");

            int vehicleLayer = LayerMask.NameToLayer(GameLayers.Vehicle);
            Assert.GreaterOrEqual(vehicleLayer, 0, "The Vehicle layer is missing. Run RALLY X > Setup > Run Project Setup.");
            probeMask = ~(1 << vehicleLayer);

            // Free driving on an empty track: no opponents, no countdown holding the car, no race rules.
            var race = Object.FindFirstObjectByType<RaceManager>();
            if (race != null)
            {
                RemoveOpponents(race);
                race.enabled = false;
            }

            // Let the player's car settle on its grid slot.
            var playerInput = Object.FindFirstObjectByType<PlayerVehicleInput>();
            Assert.IsNotNull(playerInput, "The track scene has no player car.");
            Car = playerInput.GetComponent<VehicleController>();
            yield return Drive(default, SettleSeconds);
        }

        [UnityTearDown]
        public IEnumerator UnloadTrack()
        {
            Car = null;
            if (scene.IsValid() && scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(scene);
        }

        [UnityTest]
        public IEnumerator Road_SurfaceRunsUnderTheWholeLap()
        {
            var problems = new List<string>();
            var metresBySurface = new SortedDictionary<string, float>();
            float spacing = path.Length / path.Count;

            for (int i = 0; i < path.Count; i++)
            {
                Vector3 point = path.GetPoint(i);
                Vector3 right = RightAt(path.GetDistance(i));
                float halfWidth = path.GetWidth(i) * 0.5f;
                foreach (float side in new[] { -ProbeWidthFraction, 0f, ProbeWidthFraction })
                {
                    Vector3 probe = point + right * (side * halfWidth);
                    string where = $"{path.GetDistance(i):0} m {(side < 0f ? "left" : side > 0f ? "right" : "centre")}";
                    if (!Physics.Raycast(probe + Vector3.up * 4f, Vector3.down, out var hit, 8f, probeMask, QueryTriggerInteraction.Ignore))
                    {
                        problems.Add($"{where}: nothing under the road");
                        continue;
                    }

                    var marker = hit.collider.GetComponent<SurfaceMarker>();
                    if (hit.collider is TerrainCollider || marker == null || marker.Surface == null)
                        problems.Add($"{where}: {Describe(hit.collider)} instead of road");
                    else if (Mathf.Abs(hit.point.y - probe.y) > SurfaceHeightTolerance)
                        problems.Add($"{where}: surface {hit.point.y - probe.y:+0.00;-0.00} m from the centreline height");
                    else if (side == 0f)
                    {
                        metresBySurface.TryGetValue(marker.Surface.DisplayName, out float metres);
                        metresBySurface[marker.Surface.DisplayName] = metres + spacing;
                    }
                }
            }

            var summary = new List<string>();
            foreach (var entry in metresBySurface)
                summary.Add($"{entry.Key} {entry.Value:0} m");
            TestContext.WriteLine($"Lap {path.Length:0} m, {path.Count} samples: {string.Join(", ", summary)}");
            AssertNoProblems(problems, "road surface");
            yield break;
        }

        [UnityTest]
        public IEnumerator Road_SpaceAboveTheSurfaceIsClear()
        {
            var problems = new List<string>();
            var hits = new Collider[8];
            float spacing = path.Length / path.Count;
            float height = CorridorTop - CorridorBottom;

            for (int i = 0; i < path.Count; i++)
            {
                // A box one sample long, pitched with the road, over most of its width.
                var rotation = Quaternion.LookRotation(path.GetTangent(i), Vector3.up);
                Vector3 center = path.GetPoint(i) + rotation * Vector3.up * (CorridorBottom + height * 0.5f);
                var halfExtents = new Vector3(path.GetWidth(i) * 0.5f * ProbeWidthFraction, height * 0.5f, spacing * 0.5f);
                int count = Physics.OverlapBoxNonAlloc(center, halfExtents, hits, rotation, probeMask, QueryTriggerInteraction.Ignore);
                for (int h = 0; h < count; h++)
                    problems.Add($"{path.GetDistance(i):0} m: {Describe(hits[h])}");
            }

            TestContext.WriteLine($"Checked {path.Count} boxes from {CorridorBottom} m to {CorridorTop} m above the road.");
            AssertNoProblems(problems, "obstruction");
            yield break;
        }

        [UnityTest]
        public IEnumerator Forest_TreesStandBackFromTheRoad()
        {
            var terrain = Terrain.activeTerrain;
            Assert.IsNotNull(terrain, "The track scene has no terrain.");
            var data = terrain.terrainData;
            Assert.Greater(data.treeInstanceCount, 0, "The terrain has no trees.");

            float closest = float.MaxValue;
            Vector3 closestTree = default;
            int tooClose = 0;
            foreach (var tree in data.treeInstances)
            {
                Vector3 position = terrain.transform.position + Vector3.Scale(tree.position, data.size);
                float gap = GapFromRoadEdge(position);
                if (gap < MinTreeGapFromRoadEdge)
                    tooClose++;
                if (gap < closest)
                {
                    closest = gap;
                    closestTree = position;
                }
            }

            TestContext.WriteLine($"{data.treeInstanceCount} trees; closest trunk {closest:0.0} m from the road edge " +
                                  $"at ({closestTree.x:0}, {closestTree.z:0}), {path.FindClosestDistance(closestTree):0} m along the lap.");
            Assert.AreEqual(0, tooClose, $"{tooClose} trees stand within {MinTreeGapFromRoadEdge} m of the road edge.");
            yield break;
        }

        [UnityTest]
        public IEnumerator StartGrid_SlotsSitOnTheRoadBehindTheLineFacingForward()
        {
            var grid = Object.FindFirstObjectByType<TrackSpawnGrid>();
            Assert.IsNotNull(grid, "The track scene has no TrackSpawnGrid.");
            Assert.GreaterOrEqual(grid.SlotCount, 4, "The grid needs a slot for the player and three AI cars.");

            for (int s = 0; s < grid.SlotCount; s++)
            {
                var slot = grid.GetSlot(s);
                string name = $"Slot {s + 1}";
                float distance = path.FindClosestDistance(slot.position);
                float behindLine = path.Length - distance;
                float facing = Vector3.Angle(Flat(slot.forward), Flat(TangentAt(distance)));
                bool overRoad = Physics.Raycast(slot.position + Vector3.up * 2f, Vector3.down, out var hit, 5f, probeMask, QueryTriggerInteraction.Ignore)
                                && !(hit.collider is TerrainCollider) && hit.collider.GetComponent<SurfaceMarker>() != null;
                TestContext.WriteLine($"{name}: {behindLine:0.0} m behind the line, {facing:0.0} deg off the road direction, over {(overRoad ? hit.collider.name : "no road")}");

                Assert.That(behindLine, Is.InRange(5f, 40f), $"{name} should be just behind the start line.");
                Assert.Less(facing, 10f, $"{name} should face along the road.");
                Assert.IsTrue(overRoad, $"{name} is not over the road.");
                for (int other = 0; other < s; other++)
                    Assert.Greater(Vector3.Distance(slot.position, grid.GetSlot(other).position), 4f, $"{name} overlaps slot {other + 1}.");
            }

            Assert.IsNotNull(Car, "The track scene has no player car.");
            Assert.Less(Vector3.Distance(Flat(Car.Body.position), Flat(grid.GetSlot(0).position)), 1f, "The player car should start on pole.");
            yield break;
        }

        [UnityTest]
        public IEnumerator ScriptedDriver_CompletesALapOnTheRoad()
        {
            Assert.IsNotNull(Car, "The track scene has no player car.");
            var playerInput = Car.GetComponent<PlayerVehicleInput>();
            if (playerInput != null)
                playerInput.enabled = false;

            var driver = new ScriptedDriver(Car, path);
            float distance = driver.Distance;
            float progress = 0f;
            float elapsed = 0f;
            float fastest = 0f;
            float lowestUpright = 1f;
            float worstOffRoad = float.MinValue;
            float worstOffRoadAt = 0f;
            float lowestBelowRoad = float.MaxValue;
            float longestAir = 0f;
            float longestAirAt = 0f;
            float biggestImpact = 0f;
            float biggestImpactAt = 0f;
            int impacts = 0;
            float windowStart = 0f;
            float windowProgress = 0f;

            void OnImpact(float impulse, Vector3 point)
            {
                impacts++;
                if (impulse > biggestImpact)
                {
                    biggestImpact = impulse;
                    biggestImpactAt = path.FindClosestDistance(point, distance);
                }
            }

            void OnLanded(float airTime, float landingSpeed)
            {
                if (airTime > longestAir)
                {
                    longestAir = airTime;
                    longestAirAt = distance;
                }
            }

            Car.Impact += OnImpact;
            Car.Landed += OnLanded;
            try
            {
                while (progress < path.Length)
                {
                    driver.Steer();
                    yield return NextPhysicsStep;
                    elapsed += Time.fixedDeltaTime;

                    Vector3 position = Car.Body.position;
                    float newDistance = path.FindClosestDistance(position, distance, 30f);
                    float step = Mathf.Repeat(newDistance - distance + path.Length * 0.5f, path.Length) - path.Length * 0.5f;
                    progress += step;
                    windowProgress += step;
                    distance = newDistance;

                    fastest = Mathf.Max(fastest, Car.SpeedKmh);
                    lowestUpright = Mathf.Min(lowestUpright, Uprightness());
                    Vector3 centre = path.EvaluatePosition(distance);
                    float offRoad = Flat(position - centre).magnitude - path.EvaluateWidth(distance) * 0.5f;
                    if (offRoad > worstOffRoad)
                    {
                        worstOffRoad = offRoad;
                        worstOffRoadAt = distance;
                    }

                    lowestBelowRoad = Mathf.Min(lowestBelowRoad, position.y - centre.y);

                    Assert.Greater(Uprightness(), 0.3f, $"The car rolled over at {distance:0} m.");
                    Assert.Greater(position.y - centre.y, -2f, $"The car fell through the road at {distance:0} m.");
                    Assert.Less(elapsed, LapTimeout, $"No full lap within {LapTimeout} s; stopped at {progress:0} m of {path.Length:0} m.");
                    if (elapsed - windowStart >= StuckWindow)
                    {
                        Assert.Greater(windowProgress, 20f, $"The car got stuck at {distance:0} m.");
                        windowStart = elapsed;
                        windowProgress = 0f;
                    }
                }
            }
            finally
            {
                Car.Impact -= OnImpact;
                Car.Landed -= OnLanded;
            }

            TestContext.WriteLine($"Lap {elapsed:0.0} s, average {path.Length / elapsed * 3.6f:0} km/h, fastest {fastest:0} km/h");
            TestContext.WriteLine($"Furthest from the road edge: {worstOffRoad:+0.0;-0.0} m at {worstOffRoadAt:0} m (negative = on the road)");
            TestContext.WriteLine($"Longest jump: {longestAir:0.00} s at {longestAirAt:0} m; lowest uprightness {lowestUpright:0.00}");
            TestContext.WriteLine($"Collisions: {impacts}, biggest {biggestImpact:0} N·s at {biggestImpactAt:0} m");
            Assert.Less(worstOffRoad, 1f, $"The car left the road (plus its bevel) at {worstOffRoadAt:0} m.");
        }

        private float GapFromRoadEdge(Vector3 position)
        {
            float distance = path.FindClosestDistance(position);
            return Flat(position - path.EvaluatePosition(distance)).magnitude - path.EvaluateWidth(distance) * 0.5f;
        }

        private Vector3 TangentAt(float distance) => path.EvaluatePosition(distance + 1f) - path.EvaluatePosition(distance - 1f);

        private Vector3 RightAt(float distance) => Vector3.Cross(Vector3.up, Flat(TangentAt(distance))).normalized;

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static string Describe(Collider collider)
        {
            if (collider is TerrainCollider)
                return "terrain (ground or a tree)";
            var parent = collider.transform.parent;
            return parent != null ? $"{collider.name} ({parent.name})" : collider.name;
        }

        private static void AssertNoProblems(List<string> problems, string kind)
        {
            const int shown = 12;
            if (problems.Count == 0)
                return;

            string list = string.Join("\n", problems.GetRange(0, Mathf.Min(shown, problems.Count)));
            Assert.Fail($"{problems.Count} {kind} problems:\n{list}{(problems.Count > shown ? "\n..." : "")}");
        }
    }
}
