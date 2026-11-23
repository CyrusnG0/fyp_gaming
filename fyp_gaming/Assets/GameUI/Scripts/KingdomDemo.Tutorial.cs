using UnityEngine;

namespace LuanShi
{
    public partial class KingdomDemo
    {
        private const string TutorialSeenKey = "LuanShi.FirstTutorialSeen";
        private bool tutorialOpen;
        private int tutorialStep;

        private static readonly string[] TutorialTitles =
        {
            "Select your forces",
            "March across the map",
            "Orders and seasons",
            "Fog of war",
            "Research",
            "Diplomacy",
        };

        private static readonly string[] TutorialText =
        {
            "Click one of your cities to inspect resources and issue orders. Click one of your armies to see its movement range and choose a destination. Right-click clears the current selection.",
            "Select an army, then click a highlighted hex to march. Movement consumes seasonal march capacity and orders. Cities and enemy positions may be hidden until they are within sight.",
            "Each action costs command points shown in the top bar. End your turn when ready. After every faction acts, the season rolls over and food, research, treaties and reports are resolved.",
            "Your observation is limited by fog of war: cities and armies outside your sight range are not shown in full. Move armies or use Intel missions to reveal more information.",
            "Open Research to spend research points on Agriculture, Military, Espionage or Governance. Research unlocks stronger actions and improves your kingdom over future seasons.",
            "Open Diplomacy to manage war, gifts, messages and treaties. Incoming offers appear as a badge; treaty warnings show when an agreement is close to expiring.",
        };

        private void LoadTutorialState()
        {
            tutorialOpen = PlayerPrefs.GetInt(TutorialSeenKey, 0) == 0;
            tutorialStep = 0;
        }

        private void FinishTutorial()
        {
            tutorialOpen = false;
            PlayerPrefs.SetInt(TutorialSeenKey, 1);
            PlayerPrefs.Save();
        }

        private void DrawFirstTutorial(GUIStyle body, GUIStyle btn, GUIStyle title)
        {
            FillScreen(InkScreen);
            Rect panel = new Rect(300f, 105f, 1000f, 690f);
            DrawBackdrop(panel, AccentGold);

            GUI.Label(new Rect(panel.x + 32f, panel.y + 24f, panel.width - 64f, 38f),
                "How to play", title);
            GUI.Label(new Rect(panel.x + 32f, panel.y + 70f, panel.width - 64f, 24f),
                $"Tutorial  {tutorialStep + 1}/{TutorialTitles.Length}", uiSmall);
            GUI.Label(new Rect(panel.x + 52f, panel.y + 135f, panel.width - 104f, 42f),
                TutorialTitles[tutorialStep], uiPanelTitle);
            GUI.Label(new Rect(panel.x + 52f, panel.y + 205f, panel.width - 104f, 170f),
                TutorialText[tutorialStep], uiBodyWrap);

            if (GUI.Button(new Rect(panel.x + 52f, panel.yMax - 82f, 150f, 42f), "Skip", btn))
                FinishTutorial();

            bool last = tutorialStep == TutorialTitles.Length - 1;
            string next = last ? "Start playing" : "Next";
            if (GUI.Button(new Rect(panel.xMax - 202f, panel.yMax - 82f, 150f, 42f), next, btn))
            {
                if (last) FinishTutorial();
                else tutorialStep++;
            }
        }

        private float DrawHelpPanel(Rect p, GUIStyle body)
        {
            var wrap = new GUIStyle(body) { wordWrap = true };
            float y = p.y + 52f;
            y += WrappedLabel(wrap, "Quick reference", p.x + 20f, y, 760f, 24f) + 8f;
            for (int i = 0; i < TutorialTitles.Length; i++)
            {
                y += WrappedLabel(uiPanelTitle, $"{i + 1}. {TutorialTitles[i]}",
                    p.x + 20f, y, 760f, 26f) + 4f;
                y += WrappedLabel(wrap, TutorialText[i], p.x + 36f, y, 744f, 32f) + 12f;
            }
            y += WrappedLabel(uiSmallWrap,
                "Controls: click a city or army to select it, right-click to clear selection, and use Escape for the pause menu.",
                p.x + 20f, y, 760f, 24f);
            return y;
        }
    }
}
