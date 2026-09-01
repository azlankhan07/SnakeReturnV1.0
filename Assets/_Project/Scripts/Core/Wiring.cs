using UnityEngine;

namespace SnakeReturns.Core
{
    /// <summary>
    /// Dependency resolution for [SerializeField] references.
    /// </summary>
    /// <remarks>
    /// Why this exists: an unassigned [SerializeField] field fails at runtime as a bare
    /// NullReferenceException that names neither the field nor the object it belongs to,
    /// so you get to go hunting. Resolve() turns that into a message that says exactly
    /// which field on which object was empty — and, where it can, fills it in anyway.
    ///
    /// This is a safety net, not the wiring mechanism. The Inspector is the wiring
    /// mechanism; anything Resolve() has to find for itself is logged as a problem.
    /// </remarks>
    public static class Wiring
    {
        /// <summary>
        /// Fills <paramref name="field"/> if it is empty. Resolution order:
        /// 1. already assigned (the Inspector always wins) 2. a component on the owner
        /// 3. anything of that type in the scene, with a warning 4. nothing, with an error.
        /// </summary>
        /// <param name="owner">The behaviour that owns the field. Used for the search and for log context.</param>
        /// <param name="field">The serialized field to fill, by reference.</param>
        /// <param name="fieldName">The field's name, for the log message. Use nameof().</param>
        public static void Resolve<T>(MonoBehaviour owner, ref T field, string fieldName) where T : Component
        {
            // 1. The Inspector always wins. If someone assigned it, we never second-guess it.
            if (field != null)
            {
                return;
            }

            if (owner == null)
            {
                Debug.LogError($"[Wiring] Cannot resolve '{fieldName}' ({typeof(T).Name}): no owner was supplied.");
                return;
            }

            // 2. Same GameObject. The common case for a behaviour's own siblings.
            field = owner.GetComponent<T>();
            if (field != null)
            {
                return;
            }

            // 3. Anywhere in the scene, inactive objects included. This usually finds the
            //    right thing, but it is a guess, so it is a warning and not a convenience.
            field = Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
            if (field != null)
            {
                Debug.LogWarning(
                    $"[Wiring] {owner.GetType().Name} on '{owner.name}': field '{fieldName}' ({typeof(T).Name}) " +
                    $"was not assigned, so it fell back to '{field.name}' found in the scene. " +
                    $"Please assign '{fieldName}' in the Inspector.",
                    owner);
                return;
            }

            // 4. Nothing to find. Name the field, the type and the owner so the fix is obvious.
            Debug.LogError(
                $"[Wiring] {owner.GetType().Name} on '{owner.name}': field '{fieldName}' ({typeof(T).Name}) " +
                $"is unassigned and no {typeof(T).Name} exists in the scene. Assign it in the Inspector.",
                owner);
        }
    }
}
