using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MultiplayerTemplate.Construction;
using MultiplayerTemplate.Core.Items;
using MultiplayerTemplate.Game.Placement;
using UnityEngine;

namespace BuildHelper
{
    /// <summary>The construction sites of the loaded world with their brick places, and which places are taken.</summary>
    internal static class Sites
    {
        internal sealed class Site
        {
            internal ConstructionSite Component;
            internal string Name;
            internal PlacementSlot[] Places;
        }

        private static readonly AccessTools.FieldRef<ConstructionSiteState, Dictionary<PlacementSlotId, ItemInstanceId>> Occupants =
            AccessTools.FieldRefAccess<ConstructionSiteState, Dictionary<PlacementSlotId, ItemInstanceId>>("occupants");
        private static List<Site> cache;
        private static float cacheUntil;
        private static int logged = -1;

        internal static void Forget() => cache = null;

        /// <summary>Sites the game has set up and shows in this world; looked up anew every 10 s.</summary>
        internal static List<Site> All()
        {
            if (cache != null && Time.unscaledTime < cacheUntil && cache.All(s => s.Component != null))
            {
                return cache;
            }
            cacheUntil = Time.unscaledTime + 10f;
            cache = new List<Site>();
            foreach (ConstructionSite site in Object.FindObjectsByType<ConstructionSite>(FindObjectsSortMode.None))
            {
                if (!site.isActiveAndEnabled || site.State == null || site.Sections == null)
                {
                    continue;
                }
                PlacementSlot[] places = site.Sections.Where(sec => sec != null && sec.Slots != null).SelectMany(sec => sec.Slots)
                    .Where(cs => cs != null && cs.PlacementSlot != null).Select(cs => cs.PlacementSlot).ToArray();
                if (places.Length > 0)
                {
                    cache.Add(new Site { Component = site, Name = site.DisplayName, Places = places });
                }
            }
            int total = cache.Sum(s => s.Places.Length);
            if (total != logged)
            {
                logged = total;
                Plugin.Log.LogInfo($"{cache.Count} construction sites, {total} brick places.");
            }
            return cache;
        }

        internal static bool IsTaken(Site site, PlacementSlot place)
        {
            ConstructionSiteState state = site.Component != null ? site.Component.State : null;
            return state == null || Occupants(state).ContainsKey(place.SlotId);
        }
    }
}
