using SnakeReturns.Core;
using SnakeReturns.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SnakeReturns.EditorTools
{
    /// <summary>
    /// Builds and rewires the whole game setup from a menu item.
    /// </summary>
    /// <remarks>
    /// WHY THIS EXISTS INSTEAD OF A HAND-AUTHORED .prefab OR A HAND-BUILT SCENE.
    /// A prefab asset is YAML full of GUIDs and fileIDs: unreadable, unmergeable, and
    /// impossible to review in a diff. You cannot tell from it whether GameManager's 'snake'
    /// field points at the right object — you can only open Unity and squint at the
    /// Inspector. A builder script is legible, re-runnable, and tells you what it did.
    /// When the scene gets tangled, run this again instead of rewiring by hand.
    ///
    /// It is idempotent: every step is create-or-find, so running it twice is harmless.
    /// </remarks>
    public static class SnakeSceneBuilder
    {
        private const string PrefabFolder = "Assets/_Project/Prefabs";
        private const string PrefabPath = PrefabFolder + "/Game.prefab";

        [MenuItem("SnakeReturns/Build Game Setup", false, 0)]
        public static void Build()
        {
            // a) Headers. These are organisational only — they exist so the Hierarchy reads
            //    as sections rather than a flat pile.
            Transform cameras = Header("--- CAMERAS ---");
            Transform managers = Header("--- MANAGERS ---");
            Transform gameRoot = Header("--- GAME ---");

            // b) One object, eight components.
            GameObject game = GameObject.Find("Game");
            if (game == null)
            {
                game = new GameObject("Game");
            }
            game.transform.SetParent(gameRoot, false);
            game.transform.localPosition = Vector3.zero;
            game.transform.localRotation = Quaternion.identity;
            game.transform.localScale = Vector3.one;

            // ALL EIGHT ON ONE OBJECT. Split them across several and every single reference
            // becomes a cross-object drag that somebody will forget on a rebuild.
            GridManager grid = Ensure<GridManager>(game);
            TickSystem tick = Ensure<TickSystem>(game);
            InputReader input = Ensure<InputReader>(game);
            SnakeController snake = Ensure<SnakeController>(game);
            FoodSpawner food = Ensure<FoodSpawner>(game);
            ScoreManager score = Ensure<ScoreManager>(game);
            BoardView view = Ensure<BoardView>(game);
            GameManager gm = Ensure<GameManager>(game);

            // c) Explicit wiring. Wiring.Resolve is the safety net; this is the plan.
            Wire(snake, ("grid", grid), ("input", input));
            Wire(food, ("grid", grid));
            Wire(view, ("grid", grid), ("snake", snake), ("food", food));
            Wire(gm,
                ("grid", grid), ("tick", tick), ("snake", snake), ("food", food),
                ("score", score), ("input", input), ("view", view));

            // d) Camera.
            GameObject camObject = GameObject.Find("MainCam");
            if (camObject == null)
            {
                // Adopt whatever camera the scene already has rather than adding a second one —
                // two enabled cameras rendering the same board is a confusing mess to debug.
                Camera existing = Object.FindAnyObjectByType<Camera>();
                camObject = existing != null ? existing.gameObject : new GameObject("MainCam");
                camObject.name = "MainCam";
            }

            camObject.transform.SetParent(cameras, false);
            camObject.tag = "MainCamera";

            Camera cam = Ensure<Camera>(camObject);
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.07f, 0.04f, 1f);
            cam.cullingMask = ~0;
            cam.nearClipPlane = 0.3f;
            camObject.transform.localPosition = new Vector3(0f, 1f, -10f);
            camObject.transform.localRotation = Quaternion.identity;
            camObject.transform.localScale = Vector3.one;

            CameraFramer framer = Ensure<CameraFramer>(camObject);
            Wire(framer, ("grid", grid));

            // e) Light. The greybox is unlit and does not need it, but authored meshes will.
            bool addedLight = false;
            if (Object.FindAnyObjectByType<Light>() == null)
            {
                GameObject lightObject = new GameObject("Directional Light");
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1f;
                lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                addedLight = true;
            }

            // f) Prefab.
            bool savedPrefab = false;
            if (AssetDatabase.IsValidFolder(PrefabFolder))
            {
                PrefabUtility.SaveAsPrefabAssetAndConnect(game, PrefabPath, InteractionMode.UserAction);
                savedPrefab = true;
            }
            else
            {
                Debug.LogWarning($"[SnakeSceneBuilder] {PrefabFolder} does not exist, so no prefab was saved. " +
                                 "Everything else was built. Create the folder and run this again.");
            }

            EditorUtility.SetDirty(game);
            Selection.activeGameObject = game;

            // g) Summary.
            Debug.Log($"[SnakeSceneBuilder] Wired 8 components on 'Game' under {gameRoot.name}, " +
                      $"camera 'MainCam' under {cameras.name} (framer wired), " +
                      $"managers header '{managers.name}' present, " +
                      $"light {(addedLight ? "created" : "already present")}, " +
                      $"prefab {(savedPrefab ? "saved to " + PrefabPath : "NOT saved")}.");
        }

        [MenuItem("SnakeReturns/Select Game Object", false, 20)]
        public static void SelectGame()
        {
            GameObject game = GameObject.Find("Game");
            if (game == null)
            {
                Debug.LogWarning("[SnakeSceneBuilder] No object named 'Game' in the open scene. Run Build Game Setup first.");
                return;
            }

            Selection.activeGameObject = game;
            EditorGUIUtility.PingObject(game);
        }

        /// <summary>
        /// Creates or finds a header object, then forces it to identity.
        /// </summary>
        /// <remarks>
        /// The un-parent and the zeroing are not tidiness. A header sitting at, say, y = 3
        /// silently adds that offset to everything beneath it, so the board renders three
        /// units off-centre while every cell coordinate in the logic is perfectly correct.
        /// That is a maddening bug to chase, and the fix is to make headers structurally
        /// incapable of carrying a transform.
        /// </remarks>
        private static Transform Header(string name)
        {
            GameObject header = GameObject.Find(name);
            if (header == null)
            {
                header = new GameObject(name);
            }

            header.transform.SetParent(null, false);
            header.transform.position = Vector3.zero;
            header.transform.rotation = Quaternion.identity;
            header.transform.localScale = Vector3.one;

            return header.transform;
        }

        private static T Ensure<T>(GameObject go) where T : Component
        {
            T component = go.GetComponent<T>();
            return component != null ? component : Undo.AddComponent<T>(go);
        }

        /// <summary>
        /// Assigns private [SerializeField] references.
        /// </summary>
        /// <remarks>
        /// SerializedObject is the only way in, and that is correct. The fields are private by
        /// design — that is the project's dependency convention. Making them public just so a
        /// build script could reach them would be the tail wagging the dog: weakening the
        /// runtime API to suit a piece of editor tooling.
        /// </remarks>
        private static void Wire(Component target, params (string field, Object value)[] links)
        {
            SerializedObject so = new SerializedObject(target);

            foreach ((string field, Object value) in links)
            {
                SerializedProperty property = so.FindProperty(field);
                if (property == null)
                {
                    Debug.LogError($"[SnakeSceneBuilder] {target.GetType().Name} has no serialized field '{field}'. " +
                                   "Was it renamed?", target);
                    continue; // Keep going — one bad name should not cost the other seven links.
                }

                property.objectReferenceValue = value;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // =================================================================================
        // RENDER PIPELINE
        //
        // Two cameras, two layers, one RenderTexture between them.
        //   Game_Cam  sees ONLY layer Board(7), renders into LCD_RT, and nobody watches it.
        //   Main_Cam  sees everything EXCEPT Board(7) — the phone, the backdrop — and that is
        //             what the player looks at.
        // The layer masks have to be watertight in BOTH directions. Miss one and you get the
        // signature failure of this setup: a 21-unit board of cubes floating in space next to
        // the phone, or a phone reflected inside its own screen.
        // =================================================================================

        private const string RenderTextureFolder = "Assets/_Project/RenderTextures";
        private const string RenderTexturePath = RenderTextureFolder + "/LCD_RT.asset";
        private const string MaterialFolder = "Assets/_Project/Art/Materials";
        private const string MaterialPath = MaterialFolder + "/LCD_Screen.mat";
        private const string PhoneModelPath = "Assets/_Project/Art/Models/Phone.fbx";
        private const string BackdropMaterialPath = MaterialFolder + "/Backdrop.mat";
        private const string BackgroundTexturePath = "Assets/_Project/Art/Images/BG.png";
        private const string ScreenShaderName = "SnakeReturns/LCDScreen";

        private const int BoardLayer = 7;
        private const int PhoneLayer = 6;

        // 1512 x 1080 = 1.4 = 21:15, matching the board's cell aspect exactly, at 72 render
        // pixels per cell. Any other aspect and the board is letterboxed inside its own screen.
        private const int RenderTextureWidth = 1512;
        private const int RenderTextureHeight = 1080;

        // 100 units from the board. The two never see each other — different layers — but a
        // 68-unit phone sitting on top of a 21-unit board makes the SCENE view unusable, and
        // the Scene view is where all the work happens.
        private static readonly Vector3 PhonePosition = new Vector3(100f, 0f, 0f);

        // Where the screen quad sits in the phone's local space. Main_Cam is framed so that a
        // 15-unit screen centred at y = 1 has room above it, so this is the y the framing
        // expects. The builder logs the phone's actual bounds — if the quad lands off the
        // recess, this is the single number to nudge.
        private const float ScreenCentreY = 1f;

        [MenuItem("SnakeReturns/Build Render Pipeline", false, 1)]
        public static void BuildRenderPipeline()
        {
            // The layer masks below are meaningless if the layers do not exist yet, and
            // LayerMask lookups on an unnamed layer fail silently rather than loudly.
            ProjectBootstrap.CreateLayers();

            Transform cameras = Header("--- CAMERAS ---");

            // a) The RenderTexture.
            bool createdRenderTexture = false;
            RenderTexture rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(RenderTexturePath);
            if (rt == null)
            {
                EnsureFolder(RenderTextureFolder);

                rt = new RenderTexture(RenderTextureWidth, RenderTextureHeight, 24)
                {
                    name = "LCD_RT",
                    filterMode = FilterMode.Point,
                    useMipMap = false,
                    autoGenerateMips = false,
                    anisoLevel = 0,
                    wrapMode = TextureWrapMode.Clamp
                };

                AssetDatabase.CreateAsset(rt, RenderTexturePath);
                createdRenderTexture = true;
            }

            // b) Everything that IS the game goes on the Board layer.
            GameObject game = GameObject.Find("Game");
            if (game == null)
            {
                Debug.LogError("[SnakeSceneBuilder] No 'Game' object in the scene. Run Build Game Setup first.");
                return;
            }
            SetLayerRecursively(game, BoardLayer);

            GridManager grid = game.GetComponent<GridManager>();

            // c) The board camera.
            Camera gameCam = FindCamera("Game_Cam") ?? FindCamera("MainCam") ?? FindAnyCameraExcept("Main_Cam");
            if (gameCam == null)
            {
                gameCam = new GameObject("Game_Cam").AddComponent<Camera>();
            }

            GameObject gameCamObject = gameCam.gameObject;
            gameCamObject.name = "Game_Cam";
            gameCamObject.transform.SetParent(cameras, false);

            // Main_Cam takes the MainCamera tag below. Two objects carrying it makes
            // Camera.main return whichever Unity happens to find first.
            gameCamObject.tag = "Untagged";

            gameCam.orthographic = true;
            gameCam.cullingMask = 1 << BoardLayer;   // ONLY the board
            gameCam.targetTexture = rt;
            gameCam.clearFlags = CameraClearFlags.SolidColor;
            gameCam.backgroundColor = new Color(0.05f, 0.09f, 0.05f, 1f);
            gameCam.depth = 0;

            // ANTI-ALIASING MUST BE OFF HERE. This camera draws into a fixed grid that the
            // shader then lays cell lines over. AA blends adjacent pixels, softening exactly
            // the cell edges the grid overlay is trying to sit on, and the two fight each
            // other into a shimmering mess. HDR is off for the same reason: this is a flat
            // unlit board, there is no bloom or exposure to preserve.
            gameCam.allowMSAA = false;
            gameCam.allowHDR = false;

            CameraFramer framer = Ensure<CameraFramer>(gameCamObject);
            if (grid != null)
            {
                Wire(framer, ("grid", grid));
            }

            // The AudioListener belongs on the camera the player is actually looking through.
            AudioListener strayListener = gameCamObject.GetComponent<AudioListener>();
            bool movedListener = false;
            if (strayListener != null)
            {
                Object.DestroyImmediate(strayListener);
                movedListener = true;
            }

            // d) The phone.
            bool createdPhone = false;
            GameObject phone = GameObject.Find("Phone");
            if (phone == null)
            {
                GameObject phoneAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PhoneModelPath);
                if (phoneAsset == null)
                {
                    Debug.LogError($"[SnakeSceneBuilder] {PhoneModelPath} not found. " +
                                   "Everything else was built; re-run once the model imports.");
                    return;
                }

                phone = (GameObject)PrefabUtility.InstantiatePrefab(phoneAsset);
                phone.name = "Phone";
                createdPhone = true;
            }

            phone.transform.SetParent(null, false);
            phone.transform.position = PhonePosition;
            phone.transform.rotation = Quaternion.identity;
            SetLayerRecursively(phone, PhoneLayer);

            Bounds phoneBounds = LocalBounds(phone, "Screen_LED");

            // e) The screen quad.
            //
            // A Unity Quad, not the FBX's own screen face. A Quad has clean 0-1 UVs and already
            // faces -Z, which is where Main_Cam is. An imported screen face almost always has
            // its UVs packed into the model's atlas, so the RenderTexture arrives rotated,
            // tiled, or mapped to a 40-pixel corner of the sheet — and every one of those looks
            // exactly like a bug in the shader, which is where you will waste the afternoon.
            Transform existingScreen = phone.transform.Find("Screen_LED");
            GameObject screen;
            if (existingScreen != null)
            {
                screen = existingScreen.gameObject;
            }
            else
            {
                screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
                screen.name = "Screen_LED";
                screen.transform.SetParent(phone.transform, false);
            }

            // CreatePrimitive adds a MeshCollider. No physics anywhere in this project.
            Collider screenCollider = screen.GetComponent<Collider>();
            if (screenCollider != null)
            {
                Object.DestroyImmediate(screenCollider);
            }

            screen.transform.localRotation = Quaternion.identity;
            screen.transform.localScale = new Vector3(21f, 15f, 1f);
            screen.transform.localPosition = new Vector3(
                phoneBounds.center.x,
                ScreenCentreY,
                phoneBounds.min.z - 0.05f);   // just proud of the front face, so it never z-fights
            SetLayerRecursively(screen, PhoneLayer);

            // f) The screen material.
            Material screenMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Shader screenShader = Shader.Find(ScreenShaderName);

            if (screenShader == null)
            {
                Debug.LogError($"[SnakeSceneBuilder] Shader '{ScreenShaderName}' not found. " +
                               "If it was just added, let Unity finish importing and run this again.");
            }
            else if (screenMaterial == null)
            {
                EnsureFolder(MaterialFolder);
                screenMaterial = new Material(screenShader) { name = "LCD_Screen" };
                AssetDatabase.CreateAsset(screenMaterial, MaterialPath);
            }
            else if (screenMaterial.shader != screenShader)
            {
                screenMaterial.shader = screenShader;
            }

            Renderer screenRenderer = screen.GetComponent<Renderer>();
            if (screenMaterial != null)
            {
                screenMaterial.SetTexture("_BaseMap", rt);
                EditorUtility.SetDirty(screenMaterial);
                screenRenderer.sharedMaterial = screenMaterial;
            }

            // g) The camera the player actually looks through.
            //
            // Orthographic size 11.25 shows 22.5 units of height, and at 16:9 that is 40 units
            // wide. The 15-unit screen therefore sits with real room above it and the phone's
            // lower body runs off the bottom of frame — which is the intended shot, not an
            // accident of framing.
            GameObject mainCamObject = GameObject.Find("Main_Cam");
            if (mainCamObject == null)
            {
                mainCamObject = new GameObject("Main_Cam");
            }
            mainCamObject.transform.SetParent(cameras, false);

            Camera mainCam = Ensure<Camera>(mainCamObject);
            mainCam.orthographic = true;
            mainCam.orthographicSize = 11.25f;
            mainCamObject.transform.localPosition = new Vector3(100f, 1f, -30f);
            mainCamObject.transform.localRotation = Quaternion.identity;
            mainCamObject.transform.localScale = Vector3.one;
            mainCam.cullingMask = ~(1 << BoardLayer);   // everything EXCEPT the board
            mainCam.depth = 10;
            mainCamObject.tag = "MainCamera";
            Ensure<AudioListener>(mainCamObject);

            // h) The link between camera, texture and material.
            LCDScreen lcd = Ensure<LCDScreen>(phone);
            Wire(lcd,
                ("gameCamera", gameCam),
                ("screenTexture", rt),
                ("screenRenderer", screenRenderer));


            // ---- Furniture: floor, frame, backdrop, shadows. -------------------------------
            BorderView border = Ensure<BorderView>(game);
            Wire(border, ("grid", grid));

            BoardBackground background = Ensure<BoardBackground>(game);
            Wire(background, ("grid", grid));

            // BG.png is 1484 x 1060 — exactly 7:5, the LCD's aspect — so it maps onto
            // Game_Cam's frustum one-to-one with no cropping. Without a texture assigned
            // BoardBackground disables its own renderer, and the floor step would do nothing.
            Texture2D backgroundTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(BackgroundTexturePath);
            if (backgroundTexture == null)
            {
                Debug.LogWarning($"[SnakeSceneBuilder] {BackgroundTexturePath} not found — the floor will stay hidden until a texture is assigned.");
            }
            Wire(background, ("backgroundTexture", backgroundTexture));

            // The board's shadow is bought with the floor's DEPTH, not the sun's angle — see
            // the four facts in ShadowRig. Game_Cam is orthographic, so moving the floor back
            // changes its apparent size and position by nothing; only the shadow moves.
            WireFloat(background, "depth", 3.5f);

            // The player's camera clears to near-black rather than the default blue.
            mainCam.clearFlags = CameraClearFlags.SolidColor;
            mainCam.backgroundColor = new Color(0.06f, 0.06f, 0.07f, 1f);

            // ---- The desk backdrop the phone stands against.
            // It follows the PHONE, not the world origin — the phone is parked at x = 100, and
            // a backdrop left at the origin would be 100 units away from the thing it backs.
            GameObject backdrop = GameObject.Find("Backdrop");
            if (backdrop == null)
            {
                backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
                backdrop.name = "Backdrop";
            }

            Collider backdropCollider = backdrop.GetComponent<Collider>();
            if (backdropCollider != null)
            {
                Object.DestroyImmediate(backdropCollider);
            }

            // Deliberately NOT parented to the phone, so the phone's transform stays free to
            // move, rotate or animate without dragging the whole set with it.
            backdrop.transform.SetParent(null, false);
            backdrop.transform.position = phone.transform.position + new Vector3(0f, 1f, 25f);
            backdrop.transform.rotation = Quaternion.identity;
            backdrop.transform.localScale = new Vector3(49.43f, 27.804375f, 1.2357501f);
            SetLayerRecursively(backdrop, PhoneLayer);

            MeshRenderer backdropRenderer = backdrop.GetComponent<MeshRenderer>();

            Material backdropMaterial = AssetDatabase.LoadAssetAtPath<Material>(BackdropMaterialPath);
            if (backdropMaterial == null)
            {
                Shader lit = Shader.Find("Universal Render Pipeline/Lit");
                if (lit != null)
                {
                    EnsureFolder(MaterialFolder);
                    backdropMaterial = new Material(lit) { name = "Backdrop" };
                    backdropMaterial.SetFloat("_Smoothness", 0f);
                    backdropMaterial.SetFloat("_Metallic", 0f);
                    AssetDatabase.CreateAsset(backdropMaterial, BackdropMaterialPath);
                }
            }

            if (backdropMaterial != null)
            {
                backdropRenderer.sharedMaterial = backdropMaterial;
            }

            backdropRenderer.shadowCastingMode = ShadowCastingMode.On;
            backdropRenderer.receiveShadows = true;

            // ---- One owner for the light, the renderer flags and the URP asset.
            ShadowRig rig = Ensure<ShadowRig>(cameras.gameObject);
            Light sun = Object.FindAnyObjectByType<Light>();
            if (sun == null)
            {
                Debug.LogWarning("[SnakeSceneBuilder] No directional light in the scene — ShadowRig has no sun to drive.");
            }

            Wire(rig, ("sun", sun));
            WireArray(rig, "receivers", new Object[] { backdropRenderer });

            // Screen_LED is a flat plane standing in front of the phone body. Left as a caster
            // it throws a hard rectangle across the desk that looks nothing like a phone.
            WireArray(rig, "nonCasters", new Object[] { screenRenderer });

            WireFloat(rig, "sunIntensity", 1.35f);
            WireFloat(rig, "shadowStrength", 0.78f);
            rig.Apply();

            EditorUtility.SetDirty(backdrop);
            EditorUtility.SetDirty(game);

            Debug.Log(
                $"[SnakeSceneBuilder] Furniture: BorderView + BoardBackground on 'Game' " +
                $"(texture {(backgroundTexture != null ? BackgroundTexturePath : "MISSING")}, depth 3.5), " +
                $"Main_Cam clears to solid near-black, " +
                $"Backdrop at {backdrop.transform.position} scale {backdrop.transform.localScale} " +
                $"({(backdropMaterial != null ? BackdropMaterialPath : "no material")}), " +
                $"ShadowRig on '{cameras.name}' with sun '{(sun != null ? sun.name : "none")}', " +
                $"1 receiver, 1 non-caster, intensity 1.35, strength 0.78. " +
                $"Health: {(string.IsNullOrEmpty(rig.Describe()) ? "ok" : rig.Describe())}");

            EditorUtility.SetDirty(phone);
            EditorUtility.SetDirty(gameCamObject);
            EditorUtility.SetDirty(mainCamObject);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkAllScenesDirty();

            // i) Summary.
            Debug.Log(
                $"[SnakeSceneBuilder] Render pipeline: LCD_RT {RenderTextureWidth}x{RenderTextureHeight} " +
                $"({(createdRenderTexture ? "created" : "reused")}), " +
                $"'Game' + children on layer {BoardLayer}, " +
                $"Game_Cam -> LCD_RT (mask = Board only, MSAA off, listener {(movedListener ? "moved to Main_Cam" : "already absent")}), " +
                $"Phone {(createdPhone ? "imported" : "found")} at {PhonePosition} on layer {PhoneLayer} " +
                $"(local bounds centre {phoneBounds.center}, size {phoneBounds.size}), " +
                $"Screen_LED at local {screen.transform.localPosition} scale {screen.transform.localScale}, " +
                $"material {(screenMaterial != null ? MaterialPath : "NOT created")}, " +
                $"Main_Cam ortho 11.25 at {mainCamObject.transform.position} (mask = all except Board).");
        }

        private static void WireFloat(Component target, string field, float value)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);

            if (property == null)
            {
                Debug.LogError($"[SnakeSceneBuilder] {target.GetType().Name} has no serialized field '{field}'. Was it renamed?", target);
                return;
            }

            property.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Fills a serialized array field. arraySize first, then each element by index.</summary>
        private static void WireArray(Component target, string field, Object[] values)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);

            if (property == null || !property.isArray)
            {
                Debug.LogError($"[SnakeSceneBuilder] {target.GetType().Name} has no serialized array '{field}'. Was it renamed?", target);
                return;
            }

            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Camera FindCamera(string name)
        {
            GameObject go = GameObject.Find(name);
            return go != null ? go.GetComponent<Camera>() : null;
        }

        private static Camera FindAnyCameraExcept(string excludedName)
        {
            Camera[] all = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Camera candidate in all)
            {
                if (candidate.gameObject.name != excludedName)
                {
                    return candidate;
                }
            }
            return null;
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;

            Transform t = go.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                SetLayerRecursively(t.GetChild(i).gameObject, layer);
            }
        }

        /// <summary>Renderer bounds of <paramref name="root"/> in its own local space, skipping one named child.</summary>
        private static Bounds LocalBounds(GameObject root, string skipName)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bool started = false;
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);

            foreach (Renderer r in renderers)
            {
                if (r.gameObject.name == skipName)
                {
                    continue;
                }

                Bounds world = r.bounds;
                Vector3 localMin = root.transform.InverseTransformPoint(world.min);
                Vector3 localMax = root.transform.InverseTransformPoint(world.max);

                if (!started)
                {
                    bounds = new Bounds(localMin, Vector3.zero);
                    started = true;
                }

                bounds.Encapsulate(localMin);
                bounds.Encapsulate(localMax);
            }

            return bounds;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string[] parts = folder.Split('/');
            string built = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string next = built + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(built, parts[i]);
                }
                built = next;
            }
        }

    }
}
