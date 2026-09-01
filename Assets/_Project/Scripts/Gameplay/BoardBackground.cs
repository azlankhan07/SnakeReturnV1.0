using SnakeReturns.Core;
using UnityEngine;

namespace SnakeReturns.Gameplay
{
    /// <summary>
    /// The arena floor: one textured quad filling Game_Cam's frustum, behind the pieces.
    /// </summary>
    /// <remarks>
    /// [ExecuteAlways] IS NOT DECORATION HERE. BoardView and BorderView build their geometry in
    /// Awake, so nothing at all exists in edit mode — and if the floor were the same, the
    /// phone's screen would be a flat clear colour until you pressed Play. The first time you
    /// meet that, it reads as a broken render pipeline, and you go looking for the bug in the
    /// camera or the RenderTexture. The floor existing in edit mode is what tells you at a
    /// glance that the pipeline is fine and the board is simply not running yet.
    /// </remarks>
    [ExecuteAlways]
    public class BoardBackground : MonoBehaviour
    {
        private const string QuadName = "~BoardBackground";

        [SerializeField] private GridManager grid;

        [Tooltip("Authored at the LCD's 7:5, so it maps one-to-one onto the frustum with no cropping.")]
        [SerializeField] private Texture2D backgroundTexture;

        [Tooltip("Must match CameraFramer's hudRows, or the floor will not line up with what the camera actually sees.")]
        [SerializeField] private int hudRows = 2;

        [Tooltip("Distance behind the board plane. Pieces sit within about 0.2 of z = 0.")]
        [SerializeField] private float depth = 1f;

        [SerializeField] private Color tint = Color.white;

        [Tooltip("An unlit material has no lighting term and therefore cannot receive a shadow at all — this switches the floor to a Lit shader.")]
        [SerializeField] private bool receiveShadows = true;

        [Range(0f, 1f)]
        [SerializeField] private float smoothness = 0f;

        private Transform quad;
        private Material mat;

        private void OnEnable()
        {
            AutoWire();
            Apply(true);
        }

        private void OnValidate()
        {
            AutoWire();

            // mayCreate: false. OnValidate MUST NOT create the quad.
            // Instantiating a GameObject during validation makes Unity fire OnDidAddComponent
            // and OnTransformParentChanged, which it explicitly forbids from inside OnValidate,
            // and the Console fills with SendMessage warnings on every single keystroke in the
            // Inspector. For the same reason this class never destroys anything either — a
            // create/destroy cycle per keystroke would leave orphaned quads behind.
            Apply(false);
        }

        private void AutoWire()
        {
            Wiring.Resolve(this, ref grid, nameof(grid));
        }

        private void Apply(bool mayCreate)
        {
            if (grid == null)
            {
                return;
            }

            if (quad == null)
            {
                Transform existing = transform.Find(QuadName);
                if (existing != null)
                {
                    quad = existing;
                }
                else if (mayCreate)
                {
                    quad = NewQuad();
                }
                else
                {
                    return;
                }
            }

            MeshRenderer r = quad.GetComponent<MeshRenderer>();
            if (r == null)
            {
                return;
            }

            Shader wanted = Shader.Find(receiveShadows
                ? "Universal Render Pipeline/Lit"
                : "Universal Render Pipeline/Unlit");

            if (wanted == null)
            {
                wanted = Shader.Find("Unlit/Texture");
            }

            // REBUILD the material when the shader changes rather than swapping .shader in
            // place. Assigning a new shader to an existing material keeps the old keyword set,
            // so a material that was Unlit and becomes Lit carries stale keywords and renders
            // black, or unlit, or ignores the shadow it was switched over to receive.
            if (mat == null || wanted == null || mat.shader != wanted)
            {
                mat = new Material(wanted)
                {
                    name = "BoardBackground",
                    hideFlags = HideFlags.DontSave
                };
            }

            if (mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", backgroundTexture);
            }
            if (mat.HasProperty("_MainTex"))
            {
                mat.SetTexture("_MainTex", backgroundTexture);
            }
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", tint);
            }
            if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", tint);
            }
            if (mat.HasProperty("_Smoothness"))
            {
                mat.SetFloat("_Smoothness", smoothness);
            }
            if (mat.HasProperty("_Metallic"))
            {
                mat.SetFloat("_Metallic", 0f);
            }

            r.sharedMaterial = mat;
            r.receiveShadows = receiveShadows;

            // The floor is the backmost thing in the board's world. Nothing is behind it for it
            // to cast onto, and a caster here would only cost shadowmap budget.
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // No texture, no floor — better a clear colour than a flat white slab.
            r.enabled = backgroundTexture != null;

            quad.localPosition = new Vector3(0f, hudRows * 0.5f, depth);
            quad.localRotation = Quaternion.identity;

            // Exactly Game_Cam's frustum: 21 x 15 with hudRows = 2, so the floor covers the
            // HUD strip as well as the play field.
            quad.localScale = new Vector3(grid.Width, grid.Height + hudRows, 1f);

            quad.gameObject.layer = gameObject.layer;
        }

        /// <summary>
        /// Builds the quad by hand.
        /// </summary>
        /// <remarks>
        /// Not GameObject.CreatePrimitive, which hands out a MeshCollider that would then need
        /// DestroyImmediate to remove — and destroying objects is exactly what this class must
        /// never do, because Apply() runs from OnValidate. Building the renderer directly means
        /// there is never anything to clean up.
        /// </remarks>
        private Transform NewQuad()
        {
            GameObject go = new GameObject(QuadName, typeof(MeshFilter), typeof(MeshRenderer))
            {
                hideFlags = HideFlags.DontSave
            };

            go.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            go.transform.SetParent(transform, false);
            go.layer = gameObject.layer;

            return go.transform;
        }
    }
}
