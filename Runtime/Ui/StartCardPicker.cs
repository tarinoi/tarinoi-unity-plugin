using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Tarinoi.Ui
{
    /// <summary>
    /// Lists every dialogue entry point, grouped by collection, and starts the one picked.
    /// </summary>
    /// <remarks>
    /// A development tool rather than a shipping screen — a real game starts dialogue from
    /// the world. It is the fastest way to check that content synced correctly and plays.
    /// </remarks>
    [AddComponentMenu("Tarinoi/Start Card Picker")]
    public sealed class StartCardPicker : MonoBehaviour
    {
        /// <summary>Raised when the player picks an entry point, before the dialogue starts.</summary>
        public event Action<string, string> StartSelected;

        TarinoiRuntime _runtime;
        Text _status;
        RectTransform _list;

        /// <summary>
        /// Builds the widget under <paramref name="parent"/> and subscribes it to a
        /// runtime. Call this yourself to place the picker in your own canvas.
        /// </summary>
        public void Build(TarinoiRuntime runtime, Transform parent)
        {
            _runtime = runtime;

            var root = QuickstartUi.CreatePanel(parent, "StartCardPicker", QuickstartUi.Background);
            transform.SetParent(root, false);

            var margin = new GameObject("Margin", typeof(RectTransform));
            margin.transform.SetParent(root, false);
            QuickstartUi.Stretch(margin.GetComponent<RectTransform>(), 24);

            var column = margin.AddComponent<VerticalLayoutGroup>();
            column.spacing = 12;
            column.childControlHeight = true;
            column.childForceExpandHeight = false;

            var title = QuickstartUi.CreateText(margin.transform, "Title",
                "Where would you like to start?", 24, QuickstartUi.Body, FontStyle.Bold);
            title.rectTransform.sizeDelta = new Vector2(0, 34);

            _status = QuickstartUi.CreateText(margin.transform, "Status", "", 14,
                QuickstartUi.Dimmed);

            var listHost = new GameObject("List", typeof(RectTransform));
            listHost.transform.SetParent(margin.transform, false);
            listHost.AddComponent<LayoutElement>().flexibleHeight = 1;
            _list = QuickstartUi.CreateScrollingColumn(listHost.transform, out _, 6);

            _runtime.SyncCompleted += OnSyncCompleted;
            _runtime.SyncFailed += OnSyncFailed;
        }

        void OnDestroy()
        {
            if (_runtime == null)
            {
                return;
            }

            _runtime.SyncCompleted -= OnSyncCompleted;
            _runtime.SyncFailed -= OnSyncFailed;
        }

        void OnSyncCompleted(SyncStats stats)
        {
            if (gameObject.activeInHierarchy)
            {
                Refresh();
            }
        }

        void OnSyncFailed(string reason)
        {
            SetStatus(reason);
        }

        void OnEnable()
        {
            if (_runtime != null)
            {
                Refresh();
            }
        }

        /// <summary>Reloads the list of entry points.</summary>
        public async void Refresh()
        {
            var cards = await _runtime.GetStartCardsAsync();
            if (this == null || _list == null)
            {
                return;
            }

            QuickstartUi.ClearChildren(_list);

            if (cards.Count == 0)
            {
                SetStatus("No dialogue found yet. Run Tools > Tarinoi > Sync, then press Play again.");
                return;
            }

            SetStatus($"{cards.Count} entry point(s).");
            Populate(cards);
        }

        void Populate(IReadOnlyList<StartCard> cards)
        {
            var currentCollection = "";

            foreach (var card in cards)
            {
                // The runtime returns these grouped by collection label, so a heading
                // appears whenever the label changes.
                if (card.CollectionLabel != currentCollection)
                {
                    currentCollection = card.CollectionLabel;
                    var heading = QuickstartUi.CreateText(_list, "Heading", currentCollection, 15,
                        QuickstartUi.Dimmed, FontStyle.Bold);
                    heading.rectTransform.sizeDelta = new Vector2(0, 22);
                }

                var target = card;
                QuickstartUi.CreateButton(_list, card.Label, () => Choose(target));
            }
        }

        async void Choose(StartCard card)
        {
            StartSelected?.Invoke(card.CollectionId, card.CardId);
            await _runtime.StartDialogueAsync(card.CollectionId, card.CardId);
        }

        void SetStatus(string message)
        {
            if (_status != null)
            {
                _status.text = message;
            }
        }
    }
}
