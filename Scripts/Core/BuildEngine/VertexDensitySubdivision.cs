#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace Sabresaurus.SabreCSG
{
    /// <summary>
    /// Splits the generated visual mesh along a world space grid. Surfaces that span many units
    /// end up with extra vertices so per vertex lighting (e.g. ULTRAKILL) has something to
    /// interpolate between. UVs, normals and vertex colours are interpolated across the cuts, so
    /// the surface itself is unchanged apart from the extra vertices.
    /// </summary>
    internal static class VertexDensitySubdivision
    {
        private const float EPSILON = 0.0001f;

        private struct VertexData
        {
            public Vector3 Position;
            public Vector3 Normal;
            public Vector2 UV;
            public Color32 Color;

            public VertexData(Vector3 position, Vector3 normal, Vector2 uv, Color32 color)
            {
                Position = position;
                Normal = normal;
                UV = uv;
                Color = color;
            }

            public static VertexData Lerp(VertexData a, VertexData b, float t)
            {
                Vector3 normal = Vector3.Lerp(a.Normal, b.Normal, t);
                // Guard against degenerate normals producing NaNs
                if (normal.sqrMagnitude > 0.000001f)
                {
                    normal.Normalize();
                }
                else
                {
                    normal = a.Normal;
                }

                return new VertexData(
                    Vector3.Lerp(a.Position, b.Position, t),
                    normal,
                    Vector2.Lerp(a.UV, b.UV, t),
                    Color32.Lerp(a.Color, b.Color, t));
            }
        }

        /// <summary>
        /// Replaces the supplied mesh buffers with a subdivided version. Safe to call with a
        /// cellSize of zero or less, in which case the buffers are left untouched.
        /// </summary>
        internal static void Subdivide(
            List<Vector3> positions,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<Color32> colors,
            List<int> triangles,
            float cellSize)
        {
            if (cellSize <= 0f || triangles == null || triangles.Count < 3 || positions.Count < 3)
            {
                return;
            }

            bool hasNormals = normals != null && normals.Count >= positions.Count;
            bool hasUVs = uvs != null && uvs.Count >= positions.Count;
            bool hasColors = colors != null && colors.Count >= positions.Count;

            List<VertexData> source = new List<VertexData>(triangles.Count);
            List<int> sourceTriangles = new List<int>(triangles);

            for (int triangleIndex = 0; triangleIndex + 2 < sourceTriangles.Count; triangleIndex += 3)
            {
                for (int corner = 0; corner < 3; corner++)
                {
                    int index = sourceTriangles[triangleIndex + corner];

                    source.Add(new VertexData(
                        positions[index],
                        hasNormals ? normals[index] : Vector3.up,
                        hasUVs ? uvs[index] : Vector2.zero,
                        hasColors ? colors[index] : (Color32)Color.white));
                }
            }

            positions.Clear();
            if (hasNormals) normals.Clear();
            if (hasUVs) uvs.Clear();
            if (hasColors) colors.Clear();
            triangles.Clear();

            Stack<List<VertexData>> pending = new Stack<List<VertexData>>();

            for (int i = 0; i + 2 < source.Count; i += 3)
            {
                pending.Push(new List<VertexData>() { source[i], source[i + 1], source[i + 2] });
            }

            List<VertexData> front = new List<VertexData>(8);
            List<VertexData> back = new List<VertexData>(8);

            while (pending.Count > 0)
            {
                List<VertexData> polygon = pending.Pop();

                int axis;
                float splitCoordinate;

                if (polygon.Count < 3 || !NeedsSplit(polygon, cellSize, out axis, out splitCoordinate))
                {
                    Emit(polygon, positions, normals, uvs, colors, triangles, hasNormals, hasUVs, hasColors);
                    continue;
                }

                Split(polygon, axis, splitCoordinate, front, back);

                // If the split failed to produce two real polygons, emit the original
                if (front.Count < 3 || back.Count < 3)
                {
                    Emit(polygon, positions, normals, uvs, colors, triangles, hasNormals, hasUVs, hasColors);
                    continue;
                }

                pending.Push(new List<VertexData>(front));
                pending.Push(new List<VertexData>(back));
            }
        }

        private static float GetComponent(Vector3 vector, int axis)
        {
            switch (axis)
            {
                case 0: return vector.x;
                case 1: return vector.y;
                default: return vector.z;
            }
        }

        /// <summary>
        /// Returns true if the polygon is larger than the cell size along any axis, and if so
        /// outputs which axis to cut and at what world coordinate.
        /// </summary>
        private static bool NeedsSplit(List<VertexData> polygon, float cellSize, out int axis, out float splitCoordinate)
        {
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

            for (int i = 0; i < polygon.Count; i++)
            {
                Vector3 position = polygon[i].Position;

                if (position.x < minX) minX = position.x;
                if (position.y < minY) minY = position.y;
                if (position.z < minZ) minZ = position.z;
                if (position.x > maxX) maxX = position.x;
                if (position.y > maxY) maxY = position.y;
                if (position.z > maxZ) maxZ = position.z;
            }

            float extentX = maxX - minX;
            float extentY = maxY - minY;
            float extentZ = maxZ - minZ;

            // Cut across the longest axis first so we converge quickly and don't create slivers
            if (extentX >= extentY && extentX >= extentZ)
            {
                axis = 0;
                splitCoordinate = ChooseSplitCoordinate(minX, maxX, cellSize);
                return extentX > cellSize;
            }
            else if (extentY >= extentZ)
            {
                axis = 1;
                splitCoordinate = ChooseSplitCoordinate(minY, maxY, cellSize);
                return extentY > cellSize;
            }
            else
            {
                axis = 2;
                splitCoordinate = ChooseSplitCoordinate(minZ, maxZ, cellSize);
                return extentZ > cellSize;
            }
        }

        /// <summary>
        /// Picks a cut position that lies on the global grid, falling back to the midpoint when the
        /// span is too small to contain a grid line.
        /// </summary>
        private static float ChooseSplitCoordinate(float min, float max, float cellSize)
        {
            float midpoint = (min + max) * 0.5f;

            // Snap the midpoint down onto the grid
            float snapped = Mathf.Floor(midpoint / cellSize) * cellSize;

            if (snapped <= min + EPSILON || snapped >= max - EPSILON)
            {
                return midpoint;
            }

            return snapped;
        }

        private static void Split(List<VertexData> polygon, int axis, float splitCoordinate, List<VertexData> front, List<VertexData> back)
        {
            front.Clear();
            back.Clear();

            for (int i = 0; i < polygon.Count; i++)
            {
                VertexData current = polygon[i];
                VertexData next = polygon[(i + 1) % polygon.Count];

                float currentDistance = GetComponent(current.Position, axis) - splitCoordinate;
                float nextDistance = GetComponent(next.Position, axis) - splitCoordinate;

                if (currentDistance <= EPSILON)
                {
                    back.Add(current);
                }
                if (currentDistance >= -EPSILON)
                {
                    front.Add(current);
                }

                bool crosses = (currentDistance < -EPSILON && nextDistance > EPSILON)
                    || (currentDistance > EPSILON && nextDistance < -EPSILON);

                if (crosses)
                {
                    float t = currentDistance / (currentDistance - nextDistance);
                    VertexData intersection = VertexData.Lerp(current, next, t);
                    front.Add(intersection);
                    back.Add(intersection);
                }
            }
        }

        private static void Emit(
            List<VertexData> polygon,
            List<Vector3> positions,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<Color32> colors,
            List<int> triangles,
            bool hasNormals,
            bool hasUVs,
            bool hasColors)
        {
            if (polygon.Count < 3)
            {
                return;
            }

            if (polygon.Count == 3)
            {
                AddTriangle(polygon[0], polygon[1], polygon[2], positions, normals, uvs, colors, triangles, hasNormals, hasUVs, hasColors);
                return;
            }

            // Convex polygon, so a triangle fan is safe
            for (int i = 1; i + 1 < polygon.Count; i++)
            {
                AddTriangle(polygon[0], polygon[i], polygon[i + 1], positions, normals, uvs, colors, triangles, hasNormals, hasUVs, hasColors);
            }
        }

        private static void AddTriangle(
            VertexData a,
            VertexData b,
            VertexData c,
            List<Vector3> positions,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<Color32> colors,
            List<int> triangles,
            bool hasNormals,
            bool hasUVs,
            bool hasColors)
        {
            Vector3 cross = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);

            // Skip slivers and anything that collapsed onto a line
            if (cross.sqrMagnitude < 0.000000001f)
            {
                return;
            }

            int offset = positions.Count;

            positions.Add(a.Position);
            positions.Add(b.Position);
            positions.Add(c.Position);

            if (hasNormals)
            {
                normals.Add(a.Normal);
                normals.Add(b.Normal);
                normals.Add(c.Normal);
            }
            if (hasUVs)
            {
                uvs.Add(a.UV);
                uvs.Add(b.UV);
                uvs.Add(c.UV);
            }
            if (hasColors)
            {
                colors.Add(a.Color);
                colors.Add(b.Color);
                colors.Add(c.Color);
            }

            triangles.Add(offset);
            triangles.Add(offset + 1);
            triangles.Add(offset + 2);
        }
    }
}
#endif
