using System.Collections.Generic;
using MultiplayerTemplate.Construction;
using MultiplayerTemplate.Game.Placement;
using UnityEngine;

namespace BuildHelper
{
    /// <summary>F8: the empty brick places of every site with only a few left, in magenta and through walls.</summary>
    internal static class MissingMarkers
    {
        private const float BeamHeight = 6f;
        private static readonly List<Matrix4x4> boxes = new List<Matrix4x4>();
        private static readonly List<Matrix4x4> beams = new List<Matrix4x4>();
        private static bool shown;
        private static float nextRefresh;

        internal static void Toggle()
        {
            shown = !shown;
            if (!shown)
            {
                boxes.Clear();
                beams.Clear();
                Plugin.Toast("Missing bricks hidden.", 3f);
                return;
            }
            if (!Markers.Available)
            {
                shown = false;
                Plugin.Toast("Missing bricks: cannot draw markers in this game version (see log).");
                return;
            }
            Plugin.Toast(Refresh(), 8f);
        }

        internal static void Tick()
        {
            if (!shown)
            {
                return;
            }
            if (Time.unscaledTime >= nextRefresh)
            {
                Refresh();
            }
            Markers.Draw(boxes, Markers.Missing);
            Markers.Draw(beams, Markers.Beam);
        }

        /// <summary>Collects the markers anew and says per site how many bricks are missing.</summary>
        private static string Refresh()
        {
            nextRefresh = Time.unscaledTime + 1f;
            boxes.Clear();
            beams.Clear();
            var lines = new List<string>();
            foreach (Sites.Site site in Sites.All())
            {
                ConstructionSiteState state = site.Component.State;
                int count = state != null ? state.SlotCount - state.OccupiedCount : 0;
                if (count <= 0)
                {
                    continue;
                }
                if (count > Plugin.MissingLimit.Value)
                {
                    lines.Add($"{site.Name} {count} (too many to show)");
                    continue;
                }
                lines.Add($"{site.Name} {count}");
                foreach (PlacementSlot place in site.Places)
                {
                    if (!Sites.IsTaken(site, place))
                    {
                        Markers.Shape(place, boxes);
                        beams.Add(Markers.BeamAt(place, BeamHeight));
                    }
                }
            }
            return lines.Count == 0 ? "Missing bricks: none, every site here is complete." : "Missing bricks: " + string.Join(", ", lines);
        }
    }
}
