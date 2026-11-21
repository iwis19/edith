using System.Collections.Generic;
using UnityEngine;

namespace Edith.Scrapbook
{
    /// The meshes the Places lens draws its maps on.
    public static class MapMeshes
    {
        /// A unit sphere whose texture coordinates are MercatorMath.UV, so the world image wraps it with every place
        /// at MercatorMath.Direction. The caps beyond the image's last latitude take its edge row. Faces point out.
        public static Mesh Globe(int columns = 48, int rows = 32)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (var row = 0; row <= rows; row++)
            {
                // The rows between the two poles are evenly spaced down the image, so the image is not bent between
                // them.
                var v = Mathf.Clamp01((row - 1) / (float)(rows - 2));
                var latitude = row == 0 ? -90.0 : row == rows ? 90.0 : MercatorMath.Point(0, (1.0 - v) * 256.0, 0).Lat;
                for (var column = 0; column <= columns; column++)
                {
                    var u = column / (float)columns;
                    vertices.Add(MercatorMath.Direction(new GeoPoint(latitude, u * 360.0 - 180.0)));
                    uvs.Add(new Vector2(u, v));
                }
            }
            for (var row = 0; row < rows; row++)
                for (var column = 0; column < columns; column++)
                {
                    var a = row * (columns + 1) + column;
                    var c = a + columns + 1;
                    // A pole's row is one point, so only one triangle of its band has any area.
                    if (row < rows - 1) triangles.AddRange(new[] { a + 1, c, c + 1 });
                    if (row > 0) triangles.AddRange(new[] { a, c, a + 1 });
                }

            var mesh = new Mesh { name = "Globe" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            return mesh;
        }

        /// A flat round map one unit across in the xy plane, facing -z, showing the circle inscribed in its image.
        public static Mesh Disc(int segments = 64)
        {
            var vertices = new List<Vector3> { Vector3.zero };
            var uvs = new List<Vector2> { new Vector2(0.5f, 0.5f) };
            var triangles = new List<int>();
            for (var i = 0; i <= segments; i++)
            {
                var angle = i * 2f * Mathf.PI / segments;
                var rim = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 0.5f;
                vertices.Add(rim);
                uvs.Add(rim + new Vector2(0.5f, 0.5f));
                if (i > 0) triangles.AddRange(new[] { 0, i + 1, i });
            }

            var mesh = new Mesh { name = "Disc" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            return mesh;
        }

        static readonly int MainTex = Shader.PropertyToID("_MainTex"), Tint = Shader.PropertyToID("_Color");
        static MaterialPropertyBlock block;

        /// A child of `parent` that draws `mesh` with `material`. Among the Scrapbook's transparent surfaces, a
        /// higher `order` is drawn later, over the others.
        public static MeshRenderer Add(Transform parent, string name, Mesh mesh, Material material, int order)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = order;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        /// Draws `renderer` with `texture`, or plain when it is null, tinted by `color`.
        public static void Paint(MeshRenderer renderer, Texture texture, Color color)
        {
            block ??= new MaterialPropertyBlock();
            block.SetTexture(MainTex, texture != null ? texture : Texture2D.whiteTexture);
            block.SetColor(Tint, color);
            renderer.SetPropertyBlock(block);
        }
    }
}
