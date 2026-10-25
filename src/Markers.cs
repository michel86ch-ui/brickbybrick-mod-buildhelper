using System.Collections.Generic;
using MultiplayerTemplate.Game.Placement;
using UnityEngine;
using UnityEngine.Rendering;

namespace BuildHelper
{
    /// <summary>
    /// Coloured boxes in the shape of a brick place, drawn on top of everything (through walls). Uses the game's
    /// UI/Default shader, whose depth test follows unity_GUIZTestMode; set to Always, nothing hides it.
    /// </summary>
    internal static class Markers
    {
        internal static readonly Color Missing = new Color(1f, 0f, 1f, 0.85f);
        internal static readonly Color Beam = new Color(1f, 0f, 1f, 0.3f);
        internal static readonly Color Ghost = new Color(0f, 1f, 1f, 0.85f);

        private static readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
        private static Mesh box;
        private static bool failed;

        internal static bool Available => Box != null && !failed;

        private static Mesh Box
        {
            get
            {
                if (box != null)
                {
                    return box;
                }
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box = Object.Instantiate(cube.GetComponent<MeshFilter>().sharedMesh);
                box.name = "BuildHelper_Box";
                var white = new Color[box.vertexCount];
                for (int i = 0; i < white.Length; i++)
                {
                    white[i] = Color.white;
                }
                box.colors = white;
                Object.DestroyImmediate(cube);
                return box;
            }
        }

        private static Material For(Color color)
        {
            if (materials.TryGetValue(color, out Material material))
            {
                return material;
            }
            Shader shader = Shader.Find("UI/Default");
            if (shader == null)
            {
                failed = true;
                Plugin.Log.LogWarning("Shader UI/Default not found: markers cannot be drawn.");
                return null;
            }
            material = new Material(shader) { name = "BuildHelper_OnTop", color = color, renderQueue = 4000 };
            material.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            materials[color] = material;
            return material;
        }

        /// <summary>Boxes around the brick shape of a place (its hologram), or a small cube where it has none.</summary>
        internal static void Shape(PlacementSlot place, List<Matrix4x4> into)
        {
            bool any = false;
            if (place.HologramRoot != null)
            {
                foreach (MeshFilter filter in place.HologramRoot.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null)
                    {
                        continue;
                    }
                    Bounds b = filter.sharedMesh.bounds;
                    into.Add(filter.transform.localToWorldMatrix * Matrix4x4.TRS(b.center, Quaternion.identity, b.size * 1.04f));
                    any = true;
                }
            }
            if (!any)
            {
                into.Add(Matrix4x4.TRS(place.Position, place.Rotation, Vector3.one * 0.3f));
            }
        }

        /// <summary>A thin upright line from a place, so it can be found from afar.</summary>
        internal static Matrix4x4 BeamAt(PlacementSlot place, float height) =>
            Matrix4x4.TRS(place.Position + Vector3.up * (height / 2f), Quaternion.identity, new Vector3(0.06f, height, 0.06f));

        /// <summary>Draws the boxes this frame (call every frame).</summary>
        internal static void Draw(List<Matrix4x4> boxes, Color color)
        {
            Material material = boxes.Count > 0 ? For(color) : null;
            if (material == null)
            {
                return;
            }
            for (int i = 0; i < boxes.Count; i++)
            {
                Graphics.DrawMesh(Box, boxes[i], material, 0, null, 0, null, ShadowCastingMode.Off, false);
            }
        }
    }
}
