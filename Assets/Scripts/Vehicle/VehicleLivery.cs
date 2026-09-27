using UnityEngine;

namespace RallyX.Vehicle
{
    /// <summary>
    /// The car's paintwork and door numbers. Tints the painted parts per car without creating new materials; the
    /// second livery colour (stripes, roof) stays as built.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleLivery : MonoBehaviour
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [Tooltip("Renderers whose base colour is the paint.")]
        [SerializeField] private Renderer[] paintedRenderers = System.Array.Empty<Renderer>();

        [Tooltip("The race number on each door.")]
        [SerializeField] private TextMesh[] numberTexts = System.Array.Empty<TextMesh>();

        private MaterialPropertyBlock block;

        /// <summary>The colour set with <see cref="SetPaint"/>, or null while the car has its material's own colour.</summary>
        public Color? Paint { get; private set; }

        /// <summary>The number on the doors, or empty when the car has none.</summary>
        public string RaceNumber => numberTexts.Length > 0 && numberTexts[0] != null ? numberTexts[0].text : string.Empty;

        public void SetPaintedRenderers(Renderer[] renderers) => paintedRenderers = renderers;

        public void SetNumberTexts(TextMesh[] texts) => numberTexts = texts ?? System.Array.Empty<TextMesh>();

        public void SetPaint(Color color)
        {
            Paint = color;
            block ??= new MaterialPropertyBlock();
            foreach (var paintedRenderer in paintedRenderers)
            {
                if (paintedRenderer == null)
                    continue;
                paintedRenderer.GetPropertyBlock(block);
                block.SetColor(BaseColor, color);
                paintedRenderer.SetPropertyBlock(block);
            }
        }

        /// <summary>Paints a race number on the doors (cars built without number panels ignore it).</summary>
        public void SetRaceNumber(int number)
        {
            foreach (var text in numberTexts)
            {
                if (text != null)
                    text.text = number.ToString();
            }
        }
    }
}
