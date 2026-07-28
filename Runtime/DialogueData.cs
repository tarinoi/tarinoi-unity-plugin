using Newtonsoft.Json.Linq;

namespace Tarinoi
{
    /// <summary>What the dialogue system is currently waiting for.</summary>
    public enum DialogueState
    {
        /// <summary>No dialogue in progress, or mid-traversal between cards.</summary>
        Idle,

        /// <summary>A line is on screen; the player advances past it.</summary>
        NpcLine,

        /// <summary>The player is choosing between lines.</summary>
        PcChoice,

        /// <summary>
        /// Waiting for a named pin to be picked by hand. Only reached when a card has
        /// named pins but no usable output selector — a developer-tooling fallback.
        /// </summary>
        AwaitingPin,
    }

    /// <summary>A line of dialogue to display.</summary>
    public sealed class DialogueLine
    {
        /// <summary>Card id of a system line, which has no card of its own.</summary>
        public const string SystemCardId = "__system__";

        public string CardId { get; internal set; }
        public string CollectionId { get; internal set; }

        /// <summary>Identifier of the speaking entity, or "system" for a system line.</summary>
        public string EntityRef { get; internal set; }

        /// <summary>Display name of the speaker, falling back to <see cref="EntityRef"/>.</summary>
        public string EntityLabel { get; internal set; }

        /// <summary>"pc", "npc", "inherit", or "system".</summary>
        public string LineMode { get; internal set; }

        public string Line { get; internal set; }
        public string BaseRef { get; internal set; }
        public string TemplateRef { get; internal set; }

        /// <summary>The card's authored properties, for anything the template defines.</summary>
        public JObject Data { get; internal set; }

        /// <summary>
        /// Whether this is an interstitial system line rather than authored dialogue.
        /// These come from game code calling <see cref="TarinoiRuntime.PostSystemLine"/>.
        /// </summary>
        public bool IsSystem => CardId == SystemCardId;

        public override string ToString() => $"{EntityLabel}: {Line}";
    }

    /// <summary>One selectable option presented to the player.</summary>
    public sealed class DialogueChoice
    {
        /// <summary>
        /// Position in the choice list. Pass this to
        /// <see cref="TarinoiRuntime.SelectChoiceAsync"/>.
        /// </summary>
        public int Index { get; internal set; }

        public string CardId { get; internal set; }
        public string CollectionId { get; internal set; }
        public string EntityRef { get; internal set; }
        public string LineMode { get; internal set; }
        public string Line { get; internal set; }

        public JObject Data { get; internal set; }

        /// <summary>The full card payload, for anything the fields above don't cover.</summary>
        public JObject Card { get; internal set; }

        /// <summary>
        /// Whether the player has taken this option before. Always false unless a
        /// <see cref="IHistoryStore"/> is set.
        /// </summary>
        public bool Visited { get; internal set; }

        public override string ToString() => $"{Index}. {Line}";
    }

    /// <summary>A dialogue entry point, for a "where would you like to start?" list.</summary>
    public sealed class StartCard
    {
        public string CardId { get; internal set; }
        public string CollectionId { get; internal set; }

        /// <summary>Display name of the collection this entry point lives in.</summary>
        public string CollectionLabel { get; internal set; }

        /// <summary>Display label, including the card id for disambiguation.</summary>
        public string Label { get; internal set; }

        public override string ToString() => Label;
    }
}
