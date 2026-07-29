using UnityEngine;

namespace Tarinoi.Components
{
    /// <summary>
    /// A <see cref="DialogueTrigger"/> that fires when something enters its collider.
    /// </summary>
    /// <remarks>
    /// Requires a trigger collider on the same object. Filter by tag so the player sets
    /// it off and stray physics objects do not.
    /// </remarks>
    [AddComponentMenu("Tarinoi/Dialogue Trigger Volume (3D)")]
    [RequireComponent(typeof(Collider))]
    public sealed class DialogueTriggerVolume : DialogueTrigger
    {
        [Tooltip("Only objects with this tag fire the trigger. Leave empty to accept anything.")]
        public string requiredTag = "Player";

        [Tooltip("Fire only the first time. Turn off for a trigger the player can re-enter.")]
        public bool onceOnly = true;

        bool _fired;

        void Reset()
        {
            // Nudge the collider into the state this component needs, since a plain
            // collider would block the player instead of triggering.
            var collider = GetComponent<Collider>();
            if (collider != null)
            {
                collider.isTrigger = true;
            }
        }

        void OnTriggerEnter(Collider other)
        {
            if (_fired && onceOnly)
            {
                return;
            }

            if (!string.IsNullOrEmpty(requiredTag) && !other.CompareTag(requiredTag))
            {
                return;
            }

            _fired = true;
            Activate();
        }
    }

    /// <summary>The 2D counterpart of <see cref="DialogueTriggerVolume"/>.</summary>
    [AddComponentMenu("Tarinoi/Dialogue Trigger Volume (2D)")]
    [RequireComponent(typeof(Collider2D))]
    public sealed class DialogueTriggerVolume2D : DialogueTrigger
    {
        [Tooltip("Only objects with this tag fire the trigger. Leave empty to accept anything.")]
        public string requiredTag = "Player";

        [Tooltip("Fire only the first time. Turn off for a trigger the player can re-enter.")]
        public bool onceOnly = true;

        bool _fired;

        void Reset()
        {
            var collider = GetComponent<Collider2D>();
            if (collider != null)
            {
                collider.isTrigger = true;
            }
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (_fired && onceOnly)
            {
                return;
            }

            if (!string.IsNullOrEmpty(requiredTag) && !other.CompareTag(requiredTag))
            {
                return;
            }

            _fired = true;
            Activate();
        }
    }
}
