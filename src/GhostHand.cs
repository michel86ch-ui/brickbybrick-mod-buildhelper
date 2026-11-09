using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MultiplayerTemplate.Core.Items;
using MultiplayerTemplate.Game.Placement;
using MultiplayerTemplate.Player;
using MultiplayerTemplate.Player.Carry;
using UnityEngine;

namespace BuildHelper
{
    /// <summary>
    /// F5: places the brick you carry into a free place you aim at, also through walls. Only places the game itself
    /// would take it in are offered (free, foundation in place, the right brick, you inside the site and within the
    /// game's reach); the game's own placement request does the rest, so the server checks it as usual.
    /// </summary>
    internal static class GhostHand
    {
        private const float ConfirmSeconds = 5f;
        private const float CheckSeconds = 1.5f;
        private const float MaxAimAngle = 20f;

        private static readonly AccessTools.FieldRef<NetworkPlayerPlacementController, NetworkPlayerCarryStack> CarryOf =
            AccessTools.FieldRefAccess<NetworkPlayerPlacementController, NetworkPlayerCarryStack>("carryStack");
        private static readonly AccessTools.FieldRef<NetworkPlayerPlacementController, IItemRegistry> ItemsOf =
            AccessTools.FieldRefAccess<NetworkPlayerPlacementController, IItemRegistry>("itemRegistry");
        private static readonly AccessTools.FieldRef<NetworkPlayerPlacementController, Transform> ViewOf =
            AccessTools.FieldRefAccess<NetworkPlayerPlacementController, Transform>("viewOrigin");
        private static readonly AccessTools.FieldRef<NetworkPlayerPlacementController, NetworkPlayerController> ControllerOf =
            AccessTools.FieldRefAccess<NetworkPlayerPlacementController, NetworkPlayerController>("playerController");
        /// <summary>(slot, forRemoval) up to game 1.4; since the November update also (…, throughConstruction).</summary>
        private static readonly MethodInfo SetFocusedSlot = AccessTools.Method(typeof(NetworkPlayerPlacementController), "SetFocusedSlot");
        private static readonly MethodInfo RequestFocusedIntent = AccessTools.Method(typeof(NetworkPlayerPlacementController), "RequestFocusedIntent");
        private static readonly object PlaceIntent = Enum.ToObject(RequestFocusedIntent.GetParameters()[0].ParameterType, 1);

        private static readonly List<Matrix4x4> marker = new List<Matrix4x4>();
        private static Sites.Site targetSite;
        private static PlacementSlot target;
        private static float targetUntil;
        private static Sites.Site sentSite;
        private static PlacementSlot sent;
        private static float checkAt;

        /// <summary>A place is marked and waits for the second press.</summary>
        internal static bool HasTarget => target != null;

        internal static void Tick()
        {
            if (target != null && Time.unscaledTime > targetUntil)
            {
                Clear();
            }
            if (target != null)
            {
                Markers.Draw(marker, Markers.Ghost);
            }
            if (sent != null && Time.unscaledTime >= checkAt)
            {
                if (Sites.IsTaken(sentSite, sent))
                {
                    Plugin.GhostHandUsed();
                    Plugin.Toast($"Ghost hand placed the brick. Next one in {Plugin.CooldownMinutes.Value} min.");
                }
                else
                {
                    Plugin.Toast("Ghost hand: the game did not take the brick; no waiting time.");
                }
                sent = null;
                sentSite = null;
            }
        }

        internal static void Press(NetworkPlayerPlacementController placement)
        {
            TimeSpan left = Plugin.Cooldown;
            if (left > TimeSpan.Zero)
            {
                Plugin.Toast($"Ghost hand is ready in {(int)left.TotalMinutes}:{left.Seconds:00}.", 3f);
                return;
            }
            if (sent != null || placement.HasPendingPlacement)
            {
                return;
            }
            ItemHandle item = Carried(placement);
            if (!item.IsValid)
            {
                Clear();
                Plugin.Toast("Ghost hand: carry the brick you want to place first.", 3f);
                return;
            }
            Vector3 feet = placement.transform.position;
            NetworkPlayerController controller = ControllerOf(placement);
            Vector3 reach = controller != null ? controller.GameplayInteractionOrigin : feet;   // the game measures its reach from here
            if (target != null && Time.unscaledTime <= targetUntil)
            {
                if (!Fits(targetSite, target, item, feet, reach))
                {
                    Clear();
                    Plugin.Toast("Ghost hand: the marked place no longer takes this brick (moved away, or the brick changed).", 4f);
                    return;
                }
                Place(placement, targetSite, target);
                return;
            }
            Transform view = ViewOf(placement);
            if (!Find(item, feet, reach, new Ray(view.position, view.forward), out targetSite, out target))
            {
                Clear();
                Plugin.Toast("Ghost hand: no free place for this brick within reach where you look.", 4f);
                return;
            }
            targetUntil = Time.unscaledTime + ConfirmSeconds;
            marker.Clear();
            Markers.Shape(target, marker);
            string practice = Plugin.IsHost && Plugin.CurrentRun != null && Plugin.CurrentRun.IsOpen && Plugin.CurrentRun.IsEligible
                ? " Placing makes this world practice (no leaderboard score)." : "";
            Plugin.Toast($"Ghost hand: place marked in cyan. Press {Plugin.GhostKey.Value} again to place it.{practice}", ConfirmSeconds);
        }

        private static void Place(NetworkPlayerPlacementController placement, Sites.Site site, PlacementSlot place)
        {
            Clear();
            SetFocusedSlot.Invoke(placement, SetFocusedSlot.GetParameters().Length >= 3 ? new object[] { place, false, true } : new object[] { place, false });
            RequestFocusedIntent.Invoke(placement, new[] { PlaceIntent });
            if (!placement.HasPendingPlacement)
            {
                Plugin.Toast("Ghost hand: the game refused to start the placement; no waiting time.", 4f);
                return;
            }
            sentSite = site;
            sent = place;
            checkAt = Time.unscaledTime + CheckSeconds;
        }

        private static void Clear()
        {
            target = null;
            targetSite = null;
            marker.Clear();
        }

        private static ItemHandle Carried(NetworkPlayerPlacementController placement)
        {
            NetworkPlayerCarryStack carry = CarryOf(placement);
            IItemRegistry items = ItemsOf(placement);
            if (carry != null && items != null && carry.HasSelection && items.TryResolveItem(carry.SelectedItemId, out ItemHandle item)
                && item.IsValid && item.Definition != null && item.Definition.CanBePlaced)
            {
                return item;
            }
            return default;
        }

        /// <summary>The same checks the game makes before placing, minus the line of sight, and only the right brick.</summary>
        private static bool Fits(Sites.Site site, PlacementSlot place, ItemHandle item, Vector3 feet, Vector3 reach) =>
            place != null && place.isActiveAndEnabled && !Sites.IsTaken(site, place) && place.IsWithinUseDistance(reach)
            && place.IsPlayerInside(feet) && place.IsPlacementAllowed && place.Accepts(item.Definition) && place.IsCorrectPlacement(item);

        /// <summary>The fitting place the view ray passes through first, else the one closest to the ray.</summary>
        private static bool Find(ItemHandle item, Vector3 feet, Vector3 reach, Ray ray, out Sites.Site site, out PlacementSlot place)
        {
            site = null;
            place = null;
            float bestHit = float.PositiveInfinity;
            float bestAngle = MaxAimAngle;
            Sites.Site angleSite = null;
            PlacementSlot anglePlace = null;
            foreach (Sites.Site s in Sites.All())
            {
                foreach (PlacementSlot p in s.Places)
                {
                    if (p == null || !p.IsWithinUseDistance(reach) || !Fits(s, p, item, feet, reach))
                    {
                        continue;
                    }
                    if (p.TryGetFocusDistance(ray, out float distance))
                    {
                        if (distance < bestHit)
                        {
                            bestHit = distance;
                            site = s;
                            place = p;
                        }
                        continue;
                    }
                    float angle = Vector3.Angle(ray.direction, p.Position - ray.origin);
                    if (angle < bestAngle)
                    {
                        bestAngle = angle;
                        angleSite = s;
                        anglePlace = p;
                    }
                }
            }
            if (place == null)
            {
                site = angleSite;
                place = anglePlace;
            }
            return place != null;
        }
    }
}
