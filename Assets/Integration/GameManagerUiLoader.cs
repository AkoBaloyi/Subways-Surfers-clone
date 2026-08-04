using UnityEngine;
using UnityEngine.SceneManagement;

namespace SubwaySurfers.Integration
{
    /// <summary>
    /// Brings the run coordinator and its user interface into the gameplay scene by loading the UI
    /// scene additively, so neither scene has to be edited to hold the other's contents.
    ///
    /// The two halves of the game were authored in separate scenes: gameplay in SampleScene, and the
    /// Game Manager plus the whole menu, HUD, pause, and game over canvas in Menus. Copying one into
    /// the other would fork the UI, and every later change on the coordinator's side would have to be
    /// mirrored by hand. Loading additively keeps a single source for each scene and leaves ownership
    /// where it is.
    ///
    /// There is deliberately no component to place. A component would have to live in the gameplay
    /// scene, which is the scene this is trying not to modify, so installation happens from a runtime
    /// hook instead.
    ///
    /// The hook is scoped by scene name on purpose. Only the gameplay scene pulls in the UI, so the
    /// player's own PlayerTestScene and the scenes its Play Mode tests generate are unaffected and stay
    /// runnable with no run coordinator present.
    /// </summary>
    public static class GameManagerUiLoader
    {
        private const string GameplaySceneName = "SampleScene";
        private const string UiSceneName = "Menus";

        private static bool installed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            // Statics survive when domain reload is disabled for faster play mode entry, so this guards
            // against subscribing twice.
            if (installed) return;
            installed = true;

            // Stays subscribed for the session: the coordinator's restart reloads the gameplay scene in
            // single mode, which unloads the additive UI scene with it. The runtime hook itself fires
            // only once per session, so the UI has to be re-ensured on every gameplay scene load.
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureUiPresent(SceneManager.GetActiveScene());
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == UiSceneName)
            {
                SuppressDuplicateSceneServices(scene);
                return;
            }

            if (mode == LoadSceneMode.Single) EnsureUiPresent(scene);
        }

        private static void EnsureUiPresent(Scene scene)
        {
            if (scene.name != GameplaySceneName) return;

            // A coordinator authored directly into the gameplay scene wins: this only fills a gap.
            // A destroyed singleton compares equal to null, so a stale reference from a previous run
            // does not suppress the load.
            if (GameManager.Instance != null) return;
            if (SceneManager.GetSceneByName(UiSceneName).isLoaded) return;

            SceneManager.LoadScene(UiSceneName, LoadSceneMode.Additive);
        }

        /// <summary>
        /// The UI scene also runs standalone, so it carries its own camera and directional light. An
        /// overlay canvas needs no camera of its own, and leaving them enabled would put a second camera
        /// over the gameplay view, double the directional lighting, and add a second audio listener.
        /// Only whole roots whose purpose is a camera or a light are disabled; nothing on the canvas or
        /// the coordinator is touched.
        /// </summary>
        private static void SuppressDuplicateSceneServices(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            for (var index = 0; index < roots.Length; index++)
            {
                var root = roots[index];
                if (root.GetComponent<Canvas>() != null) continue;
                if (root.GetComponent<GameManager>() != null) continue;

                if (root.GetComponent<Camera>() != null || root.GetComponent<Light>() != null)
                    root.SetActive(false);
            }
        }
    }
}
