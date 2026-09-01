using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SnakeReturns.Core
{
    /// <summary>
    /// One owner for every shadow setting in the project.
    /// </summary>
    /// <remarks>
    /// Unity splits shadow control across three unrelated places — the Light component, each
    /// Renderer's cast/receive flags, and the URP asset — and a visible shadow needs all three
    /// to agree. Tuning them from three different windows is how you end up convinced shadows
    /// are broken when one checkbox is off.
    ///
    /// FOUR FACTS THAT DECIDE THE NUMBERS BELOW. Do not "optimise" them without reading these.
    ///
    /// 1. URP CASTS SHADOWS FROM THE MAIN DIRECTIONAL LIGHT ONLY. Extra directional lights
    ///    light the scene and cast nothing at all. So ONE sun has to serve both the board
    ///    inside the LCD and the phone standing on the desk. That works here only because both
    ///    face the camera down -Z.
    ///
    /// 2. A SHADOW FURTHER FROM THE CAMERA THAN SHADOW DISTANCE IS CULLED, SILENTLY. The desk
    ///    backdrop sits about 41 units from Main_Cam, which is why shadowDistance is 70.
    ///    Shorten it and the phone's shadow disappears while the snake's survives — which
    ///    reads as a phone-specific bug, and is not one.
    ///
    /// 3. SHADOW OFFSET SCALES WITH THE CASTER-TO-RECEIVER GAP, and the desk gap is roughly
    ///    twenty times the board's. No single light angle gives a long board shadow AND a
    ///    tight desk one. So the angle stays shallow at (7, 7, 0) for a drop-shadow look on
    ///    the desk, and the board's shadow is bought back with BoardBackground.depth instead.
    ///    Game_Cam is orthographic, so moving that floor changes its apparent size and
    ///    position by exactly nothing — only the shadow moves. THAT is the knob, not the sun.
    ///
    /// 4. EDGE SOFTNESS COMES FROM SHADOWMAP RESOLUTION, NOT SOFT SHADOW QUALITY. URP blurs a
    ///    fixed number of texels, so the fade in world units is about shadowDistance divided
    ///    by resolution. 4096 over 70 units is a blunt 0.07-unit line at any quality setting;
    ///    512 gives about 0.55 units and actually fades. There is only ONE shadow map, so the
    ///    board and the desk soften together and cannot be tuned apart.
    /// </remarks>
    [ExecuteAlways]
    public class ShadowRig : MonoBehaviour
    {
        [Header("Sun")]
        [SerializeField] private Light sun;

        [Tooltip("Positive yaw throws the shadow right, positive pitch throws it down.")]
        [SerializeField] private Vector3 sunAngles = new Vector3(7f, 7f, 0f);

        [SerializeField] private Color sunColour = new Color(1f, 0.98f, 0.94f);

        [Range(0f, 4f)]
        [SerializeField] private float sunIntensity = 1.15f;

        [Header("Shadow quality")]
        [SerializeField] private LightShadows shadowType = LightShadows.Soft;

        [Range(0f, 1f)]
        [SerializeField] private float shadowStrength = 0.55f;

        [Range(0f, 3f)]
        [SerializeField] private float shadowBias = 0.35f;

        [Range(0f, 3f)]
        [SerializeField] private float shadowNormalBias = 0.6f;

        [Range(0.1f, 10f)]
        [SerializeField] private float shadowNearPlane = 0.2f;

        [Header("Pipeline — written to the URP asset")]
        [Range(10f, 200f)]
        [SerializeField] private float shadowDistance = 70f;

        [Range(1, 4)]
        [SerializeField] private int cascades = 4;

        [Range(0, 2)]
        [SerializeField] private int softness = 2;

        [SerializeField]
        private UnityEngine.Rendering.Universal.ShadowResolution shadowmapResolution
    = UnityEngine.Rendering.Universal.ShadowResolution._512;

        [Header("Receivers and casters")]
        [SerializeField] private Renderer[] receivers;
        [SerializeField] private Renderer[] nonCasters;

        [Header("Checks")]
        [SerializeField] private bool reportProblems = true;

        private void OnEnable()
        {
            Apply();
        }

        private void OnValidate()
        {
            Apply();
        }

        public void Apply()
        {
            ApplyLight();
            ApplyRenderers();
            ApplyPipeline();
        }

        /// <summary>A short description of what is wrong, or "" when healthy.</summary>
        public string Describe()
        {
            if (sun == null)
            {
                return "no sun";
            }

            if (sun.shadows == LightShadows.None)
            {
                return "sun shadows are off";
            }

            UniversalRenderPipelineAsset rp = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (rp == null)
            {
                return "no URP asset";
            }

            if (!rp.supportsMainLightShadows)
            {
                return "URP main light shadows disabled";
            }

            return string.Empty;
        }

        private void ApplyLight()
        {
            if (sun == null)
            {
                if (reportProblems)
                {
                    Debug.LogWarning("[ShadowRig] No sun assigned — nothing casts a shadow.", this);
                }
                return;
            }

            sun.type = LightType.Directional;
            sun.color = sunColour;
            sun.intensity = sunIntensity;
            sun.shadows = shadowType;
            sun.shadowStrength = shadowStrength;
            sun.shadowBias = shadowBias;
            sun.shadowNormalBias = shadowNormalBias;
            sun.shadowNearPlane = shadowNearPlane;

            sun.transform.rotation = Quaternion.Euler(sunAngles);
        }

        private void ApplyRenderers()
        {
            if (receivers != null)
            {
                foreach (Renderer r in receivers)
                {
                    if (r == null)
                    {
                        continue;
                    }

                    r.receiveShadows = true;

                    // An Unlit material has no lighting term, so it cannot receive a shadow —
                    // ticking Receive Shadows on an unlit renderer does nothing whatsoever.
                    // Carry the texture and colour across and move it to Lit.
                    Material material = r.sharedMaterial;
                    if (material == null || material.shader == null || !material.shader.name.Contains("Unlit"))
                    {
                        continue;
                    }

                    Shader lit = Shader.Find("Universal Render Pipeline/Lit");
                    if (lit == null)
                    {
                        continue;
                    }

                    Texture baseMap = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : null;
                    Color baseColour = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : Color.white;

                    material.shader = lit;

                    if (material.HasProperty("_BaseMap"))
                    {
                        material.SetTexture("_BaseMap", baseMap);
                    }
                    if (material.HasProperty("_BaseColor"))
                    {
                        material.SetColor("_BaseColor", baseColour);
                    }
                    if (material.HasProperty("_Smoothness"))
                    {
                        material.SetFloat("_Smoothness", 0f);
                    }
                    if (material.HasProperty("_Metallic"))
                    {
                        material.SetFloat("_Metallic", 0f);
                    }
                }
            }

            if (nonCasters == null)
            {
                return;
            }

            foreach (Renderer r in nonCasters)
            {
                if (r != null)
                {
                    r.shadowCastingMode = ShadowCastingMode.Off;
                }
            }
        }

        private void ApplyPipeline()
        {
            UniversalRenderPipelineAsset rp = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (rp == null)
            {
                if (reportProblems)
                {
                    Debug.LogWarning("[ShadowRig] No URP asset bound in Graphics Settings — pipeline shadow settings not applied.", this);
                }
                return;
            }

            rp.shadowDistance = shadowDistance;
            rp.shadowCascadeCount = cascades;

#if UNITY_EDITOR
            // These two have no public setter, so the serialized fields are the only way in.
            SerializedObject so = new SerializedObject(rp);

            // 0 means UsePipelineSettings; 1-3 are Low/Medium/High. Hence softness + 1.
            SerializedProperty quality = so.FindProperty("m_SoftShadowQuality");
            SerializedProperty resolution = so.FindProperty("m_MainLightShadowmapResolution");

            bool changed = false;

            if (quality != null && quality.intValue != softness + 1)
            {
                quality.intValue = softness + 1;
                changed = true;
            }

            if (resolution != null && resolution.intValue != (int)shadowmapResolution)
            {
                resolution.intValue = (int)shadowmapResolution;
                changed = true;
            }

            if (changed)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(rp);
            }
#endif
        }
    }
}
