using System.Collections.Generic;
using NUnit.Framework;
using RallyX.EditorTools;
using RallyX.Garage;
using RallyX.Vehicle;
using RallyX.VFX;
using UnityEditor;
using UnityEngine;

namespace RallyX.Tests
{
    /// <summary>
    /// Every garage car is fully dressed: lights and a leaning body (VehicleVisuals), tyre and exhaust effects with a
    /// complete config, wheels that spin inside steering-only brakes, every renderer with a mesh and materials, the paint
    /// and door numbers wired to the livery; and it still collides with exactly the boxes it had before Phase 10, with the
    /// low-friction body material, so the new look can't change how it handles.
    /// </summary>
    public sealed class VehiclePresentationTests
    {
        // The collision boxes each car had in Phase 7 (centre, size): the handling tests were tuned on these.
        private static readonly Dictionary<string, (Vector3 center, Vector3 size)[]> Colliders = new Dictionary<string, (Vector3, Vector3)[]>
        {
            ["falcon-rx"] = new[] { (new Vector3(0f, 0.7f, 0f), new Vector3(1.8f, 0.55f, 4.2f)), (new Vector3(0f, 1.2f, -0.25f), new Vector3(1.5f, 0.45f, 2.1f)) },
            ["kestrel-s"] = new[] { (new Vector3(0f, 0.68f, 0f), new Vector3(1.74f, 0.54f, 3.9f)), (new Vector3(0f, 1.17f, -0.2f), new Vector3(1.5f, 0.46f, 1.95f)) },
            ["titan-gt"] = new[] { (new Vector3(0f, 0.66f, 0.05f), new Vector3(1.92f, 0.5f, 4.5f)), (new Vector3(0f, 1.1f, -0.45f), new Vector3(1.54f, 0.42f, 2f)) },
        };

        private VehicleRoster roster;

        [SetUp]
        public void LoadRoster()
        {
            roster = GarageContentBuilder.LoadRoster();
            Assert.IsNotNull(roster, $"Missing {GarageContentBuilder.RosterPath}. Run RALLY X > Build > Create Missing Garage Content.");
        }

        [Test]
        public void EveryCar_HasLightsALeaningBodyAndEffects()
        {
            foreach (var vehicle in roster.Vehicles)
            {
                var car = vehicle.Prefab;
                var visuals = car.GetComponent<VehicleVisuals>();
                Assert.IsNotNull(visuals, $"{vehicle.DisplayName} has no VehicleVisuals. Run RALLY X > Build > Rebuild All Car Prefabs.");
                Assert.IsNotNull(visuals.Body, $"{vehicle.DisplayName}'s body pivot is missing.");
                Assert.IsTrue(visuals.Body.IsChildOf(car.transform) && visuals.Body != car.transform, "The body pivot must be a child of the car.");
                Assert.IsEmpty(visuals.Body.GetComponentsInChildren<Collider>(), "Nothing on the leaning body may collide.");
                var lights = new SerializedObject(visuals);
                foreach (string property in new[] { "brakeLights", "reverseLights", "headLights" })
                    Assert.Greater(lights.FindProperty(property).arraySize, 0, $"{vehicle.DisplayName} has no {property}.");

                var effects = car.GetComponent<VehicleEffects>();
                Assert.IsNotNull(effects, $"{vehicle.DisplayName} has no VehicleEffects.");
                Assert.IsNotNull(effects.Config, $"{vehicle.DisplayName}'s effects have no config.");
                Assert.Greater(new SerializedObject(effects).FindProperty("exhausts").arraySize, 0, $"{vehicle.DisplayName} has no exhaust.");

                var livery = new SerializedObject(car.GetComponent<VehicleLivery>());
                Assert.Greater(livery.FindProperty("paintedRenderers").arraySize, 0, $"{vehicle.DisplayName} has no paint to tint.");
                Assert.AreEqual(2, livery.FindProperty("numberTexts").arraySize, $"{vehicle.DisplayName} should carry its number on both doors.");
                StringAssert.IsMatch("^[0-9]+$", car.GetComponent<VehicleLivery>().RaceNumber);
            }
        }

        [Test]
        public void EveryCar_HasSpinningWheelsAndCompleteRenderers()
        {
            foreach (var vehicle in roster.Vehicles)
            {
                var car = vehicle.Prefab;
                Assert.AreEqual(4, car.Wheels.Count);
                foreach (var wheel in car.Wheels)
                {
                    var serialized = new SerializedObject(wheel);
                    var visual = (Transform)serialized.FindProperty("visual").objectReferenceValue;
                    var spin = (Transform)serialized.FindProperty("spinVisual").objectReferenceValue;
                    Assert.IsNotNull(visual, $"{vehicle.DisplayName} {wheel.name} has no visual.");
                    Assert.IsNotNull(spin, $"{vehicle.DisplayName} {wheel.name} has no spinning part.");
                    Assert.IsTrue(spin.IsChildOf(visual), "The spinning part must sit inside the steering visual.");
                }

                foreach (var renderer in car.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null)
                        continue;
                    Assert.IsNotNull(filter.sharedMesh, $"{vehicle.DisplayName} {renderer.name} has no mesh.");
                    Assert.Greater(filter.sharedMesh.vertexCount, 0, $"{vehicle.DisplayName} {renderer.name} has an empty mesh.");
                    Assert.AreEqual(filter.sharedMesh.subMeshCount, renderer.sharedMaterials.Length, $"{vehicle.DisplayName} {renderer.name}: one material per submesh.");
                    foreach (var material in renderer.sharedMaterials)
                        Assert.IsNotNull(material, $"{vehicle.DisplayName} {renderer.name} is missing a material.");
                }
            }
        }

        [Test]
        public void EveryCar_KeepsItsCollisionBoxes()
        {
            foreach (var vehicle in roster.Vehicles)
            {
                Assert.IsTrue(Colliders.TryGetValue(vehicle.Id, out var expected), $"No expected collision boxes for {vehicle.Id}.");
                var boxes = vehicle.Prefab.GetComponentsInChildren<Collider>(true);
                Assert.AreEqual(expected.Length, boxes.Length, $"{vehicle.DisplayName} should collide with exactly {expected.Length} boxes.");
                for (int i = 0; i < expected.Length; i++)
                {
                    var box = boxes[i] as BoxCollider;
                    Assert.IsNotNull(box, $"{vehicle.DisplayName}'s colliders should be boxes.");
                    Vector3 centre = vehicle.Prefab.transform.InverseTransformPoint(box.transform.TransformPoint(box.center));
                    Assert.That(Vector3.Distance(expected[i].center, centre), Is.LessThan(1e-3f), $"{vehicle.DisplayName} {box.name} moved.");
                    Assert.That(Vector3.Distance(expected[i].size, Vector3.Scale(box.size, box.transform.lossyScale)), Is.LessThan(1e-3f),
                        $"{vehicle.DisplayName} {box.name} changed size.");
                    Assert.IsNotNull(box.sharedMaterial, $"{vehicle.DisplayName} {box.name} has no physics material.");
                    Assert.AreEqual(PhysicsMaterialCombine.Minimum, box.sharedMaterial.frictionCombine, "Car bodies should slide along what they hit.");
                    Assert.LessOrEqual(box.sharedMaterial.dynamicFriction, 0.2f);
                }
            }
        }

        [Test]
        public void EffectsConfig_IsCompleteAndSane()
        {
            var config = AssetDatabase.LoadAssetAtPath<VehicleEffectsConfig>(VfxContentBuilder.ConfigPath);
            Assert.IsNotNull(config, $"Missing {VfxContentBuilder.ConfigPath}.");
            foreach (var material in new[] { config.PuffMaterial, config.DebrisMaterial, config.SparkMaterial, config.SkidMaterial })
            {
                Assert.IsNotNull(material);
                Assert.AreEqual("RallyX/Effect", material.shader.name);
                Assert.IsNotNull(material.mainTexture, $"{material.name} has no texture.");
            }

            Assert.Greater(config.SlideFull, config.SlideStart);
            Assert.Greater(config.SlideStart, 0.5f, "Ordinary cornering must not count as sliding.");
            Assert.LessOrEqual(config.MaxPuffs + config.MaxDebris + config.MaxSparks + config.MaxExhaust, 1000, "Keep each car's particle budget modest.");
        }
    }
}
