using SnakeReturns.Core;
using UnityEngine;

namespace SnakeReturns.Gameplay
{
    /// <summary>
    /// The arena frame: four scaled cubes around the play field, built once in Awake.
    /// </summary>
    /// <remarks>
    /// Static furniture. It reads the grid's dimensions once and never changes again — no
    /// per-tick work, no state, nothing to decide.
    /// </remarks>
    public class BorderView : MonoBehaviour
    {
        [SerializeField] private GridManager grid;

        [Header("Frame")]
        [Tooltip("One LCD pixel is 1/12 of a cell = 0.08333. Keep this a whole number of pixels, or the edge lands between two texels and the frame shimmers as the camera or the render target changes size.")]
        [SerializeField] private float thickness = 0.08333f;

        [Tooltip("0 frames the play field only (21x13), putting the frame between the board and the HUD strip the way Snake II did it. 2 frames the entire LCD instead.")]
        [SerializeField] private int hudRows = 0;

        [SerializeField] private Color colour = new Color(0.52f, 0.82f, 0.28f);

        private Transform container;
        private bool built;

        private void Reset()
        {
            AutoWire();
        }

        private void Awake()
        {
            AutoWire();
            Build();
        }

        private void AutoWire()
        {
            Wiring.Resolve(this, ref grid, nameof(grid));
        }

        private void Build()
        {
            if (grid == null || built)
            {
                return;
            }

            built = true;

            GameObject containerObject = new GameObject("~BorderView");
            container = containerObject.transform;
            container.SetParent(transform, false);

            // Layer is not inherited in Unity — a new GameObject lands on Default whatever its
            // parent is. Game_Cam renders only the Board layer, so without this the frame is
            // built perfectly and rendered by nobody.
            containerObject.layer = gameObject.layer;

            float left = -grid.Width * 0.5f;
            float right = grid.Width * 0.5f;
            float bottom = -grid.Height * 0.5f;
            float top = grid.Height * 0.5f + hudRows;

            float t = Mathf.Max(0.001f, thickness);

            float cx = (left + right) * 0.5f;
            float cy = (bottom + top) * 0.5f;
            float w = right - left;
            float h = top - bottom;

            // ALL FOUR BARS ARE DRAWN INWARD FROM THE BOUNDARY.
            // The play field touches the render target's edges exactly, so a border drawn
            // outward would sit outside the frustum and simply not appear — and it would look
            // like the border code never ran rather than like it ran and was cropped.
            Bar("Border_Top", cx, top - t * 0.5f, w, t);
            Bar("Border_Bottom", cx, bottom + t * 0.5f, w, t);
            Bar("Border_Left", left + t * 0.5f, cy, t, h);
            Bar("Border_Right", right - t * 0.5f, cy, t, h);
        }

        /// <summary>
        /// One bar. Horizontals span the full width and verticals the full height, so all four
        /// OVERLAP at the corners.
        /// </summary>
        /// <remarks>
        /// The overlap is deliberate. Mitring the corners — shortening the verticals by the
        /// thickness — leaves a half-texel notch at each one, because the join lands mid-pixel
        /// on a 72-pixel-per-cell target. Overlapping costs four hidden slivers of geometry
        /// and gives four clean corners.
        /// </remarks>
        private void Bar(string barName, float x, float y, float w, float h)
        {
            GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = barName;

            Renderer renderer = bar.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = UnlitMaterial(colour);
            }

            // No physics anywhere in this project.
            Collider collider = bar.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            bar.transform.SetParent(container, false);
            bar.transform.localPosition = new Vector3(x, y, 0f);
            bar.transform.localScale = new Vector3(w, h, 0.05f);
            bar.layer = container.gameObject.layer;
        }

        /// <summary>
        /// An unlit material of the given colour.
        /// </summary>
        /// <remarks>
        /// Yes, this is a copy of BoardView's helper. That is the point: BorderView and
        /// BoardView know nothing about each other by design, and neither should have to reach
        /// into the other or into a shared utility class to draw a coloured cube. Six
        /// duplicated lines are cheaper than a dependency between two views that have no
        /// reason to ever meet.
        /// </remarks>
        private static Material UnlitMaterial(Color c)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            Material material = new Material(shader);

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", c);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", c);
            }

            return material;
        }
    }
}
