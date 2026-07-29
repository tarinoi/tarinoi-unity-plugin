using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Tarinoi.Ui
{
    /// <summary>
    /// Shows dialogue as a scrolling transcript: lines accumulate, past entries dim, and
    /// choices appear as buttons at the bottom.
    /// </summary>
    /// <remarks>
    /// A transcript rather than a single replaced line, because it makes what the runtime
    /// is doing legible while you author — you can see the path taken. Once a choice is
    /// made its buttons are replaced by plain text, so history cannot be re-clicked.
    /// </remarks>
    [AddComponentMenu("Tarinoi/Dialogue Strip")]
    public sealed class DialogueStrip : MonoBehaviour
    {
        TarinoiRuntime _runtime;
        RectTransform _feed;
        ScrollRect _scroll;
        Text _hint;

        readonly List<GameObject> _liveGroups = new List<GameObject>();

        /// <summary>
        /// Builds the widget under <paramref name="parent"/> and subscribes it to a
        /// runtime. Call this yourself to place the strip in your own canvas.
        /// </summary>
        public void Build(TarinoiRuntime runtime, Transform parent)
        {
            _runtime = runtime;

            var root = QuickstartUi.CreatePanel(parent, "DialogueStrip", QuickstartUi.Background);
            transform.SetParent(root, false);

            var margin = new GameObject("Margin", typeof(RectTransform));
            margin.transform.SetParent(root, false);
            QuickstartUi.Stretch(margin.GetComponent<RectTransform>(), 24);

            _feed = QuickstartUi.CreateScrollingColumn(margin.transform, out _scroll);

            _hint = QuickstartUi.CreateText(root, "Hint", "", 13, QuickstartUi.Dimmed);
            var hintRect = _hint.rectTransform;
            hintRect.anchorMin = new Vector2(0, 0);
            hintRect.anchorMax = new Vector2(1, 0);
            hintRect.pivot = new Vector2(0.5f, 0);
            hintRect.offsetMin = new Vector2(24, 4);
            hintRect.offsetMax = new Vector2(-24, 24);
            _hint.alignment = TextAnchor.LowerRight;

            _runtime.LineReady += ShowLine;
            _runtime.ChoicesReady += ShowChoices;
            _runtime.PinChoiceNeeded += ShowPins;
        }

        void OnDestroy()
        {
            if (_runtime == null)
            {
                return;
            }

            _runtime.LineReady -= ShowLine;
            _runtime.ChoicesReady -= ShowChoices;
            _runtime.PinChoiceNeeded -= ShowPins;
        }

        /// <summary>Clears the transcript, ready for a new conversation.</summary>
        public void Clear()
        {
            if (_feed != null)
            {
                QuickstartUi.ClearChildren(_feed);
            }

            _liveGroups.Clear();
            SetHint("");
        }

        void ShowLine(DialogueLine line)
        {
            FreezePrevious();

            var group = NewGroup();

            if (line.IsSystem)
            {
                QuickstartUi.CreateText(group.transform, "System", line.Line, 15,
                    QuickstartUi.SystemLine, FontStyle.Italic);
            }
            else
            {
                if (!string.IsNullOrEmpty(line.EntityLabel))
                {
                    QuickstartUi.CreateText(group.transform, "Speaker", line.EntityLabel, 14,
                        QuickstartUi.Speaker, FontStyle.Bold);
                }

                QuickstartUi.CreateText(group.transform, "Line", line.Line, 17, QuickstartUi.Body);
            }

            SetHint("Click Continue to go on");
            QuickstartUi.CreateButton(group.transform, "Continue ▸", Advance);

            ScrollToEnd();
        }

        void ShowChoices(IReadOnlyList<DialogueChoice> choices)
        {
            FreezePrevious();

            var group = NewGroup();
            SetHint(choices.Count == 1 ? "One way forward" : $"{choices.Count} ways forward");

            foreach (var choice in choices)
            {
                var index = choice.Index;
                var label = $"{index + 1}. {choice.Line}";
                var button = QuickstartUi.CreateButton(group.transform, label, () => Select(index));

                if (choice.Visited)
                {
                    // Dim what the player has already been through, without hiding it.
                    var text = button.GetComponentInChildren<Text>();
                    if (text != null)
                    {
                        text.color = QuickstartUi.Dimmed;
                    }
                }
            }

            ScrollToEnd();
        }

        /// <summary>
        /// Offers the raw pin names when a card's output selector could not choose.
        /// Developer tooling: a player should never see this.
        /// </summary>
        void ShowPins(IReadOnlyList<string> pinNames)
        {
            FreezePrevious();

            var group = NewGroup();
            QuickstartUi.CreateText(group.transform, "Warning",
                "This card needs a pin chosen by hand — its output selector is missing or "
                + "unbound.", 13, QuickstartUi.SystemLine);

            foreach (var pin in pinNames)
            {
                var name = pin;
                QuickstartUi.CreateButton(group.transform, $"Pin: {name}", () => SelectPin(name));
            }

            SetHint("Waiting for a pin");
            ScrollToEnd();
        }

        /// <summary>
        /// Turns the previous entry's buttons into plain text, so history stays readable
        /// but cannot be clicked again.
        /// </summary>
        void FreezePrevious()
        {
            foreach (var group in _liveGroups)
            {
                if (group == null)
                {
                    continue;
                }

                foreach (var button in group.GetComponentsInChildren<Button>())
                {
                    button.interactable = false;

                    var image = button.GetComponent<Image>();
                    if (image != null)
                    {
                        image.color = new Color(0, 0, 0, 0);
                    }

                    var text = button.GetComponentInChildren<Text>();
                    if (text != null)
                    {
                        text.color = QuickstartUi.Dimmed;
                    }
                }

                foreach (var text in group.GetComponentsInChildren<Text>())
                {
                    text.color = QuickstartUi.Dimmed;
                }
            }

            _liveGroups.Clear();
        }

        GameObject NewGroup()
        {
            var group = new GameObject("Entry", typeof(RectTransform));
            group.transform.SetParent(_feed, false);

            var layout = group.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 4;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var fitter = group.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _liveGroups.Add(group);
            return group;
        }

        async void Advance() => await _runtime.AdvanceAsync();

        async void Select(int index) => await _runtime.SelectChoiceAsync(index);

        async void SelectPin(string pin) => await _runtime.SelectPinAsync(pin);

        void SetHint(string text)
        {
            if (_hint != null)
            {
                _hint.text = text;
            }
        }

        void ScrollToEnd()
        {
            if (isActiveAndEnabled)
            {
                StartCoroutine(ScrollNextFrame());
            }
        }

        IEnumerator ScrollNextFrame()
        {
            // The layout has not been rebuilt yet, so the scroll position would be
            // computed against the old content height.
            yield return null;
            if (_scroll != null)
            {
                _scroll.verticalNormalizedPosition = 0f;
            }
        }
    }
}
