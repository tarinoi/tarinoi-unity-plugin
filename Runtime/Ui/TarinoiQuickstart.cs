using System.Threading.Tasks;
using UnityEngine;

namespace Tarinoi.Ui
{
    /// <summary>
    /// A complete, playable Tarinoi setup in one component: configure, sync, pick an entry
    /// point, play the dialogue.
    /// </summary>
    /// <remarks>
    /// Drop this on an empty GameObject in an empty scene and press Play. It builds its own
    /// interface, so nothing else needs setting up.
    /// <para>
    /// <b>Registering your bindings.</b> Derive from this and override
    /// <see cref="SetupBindings"/>. It runs after the content database is open and before
    /// any dialogue plays, which is exactly when bindings need to exist:
    /// <code>
    /// public class MyQuickstart : TarinoiQuickstart
    /// {
    ///     protected override void SetupBindings()
    ///     {
    ///         var variables = new GlobalVariables();
    ///         Runtime.Registry.BindVariables("global", variables);
    ///         Runtime.Registry.BindFunctions("global", new MyFunctions(variables));
    ///     }
    /// }
    /// </code>
    /// This is a starting point, not a shipping UI — see <see cref="QuickstartUi"/>.
    /// </para>
    /// </remarks>
    [AddComponentMenu("Tarinoi/Tarinoi Quickstart")]
    public class TarinoiQuickstart : MonoBehaviour
    {
        [Tooltip("Sync from the Tarinoi API when play starts. Turn off to use already-synced content.")]
        public bool syncOnStart = true;

        /// <summary>The runtime this quickstart drives.</summary>
        public TarinoiRuntime Runtime { get; private set; }

        StartCardPicker _picker;
        DialogueStrip _strip;
        GameObject _pickerRoot;
        GameObject _stripRoot;

        async void Start()
        {
            Runtime = TarinoiRuntime.Instance;

            if (!await Runtime.ConfigureAsync())
            {
                TarinoiLog.Error("Tarinoi: could not start. Check the API path in "
                                 + "Project Settings > Tarinoi, then press Play again.");
                return;
            }

            SetupBindings();
            BuildInterface();

            Runtime.LineReady += _ => ShowDialogue();
            Runtime.ChoicesReady += _ => ShowDialogue();
            Runtime.DialogueEnded += ShowPicker;

            if (syncOnStart)
            {
                await Runtime.SyncAsync();
            }

            ShowPicker();
        }

        void OnDestroy()
        {
            // The runtime outlives this scene object, so leaving it configured against a
            // closed database would break the next scene that uses it.
            if (Runtime != null && Runtime == TarinoiRuntime.Instance)
            {
                TarinoiRuntime.ResetInstance();
            }
        }

        /// <summary>
        /// Register your game's functions, variables and entities here. Called once, after
        /// the content database is open and before any dialogue runs.
        /// </summary>
        protected virtual void SetupBindings()
        {
        }

        void BuildInterface()
        {
            var canvas = QuickstartUi.CreateCanvas("Tarinoi Quickstart UI", transform);

            _picker = new GameObject("Picker", typeof(RectTransform))
                .AddComponent<StartCardPicker>();
            _picker.Build(Runtime, canvas.transform);
            _pickerRoot = _picker.transform.parent.gameObject;

            _strip = new GameObject("Strip", typeof(RectTransform)).AddComponent<DialogueStrip>();
            _strip.Build(Runtime, canvas.transform);
            _stripRoot = _strip.transform.parent.gameObject;
        }

        void ShowPicker()
        {
            if (_stripRoot != null)
            {
                _stripRoot.SetActive(false);
            }

            if (_pickerRoot != null)
            {
                _pickerRoot.SetActive(true);
            }
        }

        void ShowDialogue()
        {
            if (_stripRoot == null || _stripRoot.activeSelf)
            {
                return;
            }

            _strip.Clear();
            _pickerRoot.SetActive(false);
            _stripRoot.SetActive(true);
        }
    }
}
