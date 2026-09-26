using UnityEngine;

namespace RallyX.Vehicle
{
    /// <summary>The car's paintwork. Tints the painted parts per car without creating new materials.</summary>
    [DisallowMultipleComponent]
    public sealed class VehicleLivery : MonoBehaviour
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [Tooltip("Renderers whose base colour is the paint.")]
        [SerializeField] private Renderer[] paintedRenderers = System.Array.Empty<Renderer>();

        private MaterialPropertyBlock block;

        /// <summary>The colour set with <see cref="SetPaint"/>, or null while the car has its material's own colour.</summary>
        public Color? Paint { get; private set; }

        public void SetPaintedRenderers(Renderer[] renderers) => paintedRenderers = renderers;

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
    }
}
