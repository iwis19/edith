using System.Collections.Generic;
using UnityEngine;

namespace Edith.UI
{
    /// A flat rectangle in the world: centred at `Pose.position`, `Size` metres wide and high in the plane `Pose.rotation`
    /// turns to face along +z.
    public readonly struct PanelQuad
    {
        public readonly Pose Pose;
        public readonly Vector2 Size;

        public PanelQuad(Pose pose, Vector2 size)
        {
            Pose = pose;
            Size = size;
        }

        /// The quad `rect` covers, with its scale applied.
        public static PanelQuad Of(RectTransform rect)
        {
            var r = rect.rect;
            var scale = rect.lossyScale;
            return new PanelQuad(new Pose(rect.TransformPoint(r.center), rect.rotation),
                new Vector2(r.width * Mathf.Abs(scale.x), r.height * Mathf.Abs(scale.y)));
        }
    }

    /// A panel that takes part of the view while it shows.
    public interface IShownPanel
    {
        /// Adds the quads the panel covers right now, none while it is hidden.
        void AddQuads(List<PanelQuad> into);
    }

    /// The panels other UI keeps clear of, such as EDITH's answer orb. Each panel adds itself while enabled.
    public static class ShownPanels
    {
        static readonly List<IShownPanel> panels = new List<IShownPanel>();

        public static void Add(IShownPanel panel)
        {
            if (!panels.Contains(panel)) panels.Add(panel);
        }

        public static void Remove(IShownPanel panel) => panels.Remove(panel);

        /// Fills `into` with every quad the panels cover now.
        public static void Gather(List<PanelQuad> into)
        {
            into.Clear();
            for (var i = 0; i < panels.Count; i++) panels[i].AddQuads(into);
        }
    }
}
