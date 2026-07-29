using System;
using UnityEngine;

namespace Tarinoi.Components
{
    /// <summary>
    /// Marks a place in the world that starts a dialogue.
    /// </summary>
    /// <remarks>
    /// Deliberately does <b>not</b> start the dialogue itself. It raises
    /// <see cref="InteractionTriggered"/> and lets your game decide — whether the player
    /// is holding the right item, whether a cutscene is running, which UI to open. Wiring
    /// it straight into the runtime would take that decision away.
    /// <code>
    /// trigger.InteractionTriggered += (collectionId, cardId) =>
    ///     TarinoiRuntime.Instance.StartDialogueAsync(collectionId, cardId);
    /// </code>
    /// </remarks>
    [AddComponentMenu("Tarinoi/Dialogue Trigger")]
    public class DialogueTrigger : MonoBehaviour
    {
        [Tooltip("The collection holding the dialogue to start.")]
        public string collectionId = "";

        [Tooltip("The card the dialogue starts at.")]
        public string cardId = "";

        /// <summary>Raised by <see cref="Activate"/> with the configured target.</summary>
        public event Action<string, string> InteractionTriggered;

        /// <summary>Whether this trigger has somewhere to send the player.</summary>
        public bool IsConfigured =>
            !string.IsNullOrEmpty(collectionId) && !string.IsNullOrEmpty(cardId);

        /// <summary>
        /// Fires the trigger. Call this from your interaction code — a key press, a
        /// collision, a button.
        /// </summary>
        public void Activate()
        {
            if (!IsConfigured)
            {
                TarinoiLog.Warn($"DialogueTrigger on '{name}' has no dialogue set. "
                                + "Pick one in the Inspector.");
                return;
            }

            InteractionTriggered?.Invoke(collectionId, cardId);
        }
    }
}
