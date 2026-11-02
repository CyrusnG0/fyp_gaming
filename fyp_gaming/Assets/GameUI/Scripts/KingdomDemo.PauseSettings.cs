using UnityEngine;
using UnityEngine.SceneManagement;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo
    {
        [Header("Audio")]
        [Tooltip("Optional music source controlled by the music setting. Effects use AudioListener volume.")]
        public AudioSource musicSource;

        private const string MusicVolumeKey = "LuanShi.MusicVolume";
        private const string EffectsVolumeKey = "LuanShi.EffectsVolume";
        private const string UiScaleKey = "LuanShi.UiScale";
        private const string FullscreenKey = "LuanShi.Fullscreen";

        private bool pauseMenuOpen;
        private bool settingsOpen;
        private float musicVolume = 1f;
        private float effectsVolume = 1f;
        private float uiScale = 1f;
        private bool fullscreen;

        private void LoadPresentationSettings()
        {
            musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicVolumeKey, 1f));
            effectsVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(EffectsVolumeKey, 1f));
            uiScale = UiPointerMapping.ClampUiScale(PlayerPrefs.GetFloat(UiScaleKey, 1f));
            fullscreen = PlayerPrefs.GetInt(FullscreenKey, Screen.fullScreen ? 1 : 0) != 0;
            ApplyPresentationSettings();
        }

        private void ApplyPresentationSettings()
        {
            AudioListener.volume = effectsVolume;
            if (musicSource != null) musicSource.volume = musicVolume;
            Screen.fullScreen = fullscreen;
        }

        private void SavePresentationSettings()
        {
            PlayerPrefs.SetFloat(MusicVolumeKey, musicVolume);
            PlayerPrefs.SetFloat(EffectsVolumeKey, effectsVolume);
            PlayerPrefs.SetFloat(UiScaleKey, uiScale);
            PlayerPrefs.SetInt(FullscreenKey, fullscreen ? 1 : 0);
            PlayerPrefs.Save();
        }

        private void TogglePauseMenu()
        {
            if (pauseMenuOpen)
            {
                ResumeGame();
                return;
            }

            pauseMenuOpen = true;
            settingsOpen = false;
            Time.timeScale = 0f;
        }

        private void ResumeGame()
        {
            pauseMenuOpen = false;
            settingsOpen = false;
            Time.timeScale = 1f;
        }

        private void RestartGame()
        {
            SavePresentationSettings();
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private void QuitGame()
        {
            SavePresentationSettings();
            Time.timeScale = 1f;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void DrawPauseMenu()
        {
            FillScreen(InkScreen);
            // The preferred 620x660 panel, shrunk to and centred in the visible viewport. At 120% the
            // design viewport is 1333x750 units, so a fixed y=120 panel would put Back off the bottom.
            Rect panel = ModalPanelRect(620f, 660f);
            DrawBackdrop(panel, AccentGold);

            GUI.Label(new Rect(panel.x + 28f, panel.y + 22f, panel.width - 56f, 36f),
                settingsOpen ? "Settings" : "Game paused", uiTitle);

            if (settingsOpen)
            {
                DrawSettingsPanel(panel);
                if (GUI.Button(new Rect(panel.x + 28f, panel.yMax - 62f, 160f, 38f), "Back", uiButton))
                    settingsOpen = false;
                return;
            }

            float y = panel.y + 110f;
            if (GUI.Button(new Rect(panel.x + 90f, y, 440f, 52f), "Resume", uiButton))
                ResumeGame();
            y += 70f;
            if (GUI.Button(new Rect(panel.x + 90f, y, 440f, 52f), "Settings", uiButton))
                settingsOpen = true;
            y += 70f;
            if (GUI.Button(new Rect(panel.x + 90f, y, 440f, 52f), "Restart", uiButton))
                RestartGame();
            y += 70f;
            if (GUI.Button(new Rect(panel.x + 90f, y, 440f, 52f), "Quit to desktop", uiButton))
                QuitGame();

            GUI.Label(new Rect(panel.x + 28f, panel.yMax - 56f, panel.width - 56f, 24f),
                "Press Escape to resume", uiSmall);
        }

        private void DrawSettingsPanel(Rect panel)
        {
            float x = panel.x + 42f;
            float width = panel.width - 84f;
            float y = panel.y + 100f;

            GUI.Label(new Rect(x, y, width, 24f),
                $"Music volume  {Mathf.RoundToInt(musicVolume * 100f)}%", uiBody);
            float nextMusic = GUI.HorizontalSlider(new Rect(x, y + 32f, width, 24f), musicVolume, 0f, 1f);
            if (!Mathf.Approximately(nextMusic, musicVolume))
            {
                musicVolume = nextMusic;
                ApplyPresentationSettings();
                SavePresentationSettings();
            }
            y += 82f;

            GUI.Label(new Rect(x, y, width, 24f),
                $"Effects volume  {Mathf.RoundToInt(effectsVolume * 100f)}%", uiBody);
            float nextEffects = GUI.HorizontalSlider(new Rect(x, y + 32f, width, 24f), effectsVolume, 0f, 1f);
            if (!Mathf.Approximately(nextEffects, effectsVolume))
            {
                effectsVolume = nextEffects;
                ApplyPresentationSettings();
                SavePresentationSettings();
            }
            y += 82f;

            GUI.Label(new Rect(x, y, width, 24f),
                $"UI scale  {Mathf.RoundToInt(uiScale * 100f)}%", uiBody);
            float nextScale = GUI.HorizontalSlider(new Rect(x, y + 32f, width, 24f), uiScale,
                UiPointerMapping.MinUiScale, UiPointerMapping.MaxUiScale);
            if (!Mathf.Approximately(nextScale, uiScale))
            {
                uiScale = UiPointerMapping.ClampUiScale(nextScale);
                SavePresentationSettings();
            }
            y += 92f;

            string fullscreenLabel = fullscreen ? "Display: Fullscreen" : "Display: Windowed";
            if (GUI.Button(new Rect(x, y, width, 42f), fullscreenLabel, uiButton))
            {
                fullscreen = !fullscreen;
                ApplyPresentationSettings();
                SavePresentationSettings();
            }

            GUI.Label(new Rect(x, y + 62f, width, 44f),
                musicSource == null
                    ? "Music volume is ready for a scene music source."
                    : "Music and effects settings are active.",
                uiSmallWrap);
        }
    }
}
