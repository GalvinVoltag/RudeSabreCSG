#if UNITY_EDITOR || RUNTIME_CSG

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using System.Reflection;
using UnityEngine.SceneManagement;
using System.IO;
using UnityEngine.Rendering;

namespace Sabresaurus.SabreCSG
{
    internal static class MeshGroupManager
    {
        internal static Action<GameObject, Mesh> OnFinalizeVisualMesh = null;
        internal static Action<GameObject, Mesh> OnFinalizeCollisionMesh = null;

        private const int MESH_VERTEX_LIMIT = 65500;

        internal static void Cleanup(Transform meshGroupHolder)
        {
            // Destroy all the old meshes to prevent them leaking
            MeshFilter[] filters = meshGroupHolder.GetComponentsInChildren<MeshFilter>();
            MeshCollider[] colliders = meshGroupHolder.GetComponentsInChildren<MeshCollider>();

            for (int i = 0; i < filters.Length; i++)
            {
#if UNITY_EDITOR
                if (filters[i].sharedMesh != null && !UnityEditor.AssetDatabase.Contains(filters[i].sharedMesh))
#endif
                {
#if UNITY_EDITOR && UNITY_2018_3_OR_NEWER
                    if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(filters[i].sharedMesh))
                    {
                        GameObject.DestroyImmediate(UnityEditor.PrefabUtility.GetCorrespondingObjectFromOriginalSource(filters[i].sharedMesh));
                    }
                    else
#endif
                    {
                        GameObject.DestroyImmediate(filters[i].sharedMesh);
                    }
                }
            }

            for (int i = 0; i < colliders.Length; i++)
            {
#if UNITY_EDITOR
                if (colliders[i].sharedMesh != null && !UnityEditor.AssetDatabase.Contains(colliders[i].sharedMesh))
#endif
                {
#if UNITY_EDITOR && UNITY_2018_3_OR_NEWER
                    if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(colliders[i].sharedMesh))
                    {
                        GameObject.DestroyImmediate(UnityEditor.PrefabUtility.GetCorrespondingObjectFromOriginalSource(colliders[i].sharedMesh));
                    }
                    else
#endif
                    {
                        GameObject.DestroyImmediate(colliders[i].sharedMesh);
                    }
                }
            }

            // Finally destroy the game objects and components
            meshGroupHolder.DestroyChildrenImmediate();
        }

        // Polygons that are built into the same mesh. Owner is null when meshes aren't being split
        private class MeshBucket
        {
            public readonly Transform Owner;
            public readonly Material Material;
            public readonly List<PolygonEntry> Polygons = new List<PolygonEntry>();

            public MeshBucket(Transform owner, Material material)
            {
                Owner = owner;
                Material = material;
            }

            public string GameObjectName
            {
                get { return Owner != null ? Owner.name + " - " + Material.name : "MaterialMesh"; }
            }
        }

        private struct MeshBucketKey : IEquatable<MeshBucketKey>
        {
            private readonly Transform owner;
            private readonly Material material;

            public MeshBucketKey(Transform owner, Material material)
            {
                this.owner = owner;
                this.material = material;
            }

            public bool Equals(MeshBucketKey other)
            {
                return owner == other.owner && material == other.material;
            }

            public override bool Equals(object obj)
            {
                return obj is MeshBucketKey && Equals((MeshBucketKey)obj);
            }

            public override int GetHashCode()
            {
                int ownerHash = owner != null ? owner.GetHashCode() : 0;
                return ownerHash * 397 ^ material.GetHashCode();
            }
        }

        private static List<MeshBucket> BucketPolygons(PolygonEntry[] polygonIndex, Transform[] polygonOwners, CSGBuildSettings buildSettings)
        {
            List<MeshBucket> buckets = new List<MeshBucket>();
            Dictionary<MeshBucketKey, MeshBucket> bucketsByKey = new Dictionary<MeshBucketKey, MeshBucket>();

            for (int i = 0; i < polygonIndex.Length; i++)
            {
                PolygonEntry entry = polygonIndex[i];

                if (!PolygonEntry.IsValid(entry)
                    || entry.Positions.Length == 0
                    || entry.ExcludeFromBuild) // Skip polygons that weren't built
                {
                    continue;
                }

                Material material = entry.Material != null ? entry.Material : buildSettings.DefaultVisualMaterial;
                Transform owner = polygonOwners != null ? polygonOwners[i] : null;

                MeshBucketKey key = new MeshBucketKey(owner, material);
                MeshBucket bucket;
                if (!bucketsByKey.TryGetValue(key, out bucket))
                {
                    bucket = new MeshBucket(owner, material);
                    bucketsByKey.Add(key, bucket);
                    buckets.Add(bucket);
                }

                bucket.Polygons.Add(entry);
            }

            return buckets;
        }

        internal static void BuildVisual(Transform meshGroupHolder,
            PolygonEntry[] polygonIndex,
            Transform[] polygonOwners,
            CSGBuildSettings buildSettings,
            CSGBuildContext.BuildContext buildContext,
            MaterialMeshDictionary materialMeshDictionary)
        {
            materialMeshDictionary.Clear();

            // Reset statistics
            buildContext.buildMetrics.TotalMeshes = 0;
            buildContext.buildMetrics.TotalVertices = 0;
            buildContext.buildMetrics.TotalTriangles = 0;

            foreach (MeshBucket bucket in BucketPolygons(polygonIndex, polygonOwners, buildSettings))
            {
                Mesh mesh = new Mesh();

                List<Vector3> positionsList = new List<Vector3>();
                List<Vector3> normalsList = new List<Vector3>();
                List<Vector2> uvList = new List<Vector2>();
                List<Color32> colorsList = new List<Color32>();
                List<int> trianglesList = new List<int>();

                for (int i = 0; i < bucket.Polygons.Count; i++)
                {
                    int positionOffset = positionsList.Count;
                    int triangleOffset = trianglesList.Count;

                    PolygonEntry polygonEntry = bucket.Polygons[i];
                    if (polygonEntry.Positions.Length + positionOffset > MESH_VERTEX_LIMIT)
                    {
                        FinalizeVisualMesh(meshGroupHolder, mesh, bucket.Material, bucket.GameObjectName, buildSettings, buildContext, positionsList, normalsList, uvList, colorsList, trianglesList, materialMeshDictionary);
                        mesh = new Mesh();
                        positionsList.Clear();
                        normalsList.Clear();
                        uvList.Clear();
                        colorsList.Clear();
                        trianglesList.Clear();
                        positionOffset = 0;
                    }
                    positionsList.AddRange(polygonEntry.Positions);
                    normalsList.AddRange(polygonEntry.Normals);
                    uvList.AddRange(polygonEntry.UV);
                    colorsList.AddRange(polygonEntry.Colors);

                    for (int j = 0; j < polygonEntry.Triangles.Length; j++)
                    {
                        trianglesList.Add(polygonEntry.Triangles[j] + positionOffset);
                    }

                    bucket.Polygons[i].BuiltMesh = mesh;
                    bucket.Polygons[i].BuiltVertexOffset = positionOffset;
                    bucket.Polygons[i].BuiltTriangleOffset = triangleOffset;
                }

                FinalizeVisualMesh(meshGroupHolder, mesh, bucket.Material, bucket.GameObjectName, buildSettings, buildContext, positionsList, normalsList, uvList, colorsList, trianglesList, materialMeshDictionary);
            }
        }

        internal static void FinalizeVisualMesh(Transform meshGroupHolder,
                        Mesh mesh,
                        Material material,
                        string gameObjectName,
                        CSGBuildSettings buildSettings,
                        CSGBuildContext.BuildContext buildContext,
                        List<Vector3> positionsList,
                        List<Vector3> normalsList,
                        List<Vector2> uvList,
                        List<Color32> colorsList,
                        List<int> trianglesList,
                        MaterialMeshDictionary materialMeshDictionary)
        {
            // Optional: split large faces into a grid so per vertex lighting has enough vertices
            if (buildSettings.SubdivideVisualMesh && buildSettings.SubdivisionCellSize > 0f)
            {
                VertexDensitySubdivision.Subdivide(positionsList, normalsList, uvList, colorsList, trianglesList, buildSettings.SubdivisionCellSize);
            }

            Vector3[] positionsArray = new Vector3[positionsList.Count];
            Vector3[] normalsArray = new Vector3[normalsList.Count];
            Vector2[] uvArray = new Vector2[uvList.Count];
            Color32[] colorsArray = new Color32[colorsList.Count];
            int[] trianglesArray = new int[trianglesList.Count];

            positionsList.CopyTo(positionsArray);
            normalsList.CopyTo(normalsArray);
            uvList.CopyTo(uvArray);
            trianglesList.CopyTo(trianglesArray);
            colorsList.CopyTo(colorsArray);

            // Subdivision can push a mesh past the 16 bit index limit
            if (positionsArray.Length > 65000)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }

            mesh.vertices = positionsArray;
            mesh.normals = normalsArray;
            mesh.uv = uvArray;
            mesh.colors32 = colorsArray;

            if (meshGroupHolder.position != Vector3.zero
                || meshGroupHolder.rotation != Quaternion.identity
                || meshGroupHolder.lossyScale != Vector3.one)
            {
                mesh.LocalizeToTransform(meshGroupHolder);
            }

            mesh.triangles = trianglesArray;

            if (buildSettings.GenerateTangents)
            {
                // Generate tangents, necessary for some shaders
                mesh.GenerateTangents();
            }

            buildContext.buildMetrics.TotalMeshes++;
            buildContext.buildMetrics.TotalVertices += positionsArray.Length;
            buildContext.buildMetrics.TotalTriangles += trianglesArray.Length / 3;

            GameObject newGameObject = new GameObject(gameObjectName);

            // Allow any editor dependent code to fire, e.g. lightmap unwrapping, static flags
            if (OnFinalizeVisualMesh != null)
            {
                OnFinalizeVisualMesh(newGameObject, mesh);
            }

            newGameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer meshRenderer = newGameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = buildSettings.ShadowCastingMode;
            meshRenderer.reflectionProbeUsage = buildSettings.ReflectionProbeUsage;
            //newGameObject.transform.parent = meshGroupHolder;

            newGameObject.transform.SetParent(meshGroupHolder, false);

#if UNITY_EDITOR
            if (buildSettings.SaveMeshesAsAssets)
            {
                // Make sure the folder exists to save into
                string path = SceneManager.GetActiveScene().path;
                path = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path));
                if (!Directory.Exists(path))
                {
                    UnityEditor.AssetDatabase.CreateFolder(Path.GetDirectoryName(path), Path.GetFileName(path));
                }
                // Save to a file rather than leaving it as a scene asset
                UnityEditor.AssetDatabase.CreateAsset(mesh, Path.Combine(path, "VisualMesh" + materialMeshDictionary.MeshCount + ".asset"));
            }
#endif

            materialMeshDictionary.Add(material, mesh, newGameObject);
        }

        internal static void BuildCollision(Transform meshGroupHolder,
            PolygonEntry[] polygonIndex,
            CSGBuildSettings buildSettings,
            List<Mesh> collisionMeshDictionary)
        {
            collisionMeshDictionary.Clear();

            if (polygonIndex.Length > 0)
            {
                Mesh mesh = new Mesh();
                List<Vector3> positionsList = new List<Vector3>();
                List<Vector3> normalsList = new List<Vector3>();
                List<Vector2> uvList = new List<Vector2>();
                List<int> trianglesList = new List<int>();

                for (int i = 0; i < polygonIndex.Length; i++)
                {
                    if (polygonIndex[i] != null)
                    {
                        int positionOffset = positionsList.Count;
                        int triangleOffset = trianglesList.Count;

                        PolygonEntry polygonEntry = polygonIndex[i];

                        if (PolygonEntry.IsValid(polygonEntry)
                            && polygonEntry.Positions.Length > 0
                            && !polygonEntry.ExcludeFromBuild) // Skip polygons that weren't built
                        {
                            if (polygonEntry.Positions.Length + positionOffset > MESH_VERTEX_LIMIT)
                            {
                                FinalizeCollisionMesh(meshGroupHolder, mesh, buildSettings, positionsList, normalsList, uvList, trianglesList, collisionMeshDictionary);
                                mesh = new Mesh();
                                positionsList.Clear();
                                normalsList.Clear();
                                uvList.Clear();
                                trianglesList.Clear();
                                positionOffset = 0;
                            }
                            positionsList.AddRange(polygonEntry.Positions);
                            normalsList.AddRange(polygonEntry.Normals);
                            uvList.AddRange(polygonEntry.UV);

                            for (int j = 0; j < polygonEntry.Triangles.Length; j++)
                            {
                                trianglesList.Add(polygonEntry.Triangles[j] + positionOffset);
                            }

                            polygonEntry.BuiltMesh = mesh;
                            polygonEntry.BuiltVertexOffset = positionOffset;
                            polygonEntry.BuiltTriangleOffset = triangleOffset;
                        }
                    }
                }
                FinalizeCollisionMesh(meshGroupHolder, mesh, buildSettings, positionsList, normalsList, uvList, trianglesList, collisionMeshDictionary);
            }
        }

        internal static void FinalizeCollisionMesh(Transform meshGroupHolder,
            Mesh mesh,
            CSGBuildSettings buildSettings,
            List<Vector3> positionsList,
            List<Vector3> normalsList,
            List<Vector2> uvList,
            List<int> trianglesList,
            List<Mesh> collisionMeshDictionary)
        {
            Vector3[] positionsArray = new Vector3[positionsList.Count];
            Vector3[] normalsArray = new Vector3[normalsList.Count];
            Vector2[] uvArray = new Vector2[uvList.Count];
            int[] trianglesArray = new int[trianglesList.Count];

            positionsList.CopyTo(positionsArray);
            normalsList.CopyTo(normalsArray);
            uvList.CopyTo(uvArray);
            trianglesList.CopyTo(trianglesArray);

            mesh.vertices = positionsArray;
            mesh.normals = normalsArray;
            mesh.uv = uvArray;

            if (meshGroupHolder.position != Vector3.zero
                || meshGroupHolder.rotation != Quaternion.identity
                || meshGroupHolder.lossyScale != Vector3.one)
            {
                mesh.LocalizeToTransform(meshGroupHolder);
            }

            mesh.triangles = trianglesArray;

            GameObject newGameObject = new GameObject("CollisionMesh");

            // Allow any editor dependent code to fire, e.g. assigning physics materials
            if (OnFinalizeCollisionMesh != null)
            {
                OnFinalizeCollisionMesh(newGameObject, mesh);
            }

            MeshCollider meshCollider = newGameObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;
            // Assign the physics material from build settings
            meshCollider.sharedMaterial = buildSettings.DefaultPhysicsMaterial;
            // Reparent
            newGameObject.transform.SetParent(meshGroupHolder, false);//.parent = meshGroupHolder;

#if UNITY_EDITOR
            if (buildSettings.SaveMeshesAsAssets)
            {
                // Make sure the folder exists to save into
                string path = SceneManager.GetActiveScene().path;
                path = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path));
                if (!Directory.Exists(path))
                {
                    UnityEditor.AssetDatabase.CreateFolder(Path.GetDirectoryName(path), Path.GetFileName(path));
                }
                // Save to a file rather than leaving it as a scene asset
                UnityEditor.AssetDatabase.CreateAsset(mesh, Path.Combine(path, "CollisionMesh" + collisionMeshDictionary.Count + ".asset"));
            }
#endif

            collisionMeshDictionary.Add(mesh);
        }

        internal static void TriangulateNewPolygons(bool individualVertices, Dictionary<int, List<Polygon>> groupedPolygons, PolygonEntry[] polygonIndex)
        {
            foreach (KeyValuePair<int, List<Polygon>> row in groupedPolygons)
            {
                Vector3[] newPositions;
                Vector3[] newNormals;
                Vector2[] newUV;
                Color32[] newColors;
                int[] newTriangles;

                List<Polygon> polygons = row.Value;

                TriangulatePolygons(individualVertices, polygons, out newTriangles, out newPositions, out newNormals, out newUV, out newColors);

                polygonIndex[row.Key] = new PolygonEntry(newPositions, newNormals, newUV, newColors, newTriangles, polygons[0].Material, polygons[0].UserExcludeFromFinal);
            }
        }

        internal static void TriangulatePolygons(bool individualVertices, List<Polygon> polygons, out int[] triangesToAppend, out Vector3[] positions, out Vector3[] normals, out Vector2[] uv, out Color32[] colors)
        {
            if (individualVertices)
            {
                int totalTriangleCount = 0;
                for (int i = 0; i < polygons.Count; i++)
                {
                    totalTriangleCount += polygons[i].Vertices.Length - 2;
                }
                int totalVertexCount = totalTriangleCount * 3;

                positions = new Vector3[totalVertexCount];
                normals = new Vector3[totalVertexCount];
                uv = new Vector2[totalVertexCount];
                colors = new Color32[totalVertexCount];

                triangesToAppend = new int[totalTriangleCount * 3];

                int triangleOffset = 0;
                int vertexOffset = 0;

                // Calculate triangulation
                for (int i = 0; i < polygons.Count; i++)
                {
                    Polygon polygon = polygons[i];
                    int triangleCount = polygons[i].Vertices.Length - 2;

                    for (int j = 0; j < triangleCount; j++)
                    {
                        int sourceIndex = 0;

                        positions[vertexOffset + j * 3] = polygon.Vertices[sourceIndex].Position;
                        normals[vertexOffset + j * 3] = polygon.Vertices[sourceIndex].Normal;
                        uv[vertexOffset + j * 3] = polygon.Vertices[sourceIndex].UV;
                        colors[vertexOffset + j * 3] = polygon.Vertices[sourceIndex].Color;

                        sourceIndex = j + 1;

                        positions[vertexOffset + j * 3 + 1] = polygon.Vertices[sourceIndex].Position;
                        normals[vertexOffset + j * 3 + 1] = polygon.Vertices[sourceIndex].Normal;
                        uv[vertexOffset + j * 3 + 1] = polygon.Vertices[sourceIndex].UV;
                        colors[vertexOffset + j * 3 + 1] = polygon.Vertices[sourceIndex].Color;

                        sourceIndex = j + 2;

                        positions[vertexOffset + j * 3 + 2] = polygon.Vertices[sourceIndex].Position;
                        normals[vertexOffset + j * 3 + 2] = polygon.Vertices[sourceIndex].Normal;
                        uv[vertexOffset + j * 3 + 2] = polygon.Vertices[sourceIndex].UV;
                        colors[vertexOffset + j * 3 + 2] = polygon.Vertices[sourceIndex].Color;
                    }

                    for (int j = 0; j < triangleCount; j++)
                    {
                        triangesToAppend[triangleOffset + 0] = triangleOffset + 0;
                        triangesToAppend[triangleOffset + 1] = triangleOffset + 1;
                        triangesToAppend[triangleOffset + 2] = triangleOffset + 2;

                        triangleOffset += 3;
                    }

                    vertexOffset += triangleCount * 3;
                }
            }
            else
            {
                int totalVertexCount = 0;
                for (int i = 0; i < polygons.Count; i++)
                {
                    totalVertexCount += polygons[i].Vertices.Length;
                }

                int totalTriangleCount = totalVertexCount - 2 * polygons.Count;

                positions = new Vector3[totalVertexCount];
                normals = new Vector3[totalVertexCount];
                uv = new Vector2[totalVertexCount];
                colors = new Color32[totalVertexCount];

                triangesToAppend = new int[totalTriangleCount * 3];

                int triangleOffset = 0;
                int vertexOffset = 0;

                // Calculate triangulation
                for (int i = 0; i < polygons.Count; i++)
                {
                    Polygon polygon = polygons[i];
                    int vertexCount = polygon.Vertices.Length;

                    for (int j = 0; j < vertexCount; j++)
                    {
                        positions[vertexOffset + j] = polygon.Vertices[j].Position;
                        normals[vertexOffset + j] = polygon.Vertices[j].Normal;
                        uv[vertexOffset + j] = polygon.Vertices[j].UV;
                        colors[vertexOffset + j] = polygon.Vertices[j].Color;
                    }

                    for (int j = 2; j < vertexCount; j++)
                    {
                        triangesToAppend[triangleOffset + 0] = vertexOffset + (0);
                        triangesToAppend[triangleOffset + 1] = vertexOffset + (j - 1);
                        triangesToAppend[triangleOffset + 2] = vertexOffset + (j);
                        triangleOffset += 3;
                    }

                    vertexOffset += vertexCount;
                }
            }
        }
    }
}

#endif