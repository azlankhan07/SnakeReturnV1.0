using System.Collections.Generic;
using UnityEngine;

namespace SnakeReturns.Core
{
    /// <summary>
    /// Owns the wiring between the game camera, the RenderTexture and the screen material.
    /// </summary>
    /// <remarks>
    /// It renders nothing itself. Its entire job is to make sure the same RenderTexture is on
    /// both ends of the link: set as the game camera's target, and set as the base map of the
    /// material on the phone's screen quad. Those two can drift apart trivially — someone
    /// duplicates the RT, or clears the camera's target — and the symptom is a black screen
    /// with no error anywhere. Owning both ends in one place makes that impossible.
    /// </remarks>
    [ExecuteAlways]
    public class LCDScreen : MonoBehaviour
    {
        [Tooltip("The board camera. Its Target Texture is set from here, so the two can never disagree.")]
        [SerializeField] private Camera gameCamera;

        [Tooltip("1512 x 1080, Point filter, no mip maps. Point matters: bilinear on a screen texture softens exactly the detail the high render target was raised to preserve.")]
        [SerializeField] private RenderTexture screenTexture;

        [Tooltip("The quad in the phone's screen recess.")]
        [SerializeField] private Renderer screenRenderer;

        [SerializeField] private string textureProperty = "_BaseMap";

        [SerializeField] private bool reportProblems = true;

        public RenderTexture ScreenTexture => screenTexture;

        // Reported once per enable, not once per frame. A missing reference in edit mode would
        // otherwise fill the Console with the same line thousands of times and bury the rest.
        private bool reported;

        private void OnEnable()
        {
            reported = false;
            Apply();
        }

        private void OnValidate()
        {
            Apply();
        }

        private void Apply()
        {
            if (reportProblems && !reported)
            {
                string missing = DescribeMissing();
                if (missing != null)
                {
                    Debug.LogWarning($"[LCDScreen] Not fully wired: {missing}.", this);
                }
                reported = true;
            }

            if (gameCamera != null && screenTexture != null)
            {
                gameCamera.targetTexture = screenTexture;
            }

            if (screenRenderer == null || screenTexture == null)
            {
                return;
            }

            // sharedMaterial outside play mode. Touching .material in the editor instantiates a
            // throwaway copy of the material every time the inspector revalidates, and those
            // copies leak into the scene file as "LCD_Screen (Instance)" assets nobody asked for.
            Material material = Application.isPlaying ? screenRenderer.material : screenRenderer.sharedMaterial;
            if (material == null)
            {
                return;
            }

            if (!material.HasProperty(textureProperty))
            {
                if (reportProblems)
                {
                    Debug.LogWarning(
                        $"[LCDScreen] Material '{material.name}' has no texture property '{textureProperty}'.",
                        this);
                }
                return;
            }

            material.SetTexture(textureProperty, screenTexture);
        }

        /// <summary>
        /// A short description of what is unwired, or null when everything is present.
        /// </summary>
        /// <remarks>
        /// Named pieces, not a NullReferenceException. "no RenderTexture assigned" tells you
        /// what to drag; a null ref tells you a line number in a file you did not write.
        /// </remarks>
        private string DescribeMissing()
        {
            List<string> missing = new List<string>(3);

            if (gameCamera == null)
            {
                missing.Add("no game camera");
            }
            if (screenTexture == null)
            {
                missing.Add("no RenderTexture assigned");
            }
            if (screenRenderer == null)
            {
                missing.Add("no screen renderer");
            }

            return missing.Count == 0 ? null : string.Join(", ", missing);
        }
    }
}
