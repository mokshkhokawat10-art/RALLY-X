using System.Collections.Generic;
using RallyX.Audio;
using RallyX.Core;
using RallyX.VFX;
using RallyX.Vehicle;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RallyX.EditorTools
{
    /// <summary>
    /// Builds a placeholder car prefab from generated low-poly geometry until real models exist: a lofted body with
    /// wheel arches and flares, a glass cabin with pillars and roof, bumpers, lights, mirrors, mud flaps, door numbers,
    /// exhausts and the car's own details (spoilers, scoops, stripes, light pods), plus spoked wheels with tyres and
    /// brakes. Collisions use plain boxes that never change, so the handling is the same whatever the car looks like.
    /// The body's visible parts sit on a pivot that <see cref="VehicleVisuals"/> leans; the car also gets its tyre and
    /// exhaust effects (<see cref="VehicleEffects"/>). Rebuilding an existing prefab keeps its root and root components,
    /// so references to it stay valid.
    /// </summary>
    internal static class PlaceholderCarBuilder
    {
        public const string MaterialsFolder = "Assets/Materials/Vehicles";

        private const string LogPrefix = "[RALLY X Build] ";
        private const string MeshFolder = "Assets/Models/Generated/Vehicles";
        private const string BodyPhysicsMaterialPath = "Assets/Settings/Physics/VehicleBody.physicMaterial";

        // Written by earlier builds with an extension Unity doesn't import, so it never reached the colliders.
        private const string UnusableBodyPhysicsMaterialPath = "Assets/Settings/Physics/VehicleBody.physicsMaterial";
        private const string WorldTextShaderPath = "Assets/Materials/Shaders/WorldText.shader";

        private const float TyreWidth = 0.26f;
        private const int WheelSegments = 22;

        // The body leans about this height, close to the centre of mass.
        private const float BodyPivotHeight = 0.55f;

        public enum Finish
        {
            Paint,
            Trim,
            Glass,
            Plastic,
            Chrome,
            HeadLight,
            TailLight,
            Caliper,
        }

        public enum Shape
        {
            Box,

            /// <summary>A cylinder along the part's forward axis; size is (diameter, diameter, length).</summary>
            Cylinder,
        }

        /// <summary>A car-specific detail (spoiler, scoop, light pod...), in the car's space.</summary>
        public readonly struct Part
        {
            public readonly string Name;
            public readonly Vector3 Position;
            public readonly Vector3 Size;
            public readonly Finish Finish;
            public readonly Vector3 Rotation;
            public readonly Shape Shape;

            public Part(string name, Vector3 position, Vector3 size, Finish finish, Vector3 rotation = default, Shape shape = Shape.Box)
            {
                Name = name;
                Position = position;
                Size = size;
                Finish = finish;
                Rotation = rotation;
                Shape = shape;
            }
        }

        /// <summary>A box collider on the car, in the car's space.</summary>
        public readonly struct ColliderBox
        {
            public readonly string Name;
            public readonly Vector3 Center;
            public readonly Vector3 Size;

            public ColliderBox(string name, Vector3 center, Vector3 size)
            {
                Name = name;
                Center = center;
                Size = size;
            }
        }

        public sealed class Spec
        {
            /// <summary>Root object name and material name prefix.</summary>
            public string Name;
            public string PrefabPath;
            public Color Paint;

            /// <summary>The second livery colour: stripes, roof, mud flaps. AI paint never changes it.</summary>
            public Color Trim = Color.white;
            public Color Rim = new Color(0.78f, 0.79f, 0.8f);
            public Color Caliper = new Color(0.8f, 0.08f, 0.05f);
            public int RaceNumber = 1;

            /// <summary>The body's cross-sections along the car (the wheel arches are cut out automatically).</summary>
            public BodyStation[] Body;

            /// <summary>The cabin's cross-sections, from the windscreen base to the rear window base.</summary>
            public BodyStation[] Cabin;

            /// <summary>Where the roof panel starts and ends along the car, metres.</summary>
            public float RoofFront;
            public float RoofRear;
            public bool TrimRoof;

            /// <summary>Stripes along the bonnet, roof and rear deck: width (0 = none) and gap between two (0 = one centred).</summary>
            public float StripeWidth;
            public float StripeSpacing;

            /// <summary>How far the wheel-arch flares stand out from the body, metres; painted or black plastic.</summary>
            public float FlareWidth = 0.05f;
            public bool PaintedFlares;

            public Part[] Details = System.Array.Empty<Part>();

            /// <summary>Exhaust tips in the car's space; the pipes point backwards.</summary>
            public Vector3[] Exhausts = System.Array.Empty<Vector3>();

            /// <summary>The car's collision boxes. Keep them when restyling, or the handling changes.</summary>
            public ColliderBox[] Colliders = System.Array.Empty<ColliderBox>();

            /// <summary>Wheel centres: half the track width, and the front and rear axles along the car, metres.</summary>
            public float HalfTrack;
            public float FrontAxle;
            public float RearAxle;

            /// <summary>Body lean: degrees per g of braking and of cornering, and how quick the lean is (Hz).</summary>
            public float PitchPerG = 2.2f;
            public float RollPerG = 2.8f;
            public float LeanFrequency = 2.6f;
        }

        /// <summary>
        /// Builds the prefab, or with <paramref name="overwrite"/> false only when it is missing or predates the current
        /// look (has no <see cref="VehicleVisuals"/>). Returns false (and logs why) if it couldn't be saved.
        /// </summary>
        public static bool Build(Spec spec, VehicleConfig config, bool overwrite)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath);
            if (!overwrite && existing != null && existing.GetComponent<VehicleVisuals>() != null)
                return true;

            int vehicleLayer = LayerMask.NameToLayer(GameLayers.Vehicle);
            if (vehicleLayer < 0)
            {
                Debug.LogError(LogPrefix + $"Layer '{GameLayers.Vehicle}' is missing. Run RALLY X > Setup > Run Project Setup first.");
                return false;
            }

            var effects = VfxContentBuilder.LoadOrCreateConfig();
            if (effects == null)
                return false;

            var bodyPhysics = LoadBodyPhysicsMaterial();
            var materials = CarMaterials.Load(spec);
            var root = existing != null ? PrefabUtility.LoadPrefabContents(spec.PrefabPath) : new GameObject(spec.Name);
            try
            {
                // Everything below the root is generated; the root and its components are kept (other assets point at them).
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                    Object.DestroyImmediate(root.transform.GetChild(i).gameObject);

                GetOrAdd<Rigidbody>(root);
                var controller = GetOrAdd<VehicleController>(root);
                foreach (var box in spec.Colliders)
                    CreateCollider(root.transform, box, bodyPhysics);

                // Place the suspension mounts so the tyres just touch the ground at static sag with the root at ground level.
                float angularFrequency = 2f * Mathf.PI * config.SpringFrequency;
                float staticSag = Physics.gravity.magnitude / (angularFrequency * angularFrequency);
                float restSpringLength = config.SuspensionTravel - staticSag;
                float mountHeight = config.WheelRadius + restSpringLength;

                var body = BuildBody(spec, config.WheelRadius, root.transform, materials, out var lights, out var numbers);
                var wheels = BuildWheels(spec, config.WheelRadius, mountHeight, restSpringLength, root.transform, materials);
                var exhausts = new Transform[spec.Exhausts.Length];
                for (int i = 0; i < exhausts.Length; i++)
                {
                    exhausts[i] = new GameObject($"Exhaust_{i + 1}").transform;
                    exhausts[i].SetParent(root.transform, false);
                    exhausts[i].SetLocalPositionAndRotation(spec.Exhausts[i] + Vector3.back * 0.1f, Quaternion.LookRotation(Vector3.back));
                }

                var serializedController = new SerializedObject(controller);
                serializedController.FindProperty("config").objectReferenceValue = config;
                serializedController.FindProperty("fallbackSurface").objectReferenceValue = VehicleContentBuilder.LoadSurface("Asphalt");
                var wheelsProperty = serializedController.FindProperty("wheels");
                wheelsProperty.arraySize = wheels.Length;
                for (int i = 0; i < wheels.Length; i++)
                    wheelsProperty.GetArrayElementAtIndex(i).objectReferenceValue = wheels[i];
                serializedController.ApplyModifiedPropertiesWithoutUndo();

                var livery = GetOrAdd<VehicleLivery>(root);
                livery.SetPaintedRenderers(new[] { lights.Paint });
                livery.SetNumberTexts(numbers);

                var visuals = GetOrAdd<VehicleVisuals>(root);
                visuals.Configure(body, new[] { lights.Tail }, new[] { lights.Reverse }, new[] { lights.Head });
                visuals.SetLean(spec.PitchPerG, spec.RollPerG, spec.LeanFrequency);

                GetOrAdd<VehicleEffects>(root).Configure(effects, exhausts);

                // Sound: the car's own audio config once the audio content exists (RALLY X > Build > Create Missing Audio Content).
                var carAudio = GetOrAdd<VehicleAudio>(root);
                if (carAudio.Config == null)
                    carAudio.Configure(AudioContentBuilder.LoadVehicleConfig(spec.Name));

                EditorAssetUtility.SetLayerRecursively(root, vehicleLayer);
                EditorAssetUtility.EnsureFolder(System.IO.Path.GetDirectoryName(spec.PrefabPath)?.Replace('\\', '/'));
                PrefabUtility.SaveAsPrefabAsset(root, spec.PrefabPath, out bool saved);
                if (!saved)
                {
                    Debug.LogError(LogPrefix + $"Failed to save {spec.PrefabPath}.");
                    return false;
                }

                Debug.Log(LogPrefix + $"Built {spec.PrefabPath}.");
                return true;
            }
            finally
            {
                if (existing != null)
                    PrefabUtility.UnloadPrefabContents(root);
                else
                    Object.DestroyImmediate(root);
            }
        }

        // ---------------------------------------------------------------- body

        private struct BodyRenderers
        {
            public Renderer Paint;
            public Renderer Head;
            public Renderer Tail;
            public Renderer Reverse;
        }

        private sealed class BodyParts
        {
            public readonly LowPolyMeshBuilder Paint = new LowPolyMeshBuilder();
            public readonly LowPolyMeshBuilder Trim = new LowPolyMeshBuilder();
            public readonly LowPolyMeshBuilder Glass = new LowPolyMeshBuilder();
            public readonly LowPolyMeshBuilder Plastic = new LowPolyMeshBuilder();
            public readonly LowPolyMeshBuilder Chrome = new LowPolyMeshBuilder();
            public readonly LowPolyMeshBuilder Number = new LowPolyMeshBuilder();
            public readonly LowPolyMeshBuilder Caliper = new LowPolyMeshBuilder();
            public readonly LowPolyMeshBuilder Head = new LowPolyMeshBuilder();
            public readonly LowPolyMeshBuilder Tail = new LowPolyMeshBuilder();
            public readonly LowPolyMeshBuilder Reverse = new LowPolyMeshBuilder();

            public LowPolyMeshBuilder For(Finish finish) => finish switch
            {
                Finish.Paint => Paint,
                Finish.Trim => Trim,
                Finish.Glass => Glass,
                Finish.Plastic => Plastic,
                Finish.Chrome => Chrome,
                Finish.HeadLight => Head,
                Finish.TailLight => Tail,
                _ => Caliper,
            };
        }

        private static Transform BuildBody(Spec spec, float wheelRadius, Transform root, CarMaterials materials, out BodyRenderers renderers,
            out TextMesh[] numbers)
        {
            var parts = new BodyParts();
            var body = new List<BodyStation>(spec.Body);
            body.Sort((a, b) => a.Z.CompareTo(b.Z));
            var cabin = new List<BodyStation>(spec.Cabin);
            cabin.Sort((a, b) => a.Z.CompareTo(b.Z));

            // The wheel openings: a little longer than the tyre, topped just below the tyre's top so the flare's
            // round edge frames them.
            float archHalf = wheelRadius * 1.1f;
            float archTop = wheelRadius * 1.92f;
            parts.Paint.LoftWithArches(body, new[] { spec.FrontAxle, spec.RearAxle }, archHalf, archTop);
            AddArches(spec, body, wheelRadius, archHalf, archTop, spec.PaintedFlares ? parts.Paint : parts.Plastic, parts.Plastic);

            parts.Glass.Loft(cabin);
            AddRoofAndPillars(spec, body, cabin, spec.TrimRoof ? parts.Trim : parts.Paint, parts.Paint, parts.Plastic);
            AddStripes(spec, body, cabin, parts.Trim);
            AddFrontAndRear(body, parts);
            AddMirrorsAndFlaps(spec, body, cabin, wheelRadius, parts);
            var numberPlacements = AddNumberPanels(spec, body, parts.Number);
            foreach (var exhaust in spec.Exhausts)
            {
                parts.Chrome.Cylinder(exhaust, Vector3.forward, 0.045f, 0.2f, 12);
                parts.Plastic.Cylinder(exhaust + Vector3.back * 0.01f, Vector3.forward, 0.032f, 0.2f, 12);
            }

            foreach (var detail in spec.Details)
            {
                var target = parts.For(detail.Finish);
                var rotation = Quaternion.Euler(detail.Rotation);
                if (detail.Shape == Shape.Cylinder)
                    target.Cylinder(detail.Position, rotation * Vector3.forward, detail.Size.x * 0.5f, detail.Size.z, 14);
                else
                    target.Box(detail.Position, detail.Size, rotation);
            }

            // The visible body hangs from a pivot near the centre of mass, which the lean turns.
            var pivot = new GameObject("Body").transform;
            pivot.SetParent(root, false);
            pivot.localPosition = new Vector3(0f, BodyPivotHeight, 0f);
            Vector3 toCar = -pivot.localPosition;

            renderers = new BodyRenderers
            {
                Paint = CreateRenderer("Paint", pivot, SaveMesh(spec.Name, "Paint", parts.Paint), materials.Paint),
                Head = CreateRenderer("HeadLights", pivot, SaveMesh(spec.Name, "HeadLights", parts.Head), materials.HeadLight),
                Tail = CreateRenderer("TailLights", pivot, SaveMesh(spec.Name, "TailLights", parts.Tail), materials.TailLight),
                Reverse = CreateRenderer("ReverseLights", pivot, SaveMesh(spec.Name, "ReverseLights", parts.Reverse), materials.ReverseLight),
            };
            var details = SaveMesh(spec.Name, "Details", LowPolyMeshBuilder.Combine("Details", Vector3.zero, parts.Trim, parts.Glass, parts.Plastic,
                parts.Chrome, parts.Number, parts.Caliper));
            CreateRenderer("Details", pivot, details, materials.Trim, materials.Glass, materials.Plastic, materials.Chrome, materials.Number,
                materials.Caliper);
            foreach (Transform child in pivot)
                child.localPosition = toCar;

            numbers = new TextMesh[numberPlacements.Count];
            for (int i = 0; i < numbers.Length; i++)
                numbers[i] = CreateNumber(pivot, numberPlacements[i].position - pivot.localPosition, numberPlacements[i].facing, spec.RaceNumber, materials);
            return pivot;
        }

        /// <summary>Flares around each wheel opening, and dark wheel wells inside so the openings don't show through the car.</summary>
        private static void AddArches(Spec spec, List<BodyStation> body, float wheelRadius, float archHalf, float archTop, LowPolyMeshBuilder flares,
            LowPolyMeshBuilder wells)
        {
            float centreHeight = wheelRadius;
            float outer = Mathf.Sqrt(archHalf * archHalf + (archTop - centreHeight) * (archTop - centreHeight)) + 0.01f;
            float inner = outer - 0.075f;
            foreach (float axle in new[] { spec.FrontAxle, spec.RearAxle })
            {
                var station = LowPolyMeshBuilder.SampleStation(body, axle);
                float side = station.HalfWidthAt(archTop) - 0.03f;
                for (int s = -1; s <= 1; s += 2)
                    flares.Arch(new Vector3(0f, centreHeight, axle), inner, outer, s * side, s * (side + 0.03f + spec.FlareWidth), -12f, 192f, 16);

                float wellHalfWidth = spec.HalfTrack - TyreWidth * 0.5f - 0.03f;
                wells.Box(new Vector3(0f, (archTop + 0.3f) * 0.5f + 0.02f, axle), new Vector3(wellHalfWidth * 2f, archTop - 0.3f, archHalf * 2f));
            }
        }

        private static void AddRoofAndPillars(Spec spec, List<BodyStation> body, List<BodyStation> cabin, LowPolyMeshBuilder roof, LowPolyMeshBuilder pillars,
            LowPolyMeshBuilder trim)
        {
            // Roof panel: a slab over the cabin's top between the roof's ends.
            var slab = new List<BodyStation>();
            for (int i = 0; i <= 4; i++)
            {
                float z = Mathf.Lerp(spec.RoofRear, spec.RoofFront, i / 4f);
                var s = LowPolyMeshBuilder.SampleStation(cabin, z);
                slab.Add(new BodyStation(z, s.Top - 0.04f, s.Top + 0.02f, s.HalfWidthTop + 0.012f, s.HalfWidthTop - 0.02f, 0.035f));
            }

            roof.Loft(slab);

            var front = cabin[cabin.Count - 1];
            var back = cabin[0];
            float belt = front.Bottom + 0.03f;
            float middle = Mathf.Lerp(spec.RoofRear, spec.RoofFront, 0.45f);
            for (int s = -1; s <= 1; s += 2)
            {
                // A, B and C pillars sit on the glass; a rubber strip runs along the bottom of the side windows.
                var roofFront = LowPolyMeshBuilder.SampleStation(cabin, spec.RoofFront);
                var roofBack = LowPolyMeshBuilder.SampleStation(cabin, spec.RoofRear);
                var mid = LowPolyMeshBuilder.SampleStation(cabin, middle);
                pillars.Beam(new Vector3(s * (front.HalfWidthAt(belt) + 0.015f), belt, front.Z - 0.02f),
                    new Vector3(s * (roofFront.HalfWidthTop + 0.01f), roofFront.Top - 0.02f, spec.RoofFront), 0.09f, 0.05f, Vector3.right * s);
                pillars.Beam(new Vector3(s * (mid.HalfWidthAt(belt) + 0.012f), belt, middle), new Vector3(s * (mid.HalfWidthTop + 0.01f), mid.Top - 0.02f, middle),
                    0.08f, 0.04f, Vector3.right * s);
                pillars.Beam(new Vector3(s * (roofBack.HalfWidthTop + 0.01f), roofBack.Top - 0.02f, spec.RoofRear),
                    new Vector3(s * (back.HalfWidthAt(belt) + 0.015f), belt, back.Z + 0.02f), 0.13f, 0.05f, Vector3.right * s);

                var beltFront = LowPolyMeshBuilder.SampleStation(cabin, front.Z - 0.05f);
                var beltBack = LowPolyMeshBuilder.SampleStation(cabin, back.Z + 0.05f);
                trim.Beam(new Vector3(s * (beltFront.HalfWidthAt(belt) + 0.01f), belt, beltFront.Z), new Vector3(s * (beltBack.HalfWidthAt(belt) + 0.01f), belt, beltBack.Z),
                    0.03f, 0.03f, Vector3.up);
            }
        }

        /// <summary>Livery stripes along the bonnet, over the roof and along the rear deck.</summary>
        private static void AddStripes(Spec spec, List<BodyStation> body, List<BodyStation> cabin, LowPolyMeshBuilder trim)
        {
            if (spec.StripeWidth <= 0f)
                return;

            float nose = body[body.Count - 1].Z - 0.08f;
            float tail = body[0].Z + 0.08f;
            float cabinFront = cabin[cabin.Count - 1].Z;
            float cabinBack = cabin[0].Z;
            var xs = spec.StripeSpacing > 0f ? new[] { -spec.StripeSpacing * 0.5f, spec.StripeSpacing * 0.5f } : new[] { 0f };
            foreach (float x in xs)
            {
                LayStripe(body, x, cabinFront, nose, spec.StripeWidth, 0.004f, trim);
                LayStripe(body, x, tail, cabinBack, spec.StripeWidth, 0.004f, trim);
                LayStripe(cabin, x, spec.RoofRear, spec.RoofFront, spec.StripeWidth, 0.024f, trim);
            }
        }

        private static void LayStripe(List<BodyStation> stations, float x, float from, float to, float width, float lift, LowPolyMeshBuilder trim)
        {
            const int pieces = 8;
            for (int i = 0; i < pieces; i++)
            {
                float z0 = Mathf.Lerp(from, to, i / (float)pieces);
                float z1 = Mathf.Lerp(from, to, (i + 1) / (float)pieces);
                var a = new Vector3(x, LowPolyMeshBuilder.SampleStation(stations, z0).Top + lift, z0);
                var b = new Vector3(x, LowPolyMeshBuilder.SampleStation(stations, z1).Top + lift, z1);
                trim.Beam(a, b, width, 0.012f, Vector3.up);
            }
        }

        private static void AddFrontAndRear(List<BodyStation> body, BodyParts parts)
        {
            // Front: headlights and grille in the nose, a bumper and tow hook below.
            var nose = body[body.Count - 1];
            var front = LowPolyMeshBuilder.SampleStation(body, nose.Z - 0.06f);
            float lampY = Mathf.Max(front.Top - 0.12f, front.Bottom + 0.22f);
            float lampX = front.HalfWidthTop * 0.6f;
            for (int s = -1; s <= 1; s += 2)
            {
                parts.Head.Box(new Vector3(s * lampX, lampY, nose.Z - 0.035f), new Vector3(front.HalfWidthTop * 0.46f, 0.13f, 0.1f));
                parts.Plastic.Box(new Vector3(s * lampX, lampY, nose.Z - 0.045f), new Vector3(front.HalfWidthTop * 0.52f, 0.17f, 0.1f));
            }

            parts.Plastic.Box(new Vector3(0f, lampY - 0.015f, nose.Z - 0.03f), new Vector3(front.HalfWidthTop * 0.62f, 0.11f, 0.08f));
            float bumperY = nose.Bottom + 0.06f;
            parts.Plastic.Box(new Vector3(0f, bumperY, nose.Z - 0.05f), new Vector3(front.HalfWidthBottom * 2f + 0.02f, 0.16f, 0.2f));
            parts.Caliper.Box(new Vector3(front.HalfWidthBottom * 0.5f, bumperY - 0.02f, nose.Z + 0.07f), new Vector3(0.05f, 0.05f, 0.1f));

            // Rear: tail lights with reversing lights inboard, a bumper and tow hook.
            var tail = body[0];
            var rear = LowPolyMeshBuilder.SampleStation(body, tail.Z + 0.06f);
            float tailY = Mathf.Max(rear.Top - 0.13f, rear.Bottom + 0.24f);
            for (int s = -1; s <= 1; s += 2)
            {
                parts.Tail.Box(new Vector3(s * rear.HalfWidthTop * 0.68f, tailY, tail.Z + 0.035f), new Vector3(0.36f, 0.12f, 0.1f));
                parts.Reverse.Box(new Vector3(s * rear.HalfWidthTop * 0.34f, tailY, tail.Z + 0.035f), new Vector3(0.14f, 0.08f, 0.1f));
            }

            float rearBumperY = tail.Bottom + 0.05f;
            parts.Plastic.Box(new Vector3(0f, rearBumperY, tail.Z + 0.05f), new Vector3(rear.HalfWidthBottom * 2f + 0.02f, 0.17f, 0.18f));
            parts.Caliper.Box(new Vector3(-rear.HalfWidthBottom * 0.5f, rearBumperY - 0.02f, tail.Z - 0.07f), new Vector3(0.05f, 0.05f, 0.1f));
        }

        private static void AddMirrorsAndFlaps(Spec spec, List<BodyStation> body, List<BodyStation> cabin, float wheelRadius, BodyParts parts)
        {
            var front = cabin[cabin.Count - 1];
            float belt = front.Bottom + 0.03f;
            var bodyAtMirror = LowPolyMeshBuilder.SampleStation(body, front.Z - 0.12f);
            for (int s = -1; s <= 1; s += 2)
            {
                float x = bodyAtMirror.HalfWidthAt(bodyAtMirror.Top - 0.02f);
                parts.Plastic.Box(new Vector3(s * (x + 0.03f), belt + 0.03f, front.Z - 0.12f), new Vector3(0.08f, 0.04f, 0.05f));
                parts.Paint.Box(new Vector3(s * (x + 0.09f), belt + 0.08f, front.Z - 0.14f), new Vector3(0.12f, 0.1f, 0.13f));

                // Mud flaps behind each wheel.
                foreach (float axle in new[] { spec.FrontAxle, spec.RearAxle })
                    parts.Trim.Box(new Vector3(s * spec.HalfTrack, 0.27f, axle - wheelRadius - 0.14f), new Vector3(0.3f, 0.34f, 0.02f));
            }
        }

        /// <summary>
        /// White door panels for the race number, one on each side, midway between the wheels. Returns where each number goes
        /// and which way it faces.
        /// </summary>
        private static List<(Vector3 position, Vector3 facing)> AddNumberPanels(Spec spec, List<BodyStation> body, LowPolyMeshBuilder number)
        {
            float z = (spec.FrontAxle + spec.RearAxle) * 0.5f;
            var station = LowPolyMeshBuilder.SampleStation(body, z);
            float y = Mathf.Lerp(station.Bottom, station.Top, 0.55f);
            float x = station.HalfWidthAt(y);
            var placements = new List<(Vector3, Vector3)>();
            for (int s = -1; s <= 1; s += 2)
            {
                number.Box(new Vector3(s * x, y, z), new Vector3(0.02f, 0.3f, 0.44f));
                placements.Add((new Vector3(s * (x + 0.012f), y, z), Vector3.right * s));
            }

            return placements;
        }

        private static TextMesh CreateNumber(Transform parent, Vector3 localPosition, Vector3 facing, int raceNumber, CarMaterials materials)
        {
            var text = new GameObject("RaceNumber");
            text.transform.SetParent(parent, false);

            // Text is drawn in the object's XY plane and read looking along its forward, so forward points into the door.
            text.transform.SetLocalPositionAndRotation(localPosition, Quaternion.LookRotation(-facing, Vector3.up));
            var textMesh = text.AddComponent<TextMesh>();
            textMesh.text = raceNumber.ToString();
            textMesh.font = materials.NumberFont;
            textMesh.fontSize = 64;
            textMesh.characterSize = 0.036f;
            textMesh.fontStyle = FontStyle.Bold;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = new Color(0.05f, 0.05f, 0.06f);
            var textRenderer = text.GetComponent<MeshRenderer>();
            textRenderer.sharedMaterial = materials.NumberText;
            textRenderer.shadowCastingMode = ShadowCastingMode.Off;
            return textMesh;
        }

        // ---------------------------------------------------------------- wheels

        private static VehicleWheel[] BuildWheels(Spec spec, float radius, float mountHeight, float restSpringLength, Transform root,
            CarMaterials materials)
        {
            var left = WheelMeshes(spec.Name, "L", -1f, radius);
            var right = WheelMeshes(spec.Name, "R", 1f, radius);
            return new[]
            {
                CreateWheel("Wheel_FL", root, new Vector3(-spec.HalfTrack, mountHeight, spec.FrontAxle), true, restSpringLength, left, materials),
                CreateWheel("Wheel_FR", root, new Vector3(spec.HalfTrack, mountHeight, spec.FrontAxle), true, restSpringLength, right, materials),
                CreateWheel("Wheel_RL", root, new Vector3(-spec.HalfTrack, mountHeight, spec.RearAxle), false, restSpringLength, left, materials),
                CreateWheel("Wheel_RR", root, new Vector3(spec.HalfTrack, mountHeight, spec.RearAxle), false, restSpringLength, right, materials),
            };
        }

        /// <summary>
        /// A wheel's spinning part (knobbly tyre, spoked rim, dark wheel centre) and its brake (disc and caliper), which
        /// only steers. <paramref name="outside"/> is the side of the car the wheel's face looks out of (-1 left, 1 right).
        /// </summary>
        private static (Mesh spin, Mesh brake) WheelMeshes(string carName, string side, float outside, float radius)
        {
            var tyre = new LowPolyMeshBuilder();
            var rim = new LowPolyMeshBuilder();
            var centre = new LowPolyMeshBuilder();
            var disc = new LowPolyMeshBuilder();
            var caliper = new LowPolyMeshBuilder();

            float half = TyreWidth * 0.5f;
            float rimRadius = radius * 0.66f;
            var tyreProfile = new[]
            {
                new Vector2(-half, rimRadius), new Vector2(-half, radius - 0.04f), new Vector2(-half + 0.035f, radius),
                new Vector2(half - 0.035f, radius), new Vector2(half, radius - 0.04f), new Vector2(half, rimRadius),
            };

            // Tread blocks stand proud of the tyre, so it reads as a gravel tyre and its turning shows.
            tyre.Lathe(Vector3.zero, Vector3.right, tyreProfile, WheelSegments);
            const int blocks = 14;
            for (int block = 0; block < blocks; block++)
            {
                float angle = (block + 0.5f) * 360f / blocks;
                var rotation = Quaternion.Euler(angle, 0f, 0f);
                tyre.Box(rotation * new Vector3(0f, radius + 0.004f, 0f), new Vector3(TyreWidth - 0.08f, 0.018f, 0.07f), rotation);
            }

            float face = outside * (half - 0.03f);
            var axis = Vector3.right * outside;
            var lip = new[]
            {
                new Vector2(-0.12f, rimRadius - 0.03f), new Vector2(0f, rimRadius - 0.03f), new Vector2(0f, rimRadius + 0.005f),
                new Vector2(-0.12f, rimRadius + 0.005f),
            };
            rim.Lathe(Vector3.right * face, axis, lip, WheelSegments);
            rim.Cylinder(Vector3.right * (face - outside * 0.02f), Vector3.right, 0.055f, 0.05f, 10);
            for (int spoke = 0; spoke < 5; spoke++)
            {
                float angle = spoke * Mathf.PI * 2f / 5f;
                var direction = new Vector3(0f, Mathf.Sin(angle), Mathf.Cos(angle));
                var at = Vector3.right * (face - outside * 0.025f);
                rim.Beam(at + direction * 0.045f, at + direction * (rimRadius - 0.025f), 0.05f, 0.03f, Vector3.right);
            }

            centre.Cylinder(Vector3.right * (face - outside * 0.11f), Vector3.right, rimRadius - 0.025f, 0.02f, WheelSegments);
            disc.Cylinder(Vector3.right * (face - outside * 0.075f), Vector3.right, rimRadius - 0.05f, 0.022f, WheelSegments);
            float caliperRadius = rimRadius - 0.07f;
            caliper.Box(Vector3.right * (face - outside * 0.07f) + new Vector3(0f, 0.87f * caliperRadius, -0.5f * caliperRadius),
                new Vector3(0.06f, 0.1f, 0.08f), Quaternion.Euler(-30f, 0f, 0f));

            var spin = SaveMesh(carName, $"Wheel_{side}", LowPolyMeshBuilder.Combine($"Wheel_{side}", Vector3.zero, tyre, rim, centre));
            var brake = SaveMesh(carName, $"Brake_{side}", LowPolyMeshBuilder.Combine($"Brake_{side}", Vector3.zero, disc, caliper));
            return (spin, brake);
        }

        private static VehicleWheel CreateWheel(string name, Transform parent, Vector3 localPosition, bool isFront, float restSpringLength,
            (Mesh spin, Mesh brake) meshes, CarMaterials materials)
        {
            var mount = new GameObject(name);
            mount.transform.SetParent(parent, false);
            mount.transform.localPosition = localPosition;
            var wheel = mount.AddComponent<VehicleWheel>();

            var visual = new GameObject("Visual").transform;
            visual.SetParent(mount.transform, false);
            visual.localPosition = new Vector3(0f, -restSpringLength, 0f);
            CreateRenderer("Brake", visual, meshes.brake, materials.Metal, materials.Caliper);

            var spin = CreateRenderer("Spin", visual, meshes.spin, materials.Rubber, materials.Rim, materials.Metal).transform;

            var serializedWheel = new SerializedObject(wheel);
            serializedWheel.FindProperty("isFront").boolValue = isFront;
            serializedWheel.FindProperty("visual").objectReferenceValue = visual;
            serializedWheel.FindProperty("spinVisual").objectReferenceValue = spin;
            serializedWheel.ApplyModifiedPropertiesWithoutUndo();
            return wheel;
        }

        // ---------------------------------------------------------------- helpers

        private static void CreateCollider(Transform root, ColliderBox box, PhysicsMaterial physicsMaterial)
        {
            var colliderObject = new GameObject(box.Name);
            colliderObject.transform.SetParent(root, false);
            colliderObject.transform.localPosition = box.Center;
            var collider = colliderObject.AddComponent<BoxCollider>();
            collider.size = box.Size;
            collider.sharedMaterial = physicsMaterial;
        }

        private static MeshRenderer CreateRenderer(string name, Transform parent, Mesh mesh, params Material[] rendererMaterials)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = rendererMaterials;
            return renderer;
        }

        private static Mesh SaveMesh(string carName, string partName, LowPolyMeshBuilder builder) =>
            SaveMesh(carName, partName, LowPolyMeshBuilder.Combine(partName, Vector3.zero, builder));

        /// <summary>Saves a generated mesh as an asset, replacing the contents of an existing one so references to it stay valid.</summary>
        private static Mesh SaveMesh(string carName, string partName, Mesh mesh)
        {
            string path = $"{MeshFolder}/{carName}_{partName}.asset";
            mesh.name = $"{carName}_{partName}";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                EditorAssetUtility.EnsureFolder(MeshFolder);
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }

            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static T GetOrAdd<T>(GameObject gameObject) where T : Component =>
            gameObject.TryGetComponent(out T component) ? component : gameObject.AddComponent<T>();

        /// <summary>
        /// The car body's physics material. Low friction lets cars scrape along barriers and each other instead of
        /// snagging; a little bounce makes knocks glance off.
        /// </summary>
        private static PhysicsMaterial LoadBodyPhysicsMaterial()
        {
            if (AssetDatabase.AssetPathExists(UnusableBodyPhysicsMaterialPath))
                AssetDatabase.DeleteAsset(UnusableBodyPhysicsMaterialPath);

            return EditorAssetUtility.LoadOrCreate(BodyPhysicsMaterialPath, () => new PhysicsMaterial("VehicleBody")
            {
                dynamicFriction = 0.12f,
                staticFriction = 0.12f,
                bounciness = 0.1f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Maximum,
            });
        }

        /// <summary>The car's materials: its own paint, trim, rims and calipers, and finishes every car shares.</summary>
        private sealed class CarMaterials
        {
            public Material Paint;
            public Material Trim;
            public Material Rim;
            public Material Caliper;
            public Material Glass;
            public Material Plastic;
            public Material Chrome;
            public Material Metal;
            public Material Rubber;
            public Material Number;
            public Material HeadLight;
            public Material TailLight;
            public Material ReverseLight;
            public Material NumberText;
            public Font NumberFont;

            public static CarMaterials Load(Spec spec)
            {
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return new CarMaterials
                {
                    Paint = PaintMaterial($"{MaterialsFolder}/{spec.Name}_Body.mat", spec.Paint),
                    Trim = Lit($"{spec.Name}_Trim", spec.Trim, 0.62f),
                    Rim = Lit($"{spec.Name}_Rim", spec.Rim, 0.72f, metallic: 0.85f),
                    Caliper = Lit($"{spec.Name}_Caliper", spec.Caliper, 0.55f),
                    Glass = Lit("Vehicle_Glass", new Color(0.035f, 0.045f, 0.055f), 0.94f, metallic: 0.2f),
                    Plastic = Lit("Vehicle_Plastic", new Color(0.045f, 0.045f, 0.05f), 0.32f),
                    Chrome = Lit("Vehicle_Chrome", new Color(0.78f, 0.78f, 0.8f), 0.86f, metallic: 1f),
                    Metal = Lit("Vehicle_Metal", new Color(0.16f, 0.16f, 0.17f), 0.5f, metallic: 0.7f),
                    Rubber = Lit("Vehicle_Rubber", new Color(0.035f, 0.035f, 0.037f), 0.22f),
                    Number = Lit("Vehicle_NumberPanel", new Color(0.93f, 0.93f, 0.9f), 0.5f),
                    HeadLight = Lit("Vehicle_HeadLight", new Color(0.92f, 0.92f, 0.88f), 0.92f, emission: new Color(1f, 0.93f, 0.78f) * 2.2f),
                    TailLight = Lit("Vehicle_TailLight", new Color(0.4f, 0.02f, 0.02f), 0.85f, emission: new Color(0.9f, 0.05f, 0.03f) * 0.8f),
                    ReverseLight = Lit("Vehicle_ReverseLight", new Color(0.75f, 0.75f, 0.72f), 0.85f, emission: Color.black),
                    NumberText = TextMaterial(font),
                    NumberFont = font,
                };
            }

            /// <summary>Glossy car paint: Complex Lit with a clear coat where available, otherwise Lit.</summary>
            private static Material PaintMaterial(string path, Color color)
            {
                var shader = Shader.Find("Universal Render Pipeline/Complex Lit") ?? GraphicsSettings.defaultRenderPipeline.defaultMaterial.shader;
                var material = EditorAssetUtility.LoadOrCreateLitMaterial(path, color, 0.6f);
                material.shader = shader;
                material.SetColor("_BaseColor", color);
                material.SetFloat("_Smoothness", 0.62f);
                material.SetFloat("_Metallic", 0.15f);
                material.SetFloat("_ClearCoat", 1f);
                material.SetFloat("_ClearCoatMask", 1f);
                material.SetFloat("_ClearCoatSmoothness", 0.93f);
                material.EnableKeyword("_CLEARCOAT");
                EditorUtility.SetDirty(material);
                return material;
            }

            private static Material Lit(string name, Color color, float smoothness, float metallic = 0f, Color? emission = null)
            {
                var material = EditorAssetUtility.LoadOrCreateLitMaterial($"{MaterialsFolder}/{name}.mat", color, smoothness);
                material.shader = GraphicsSettings.defaultRenderPipeline.defaultMaterial.shader;
                material.SetColor("_BaseColor", color);
                material.SetFloat("_Smoothness", smoothness);
                material.SetFloat("_Metallic", metallic);
                if (emission.HasValue)
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", emission.Value);
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                }

                EditorUtility.SetDirty(material);
                return material;
            }

            /// <summary>Door numbers, depth-tested like the track's banners.</summary>
            private static Material TextMaterial(Font font)
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(WorldTextShaderPath);
                var material = EditorAssetUtility.LoadOrCreate($"{MaterialsFolder}/Vehicle_NumberText.mat", () => new Material(shader));
                material.shader = shader;
                material.mainTexture = font.material.mainTexture;
                EditorUtility.SetDirty(material);
                return material;
            }
        }
    }
}
