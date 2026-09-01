using System.Collections.Generic;
using SnakeReturns.Core;
using UnityEngine;

namespace SnakeReturns.Gameplay
{
    /// <summary>
    /// Draws the board. Reads the logic, owns the transforms, decides nothing.
    /// </summary>
    /// <remarks>
    /// Render() is called once per tick AFTER the logic has advanced, and sets where every
    /// piece belongs. LateUpdate() then eases the head and tail toward those targets every
    /// frame. Neither one mutates game state, decides whether a move was legal, or runs a rule.
    ///
    /// THE LOGIC IS UNTOUCHED BY THE SMOOTHING. The snake still moves exactly one whole cell
    /// per tick and every collision test is still done on integer cells. Only the drawing is
    /// interpolated.
    /// </remarks>
    public class BoardView : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private GridManager grid;
        [SerializeField] private SnakeController snake;
        [SerializeField] private FoodSpawner food;

        [Header("Meshes — leave empty for greybox cubes")]
        [SerializeField] private GameObject headPrefab;
        [SerializeField] private GameObject bodyPrefab;
        [SerializeField] private GameObject tailPrefab;
        [SerializeField] private GameObject foodPrefab;
        [SerializeField] private GameObject bigFoodPrefab;

        [Tooltip("Left/right is the direction of the TURN, not of travel. Leave both empty and corners fall back to the straight body mesh.")]
        [SerializeField] private GameObject cornerLeftPrefab;
        [SerializeField] private GameObject cornerRightPrefab;

        [Header("Mesh facing — degrees added on top of the travel direction")]
        // Rotation corrections live HERE and never in the prefabs. Render() writes .rotation
        // as an ABSOLUTE world rotation on every piece every tick, so a rotation baked into a
        // prefab root is not merely redundant — it is erased on the first frame and the only
        // thing it can still do is mislead whoever reads the prefab next.
        [SerializeField] private float headYaw = 0f;
        [SerializeField] private float bodyYaw = 0f;
        [SerializeField] private float tailYaw = 0f;

        // The elbows open onto +X and, for the second mouth, +Y (left) or -Y (right). Combined
        // with meshRoll below, 180 swings each one onto the pair of cell faces its turn needs.
        // These REPLACE bodyYaw for corner cells rather than adding to it: they correct a
        // different mesh, so a body correction has no business leaking into them.
        [SerializeField] private float cornerLeftYaw = 180f;
        [SerializeField] private float cornerRightYaw = 180f;

        [Header("Mesh roll — spin about the direction of travel")]
        // THE WHOLE SNAKE IS AUTHORED BACK-UP TOWARDS +Z AND THE CAMERA SITS AT -Z LOOKING BACK
        // ALONG +Z, so every piece presents its underside to the board. On the head that hides
        // the eyes, pupils and nostrils behind an opaque skull; on the body, tail and elbows it
        // is subtler and only reads as the scales looking flat and washed out, because the
        // belly is modelled and textured with far less relief than the back.
        //
        // This is a roll about the travel axis, NOT a yaw, which is why no amount of headYaw or
        // bodyYaw could ever express it. One value for every piece: they come from one model
        // set and share one convention, so a second knob would only ever be set to the same
        // number or be wrong.
        [SerializeField] private float meshRoll = 180f;

        [Header("Motion")]
        [Tooltip("The LOGIC still moves a whole cell per tick and is untouched by this; only the drawing is interpolated.")]
        [SerializeField] private bool smoothMovement = true;

        [Tooltip("Needed for the step fraction — without it the board still draws, it just snaps.")]
        [SerializeField] private TickSystem tick;

        [Header("Greybox sizes — ignored once a prefab is assigned")]
        [Tooltip("0.8 in a 1.0 cell. The 0.2 gap is what makes the snake read as separate segments rather than one solid bar — and it is the ONLY cue for cell boundaries, because the grid is never drawn to the player.")]
        [SerializeField] private float segmentSize = 0.8f;
        [SerializeField] private float foodSize = 0.8f;
        [SerializeField] private float bigFoodSize = 1.5f;

        [Header("Greybox colours — ignored once prefabs are assigned")]
        [SerializeField] private Color headColour = new Color(0.80f, 1.00f, 0.45f);
        [SerializeField] private Color bodyColour = new Color(0.52f, 0.82f, 0.28f);
        [SerializeField] private Color foodColour = new Color(1.00f, 0.93f, 0.40f);
        [SerializeField] private Color bigFoodColour = new Color(1.00f, 0.52f, 0.18f);

        private Transform container;
        private Transform headView;
        private Transform tailView;
        private Transform foodView;
        private Transform bigFoodView;

        // THE ROLL IS NOT FREE: it spins a piece about its transform origin, and only the head
        // is modelled centred on that origin. The body, tail and elbows all sit in z 0.0 to 0.4,
        // so rolling them about z=0 would swing them to -0.4..0.0 — a full 0.4 nearer the
        // camera than they were, and 0.175 in front of the head, which would then be drawn
        // over by its own neck. These offsets put each piece back in the slice of z it was
        // authored to occupy. Measured from the meshes rather than typed in, so a re-export
        // that changes the pivot corrects itself instead of silently drifting.
        private float headDepthFix;
        private float tailDepthFix;
        private float bodyDepthFix;
        private float cornerLeftDepthFix;
        private float cornerRightDepthFix;

        // THREE PARALLEL POOLS, not one pool with a swapped mesh.
        // Swapping a MeshFilter's mesh at runtime is a per-tick allocation in all but name —
        // it dirties the renderer's batching state and churns exactly on the game's cadence,
        // which is the pattern that produces rhythmic GC hitches in a game whose whole feel
        // is an even tempo. Three pools cost objects, which are cheap and built once.
        // 4 + 3 x 273 = 823 objects, built in Awake and never grown.
        private readonly List<Transform> bodyPool = new List<Transform>(273);
        private readonly List<Transform> cornerLeftPool = new List<Transform>(273);
        private readonly List<Transform> cornerRightPool = new List<Transform>(273);

        // One material per distinct colour, shared across every object using it. Without this
        // the pools would carry hundreds of identical materials and as many draw-call setups.
        private readonly Dictionary<Color, Material> materialCache = new Dictionary<Color, Material>();

        /// <summary>Where a piece is sliding from and to across the current tick.</summary>
        private struct Motion
        {
            public Vector3 fromPos;
            public Vector3 toPos;
            public Quaternion fromRot;
            public Quaternion toRot;
        }

        private Motion headMotion;
        private Motion tailMotion;

        // True until the first Render after a reset or an unpause, so pieces appear where they
        // belong instead of sliding in from wherever they were standing before.
        private bool snapNext = true;

        // The head and tail each share a cell with a body segment for part of every tick — the
        // head with the cell it just left, the tail with the cell it is sliding onto — and two
        // identical opaque meshes at the same z fight into a flickering mess. The camera is
        // orthographic, so pulling the moving pieces 0.02 toward it costs no perspective shift
        // whatsoever; it only breaks the tie.
        private const float MovingPieceDepth = -0.02f;

        private void Reset()
        {
            AutoWire();
        }

        private void Awake()
        {
            AutoWire();

            if (grid == null)
            {
                // Without the grid there is no CellCount to size the pools and no CellToWorld to
                // place anything, so build nothing rather than half a view. Render() no-ops.
                return;
            }

            GameObject containerObject = new GameObject("~BoardView");
            container = containerObject.transform;
            container.SetParent(transform, false);

            // THE POOL MUST INHERIT THIS OBJECT'S LAYER.
            // Game_Cam renders only the Board layer. A new GameObject() is created on layer 0
            // whatever its parent is, so without this the entire snake would be built on
            // Default, the board camera would render an empty RenderTexture, and the phone
            // screen would be a flat colour with no error anywhere to explain it.
            containerObject.layer = gameObject.layer;

            headView = Create("Head", headPrefab, segmentSize, headColour);
            tailView = Create("Tail", tailPrefab, segmentSize, bodyColour);
            foodView = Create("Food", foodPrefab, foodSize, foodColour);
            bigFoodView = Create("BigFood", bigFoodPrefab, bigFoodSize, bigFoodColour);

            // A null corner prefab falls back to the straight body mesh, so corners degrade to
            // a rotated straight segment rather than vanishing and leaving a hole in the snake.
            GameObject leftMesh = cornerLeftPrefab != null ? cornerLeftPrefab : bodyPrefab;
            GameObject rightMesh = cornerRightPrefab != null ? cornerRightPrefab : bodyPrefab;

            for (int i = 0; i < grid.CellCount; i++)
            {
                bodyPool.Add(Create("Body_" + i, bodyPrefab, segmentSize, bodyColour));
                cornerLeftPool.Add(Create("CornerL_" + i, leftMesh, segmentSize, bodyColour));
                cornerRightPool.Add(Create("CornerR_" + i, rightMesh, segmentSize, bodyColour));
            }

            // Measured once, off one built piece of each kind, while every piece is still
            // sitting unrotated at the container's origin — which is the only moment the
            // meshes are guaranteed to be in their authored pose.
            headDepthFix = RollDepthFix(headView);
            tailDepthFix = RollDepthFix(tailView);
            bodyDepthFix = RollDepthFix(bodyPool[0]);
            cornerLeftDepthFix = RollDepthFix(cornerLeftPool[0]);
            cornerRightDepthFix = RollDepthFix(cornerRightPool[0]);
        }

        /// <summary>
        /// The z shift that returns a piece to its authored depth after a 180-degree roll.
        /// </summary>
        /// <remarks>
        /// A roll about the travel axis negates z, so a piece spanning [min, max] lands on
        /// [-max, -min]. Adding min + max — twice the depth centre — slides it back. A piece
        /// already centred on its origin, as the head is, measures zero and is left alone.
        /// </remarks>
        private static float RollDepthFix(Transform piece)
        {
            if (piece == null)
            {
                return 0f;
            }

            float min = float.MaxValue;
            float max = float.MinValue;

            MeshFilter[] filters = piece.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                // The mesh's own AABB is enough: the piece is unrotated here, so the box's z
                // range is the geometry's z range, and eight corners beat thousands of verts.
                Bounds b = mesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = b.center + new Vector3(
                        ((c & 1) == 0 ? -1f : 1f) * b.extents.x,
                        ((c & 2) == 0 ? -1f : 1f) * b.extents.y,
                        ((c & 4) == 0 ? -1f : 1f) * b.extents.z);

                    float z = filters[i].transform.TransformPoint(corner).z - piece.position.z;
                    if (z < min) min = z;
                    if (z > max) max = z;
                }
            }

            return min > max ? 0f : min + max;
        }

        private void AutoWire()
        {
            Wiring.Resolve(this, ref grid, nameof(grid));
            Wiring.Resolve(this, ref snake, nameof(snake));
            Wiring.Resolve(this, ref food, nameof(food));
            Wiring.Resolve(this, ref tick, nameof(tick));
        }

        private Transform Create(string pieceName, GameObject prefab, float size, Color colour)
        {
            GameObject instance;

            if (prefab != null)
            {
                instance = Instantiate(prefab);
            }
            else
            {
                instance = GameObject.CreatePrimitive(PrimitiveType.Cube);

                // CreatePrimitive hands out the built-in Standard material, which URP cannot
                // render. That is the classic "my cubes are magenta, or black, or just gone"
                // trap — so the greybox assigns its own explicit unlit material instead.
                Renderer renderer = instance.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = GetMaterial(colour);
                }
            }

            // No physics anywhere in this project. Colliders on 823 objects would cost
            // broadphase work every frame in exchange for nothing at all.
            Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Destroy(colliders[i]);
            }

            instance.name = pieceName;
            instance.transform.SetParent(container, false);
            instance.SetActive(false);

            // Same reason as the container: primitives and prefab instances both arrive on
            // their own layer, not their parent's.
            SetLayerRecursively(instance, container.gameObject.layer);

            // Scale ONLY the fallback primitive. CreatePrimitive gives a 1-unit cube that has
            // to come down to 0.8, but an authored mesh is already the right size as exported
            // from Blender — rescaling it here would apply 0.8 a second time and silently give
            // a 0.64 segment.
            // Prefab ROTATION is not preserved any more: Render() now writes .rotation on every
            // piece, because the meshes are 1.0 unit and seamless and must line up across the
            // join. A mesh whose axis points the wrong way is corrected with headYaw/bodyYaw/
            // tailYaw above, NOT by rotating the prefab.
            if (prefab == null)
            {
                instance.transform.localScale = Vector3.one * size;
            }

            return instance.transform;
        }

        private Material GetMaterial(Color colour)
        {
            if (materialCache.TryGetValue(colour, out Material cached))
            {
                return cached;
            }

            Material material = UnlitMaterial(colour);
            materialCache[colour] = material;
            return material;
        }

        /// <summary>
        /// An unlit material of the given colour, whatever shaders this project happens to have.
        /// </summary>
        /// <remarks>
        /// Unlit is deliberate: it means the board can never go invisible because a scene is
        /// missing a light. Both _BaseColor and _Color are set when present — URP's unlit
        /// shader reads the first, the older built-in unlit shaders read the second.
        /// </remarks>
        private static Material UnlitMaterial(Color colour)
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
                material.SetColor("_BaseColor", colour);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", colour);
            }

            return material;
        }

        // -----------------------------------------------------------------------------
        // Heading
        // -----------------------------------------------------------------------------

        /// <summary>The one-cell step from <paramref name="from"/> to <paramref name="to"/>.</summary>
        /// <remarks>
        /// Adjacent cells differ by exactly 1 on one axis — EXCEPT across a wrap, where the raw
        /// difference is the full width or height with the sign inverted. Leave that unhandled
        /// and the segments either side of an edge crossing spin to face the long way across
        /// the board for one tick. wrapAround ships ON, so this is the common path, not an
        /// edge case.
        /// </remarks>
        private Vector2Int StepDelta(Vector2Int from, Vector2Int to)
        {
            int dx = to.x - from.x;
            int dy = to.y - from.y;

            if (dx > 1) dx -= grid.Width; else if (dx < -1) dx += grid.Width;
            if (dy > 1) dy -= grid.Height; else if (dy < -1) dy += grid.Height;

            return new Vector2Int(dx, dy);
        }

        /// <summary>The direction the snake was travelling through the cell at <paramref name="i"/>.</summary>
        private Vector2Int HeadingAt(IReadOnlyList<Vector2Int> body, int i)
        {
            // A one-cell snake has no neighbour to derive a heading from, so hold the default
            // rather than snapping to something arbitrary.
            if (body.Count < 2)
            {
                return Vector2Int.right;
            }

            if (i + 1 == body.Count)
            {
                // The tail: point at the segment ahead of it.
                return StepDelta(body[i], body[i - 1]);
            }

            // Everything else: from the segment behind, pointing at itself — which is the
            // direction the snake moved to arrive here.
            return StepDelta(body[i + 1], body[i]);
        }

        /// <summary>
        /// A heading as a rotation, plus a per-mesh correction.
        /// </summary>
        /// <remarks>
        /// The board is the XY plane with the camera on -Z, so a heading is a spin about Z and
        /// nothing else. Segments are 1.0 unit and seamless, so every piece is rotated to face
        /// the direction the snake travelled through its cell — without that the meshes simply
        /// do not line up across the join.
        /// </remarks>
        /// <summary>
        /// The shared model-space roll that turns the snake's back towards the camera.
        /// </summary>
        /// <remarks>
        /// ALWAYS COMPOSE THIS ON THE RIGHT OF A YAW. On the right it is applied in the mesh's
        /// own space first, and the yaw then swings the already-righted piece to face the way
        /// it is travelling. On the left it would tumble the piece about a world axis instead,
        /// so a snake running north would be belly-up and one running south back-up.
        /// </remarks>
        private Quaternion Roll => Quaternion.Euler(meshRoll, 0f, 0f);

        /// <summary>The same point, pushed along z by a piece's roll compensation.</summary>
        private static Vector3 AtDepth(Vector3 position, float depthFix)
        {
            position.z += depthFix;
            return position;
        }

        private static Quaternion Yaw(Vector2Int dir, float offsetDegrees)
        {
            if (dir == Vector2Int.zero)
            {
                dir = Vector2Int.right;
            }

            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            return Quaternion.Euler(0f, 0f, angle + offsetDegrees);
        }

        // -----------------------------------------------------------------------------
        // Drawing
        // -----------------------------------------------------------------------------

        /// <summary>Sets every piece's target for this tick. Called once per tick.</summary>
        public void Render()
        {
            if (container == null || grid == null || snake == null || food == null)
            {
                return;
            }

            IReadOnlyList<Vector2Int> body = snake.Body;
            int count = body.Count;
            if (count == 0)
            {
                Hide();
                return;
            }

            SetTarget(ref headMotion,
                AtDepth(grid.CellToWorld(body[0]), headDepthFix),
                Yaw(HeadingAt(body, 0), headYaw) * Roll);
            SetActive(headView, true);

            // A one-cell snake is all head, so there is no separate tail piece to show.
            bool hasTail = count > 1;
            if (hasTail)
            {
                SetTarget(ref tailMotion,
                    AtDepth(grid.CellToWorld(body[count - 1]), tailDepthFix),
                    Yaw(HeadingAt(body, count - 1), tailYaw) * Roll);
            }
            SetActive(tailView, hasTail);

            // count - 1, NOT count - 2.
            // The tail spends the whole tick sliding out of the cell it is vacating and only
            // lands on its new cell as the tick completes. Stopping one short leaves that cell
            // belonging to nothing for almost the entire step — a one-cell hole just behind the
            // snake that closes and reopens every tick, which reads as a flickering gap.
            int middle = Mathf.Max(0, count - 1);

            for (int i = 0; i < bodyPool.Count; i++)
            {
                if (i >= middle)
                {
                    SetActive(bodyPool[i], false);
                    SetActive(cornerLeftPool[i], false);
                    SetActive(cornerRightPool[i], false);
                    continue;
                }

                int cell = i + 1;

                Vector2Int outDir = StepDelta(body[cell], body[cell - 1]);
                Vector2Int inDir = cell + 1 < count ? StepDelta(body[cell + 1], body[cell]) : outDir;

                // CORNER CELLS GET AN ELBOW MESH, NOT A ROTATED STRAIGHT ONE. A straight segment
                // turned 90 degrees leaves a hard outer edge where the two runs meet. Which
                // elbow is the sign of the 2D cross product: positive turns left, negative
                // right.
                //
                // The elbows do NOT share the straight mesh's rotation. They are a mirrored
                // pair authored opening onto +X, not -X, so each needs its own quarter turn in
                // its own direction — see cornerLeftYaw / cornerRightYaw. Give them the body's
                // rotation and the openings land on the wrong two cell faces, which tears the
                // elbow away from the runs either side of it and leaves the smooth outer edge
                // of the bend pointing into the turn instead of out of it.
                int cross = inDir.x * outDir.y - inDir.y * outDir.x;
                bool isCorner = inDir != outDir && cross != 0;

                Transform use;
                float yaw;
                float depthFix;
                if (!isCorner)
                {
                    use = bodyPool[i];
                    yaw = bodyYaw;
                    depthFix = bodyDepthFix;
                }
                else if (cross > 0)
                {
                    use = cornerLeftPool[i];
                    yaw = cornerLeftYaw;
                    depthFix = cornerLeftDepthFix;
                }
                else
                {
                    use = cornerRightPool[i];
                    yaw = cornerRightYaw;
                    depthFix = cornerRightDepthFix;
                }

                // BODY SEGMENTS NEVER SLIDE — they are set directly, not through a Motion.
                // Every cell between the head and the tail is occupied both before and after
                // the tick, so a still body is what a grid snake actually looks like. Sliding
                // them is wrong twice over: the whole body appears to crawl, and it drags the
                // elbows off their cells — an elbow fills ONE cell in ONE orientation, so half
                // a cell of drift visibly tears the L apart.
                use.position = AtDepth(grid.CellToWorld(body[cell]), depthFix);
                use.rotation = Yaw(inDir, yaw) * Roll;

                // Written out rather than looped over a temporary array: new[] { ... } here
                // would allocate on a fixed cadence, and allocations on a fixed cadence are
                // exactly how a game whose feel depends on an even tempo gets periodic GC
                // hitches.
                SetActive(bodyPool[i], use == bodyPool[i]);
                SetActive(cornerLeftPool[i], use == cornerLeftPool[i]);
                SetActive(cornerRightPool[i], use == cornerRightPool[i]);
            }

            // Food does not slide. It teleports when it respawns, and easing that reads as the
            // apple crawling across the board to its new cell.
            foodView.position = grid.CellToWorld(food.NormalFood);
            SetActive(foodView, true);

            // The BLINK IS A PROPERTY OF THE VIEW. The spawner keeps counting its lifetime
            // down regardless of what is drawn; all the view does is decide whether to show
            // the piece this tick. The food is exactly as edible while it is invisible.
            bool showBig = food.BigFood.HasValue && food.BigFoodVisible;
            if (showBig)
            {
                bigFoodView.position = grid.CellToWorld(food.BigFood.Value);
            }
            SetActive(bigFoodView, showBig);

            snapNext = false;
        }

        /// <summary>Deactivates every piece. Used by the paused screen.</summary>
        public void Hide()
        {
            if (container == null)
            {
                return;
            }

            SetActive(headView, false);
            SetActive(tailView, false);
            SetActive(foodView, false);
            SetActive(bigFoodView, false);

            for (int i = 0; i < bodyPool.Count; i++)
            {
                SetActive(bodyPool[i], false);
                SetActive(cornerLeftPool[i], false);
                SetActive(cornerRightPool[i], false);
            }

            // Whatever comes back after a pause appears where it belongs, rather than sliding
            // in from the pose it was holding when the board was hidden.
            snapNext = true;
        }

        // -----------------------------------------------------------------------------
        // Motion
        // -----------------------------------------------------------------------------

        /// <summary>
        /// Points a piece at a new pose. The PREVIOUS target becomes the new start, so a slide
        /// always begins exactly where the last one ended and no position is ever skipped.
        /// </summary>
        private void SetTarget(ref Motion m, Vector3 pos, Quaternion rot)
        {
            if (snapNext)
            {
                m.fromPos = pos;
                m.toPos = pos;
                m.fromRot = rot;
                m.toRot = rot;
                return;
            }

            m.fromPos = m.toPos;
            m.fromRot = m.toRot;
            m.toPos = pos;
            m.toRot = rot;

            // A slide longer than 1.5 units is a wrap. Interpolating it would send the head
            // gliding all the way back across the board instead of reappearing on the far edge.
            if ((m.toPos - m.fromPos).sqrMagnitude > 2.25f)
            {
                m.fromPos = m.toPos;
                m.fromRot = m.toRot;
            }
        }

        /// <summary>
        /// The one place the board moves outside a tick — and it moves only the drawing.
        /// </summary>
        private void LateUpdate()
        {
            if (container == null)
            {
                return;
            }

            // Without a TickSystem there is no step fraction to interpolate against, so the
            // board still draws — it just snaps, exactly as it did before.
            float t = 1f;
            if (smoothMovement && tick != null && tick.IsRunning)
            {
                t = tick.StepProgress;
            }

            if (headView != null && headView.gameObject.activeSelf)
            {
                Apply(headView, headMotion, t);
            }

            if (tailView != null && tailView.gameObject.activeSelf)
            {
                Apply(tailView, tailMotion, t);
            }
        }

        private static void Apply(Transform piece, Motion m, float t)
        {
            Vector3 position = Vector3.LerpUnclamped(m.fromPos, m.toPos, t);
            position.z += MovingPieceDepth;

            piece.position = position;
            piece.rotation = Quaternion.SlerpUnclamped(m.fromRot, m.toRot, t);
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

        // SetActive is not free, so only touch it when the state actually changes.
        private static void SetActive(Transform piece, bool active)
        {
            if (piece == null)
            {
                return;
            }

            if (piece.gameObject.activeSelf != active)
            {
                piece.gameObject.SetActive(active);
            }
        }
    }
}
