using UnityEditor;
using UnityEngine;

namespace RallyX.EditorTools
{
    /// <summary>Static, batched primitive shapes for generated scenery.</summary>
    internal static class EditorPrimitives
    {
        public static GameObject Box(string name, Transform parent, Vector3 position, Quaternion rotation, Vector3 size, Material material,
            bool collider)
        {
            var box = Create(PrimitiveType.Cube, name, parent, position, rotation, material, collider);
            box.transform.localScale = size;
            return box;
        }

        /// <summary>A quad is visible from its local -Z side.</summary>
        public static GameObject Quad(string name, Transform parent, Vector3 position, Quaternion rotation, Vector2 size, Material material)
        {
            var quad = Create(PrimitiveType.Quad, name, parent, position, rotation, material, collider: false);
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);
            return quad;
        }

        private static GameObject Create(PrimitiveType type, string name, Transform parent, Vector3 position, Quaternion rotation,
            Material material, bool collider)
        {
            var gameObject = GameObject.CreatePrimitive(type);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.SetPositionAndRotation(position, rotation);
            gameObject.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (!collider)
                Object.DestroyImmediate(gameObject.GetComponent<Collider>());
            GameObjectUtility.SetStaticEditorFlags(gameObject, StaticEditorFlags.BatchingStatic);
            return gameObject;
        }
    }
}
