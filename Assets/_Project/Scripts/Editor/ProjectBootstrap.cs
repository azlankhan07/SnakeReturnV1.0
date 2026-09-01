using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SnakeReturns.EditorTools
{
    /// <summary>
    /// One-click project setup: named layers, art import settings, Game view size, scene build.
    /// </summary>
    /// <remarks>
    /// Every step here is something you would otherwise do by hand in a settings window and
    /// then forget you did — which means the next person to clone the repo gets a subtly
    /// different project. Making them menu items makes the setup reproducible and reviewable.
    /// </remarks>
    public static class ProjectBootstrap
    {
        private const string ModelsFolder = "Assets/_Project/Art/Models";
        private const string TexturesFolder = "Assets/_Project/Art/Textures";

        private const int PhoneLayer = 6;
        private const int BoardLayer = 7;
        private const string PhoneLayerName = "Phone";
        private const string BoardLayerName = "Board";

        // Normal maps must be imported AS normal maps or Unity leaves them as plain colour
        // and the lighting reads the raw RGB as a direction.
        private static readonly HashSet<string> NormalMaps = new HashSet<string>
        {
            "Snake_Normal", "Watermelon_Normal"
        };

        // Linear DATA, not colour. Roughness and packed ORM channels are numbers the shader
        // reads directly — pushing them through the sRGB curve corrupts every value.
        private static readonly HashSet<string> LinearDataMaps = new HashSet<string>
        {
            "Snake_Roughness", "Watermelon_ORM"
        };

        [MenuItem("SnakeReturns/Setup/Create Layers")]
        public static void CreateLayers()
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0)
            {
                Debug.LogError("[ProjectBootstrap] Could not open ProjectSettings/TagManager.asset.");
                return;
            }

            SerializedObject tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");

            if (layers == null || !layers.isArray)
            {
                Debug.LogError("[ProjectBootstrap] TagManager has no 'layers' array. Unity's format has changed.");
                return;
            }

            List<string> changes = new List<string>();
            SetLayer(layers, PhoneLayer, PhoneLayerName, changes);
            SetLayer(layers, BoardLayer, BoardLayerName, changes);

            if (changes.Count == 0)
            {
                Debug.Log($"[ProjectBootstrap] Layers already correct: {PhoneLayer}={PhoneLayerName}, {BoardLayer}={BoardLayerName}.");
                return;
            }

            tagManager.ApplyModifiedProperties();

            // ApplyModifiedProperties only marks the asset dirty IN MEMORY. Unity does not
            // flush ProjectSettings/*.asset to disk until the project is saved, so without
            // these two lines the layers exist in this editor session, look completely
            // correct in the Tags and Layers window, and are simply gone after a restart.
            // Same class of bug as editing a scene in Play mode.
            AssetDatabase.SaveAssetIfDirty(assets[0]);
            EditorApplication.ExecuteMenuItem("File/Save Project");

            // Read the values back rather than trusting the write. A settings change that
            // silently did nothing is the hardest kind to notice.
            SerializedObject verify = new SerializedObject(assets[0]);
            SerializedProperty after = verify.FindProperty("layers");
            string phoneNow = after.GetArrayElementAtIndex(PhoneLayer).stringValue;
            string boardNow = after.GetArrayElementAtIndex(BoardLayer).stringValue;

            if (phoneNow != PhoneLayerName || boardNow != BoardLayerName)
            {
                Debug.LogError($"[ProjectBootstrap] Layer write did NOT persist. " +
                               $"Layer {PhoneLayer} is '{phoneNow}' (wanted '{PhoneLayerName}'), " +
                               $"layer {BoardLayer} is '{boardNow}' (wanted '{BoardLayerName}').");
                return;
            }

            Debug.Log($"[ProjectBootstrap] Layers updated and verified: {string.Join(", ", changes)}. " +
                      $"Layer {PhoneLayer}='{phoneNow}', layer {BoardLayer}='{boardNow}'.");
        }

        private static void SetLayer(SerializedProperty layers, int index, string name, List<string> changes)
        {
            if (index >= layers.arraySize)
            {
                Debug.LogError($"[ProjectBootstrap] Layer index {index} is out of range ({layers.arraySize} layers).");
                return;
            }

            SerializedProperty slot = layers.GetArrayElementAtIndex(index);
            if (slot.stringValue == name)
            {
                return;
            }

            string previous = string.IsNullOrEmpty(slot.stringValue) ? "(empty)" : slot.stringValue;
            slot.stringValue = name;
            changes.Add($"{index}: {previous} -> {name}");
        }

        [MenuItem("SnakeReturns/Setup/Apply Import Settings")]
        public static void ApplyImportSettings()
        {
            int models = ApplyModelSettings();
            int textures = ApplyTextureSettings();

            Debug.Log($"[ProjectBootstrap] Import settings applied to {models} model(s) and {textures} texture(s).");
        }

        private static int ApplyModelSettings()
        {
            string[] guids = AssetDatabase.FindAssets("t:Model", new[] { ModelsFolder });
            int count = 0;

            // A silent zero-result scan is the likeliest reason "it ran and nothing changed".
            if (guids.Length == 0)
            {
                Debug.LogError($"[ProjectBootstrap] No models found in '{ModelsFolder}'. " +
                               "Check the folder path — the scan found nothing to import.");
                return 0;
            }

            Debug.Log($"[ProjectBootstrap] Found {guids.Length} model(s) in {ModelsFolder}.");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null)
                {
                    continue;
                }

                // MikkTSpace tangents are required, not preferred: the normal maps were baked
                // against Mikk in the DCC tool, so any other tangent basis makes them sample
                // garbage — seams along every UV edge and lighting that swims as the mesh turns.
                importer.importTangents = ModelImporterTangents.CalculateMikk;
                importer.importNormals = ModelImporterNormals.Calculate;
                importer.weldVertices = true;

                // No physics anywhere in this project.
                importer.addCollider = false;

                // The scene owns its camera and its light. An FBX quietly dragging its own in
                // is how you end up with two MainCameras and no idea where the second came from.
                importer.importCameras = false;
                importer.importLights = false;

                importer.SaveAndReimport();
                count++;
            }

            return count;
        }

        private static int ApplyTextureSettings()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { TexturesFolder });
            int count = 0;

            if (guids.Length == 0)
            {
                Debug.LogError($"[ProjectBootstrap] No textures found in '{TexturesFolder}'. " +
                               "Check the folder path — the scan found nothing to import.");
                return 0;
            }

            Debug.Log($"[ProjectBootstrap] Found {guids.Length} texture(s) in {TexturesFolder}.");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    continue;
                }

                string name = System.IO.Path.GetFileNameWithoutExtension(path);

                if (NormalMaps.Contains(name))
                {
                    importer.textureType = TextureImporterType.NormalMap;
                }
                else
                {
                    importer.textureType = TextureImporterType.Default;

                    // Linear data stays linear; everything else is colour and wants sRGB.
                    importer.sRGBTexture = !LinearDataMaps.Contains(name);
                }

                // These textures are small and there are ten of them. Unity's default
                // compression puts visible blocking artefacts into large flat colour areas,
                // and flat colour areas are exactly what this art is made of — so the whole
                // set is worth far more uncompressed than the few megabytes it costs.
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.anisoLevel = 8;
                importer.filterMode = FilterMode.Trilinear;

                importer.SaveAndReimport();
                count++;

                Debug.Log($"[ProjectBootstrap]   {name}: type={importer.textureType}, " +
                          $"sRGB={importer.sRGBTexture}, aniso={importer.anisoLevel}, uncompressed.");
            }

            return count;
        }

        [MenuItem("SnakeReturns/Setup/Everything")]
        public static void Everything()
        {
            CreateLayers();
            ApplyImportSettings();
            GameViewSizeSetup.Apply();
            SnakeSceneBuilder.Build();

            Debug.Log("[ProjectBootstrap] Setup/Everything finished: layers, import settings, Game view size, scene build.");
        }
    }
}
