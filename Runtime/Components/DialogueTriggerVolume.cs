using System;
using UnityEngine;

namespace Tarinoi.Components
{
    /// <summary>When a <see cref="DialogueTriggerVolume"/> starts its dialogue.</summary>
    public enum TriggerMode
    {
        /// <summary>The moment something walks in. A cutscene, an ambush, a threshold.</summary>
        OnEnter,

        /// <summary>
        /// Never on its own. The volume reports who is inside and the game decides —
        /// the shape you want for "press E to talk".
        /// </summary>
        WhileInside,
    }

    /// <summary>
    /// A <see cref="DialogueTrigger"/> driven by its collider.
    /// </summary>
    /// <remarks>
    /// Requires a trigger collider on the same object. Filter by tag so the player sets
    /// it off and stray physics objects do not.
    /// <para>
    /// With <see cref="TriggerMode.WhileInside"/> it fires nothing by itself. It raises
    /// <see cref="OccupantEntered"/> and <see cref="OccupantExited"/>, and your game shows
    /// a prompt and calls <see cref="DialogueTrigger.Activate"/> when the player asks:
    /// <code>
    /// volume.OccupantEntered += who => prompt.Show("[E] Talk");
    /// volume.OccupantExited  += who => prompt.Hide();
    /// // …and on the key press:
    /// if (volume.IsOccupied) volume.Activate();
    /// </code>
    /// </para>
    /// </remarks>
    [AddComponentMenu("Tarinoi/Dialogue Trigger Volume (3D)")]
    [RequireComponent(typeof(Collider))]
    public sealed class DialogueTriggerVolume : DialogueTrigger
    {
        [Tooltip("Only objects with this tag fire the trigger. Leave empty to accept anything.")]
        public string requiredTag = "Player";

        [Tooltip("Whether entering starts the dialogue, or only offers it.")]
        public TriggerMode mode = TriggerMode.OnEnter;

        [Tooltip("Fire only the first time. Ignored while the mode is While Inside.")]
        public bool onceOnly = true;

        /// <summary>Raised when a matching collider enters.</summary>
        public event Action<Collider> OccupantEntered;

        /// <summary>Raised when it leaves.</summary>
        public event Action<Collider> OccupantExited;

        /// <summary>Whether a matching collider is inside right now.</summary>
        public bool IsOccupied => _occupant != null;

        Collider _occupant;
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
            if (!Matches(other))
            {
                return;
            }

            _occupant = other;
            OccupantEntered?.Invoke(other);

            if (mode == TriggerMode.WhileInside)
            {
                return;
            }

            if (_fired && onceOnly)
            {
                return;
            }

            _fired = true;
            Activate();
        }

        void OnTriggerExit(Collider other)
        {
            if (!Matches(other) || _occupant != other)
            {
                return;
            }

            _occupant = null;
            OccupantExited?.Invoke(other);
        }

        bool Matches(Collider other) =>
            string.IsNullOrEmpty(requiredTag) || other.CompareTag(requiredTag);
    }

    /// <summary>The 2D counterpart of <see cref="DialogueTriggerVolume"/>.</summary>
    [AddComponentMenu("Tarinoi/Dialogue Trigger Volume (2D)")]
    [RequireComponent(typeof(Collider2D))]
    public sealed class DialogueTriggerVolume2D : DialogueTrigger
    {
        [Tooltip("Only objects with this tag fire the trigger. Leave empty to accept anything.")]
        public string requiredTag = "Player";

        [Tooltip("Whether entering starts the dialogue, or only offers it.")]
        public TriggerMode mode = TriggerMode.OnEnter;

        [Tooltip("Fire only the first time. Ignored while the mode is While Inside.")]
        public bool onceOnly = true;

        /// <summary>Raised when a matching collider enters.</summary>
        public event Action<Collider2D> OccupantEntered;

        /// <summary>Raised when it leaves.</summary>
        public event Action<Collider2D> OccupantExited;

        /// <summary>Whether a matching collider is inside right now.</summary>
        public bool IsOccupied => _occupant != null;

        Collider2D _occupant;
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
            if (!Matches(other))
            {
                return;
            }

            _occupant = other;
            OccupantEntered?.Invoke(other);

            if (mode == TriggerMode.WhileInside)
            {
                return;
            }

            if (_fired && onceOnly)
            {
                return;
            }

            _fired = true;
            Activate();
        }

        void OnTriggerExit2D(Collider2D other)
        {
            if (!Matches(other) || _occupant != other)
            {
                return;
            }

            _occupant = null;
            OccupantExited?.Invoke(other);
        }

        bool Matches(Collider2D other) =>
            string.IsNullOrEmpty(requiredTag) || other.CompareTag(requiredTag);
    }
}
