using System;
using BigCityLegacy.Api;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace LegacyMapExpansion
{
    internal static class MapSettings
    {
        private const string ItemName = "LegacyMapExpansionSetting";

        internal static void Install()
        {
            try
            {
                var harmony = new Harmony("com.bigcitylegacy.map1018.settings");
                var postfix = new HarmonyMethod(typeof(MapSettings), nameof(Apply));
                foreach (var name in new[] { "Awake", "Start", "OnEnable", "LateUpdate" })
                {
                    var method = AccessTools.DeclaredMethod(typeof(MenuGroup), name);
                    if (method != null) harmony.Patch(method, postfix: postfix);
                }
            }
            catch (Exception error) { Plugin.Warn("Map settings menu unavailable: " + error.Message); }
        }

        private static void Apply(MenuGroup __instance)
        {
            if (!__instance || __instance.name.IndexOf("User", StringComparison.OrdinalIgnoreCase) < 0) return;
            if (__instance.GetComponentInChildren<MapSettingToggle>(true)) return;
            MenuSettingBool template = null;
            foreach (var candidate in __instance.GetComponentsInChildren<MenuSettingBool>(true))
            {
                if (candidate.name.StartsWith("BigCityLegacySetting_", StringComparison.Ordinal)) continue;
                template = candidate;
                break;
            }
            if (!template) return;
            GameObject clone = null;
            try
            {
                clone = UnityEngine.Object.Instantiate(template.gameObject);
                clone.SetActive(false);
                clone.name = ItemName;
                clone.transform.SetParent(template.transform.parent, false);
                var rect = clone.GetComponent<RectTransform>();
                var bottom = 0f;
                foreach (var button in __instance.GetComponentsInChildren<MenuButton>(true))
                {
                    if (button.gameObject == clone || !button.gameObject.activeSelf || button.name == "BigCityLegacySetting_user.about") continue;
                    var other = button.transform as RectTransform;
                    if (other) bottom = Mathf.Min(bottom, other.anchoredPosition.y - Mathf.Max(36f, other.rect.height) - 5f);
                }
                rect.anchoredPosition = new Vector2(0f, bottom);
                var layout = clone.GetComponent<LayoutElement>() ?? clone.AddComponent<LayoutElement>();
                layout.ignoreLayout = true;
                var toggle = clone.GetComponent<MenuSettingBool>();
                toggle.isSkipSaves = true;
                toggle.isStaickPos = true;
                clone.AddComponent<MapSettingToggle>().Init(toggle);
                toggle.InitParent();
                if (!__instance.buttons.Contains(toggle)) __instance.buttons.Add(toggle);
                clone.SetActive(true);
            }
            catch (Exception error)
            {
                if (clone) UnityEngine.Object.Destroy(clone);
                Plugin.Warn("Could not create map setting: " + error.Message);
            }
        }
    }

    internal sealed class MapSettingToggle : MonoBehaviour
    {
        private MenuSettingBool toggle;
        private bool lastValue;
        private bool restartPromptOpen;

        internal void Init(MenuSettingBool item)
        {
            toggle = item;
            lastValue = Plugin.Enabled.Value;
            toggle.SetBool(lastValue);
            RefreshText();
        }

        private async void Update()
        {
            if (!toggle || Plugin.Enabled == null) return;
            if (restartPromptOpen)
            {
                toggle.SetBool(lastValue);
                RefreshText();
                return;
            }
            var value = toggle.GetBool();
            if (value != lastValue)
            {
                try
                {
                    Plugin.SetEnabled(value);
                    lastValue = value;
                }
                catch (Exception error)
                {
                    toggle.SetBool(lastValue);
                    Plugin.Warn("Could not save map setting: " + error.Message);
                    return;
                }
                restartPromptOpen = true;
                try { await LegacyGame.RestartAsync(); }
                catch (Exception error) { Plugin.Warn("Could not restart game: " + error.Message); }
                finally { restartPromptOpen = false; }
            }
            else if (Plugin.Enabled.Value != lastValue)
            {
                lastValue = Plugin.Enabled.Value;
                toggle.SetBool(lastValue);
            }
            // The settings menu may have been destroyed while awaiting the dialog.
            if (this && toggle) RefreshText();
        }

        private void RefreshText()
        {
            var language = SettingsValues.me != null && SettingsValues.me.user != null && SettingsValues.me.user.language != null
                ? SettingsValues.me.user.language.GetValue() : Application.systemLanguage.ToString();
            var russian = language != null && (language.StartsWith("ru", StringComparison.OrdinalIgnoreCase) || language == "Русский");
            SetText(toggle.text, russian ? "Legacy Map Expansion (перезапуск)" : "Legacy Map Expansion (restart)");
            SetText(toggle.value, lastValue ? (russian ? "Да" : "On") : (russian ? "Выкл" : "Off"));
        }

        private static void SetText(Localize target, string text)
        {
            if (!target) return;
            target.text = text;
            if (target.ui && target.ui.text != text) target.ui.text = text;
        }
    }
}
