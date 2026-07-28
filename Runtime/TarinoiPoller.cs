using UnityEngine;

namespace Tarinoi
{
    /// <summary>
    /// Re-syncs on a timer so authored changes appear without restarting play mode.
    /// </summary>
    /// <remarks>
    /// Created automatically when polling is enabled in the project settings, and only
    /// in play mode — it exists to shorten the author's edit-and-see loop, not to run in
    /// a shipped game. Leave polling off for builds.
    /// <para>
    /// Hidden from the Add Component menu and from the hierarchy: nothing should attach
    /// this by hand, and it is not part of the scene the developer authored.
    /// </para>
    /// </remarks>
    [AddComponentMenu("")]
    sealed class TarinoiPoller : MonoBehaviour
    {
        TarinoiRuntime _runtime;
        float _interval;
        float _nextSyncTime;

        internal static TarinoiPoller Create(TarinoiRuntime runtime, float intervalSeconds)
        {
            var host = new GameObject("Tarinoi Poller")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };

            DontDestroyOnLoad(host);

            var poller = host.AddComponent<TarinoiPoller>();
            poller._runtime = runtime;
            poller.SetInterval(intervalSeconds);
            return poller;
        }

        internal void SetInterval(float intervalSeconds)
        {
            _interval = Mathf.Max(1f, intervalSeconds);
            // Unscaled, so pausing the game doesn't stop content updates.
            _nextSyncTime = Time.unscaledTime + _interval;
        }

        internal void Stop()
        {
            if (this != null && gameObject != null)
            {
                Destroy(gameObject);
            }
        }

        void Update()
        {
            if (_runtime == null)
            {
                Destroy(gameObject);
                return;
            }

            if (Time.unscaledTime < _nextSyncTime)
            {
                return;
            }

            _nextSyncTime = Time.unscaledTime + _interval;

            // Deliberately not awaited: this is a background refresh, and SyncAsync
            // already ignores overlapping calls.
            _ = _runtime.SyncAsync();
        }
    }
}
