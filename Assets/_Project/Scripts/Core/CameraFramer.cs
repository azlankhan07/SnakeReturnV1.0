using UnityEngine;

namespace SnakeReturns.Core
{
    /// <summary>
    /// Frames the whole board in an orthographic camera, at any aspect ratio.
    /// </summary>
    /// <remarks>
    /// WHY THIS EXISTS INSTEAD OF A HARD-CODED ORTHOGRAPHIC SIZE.
    /// "Size 7.5" only frames a 21 x 13 board at an aspect of about 1.4 or wider. Go
    /// narrower — a 4:3 Game view, or Free Aspect in a window someone has dragged narrow —
    /// and the left and right columns fall outside the frustum. The board has not moved, but
    /// it reads as if it has drifted off camera, and the bug is invisible on the machine it
    /// was authored on. Fitting by whichever axis actually binds is correct at every aspect.
    ///
    /// The play field is centred on the origin and the HUD sits above it, so the centre of
    /// the VIEW is half the HUD height above the centre of the board — hence the y offset.
    /// </remarks>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public class CameraFramer : MonoBehaviour
    {
        [SerializeField] private GridManager grid;

        [Tooltip("Rows of HUD reserved above the play field.")]
        [SerializeField] private int hudRows = 2;

        [Tooltip("Extra breathing room around the board, in cells.")]
        [SerializeField] private float paddingCells = 0f;

        private Camera cam;

        private void OnEnable()
        {
            cam = GetComponent<Camera>();
            Wiring.Resolve(this, ref grid, nameof(grid));
            Frame();
        }

        private void LateUpdate()
        {
            // Every frame, because the aspect can change at any moment: the Game view tab is
            // resized, the aspect dropdown changes, the player drags the window edge.
            Frame();
        }

        private void Frame()
        {
            if (cam == null || grid == null)
            {
                return;
            }

            cam.orthographic = true;

            float viewWidth = grid.Width + paddingCells * 2f;
            float viewHeight = grid.Height + hudRows + paddingCells * 2f;

            // orthographicSize is HALF the visible height. To fit a given width the camera
            // needs half that width divided by the aspect, so take whichever demand is larger.
            float byHeight = viewHeight * 0.5f;
            float byWidth = viewWidth * 0.5f / Mathf.Max(cam.aspect, 0.01f);

            cam.orthographicSize = Mathf.Max(byHeight, byWidth);

            transform.position = new Vector3(0f, hudRows * 0.5f, transform.position.z);
        }
    }
}
