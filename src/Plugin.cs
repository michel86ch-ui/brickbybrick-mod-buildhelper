using System;
using System.Globalization;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MultiplayerTemplate.Core.Completion;
using MultiplayerTemplate.Core.Input;
using MultiplayerTemplate.Game.Placement;
using MultiplayerTemplate.Player;
using Unity.Netcode;
using UnityEngine;

namespace BuildHelper
{
    /// <summary>
    /// Two helpers for building, in every world (the town's buildings and extra ones): F8 shows the empty brick places of
    /// the sites with only a few left, in magenta and through walls; F5 (ghost hand) places the brick you carry into a
    /// place you aim at through walls, within the normal reach, once every 10 minutes. A world in which the ghost hand
    /// placed a brick counts as practice (no leaderboard score); the display alone changes nothing.
    /// </summary>
    [BepInPlugin(Guid, "BuildHelper", Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "michel.brickbybrick.buildhelper";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<KeyboardShortcut> MissingKey;
        internal static ConfigEntry<int> MissingLimit;
        internal static ConfigEntry<KeyboardShortcut> GhostKey;
        internal static ConfigEntry<int> CooldownMinutes;
        internal static ConfigEntry<bool> ShowTimer;
        internal static ConfigEntry<string> LastGhostHand;
        internal static CompletionRun CurrentRun;
        private static Plugin instance;

        private static readonly AccessTools.FieldRef<NetworkPlayerController, IInputReader> InputOf =
            AccessTools.FieldRefAccess<NetworkPlayerController, IInputReader>("input");
        private static readonly AccessTools.FieldRef<NetworkPlayerPlacementController, NetworkPlayerController> ControllerOf =
            AccessTools.FieldRefAccess<NetworkPlayerPlacementController, NetworkPlayerController>("playerController");

        private static string toast;
        private static float toastUntil;
        private NetworkPlayerPlacementController placement;
        private NetworkPlayerController controller;
        private float nextSearch;

        private void Awake()
        {
            Log = Logger;
            instance = this;
            MissingKey = Config.Bind("Missing bricks", "Key", new KeyboardShortcut(KeyCode.F8),
                "Shows the empty brick places of every construction site with only a few left (in magenta, through walls); press again to hide them.");
            MissingLimit = Config.Bind("Missing bricks", "Limit", 50, new ConfigDescription(
                "Sites with more empty places than this are only counted, not shown.", new AcceptableValueRange<int>(1, 2000)));
            GhostKey = Config.Bind("Ghost hand", "Key", new KeyboardShortcut(KeyCode.F5),
                "First press: marks the free place you aim at (through walls, within reach) that takes the brick you carry. " +
                "Second press within 5 seconds: places the brick there.");
            CooldownMinutes = Config.Bind("Ghost hand", "CooldownMinutes", 10, new ConfigDescription(
                "Minutes between two bricks placed by ghost hand.", new AcceptableValueRange<int>(0, 240)));
            ShowTimer = Config.Bind("Ghost hand", "ShowTimer", true, "Show the ghost hand's countdown as an icon in the skill bar, left of sprint.");
            LastGhostHand = Config.Bind("Ghost hand", "LastUse", "", "When the ghost hand last placed a brick (UTC, kept by the mod).");
            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"BuildHelper {Version} loaded: {MissingKey.Value} missing bricks, {GhostKey.Value} ghost hand every {CooldownMinutes.Value} min.");
        }

        internal static void Toast(string text, float seconds = 5f)
        {
            toast = text;
            toastUntil = Time.unscaledTime + seconds;
        }

        internal static bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        /// <summary>Time left until the ghost hand may place again.</summary>
        internal static TimeSpan Cooldown
        {
            get
            {
                if (!DateTime.TryParse(LastGhostHand.Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime last))
                {
                    return TimeSpan.Zero;
                }
                TimeSpan left = last.AddMinutes(CooldownMinutes.Value) - DateTime.UtcNow;
                return left > TimeSpan.Zero ? left : TimeSpan.Zero;
            }
        }

        internal static void GhostHandUsed()
        {
            LastGhostHand.Value = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            instance.Config.Save();
            if (IsHost && CurrentRun != null && CurrentRun.IsOpen && CurrentRun.IsEligible)
            {
                CurrentRun.Invalidate("BuildHelper mod: a brick was placed by ghost hand.");
                Log.LogInfo("World marked as practice (no leaderboard score): a brick was placed by ghost hand.");
            }
        }

        private void Update()
        {
            if (placement == null)
            {
                controller = null;
                if (Time.unscaledTime < nextSearch)
                {
                    return;
                }
                nextSearch = Time.unscaledTime + 1f;
                foreach (NetworkPlayerPlacementController p in FindObjectsByType<NetworkPlayerPlacementController>(FindObjectsSortMode.None))
                {
                    if (p.IsSpawned && p.IsOwner)
                    {
                        placement = p;
                        controller = ControllerOf(p);
                        Sites.Forget();
                        break;
                    }
                }
                if (placement == null)
                {
                    return;
                }
            }
            MissingMarkers.Tick();
            GhostHand.Tick();
            HudSlot.Tick(Cooldown, GhostHand.HasTarget);
            IInputReader input = controller != null ? InputOf(controller) : null;
            if (input == null || !input.GameplayInputEnabled)
            {
                return;
            }
            if (MissingKey.Value.IsDown())
            {
                MissingMarkers.Toggle();
            }
            else if (GhostKey.Value.IsDown())
            {
                GhostHand.Press(placement);
            }
        }

        private void OnGUI()
        {
            if (placement != null && ShowTimer.Value && !HudSlot.InBar)
            {
                TimeSpan left = Cooldown;
                string text = left > TimeSpan.Zero ? $"Ghost hand: {(int)left.TotalMinutes}:{left.Seconds:00}" : $"Ghost hand ready ({GhostKey.Value})";
                var style = new GUIStyle(GUI.skin.box) { fontSize = 15, alignment = TextAnchor.MiddleLeft };
                GUI.Box(new Rect(18f, Screen.height - 96f, 210f, 26f), text, style);
            }
            if (toast == null || Time.unscaledTime > toastUntil)
            {
                return;
            }
            var big = new GUIStyle(GUI.skin.box) { fontSize = 20, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            GUI.Box(new Rect(Screen.width / 2f - 440f, 220f, 880f, 64f), toast, big);
        }
    }

    /// <summary>The world's leaderboard run, so the ghost hand can mark the world as practice.</summary>
    [HarmonyPatch(typeof(CompletionRun), nameof(CompletionRun.Open))]
    internal static class CompletionRunPatch
    {
        private static void Postfix(CompletionRun __instance) => Plugin.CurrentRun = __instance;
    }
}
