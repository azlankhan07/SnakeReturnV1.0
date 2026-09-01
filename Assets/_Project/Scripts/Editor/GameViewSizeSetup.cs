using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SnakeReturns.EditorTools
{
    /// <summary>
    /// Adds a fixed 1920x1080 Game view size and selects it.
    /// </summary>
    /// <remarks>
    /// Unity exposes no public API for Game view sizes, so this is all reflection into
    /// internal editor types. ALL of that reflection is deliberately confined to this one
    /// file: when a future Unity version renames something, there is exactly one place to
    /// fix, and nothing else in the project has to know these types exist.
    ///
    /// Everything is wrapped in try/catch. A Game view that is the wrong size is a nuisance;
    /// an exception thrown out of a menu item that half-ran is worse.
    /// </remarks>
    public static class GameViewSizeSetup
    {
        public const string SizeName = "SnakeReturns 16:9";
        public const int Width = 1920;
        public const int Height = 1080;

        [MenuItem("SnakeReturns/Set Game View To 1920x1080 (16:9)")]
        public static void Apply()
        {
            int index = EnsureSize();
            if (index < 0)
            {
                return;
            }

            if (Select(index))
            {
                Debug.Log($"[GameViewSizeSetup] Game view set to '{SizeName}' ({Width}x{Height}), index {index}.");
            }
        }

        /// <summary>
        /// Makes sure a size named <see cref="SizeName"/> exists and is correct.
        /// Returns its index in the current group, or -1 if the internal API could not be reached.
        /// </summary>
        public static int EnsureSize()
        {
            try
            {
                object group = GetCurrentGroup();
                if (group == null)
                {
                    return -1;
                }

                Type groupType = group.GetType();
                int total = (int)groupType.GetMethod("GetTotalCount").Invoke(group, null);
                MethodInfo getSize = groupType.GetMethod("GetGameViewSize");

                // Backwards, because custom sizes live at the end of the list and because
                // removing an entry shifts everything after it.
                for (int i = total - 1; i >= 0; i--)
                {
                    object size = getSize.Invoke(group, new object[] { i });
                    Type sizeType = size.GetType();

                    string baseText = (string)sizeType.GetProperty("baseText").GetValue(size);
                    if (baseText != SizeName)
                    {
                        continue;
                    }

                    int w = (int)sizeType.GetProperty("width").GetValue(size);
                    int h = (int)sizeType.GetProperty("height").GetValue(size);
                    object kind = sizeType.GetProperty("sizeType").GetValue(size);
                    bool isFixed = kind.ToString() == "FixedResolution";

                    if (isFixed && w == Width && h == Height)
                    {
                        return i;
                    }

                    // Wrong shape under the right name — drop it and rebuild it below.
                    groupType.GetMethod("RemoveCustomSize").Invoke(group, new object[] { i });
                }

                Type gameViewSizeType = EditorAssembly.GetType("UnityEditor.GameViewSize");
                Type gameViewSizeTypeEnum = EditorAssembly.GetType("UnityEditor.GameViewSizeType");

                // MUST be FixedResolution, NOT AspectRatio. An aspect entry keeps the right
                // SHAPE but still renders at whatever pixel size the panel happens to be, so
                // the RenderTexture the phone screen samples changes resolution every time
                // someone drags the window. A fixed resolution is the same every run.
                object fixedResolution = Enum.Parse(gameViewSizeTypeEnum, "FixedResolution");

                object newSize = Activator.CreateInstance(
                    gameViewSizeType,
                    fixedResolution, Width, Height, SizeName);

                groupType.GetMethod("AddCustomSize").Invoke(group, new[] { newSize });

                return (int)groupType.GetMethod("GetTotalCount").Invoke(group, null) - 1;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[GameViewSizeSetup] Could not add the Game view size — Unity's internal API has " +
                                 $"probably moved. Set the Game view to {Width}x{Height} by hand. ({exception.Message})");
                return -1;
            }
        }

        /// <summary>Selects the size at <paramref name="index"/> in the Game view window.</summary>
        public static bool Select(int index)
        {
            try
            {
                Type gameViewType = EditorAssembly.GetType("UnityEditor.GameView");
                EditorWindow gameView = EditorWindow.GetWindow(gameViewType, false, null, false);
                if (gameView == null)
                {
                    return false;
                }

                PropertyInfo lowRes = gameViewType.GetProperty(
                    "lowResolutionForAspectRatios",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (lowRes != null && lowRes.CanWrite)
                {
                    lowRes.SetValue(gameView, false, null);
                }

                // SizeSelectionCallback is what actually applies the choice. Setting
                // selectedSizeIndex directly updates the field but never makes the window
                // re-layout, so the value is "right" and absolutely nothing appears to happen —
                // which is a genuinely baffling ten minutes if you have not hit it before.
                MethodInfo callback = gameViewType.GetMethod(
                    "SizeSelectionCallback",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (callback != null)
                {
                    callback.Invoke(gameView, new object[] { index, null });
                }
                else
                {
                    PropertyInfo selected = gameViewType.GetProperty(
                        "selectedSizeIndex",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (selected == null)
                    {
                        Debug.LogWarning("[GameViewSizeSetup] Neither SizeSelectionCallback nor selectedSizeIndex " +
                                         "was found. Pick the size from the Game view dropdown by hand.");
                        return false;
                    }

                    selected.SetValue(gameView, index, null);
                }

                gameView.Repaint();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[GameViewSizeSetup] Could not select the Game view size: {exception.Message}");
                return false;
            }
        }

        private static Assembly EditorAssembly => typeof(UnityEditor.Editor).Assembly;

        private static object GetCurrentGroup()
        {
            Type sizesType = EditorAssembly.GetType("UnityEditor.GameViewSizes");
            if (sizesType == null)
            {
                return null;
            }

            Type singletonType = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            object instance = singletonType
                .GetProperty("instance", BindingFlags.Static | BindingFlags.Public)
                .GetValue(null);

            return sizesType.GetProperty("currentGroup").GetValue(instance);
        }
    }
}
