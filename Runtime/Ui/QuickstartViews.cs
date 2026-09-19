using UnityEngine;

namespace Tarinoi.Ui
{
    /// <summary>
    /// Switches the quickstart between its two views: the entry-point picker while no
    /// dialogue is running, and the dialogue strip while one is.
    /// </summary>
    /// <remarks>
    /// The strip subscribes to the runtime's events itself, before this does, so when a
    /// line arrives the strip has already appended it by the time <see cref="ShowDialogue"/>
    /// runs. The transcript is therefore cleared on the way <i>back</i> to the picker, when
    /// it is stale, never on the way in — a clear there would wipe the line just shown.
    /// </remarks>
    public sealed class QuickstartViews
    {
        readonly GameObject _pickerRoot;
        readonly GameObject _stripRoot;
        readonly DialogueStrip _strip;

        public QuickstartViews(GameObject pickerRoot, GameObject stripRoot, DialogueStrip strip)
        {
            _pickerRoot = pickerRoot;
            _stripRoot = stripRoot;
            _strip = strip;
        }

        /// <summary>Follows the runtime: a line or choices show the strip, the end shows the picker.</summary>
        public void Wire(TarinoiRuntime runtime)
        {
            runtime.LineReady += _ => ShowDialogue();
            runtime.ChoicesReady += _ => ShowDialogue();
            runtime.DialogueEnded += ShowPicker;
        }

        public void ShowPicker()
        {
            if (_stripRoot != null)
            {
                _stripRoot.SetActive(false);
                _strip.Clear();
            }

            if (_pickerRoot != null)
            {
                _pickerRoot.SetActive(true);
            }
        }

        public void ShowDialogue()
        {
            if (_stripRoot == null || _stripRoot.activeSelf)
            {
                return;
            }

            _pickerRoot.SetActive(false);
            _stripRoot.SetActive(true);
        }
    }
}
