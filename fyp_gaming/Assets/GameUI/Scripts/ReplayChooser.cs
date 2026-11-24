// KingdomDemo (partial) - replay run chooser: a run-id entry box and named presets.
//
// Offline replay only. Presets come from the local preset file when present (see ReplayCatalog), else a
// short built-in list; a missing, unreadable or empty file is stated, never silently ignored. Opening a
// run goes through ReplayCatalog.Open, so a typed id is replayed only when its files resolve and validate
// and the recording is LLM-only. Run files are local-only and do not come with a clone.

using System.Collections.Generic;
using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        private bool _replayChooserOpen;
        private string _replayRunInput = "";
        private string _replayOpenMessage;
        private bool _replayOpenOk;
        private List<string> _replayOpenNotes;
        private bool _replayShowProviderReasoning;
        private Vector2 _replayChooserScroll;
        private readonly Dictionary<string, string> _replayPresetStatus = new Dictionary<string, string>();

        void ToggleReplayChooser()
        {
            _replayChooserOpen = !_replayChooserOpen;
            if (_replayChooserOpen) RefreshReplayPresetStatus();
        }

        void RefreshReplayPresetStatus()
        {
            _replayPresetStatus.Clear();
            if (_replayPresets == null) _replayPresets = ReplayCatalog.LoadPresets();
            foreach (ReplayPreset preset in _replayPresets.Presets)
            {
                ReplayOpenStatus status = ReplayCatalog.Probe(preset.RunId, _replayPresets.SearchRoots, out string message);
                _replayPresetStatus[preset.RunId] = status == ReplayOpenStatus.Ready ? "files found"
                    : status == ReplayOpenStatus.Incomplete ? "incomplete files"
                    : status == ReplayOpenStatus.NotFound ? "files not found" : "unusable run id";
            }
        }

        void DrawReplayChooser(float x, float y, float width, Rect area)
        {
            if (_replayPresets == null) { _replayPresets = ReplayCatalog.LoadPresets(); RefreshReplayPresetStatus(); }

            GUI.Label(new Rect(x, y, width, 18f), "LOAD A RECORDED RUN", uiPanelTitle);
            y += 22f;
            GUI.Label(new Rect(x, y, width, 28f),
                ReplayCatalog.LlmOnlyLabel + " A run opens only if its local files are found and valid.", uiSmallWrap);
            y += 32f;

            GUI.Label(new Rect(x, y + 2f, 54f, 20f), "Run ID", uiSmall);
            _replayRunInput = GUI.TextField(new Rect(x + 56f, y, width - 56f - 64f, 24f), _replayRunInput ?? "", 96, uiField);
            if (GUI.Button(new Rect(x + width - 60f, y, 60f, 24f), "Load", uiButton)) OpenReplayRun(_replayRunInput);
            y += 30f;

            if (!string.IsNullOrEmpty(_replayOpenMessage))
            {
                float h = Mathf.Max(30f, uiSmallWrap.CalcHeight(new GUIContent(_replayOpenMessage), width));
                GUI.Label(new Rect(x, y, width, h), _replayOpenMessage, _replayOpenOk ? uiSmallWrap : uiReasonWrap);
                y += h + 4f;
            }

            GUI.Label(new Rect(x, y, width, 18f), "PRESETS", uiPanelTitle);
            y += 22f;
            float listHeight = Mathf.Max(60f, area.yMax - y - 70f);
            float rowH = 46f;
            Rect view = new Rect(x, y, width, listHeight);
            _replayChooserScroll = GUI.BeginScrollView(view, _replayChooserScroll,
                new Rect(0, 0, width - 18f, _replayPresets.Presets.Count * rowH));
            for (int i = 0; i < _replayPresets.Presets.Count; i++)
            {
                ReplayPreset preset = _replayPresets.Presets[i];
                float rowY = i * rowH;
                if (GUI.Button(new Rect(0, rowY, width * 0.45f, 22f), preset.Name, uiButton))
                {
                    _replayRunInput = preset.RunId;
                    OpenReplayRun(preset.RunId);
                }
                string status = _replayPresetStatus.TryGetValue(preset.RunId, out string st) ? st : "not checked";
                GUI.Label(new Rect(width * 0.45f + 6f, rowY + 2f, width * 0.55f - 24f, 18f),
                    status + "  |  " + preset.RunId, uiSmall);
                if (!string.IsNullOrEmpty(preset.Note))
                    GUI.Label(new Rect(0, rowY + 23f, width - 18f, 22f), preset.Note, uiHint);
            }
            GUI.EndScrollView();
            y += listHeight + 4f;

            if (!string.IsNullOrEmpty(_replayPresets.Message))
                GUI.Label(new Rect(x, y, width, 30f), _replayPresets.Message, uiHint);
            if (_replaySession != null && GUI.Button(new Rect(x + width - 80f, area.yMax - 30f, 80f, 22f), "Close", uiButton))
                _replayChooserOpen = false;
        }
    }
}
