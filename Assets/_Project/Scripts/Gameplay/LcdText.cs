using System.Collections.Generic;
using UnityEngine;

namespace SnakeReturns.Gameplay
{
    public enum TextAlign
    {
        Left,
        Centre,
        Right
    }

    /// <summary>
    /// Builds a mesh of coloured quads from LcdFont glyphs. Immediate-mode: Clear, Add, Rebuild.
    /// </summary>
    /// <remarks>
    /// This class references LcdFont and nothing else. It knows nothing about the game — no
    /// score, no state, no snake. Somebody else decides what the words are.
    ///
    /// Every buffer is allocated once at the declared capacity and NEVER grown. Text is rebuilt
    /// on score changes and state changes, which in a run means "regularly" — and anything that
    /// allocates regularly in this game is a periodic GC hitch waiting to happen. Exceeding
    /// capacity truncates instead, because a few missing glyphs are survivable and a stutter is
    /// the one thing this project is built to avoid.
    /// </remarks>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class LcdText : MonoBehaviour
    {
        [Tooltip("Size of one font pixel in world units. 1/12 of a cell is one pixel of the original 252x180 LCD — sizing the type in the HARDWARE's pixels rather than the render target's keeps it proportioned like the real thing at any resolution.")]
        [SerializeField] private float pixelSize = 1f / 12f;

        [Tooltip("Maximum quads on screen at once, outline included. Exceeding this truncates rather than growing the buffers, because growing would allocate.")]
        [SerializeField] private int capacity = 8000;

        [SerializeField] private Color colour = Color.white;

        [Header("Outline")]
        [Tooltip("A one-pixel black border around every glyph, drawn in FONT pixels rather than as a scaled copy of the text, which keeps it exactly one pixel wide at any size.")]
        [SerializeField] private bool outline = true;

        [SerializeField] private Color outlineColour = new Color(0f, 0f, 0f, 1f);

        [Tooltip("How far behind the glyph the outline sits. Only needs to beat z-fighting; the camera is orthographic so it costs no parallax.")]
        [SerializeField] private float outlineDepth = 0.01f;

        public float PixelSize => pixelSize;

        /// <summary>One queued draw. A null <see cref="text"/> marks the item as a bar.</summary>
        private struct Item
        {
            public string text;
            public float x;
            public float yTop;
            public TextAlign align;
            public float w;
            public float h;
            public float scale;
        }

        private Mesh mesh;
        private Vector3[] verts;
        private int[] tris;
        private Color[] cols;
        private int quadCount;
        private readonly List<Item> items = new List<Item>(16);
        private bool dirty;

        private void Awake()
        {
            EnsureBuffers();
        }

        private void OnEnable()
        {
            EnsureBuffers();
        }

        private void EnsureBuffers()
        {
            if (mesh != null && verts != null)
            {
                return;
            }

            int max = Mathf.Max(1, capacity);

            verts = new Vector3[max * 4];
            cols = new Color[max * 4];
            tris = new int[max * 6];

            // The index buffer is written ONCE and never touched again. Quad n always uses
            // vertices 4n..4n+3, so the triangles never change no matter what the text says —
            // only the vertex positions do.
            for (int q = 0; q < max; q++)
            {
                int v = q * 4;
                int t = q * 6;

                tris[t + 0] = v + 0;
                tris[t + 1] = v + 1;
                tris[t + 2] = v + 2;
                tris[t + 3] = v + 0;
                tris[t + 4] = v + 2;
                tris[t + 5] = v + 3;
            }

            mesh = new Mesh
            {
                name = "LcdText",
                hideFlags = HideFlags.DontSave
            };
            mesh.MarkDynamic();

            GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        public void Clear()
        {
            items.Clear();
            dirty = true;
        }

        public void Add(string text, float x, float yTop, TextAlign align = TextAlign.Left, float scale = 1f)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            items.Add(new Item { text = text, x = x, yTop = yTop, align = align, scale = scale });
            dirty = true;
        }

        public void AddBar(float x, float yTop, float width, float height)
        {
            items.Add(new Item { text = null, x = x, yTop = yTop, w = width, h = height, scale = 1f });
            dirty = true;
        }

        public void Rebuild()
        {
            if (!dirty)
            {
                return;
            }

            dirty = false;
            EnsureBuffers();
            quadCount = 0;

            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];

                if (item.text == null)
                {
                    EmitBar(item);
                }
                else
                {
                    EmitLine(item);
                }
            }

            // Collapse the unused tail instead of resizing the arrays — resizing would
            // allocate, and a triangle whose three vertices are the same point is degenerate,
            // rasterises to nothing, and costs essentially zero.
            for (int v = quadCount * 4; v < verts.Length; v++)
            {
                verts[v] = Vector3.zero;
            }

            mesh.Clear();
            mesh.vertices = verts;
            mesh.colors = cols;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
        }

        private void EmitLine(Item line)
        {
            // The scale scales the FONT PIXEL, not the mesh. A big number stays built from the
            // same square pixels as a small one, which is what a real segment display does;
            // scaling the finished mesh would just magnify it and read as a zoomed screenshot.
            // The outline scales with it, so it stays exactly one pixel wide at any size.
            float ps = pixelSize * (line.scale <= 0f ? 1f : line.scale);
            float w = LcdFont.MeasureWidth(line.text) * ps;

            float startX = line.align == TextAlign.Centre ? line.x - w * 0.5f
                         : line.align == TextAlign.Right ? line.x - w
                         : line.x;

            if (outline)
            {
                // ONE COMPLETE OUTLINE PASS BEFORE ANY GLYPH IS EMITTED.
                // Interleaving the two — outline then glyph, per character — lets a later
                // character's outline punch a black hole through an earlier character wherever
                // the two sit a pixel apart, which at this size is most of the time.
                for (int i = 0; i < line.text.Length; i++)
                {
                    if (quadCount >= capacity)
                    {
                        return;
                    }

                    char c = line.text[i];
                    float gx = startX + i * LcdFont.Advance * ps;

                    // One PAST each edge, so the border of a glyph whose pixels touch its cell
                    // boundary is not clipped away.
                    for (int row = -1; row <= LcdFont.Height; row++)
                    {
                        for (int col = -1; col <= LcdFont.Width; col++)
                        {
                            if (quadCount >= capacity)
                            {
                                return;
                            }

                            if (LcdFont.Pixel(c, col, row))
                            {
                                continue;   // lit pixels are glyph, not border
                            }

                            if (!HasLitNeighbour(c, col, row))
                            {
                                continue;   // empty space with nothing to outline
                            }

                            EmitQuad(gx + col * ps, line.yTop - row * ps, ps, ps, outlineColour, outlineDepth);
                        }
                    }
                }
            }

            for (int i = 0; i < line.text.Length; i++)
            {
                if (quadCount >= capacity)
                {
                    return;
                }

                char c = line.text[i];
                float gx = startX + i * LcdFont.Advance * ps;

                for (int row = 0; row < LcdFont.Height; row++)
                {
                    for (int col = 0; col < LcdFont.Width; col++)
                    {
                        if (quadCount >= capacity)
                        {
                            return;
                        }

                        if (!LcdFont.Pixel(c, col, row))
                        {
                            continue;
                        }

                        // yTop - row * ps: y is the TOP of the text and rows run downward,
                        // matching how the glyph tables read on the page.
                        EmitQuad(gx + col * ps, line.yTop - row * ps, ps, ps, colour, 0f);
                    }
                }
            }
        }

        private static bool HasLitNeighbour(char c, int col, int row)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }

                    if (LcdFont.Pixel(c, col + dx, row + dy))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>One quad, not a grid of pixel-sized ones — a bar has no glyph detail to preserve.</summary>
        private void EmitBar(Item bar)
        {
            if (outline)
            {
                EmitQuad(
                    bar.x - pixelSize,
                    bar.yTop + pixelSize,
                    bar.w + pixelSize * 2f,
                    bar.h + pixelSize * 2f,
                    outlineColour,
                    outlineDepth);
            }

            EmitQuad(bar.x, bar.yTop, bar.w, bar.h, colour, 0f);
        }

        private void EmitQuad(float x, float yTop, float w, float h, Color quadColour, float z)
        {
            if (quadCount >= capacity)
            {
                return;
            }

            int v = quadCount * 4;

            verts[v + 0] = new Vector3(x, yTop - h, z);       // bottom-left
            verts[v + 1] = new Vector3(x, yTop, z);           // top-left
            verts[v + 2] = new Vector3(x + w, yTop, z);       // top-right
            verts[v + 3] = new Vector3(x + w, yTop - h, z);   // bottom-right

            cols[v + 0] = quadColour;
            cols[v + 1] = quadColour;
            cols[v + 2] = quadColour;
            cols[v + 3] = quadColour;

            quadCount++;
        }
    }
}
