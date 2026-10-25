using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using MultiplayerTemplate.Core.Input;
using MultiplayerTemplate.SkillTree;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BuildHelper
{
    /// <summary>
    /// The ghost hand's countdown as one more round icon in the game's skill bar: a copy of the sprint icon, kept left
    /// of the first skill shown, at the same height and spacing. The game's own bar is left as it is.
    /// </summary>
    internal static class HudSlot
    {
        private static readonly AccessTools.FieldRef<SkillHudPresenter, SkillHudSlot[]> SlotsOf =
            AccessTools.FieldRefAccess<SkillHudPresenter, SkillHudSlot[]>("slots");
        private static readonly AccessTools.FieldRef<SkillHudSlot, Image> RingOf = AccessTools.FieldRefAccess<SkillHudSlot, Image>("ring");
        private static readonly AccessTools.FieldRef<SkillHudSlot, Image> IconOf = AccessTools.FieldRefAccess<SkillHudSlot, Image>("icon");
        private static readonly AccessTools.FieldRef<SkillHudSlot, Image> DetailOf = AccessTools.FieldRefAccess<SkillHudSlot, Image>("iconDetail");
        private static readonly AccessTools.FieldRef<SkillHudSlot, TMP_Text> NameOf = AccessTools.FieldRefAccess<SkillHudSlot, TMP_Text>("nameLabel");
        private static readonly AccessTools.FieldRef<SkillHudSlot, TMP_Text> SecondsOf = AccessTools.FieldRefAccess<SkillHudSlot, TMP_Text>("secondsLabel");
        private static readonly AccessTools.FieldRef<SkillHudSlot, TMP_Text> LevelOf = AccessTools.FieldRefAccess<SkillHudSlot, TMP_Text>("levelLabel");
        private static readonly AccessTools.FieldRef<SkillHudSlot, TMP_Text> BindingOf = AccessTools.FieldRefAccess<SkillHudSlot, TMP_Text>("bindingLabel");
        private static readonly AccessTools.FieldRef<SkillHudSlot, InputPromptView> PromptOf = AccessTools.FieldRefAccess<SkillHudSlot, InputPromptView>("inputPrompt");
        private static readonly AccessTools.FieldRef<InputPromptView, GameObject> SingleKeyOf = AccessTools.FieldRefAccess<InputPromptView, GameObject>("singleKey");
        private static readonly AccessTools.FieldRef<InputPromptView, TMP_Text> KeyLabelOf = AccessTools.FieldRefAccess<InputPromptView, TMP_Text>("singleKeyLabel");
        private static readonly AccessTools.FieldRef<InputPromptView, Image> KeyImageOf = AccessTools.FieldRefAccess<InputPromptView, Image>("singleKeyImage");
        private static readonly AccessTools.FieldRef<InputPromptView, RectTransform> KeyRectOf = AccessTools.FieldRefAccess<InputPromptView, RectTransform>("singleKeyRect");
        private static readonly AccessTools.FieldRef<InputPromptView, Image> GlyphOf = AccessTools.FieldRefAccess<InputPromptView, Image>("glyph");
        private static readonly AccessTools.FieldRef<InputPromptView, GameObject> MovementKeysOf = AccessTools.FieldRefAccess<InputPromptView, GameObject>("movementKeys");
        private static readonly AccessTools.FieldRef<InputPromptView, InputGlyphCatalog> CatalogOf = AccessTools.FieldRefAccess<InputPromptView, InputGlyphCatalog>("catalog");
        private static readonly AccessTools.FieldRef<InputPromptView, float> KeyHeightOf = AccessTools.FieldRefAccess<InputPromptView, float>("keyHeight");
        private static readonly AccessTools.FieldRef<InputPromptView, float> KeyPaddingOf = AccessTools.FieldRefAccess<InputPromptView, float>("keyHorizontalPadding");
        private static readonly AccessTools.FieldRef<InputPromptView, float> KeyWidthOf = AccessTools.FieldRefAccess<InputPromptView, float>("maximumKeyWidth");

        private static readonly Color32 Ready = new Color32(50, 197, 250, 255);
        private static readonly Color32 Marked = new Color32(69, 217, 145, 255);

        private static SkillHudSlot[] gameSlots;
        private static RectTransform root;
        private static Image ring;
        private static Image icon;
        private static TMP_Text nameLabel;
        private static TMP_Text seconds;
        private static Vector3 step;
        private static float nextTry;
        private static bool failed;

        /// <summary>True while the icon sits in the skill bar; otherwise the plugin draws its plain text line.</summary>
        internal static bool InBar => root != null;

        internal static void Tick(TimeSpan left, bool marked)
        {
            if (failed || (root == null && !TryCreate()))
            {
                return;
            }
            bool show = Plugin.ShowTimer.Value;
            if (root.gameObject.activeSelf != show)
            {
                root.gameObject.SetActive(show);
            }
            if (!show)
            {
                return;
            }
            Place();
            float remaining = (float)left.TotalSeconds;
            float duration = Plugin.CooldownMinutes.Value * 60f;
            ring.fillAmount = remaining > 0f && duration > 0f ? Mathf.Clamp01(1f - remaining / duration) : 1f;
            ring.color = marked ? Marked : Ready;
            icon.color = new Color(0.6f, 1f, 1f, remaining > 0f ? 0.18f : 1f);
            if (seconds.gameObject.activeSelf != remaining > 0f)
            {
                seconds.gameObject.SetActive(remaining > 0f);
            }
            if (remaining > 0f)
            {
                Set(seconds, $"{(int)left.TotalMinutes}:{left.Seconds:00}");
            }
            Set(nameLabel, "Ghost hand");
        }

        /// <summary>Every frame, since a localizer on the copied labels may write the sprint texts back.</summary>
        private static void Set(TMP_Text label, string text)
        {
            if (label != null && label.text != text)
            {
                label.text = text;
            }
        }

        /// <summary>Left of the leftmost skill shown, by the distance between the first two.</summary>
        private static void Place()
        {
            List<Transform> shown = gameSlots.Where(s => s != null && s.gameObject.activeInHierarchy).Select(s => s.transform)
                .OrderBy(t => t.position.x).ToList();
            if (shown.Count >= 2)
            {
                step = shown[1].position - shown[0].position;
            }
            Transform first = shown.Count > 0 ? shown[0] : gameSlots.FirstOrDefault(s => s != null)?.transform;
            if (first != null)
            {
                root.position = first.position - step;
            }
        }

        private static bool TryCreate()
        {
            if (Time.unscaledTime < nextTry)
            {
                return false;
            }
            nextTry = Time.unscaledTime + 1f;
            SkillHudPresenter hud = UnityEngine.Object.FindAnyObjectByType<SkillHudPresenter>();
            if (hud == null)
            {
                return false;
            }
            try
            {
                gameSlots = SlotsOf(hud);
                SkillHudSlot sprint = gameSlots?.FirstOrDefault(s => s != null && s.SkillId == "skill.sprint");
                SkillHudSlot trowel = gameSlots?.FirstOrDefault(s => s != null && s.SkillId == "skill.continuous-placement");
                if (sprint == null)
                {
                    failed = true;
                    Plugin.Log.LogWarning("Skill bar without sprint icon: the ghost hand timer stays a text line.");
                    return false;
                }
                GameObject copy = UnityEngine.Object.Instantiate(sprint.gameObject, sprint.transform.parent, false);
                copy.name = "BuildHelper_GhostHand";
                SkillHudSlot slot = copy.GetComponent<SkillHudSlot>();
                ring = RingOf(slot);
                icon = IconOf(slot);
                Image detail = DetailOf(slot);
                nameLabel = NameOf(slot);
                seconds = SecondsOf(slot);
                TMP_Text level = LevelOf(slot);
                TMP_Text binding = BindingOf(slot);
                InputPromptView prompt = PromptOf(slot);
                UnityEngine.Object.Destroy(slot);

                if (trowel != null && IconOf(trowel) != null)
                {
                    icon.sprite = IconOf(trowel).sprite;
                    Image trowelDetail = DetailOf(trowel);
                    if (detail != null)
                    {
                        detail.enabled = trowelDetail != null;
                        if (trowelDetail != null)
                        {
                            detail.sprite = trowelDetail.sprite;
                        }
                    }
                }
                else if (detail != null)
                {
                    detail.enabled = false;
                }
                level.gameObject.SetActive(false);
                foreach (Transform part in level.transform.parent)
                {
                    if (part.name.StartsWith("Level", StringComparison.Ordinal))
                    {
                        part.gameObject.SetActive(false);
                    }
                }
                ShowKey(prompt, binding, Plugin.GhostKey.Value.MainKey);
                seconds.enableAutoSizing = true;
                seconds.fontSizeMax = seconds.fontSize;
                seconds.fontSizeMin = 8f;

                LayoutElement layout = copy.GetComponent<LayoutElement>();
                if (layout == null)
                {
                    layout = copy.AddComponent<LayoutElement>();
                }
                layout.ignoreLayout = true;
                root = (RectTransform)copy.transform;
                step = new Vector3(root.rect.width * root.lossyScale.x * 1.5f, 0f, 0f);
                copy.SetActive(true);
                Plugin.Log.LogInfo("Ghost hand timer added to the skill bar:" + Describe(copy.transform, 0));
                return true;
            }
            catch (Exception e)
            {
                failed = true;
                Plugin.Log.LogWarning($"Could not add the ghost hand timer to the skill bar, it stays a text line: {e}");
                return false;
            }
        }

        /// <summary>
        /// The key under the icon as the game draws its keys: its own prompt code first; where its glyph catalog does not
        /// know the key, a plain keycap with the key's name, sized the same way.
        /// </summary>
        private static void ShowKey(InputPromptView prompt, TMP_Text binding, KeyCode key)
        {
            string name = key.ToString();
            prompt.PresentBinding("<Keyboard>/" + name.ToLowerInvariant(), name);
            GameObject single = SingleKeyOf(prompt);
            if (!single.activeSelf)
            {
                MovementKeysOf(prompt).SetActive(false);
                GlyphOf(prompt).gameObject.SetActive(false);
                single.SetActive(true);
                TMP_Text label = KeyLabelOf(prompt);
                label.gameObject.SetActive(true);
                label.text = name;
                Image cap = KeyImageOf(prompt);
                cap.sprite = CatalogOf(prompt).Keycap;
                cap.type = Image.Type.Sliced;
                cap.preserveAspect = false;
                RectTransform rect = KeyRectOf(prompt);
                float height = KeyHeightOf(prompt);
                float widest = KeyWidthOf(prompt);
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                    Mathf.Clamp(label.GetPreferredValues(name).x + KeyPaddingOf(prompt), Mathf.Min(height, widest), widest));
            }
            binding.gameObject.SetActive(false);
        }

        /// <summary>The copied icon's parts for the log, in case the game's bar changes.</summary>
        private static string Describe(Transform t, int depth)
        {
            var text = new StringBuilder();
            text.Append('\n').Append(' ', depth * 2).Append(t.name).Append(t.gameObject.activeSelf ? "" : " (hidden)").Append(": ")
                .Append(string.Join(", ", t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name)));
            if (depth < 3)
            {
                foreach (Transform child in t)
                {
                    text.Append(Describe(child, depth + 1));
                }
            }
            return text.ToString();
        }
    }
}
