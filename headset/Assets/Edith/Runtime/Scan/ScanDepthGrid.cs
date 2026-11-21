using UnityEngine;

namespace Edith.Scan
{
    /// How far the room is along the sight lines of one camera frame, gathered while its reply is awaited. Depth only
    /// covers what the headset faces at that moment, so a pin placed when the reply lands would lose its distance if
    /// the head had turned away by then. Each cell keeps the farthest depth it was given: a hand still raised from the
    /// pose reads nearer than the surface behind it, and gives way once it drops.
    public sealed class ScanDepthGrid
    {
        readonly float[] meters;

        public readonly int Columns, Rows;

        public ScanDepthGrid(int columns, int rows)
        {
            Columns = columns;
            Rows = rows;
            meters = new float[columns * rows];
        }

        public int Count => meters.Length;

        /// How many cells have a depth.
        public int Known
        {
            get
            {
                var known = 0;
                foreach (var depth in meters)
                    if (depth > 0f) known++;
                return known;
            }
        }

        /// The middle of `cell` in the frame's viewport.
        public Vector2 Viewport(int cell) => new Vector2((cell % Columns + 0.5f) / Columns, (cell / Columns + 0.5f) / Rows);

        /// `depth` is measured along the camera's forward axis, not along the sight line.
        public void Record(int cell, float depth)
        {
            if (depth > meters[cell]) meters[cell] = depth;
        }

        /// The depth of the cell `viewport` falls in, or null when it has none yet.
        public float? At(Vector2 viewport)
        {
            var column = Mathf.Clamp(Mathf.FloorToInt(viewport.x * Columns), 0, Columns - 1);
            var row = Mathf.Clamp(Mathf.FloorToInt(viewport.y * Rows), 0, Rows - 1);
            var depth = meters[row * Columns + column];
            return depth > 0f ? depth : (float?)null;
        }
    }
}
