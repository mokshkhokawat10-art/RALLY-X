using RallyX.Racing;
using UnityEngine;
using UnityEngine.UIElements;

namespace RallyX.UI
{
    /// <summary>
    /// The race HUD's minimap: the circuit's centreline, north up, with a dot for every racer (the player's larger and
    /// yellow, drawn on top). The outline is built once from the circuit's path; the dots move a few times a second, which is
    /// smooth enough at this size and keeps the redraw cheap.
    /// </summary>
    internal sealed class RaceMinimap
    {
        private const int OutlinePoints = 200;
        private const float RefreshInterval = 0.05f;

        private static readonly Color PlayerColor = new Color(1f, 0.77f, 0.14f);
        private static readonly Color RivalColor = new Color(0.92f, 0.94f, 0.97f);

        private readonly TrackMap map;
        private RaceCircuit builtFor;
        private Vector2 centre;
        private float size = 1f;
        private float refreshIn;

        public RaceMinimap(VisualElement holder)
        {
            map = new TrackMap { LineWidth = 4f, Padding = 0.06f };
            map.AddToClassList("minimap-drawing");
            holder.Add(map);
        }

        public void Update(RaceManager race, float deltaTime)
        {
            var circuit = race.Circuit;
            if (circuit == null || circuit.Path == null || circuit.Path.Count < 2)
                return;

            if (circuit != builtFor)
                Build(circuit);

            refreshIn -= deltaTime;
            if (refreshIn > 0f)
                return;

            refreshIn = RefreshInterval;
            map.ClearMarkers();
            RaceParticipant player = null;
            foreach (var participant in race.Participants)
            {
                if (participant == null)
                    continue;
                if (participant.IsPlayer)
                {
                    player = participant;
                    continue;
                }

                map.AddMarker(Place(participant), RivalColor, 5f);
            }

            if (player != null)
                map.AddMarker(Place(player), PlayerColor, 7.5f);
            map.Refresh();
        }

        private void Build(RaceCircuit circuit)
        {
            builtFor = circuit;
            var path = circuit.Path;
            int count = Mathf.Min(OutlinePoints, path.Count);
            var points = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                Vector3 point = path.GetPoint(Mathf.RoundToInt(i * path.Count / (float)count));
                points[i] = new Vector2(point.x, point.z);
            }

            map.SetOutline(TrackMap.Normalise(points, out centre, out size));
        }

        private Vector2 Place(RaceParticipant participant)
        {
            Vector3 position = participant.Vehicle.Body.position;
            return TrackMap.ToNormalised(new Vector2(position.x, position.z), centre, size);
        }
    }
}
