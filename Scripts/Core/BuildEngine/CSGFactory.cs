#if UNITY_EDITOR || RUNTIME_CSG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using Object = System.Object;

namespace Sabresaurus.SabreCSG
{
    public enum BuildStatus { Unnecessary, Started, Complete };

    public static class CSGFactory
    {
        const bool ENABLE_OPTIMIZE_GEOMETRY = false;

		static List<Brush> brushes;
		static CSGBuildSettings buildSettings;
		static CSGBuildContext.BuildContext buildContext;
		static Transform rootTransform;
		static MaterialMeshDictionary materialMeshDictionary;
		static List<Mesh> collisionMeshDictionary;
		static bool polygonsRemoved;
		static bool forceRebuild;
		static Action<float> onProgressChange;
		static Action<GameObject, Mesh> onFinalizeVisualMesh;
		static Action<GameObject, Mesh> onFinalizeCollisionMesh;

		static DateTime buildStartTime = DateTime.Now;
		static DateTime time1 = DateTime.Now;
		static int brushesBuilt = 0;

		static bool buildInProgress = false;
		static bool readyForFinalize = false;

		private static Transform _meshGroup;
		static Transform meshGroup
		{
			get
			{
				if (_meshGroup != null) return _meshGroup;
				_meshGroup = rootTransform.Find("MeshGroup");
				if (meshGroup == null)
				{
					_meshGroup = new GameObject("MeshGroup").transform;
					_meshGroup.parent = rootTransform;
				}
				return _meshGroup;
			}
		}
		static Transform meshGroupHolder;

        static bool Prepare(Transform rootTransform)
		{
			meshGroupHolder = rootTransform.Find("MeshGroup");

			if (meshGroupHolder != null)
			{
				meshGroupHolder.SetAsLastSibling();
				return false;
			}
			else
			{
				meshGroupHolder = rootTransform.AddChild("MeshGroup");
				return true;
			}
		}



		internal static BuildStatus Build(List<Brush> brushes, 
			CSGBuildSettings buildSettings,
			CSGBuildContext.BuildContext buildContext, 
			Transform rootTransform,
			MaterialMeshDictionary materialMeshDictionary,
			List<Mesh> collisionMeshDictionary,
			bool polygonsRemoved,
			bool forceRebuild,
			Action<float> onProgressChange,
			Action<GameObject, Mesh> onFinalizeVisualMesh,
			Action<GameObject, Mesh> onFinalizeCollisionMesh,
			bool multithreaded)
		{
			CSGFactory.brushes = brushes;
			CSGFactory.buildSettings = buildSettings;
			CSGFactory.buildContext = buildContext;
			CSGFactory.rootTransform = rootTransform; 
			CSGFactory.materialMeshDictionary = materialMeshDictionary;
			CSGFactory.collisionMeshDictionary = collisionMeshDictionary;
			CSGFactory.polygonsRemoved = polygonsRemoved;
			CSGFactory.forceRebuild = forceRebuild;
			CSGFactory.onProgressChange = onProgressChange;
			CSGFactory.onFinalizeVisualMesh = onFinalizeVisualMesh;
			CSGFactory.onFinalizeCollisionMesh = onFinalizeCollisionMesh;

//			DebugExclude.hackyHolder = DebugExclude.GetMetaDataHolder();
//			BrushChunk.ResetNextID();

			if(buildInProgress)
			{
				Debug.LogWarning("Existing build has not completed");
			}

			if(multithreaded)
			{
				readyForFinalize = false;
				buildInProgress = true;
				ThreadPool.QueueUserWorkItem(CoreBuild, true);
				// FinalizeBuild is called by Tick when CoreBuild is complete
				return BuildStatus.Started;
			}
			else
			{
				readyForFinalize = false;
				CoreBuild(null);
				bool buildOccurred = FinalizeBuild();
				buildInProgress = false;

				return buildOccurred ? BuildStatus.Complete : BuildStatus.Unnecessary;
			}
		}
		
		
		internal static void CoreBuild(object state)
		{
			MeshGroupManager.OnFinalizeVisualMesh = onFinalizeVisualMesh;
			MeshGroupManager.OnFinalizeCollisionMesh = onFinalizeCollisionMesh;

			if(forceRebuild)
			{				
				buildContext.ClearAll();
				meshGroup.DestroyChildrenImmediate();
			}

			buildStartTime = DateTime.Now;

			int brushesToBuild = 0;
			brushesBuilt = 0;

			BrushCache[] allBrushCaches = new BrushCache[brushes.Count];

			int totalPolygons = 0;
			for (int i = 0; i < allBrushCaches.Length; i++) 
			{
				allBrushCaches[i] = brushes[i].BrushCache;
				totalPolygons += allBrushCaches[i].Polygons.Length;
			}

			PolygonEntry[] oldVisualPolygonIndex = buildContext.VisualPolygonIndex;
			PolygonEntry[] oldCollisionPolygonIndex = buildContext.CollisionPolygonIndex;

			PolygonEntry[] newVisualPolygonIndex = new PolygonEntry[totalPolygons];
			PolygonEntry[] newCollisionPolygonIndex = new PolygonEntry[totalPolygons];

			int polygonUniqueID = 0;

			// Walk through each brush, assigning unique IDs to each polygon (since Unity serialisation will break
			// references on a recompile, reload etc)
			for (int i = 0; i < allBrushCaches.Length; i++) 
			{
				brushes[i].AssignUniqueIDs(polygonUniqueID);

				// TODO: Find a way to remove this when Nova is removed
				polygonUniqueID += allBrushCaches[i].AssignUniqueIDs(polygonUniqueID, 
					brushes[i].IsVisible,
					oldVisualPolygonIndex, 
					newVisualPolygonIndex, 
					brushes[i].HasCollision,
					oldCollisionPolygonIndex, 
					newCollisionPolygonIndex);
			}

			buildContext.VisualPolygonIndex = newVisualPolygonIndex;
			buildContext.CollisionPolygonIndex = newCollisionPolygonIndex;

			// Create a list of builders that need to be built
			bool[] shouldBuildVisible = new bool[allBrushCaches.Length];
			
			// Dude! What the fuck was that nesting? Ew! Good thing I fixed it for ya.
			for (int brushIndex = 0; brushIndex < allBrushCaches.Length; brushIndex++)
			{
				if (allBrushCaches[brushIndex].Built || !brushes[brushIndex].IsVisible) 
					continue;
				
				shouldBuildVisible[brushIndex] = true;
				brushesToBuild++;

				// Mark all the intersecting brushes as needing to build
				foreach (BrushCache brushCache in allBrushCaches[brushIndex].IntersectingVisualBrushCaches) 
				{
					// TODO: This is slower than it needs to be
					int otherIndex = Array.IndexOf(allBrushCaches, brushCache);
					if (otherIndex == -1) continue;
					if (shouldBuildVisible[otherIndex] || !brushes[otherIndex].IsVisible) 
						continue;
					
					shouldBuildVisible[otherIndex] = true;
					brushesToBuild++;

					if (brushes[otherIndex].Mode != CSGMode.Subtract) 
						continue;
					
					foreach (BrushCache brushCache2 in allBrushCaches[otherIndex].IntersectingVisualBrushCaches) 
					{
						// TODO: This is slower than it needs to be
						int otherIndex2 = Array.IndexOf(allBrushCaches, brushCache2);
						if (otherIndex2 == -1 || shouldBuildVisible[otherIndex2] || !brushes[otherIndex2].IsVisible)
							continue;
						
						shouldBuildVisible[otherIndex2] = true;
						brushesToBuild++;
					}
				}
				
			}

			for (int brushIndex = 0; brushIndex < allBrushCaches.Length; brushIndex++) 
			{
				if(shouldBuildVisible[brushIndex])
				{
					allBrushCaches[brushIndex].ResetForBuild(newVisualPolygonIndex, newCollisionPolygonIndex);
				}
			}
			bool showProgressBar = false;

			if(DateTime.Now - buildStartTime > new TimeSpan(0,0,1))
			{
				showProgressBar = true;
				if(state == null && onProgressChange != null) // Can only fire on main thread
				{
					onProgressChange(0);
				}
			}
			
			
			// bake indexes
			var indexOf = new Dictionary<BrushCache,int>(allBrushCaches.Length);
			for (int i = 0; i < allBrushCaches.Length; i++) indexOf[allBrushCaches[i]] = i;
			
			
			for (int index = 0; index < allBrushCaches.Length; index++)
			{
				if (!shouldBuildVisible[index]) continue;
				
				BrushCache current = allBrushCaches[index];
				
				
				List<Polygon> builtPolygons = BrushBuilder.Build(new BrushSnapshot
				{
					Index = index,
					Cache = current,
					Earlier = current.IntersectingVisualBrushCaches
						.Where(bc => indexOf[bc] < index).ToArray(),
					Later = current.IntersectingVisualBrushCaches
						.Where(bc => indexOf[bc] > index).ToArray()
				});
				brushesBuilt++;
				
				current.SetBuilt();

				// Can only fire on main thread
				if (state != null || onProgressChange == null) continue;
				
				if(showProgressBar)
					onProgressChange(brushesBuilt / (float)brushesToBuild);
				else if(DateTime.Now - buildStartTime > new TimeSpan(0,0,1))
				{
					showProgressBar = true;
					onProgressChange(brushesBuilt / (float)brushesToBuild);
				}
				// BrushCache.ReclaimStolenVisualPolygons(current);

				if (builtPolygons.Count == 0) continue;
				
				if (buildSettings.SubdivideVisualMesh)
				{
					builtPolygons = GridSubdivide(builtPolygons,
						brushes[index].transform,
						buildSettings.SubdivisionCellSize);
					current.SetVisualBuiltPolygons(builtPolygons);
				}
				
				buildContext.SetVisualMapping(brushes[index], builtPolygons);
				
				if (builtPolygons.Count == 0)
				{
					// Debug.LogWarning("skipping empty brush");
					continue;
				}

				Prepare(rootTransform);
				GameObject builtMesh = BuildBrushVisualMesh(brushes[index].name, builtPolygons);
				builtMesh.transform.SetParent(meshGroup, false);
				if (brushes[index].builtMesh != null) UnityEngine.Object.DestroyImmediate(brushes[index].builtMesh.gameObject);
				brushes[index].builtMesh = builtMesh.transform;
			}
			
			time1 = DateTime.Now;

			// If multithreaded
			if(state != null)
			{
				// All done, tell the main thread to finish the build up
				readyForFinalize = true;
			}
		}
		
		/// <summary>
		/// Subdivide a list of polygons into a brush-axis-aligned grid.
		/// </summary>
		/// <param name="polygons">original geometry</param>
		/// <param name="brushTransform">transform of target brush</param>
		/// <param name="cellSize">size of cells in the grid</param>
		/// <returns></returns>
		internal static List<Polygon> GridSubdivide(List<Polygon> polygons, Transform brushTransform, float cellSize)
		{
			if (polygons == null || polygons.Count == 0 || cellSize <= 0f || brushTransform == null)
				return polygons;

			
			Vector3[] axes = { brushTransform.right, brushTransform.up, brushTransform.forward };

			List<Polygon> current = polygons;

			for (int axisIndex = 0; axisIndex < 3; axisIndex++)
			{
				Vector3 axis = axes[axisIndex];

				float min = float.MaxValue;
				float max = float.MinValue;

				for (int polygonIndex = 0; polygonIndex < current.Count; polygonIndex++)
				{
					Vertex[] vertices = current[polygonIndex].Vertices;
					for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
					{
						float distance = Vector3.Dot(vertices[vertexIndex].Position, axis);
						if (distance < min) min = distance;
						if (distance > max) max = distance;
					}
				}

				// skip brushes that are smaller than the grid
				// if (max - min < cellSize)
				// 	continue;

				int firstPlane = Mathf.CeilToInt((min / cellSize) + 0.0001f);
				int lastPlane = Mathf.FloorToInt((max / cellSize) - 0.0001f);

				for (int k = firstPlane; k <= lastPlane; k++)
				{
					Plane plane = new Plane(axis, -k * cellSize);

					List<Polygon> frontPolygons;
					List<Polygon> backPolygons;

					// Unlike SplitPolygonsByPlane this does not construct caps, which is good
					if (!PolygonFactory.SplitCoplanarPolygonsByPlane(current, plane, out frontPolygons, out backPolygons))
						continue;

					frontPolygons.AddRange(backPolygons);
					current = frontPolygons;
				}
			}

			return current;
		}

		
		internal static GameObject BuildBrushVisualMesh(string objectName, List<Polygon> polygons)
		{
		    // 1. Bucket by material, first-seen order so output is stable
		    List<Material> materials = new List<Material>();
		    List<List<Polygon>> buckets = new List<List<Polygon>>();
		    Dictionary<Material, int> lookup = new Dictionary<Material, int>();

		    for (int i = 0; i < polygons.Count; i++)
		    {
		        Material material = polygons[i].Material;
		        if (material == null) material = buildSettings.DefaultVisualMaterial;

		        int bucket;
		        if (!lookup.TryGetValue(material, out bucket))
		        {
		            bucket = materials.Count;
		            lookup.Add(material, bucket);
		            materials.Add(material);
		            buckets.Add(new List<Polygon>());
		        }
		        buckets[bucket].Add(polygons[i]);
		    }

		    if (materials.Count == 0) return null;   // subtractives, fully carved brushes

		    bool individualVertices = buildSettings.GenerateLightmapUVs;

		    List<Vector3> positions = new List<Vector3>();
		    List<Vector3> normals = new List<Vector3>();
		    List<Vector2> uvs = new List<Vector2>();
		    List<Color32> colors = new List<Color32>();
		    List<int[]> submeshes = new List<int[]>(materials.Count);

		    for (int i = 0; i < buckets.Count; i++)
		    {
		        Vector3[] p; Vector3[] n; Vector2[] u; Color32[] c; int[] t;
		        MeshGroupManager.TriangulatePolygons(individualVertices, buckets[i], out t, out p, out n, out u, out c);

		        int vertexOffset = positions.Count;
		        positions.AddRange(p); normals.AddRange(n); uvs.AddRange(u); colors.AddRange(c);

		        for (int j = 0; j < t.Length; j++) t[j] += vertexOffset;
		        submeshes.Add(t);
		    }

		    Mesh mesh = new Mesh();
		    if (positions.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

		    mesh.SetVertices(positions);
		    mesh.SetNormals(normals);
		    mesh.SetUVs(0, uvs);
		    mesh.SetColors(colors);

		    if (meshGroupHolder.position != Vector3.zero || meshGroupHolder.rotation != Quaternion.identity
		        || meshGroupHolder.lossyScale != Vector3.one)
		    {
		        mesh.LocalizeToTransform(meshGroupHolder);   // before triangles, as FinalizeVisualMesh does
		    }

		    mesh.subMeshCount = submeshes.Count;
		    for (int i = 0; i < submeshes.Count; i++) mesh.SetTriangles(submeshes[i], i, false);

		    if (buildSettings.GenerateTangents) mesh.GenerateTangents();

		    buildContext.buildMetrics.TotalMeshes++;
		    buildContext.buildMetrics.TotalVertices += positions.Count;
		    int triangleCount = 0;
		    for (int i = 0; i < submeshes.Count; i++) triangleCount += submeshes[i].Length / 3;
		    buildContext.buildMetrics.TotalTriangles += triangleCount;

		    GameObject newGameObject = new GameObject(objectName);
		    if (MeshGroupManager.OnFinalizeVisualMesh != null) MeshGroupManager.OnFinalizeVisualMesh(newGameObject, mesh);

		    newGameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
		    MeshRenderer renderer = newGameObject.AddComponent<MeshRenderer>();
		    renderer.sharedMaterials = materials.ToArray();
		    renderer.shadowCastingMode = buildSettings.ShadowCastingMode;
		    renderer.reflectionProbeUsage = buildSettings.ReflectionProbeUsage;
		    // newGameObject.transform.SetParent(meshGroupHolder, false);

		    for (int i = 0; i < materials.Count; i++)
		        materialMeshDictionary.Add(materials[i], mesh, newGameObject);   // Add() already supports many meshes per material
		    
		    return newGameObject;
		}

		internal static bool FinalizeBuild()
		{
			if(brushesBuilt > 0)
			{
				buildContext.WriteVisualMappings();
			}

			// Ensure a mesh group exists
			bool newGroupCreated = Prepare(rootTransform);
			if(brushesBuilt > 0 || polygonsRemoved || newGroupCreated)
			{
				// MeshGroupManager.Cleanup(meshGroupHolder);

				// Transform[] polygonOwners = MeshOwnerResolver.Resolve(brushes, buildContext.VisualPolygonIndex, buildSettings.MeshSplitMode, rootTransform);

				// MeshGroupManager.BuildVisual(meshGroupHolder, buildContext.VisualPolygonIndex, polygonOwners, buildSettings, buildContext, materialMeshDictionary);

                // All done
                DateTime time2 = DateTime.Now;

				buildContext.buildMetrics.BuildMetaData = (time1-buildStartTime).TotalSeconds + " " + (time2-time1).TotalSeconds + " " + brushesBuilt;
				buildContext.buildMetrics.BuildTime = (float)(DateTime.Now - buildStartTime).TotalSeconds;
				buildInProgress = false;
				return true;
			}
			else
			{
				buildInProgress = false;
				return false;
			}
		}

		internal static bool Tick()
		{
			if(readyForFinalize)
			{
				readyForFinalize = false;
				return FinalizeBuild();
			}
			else
			{
				return false;
			}
		}

       
            

            public static GameObject CreateMaterialMesh(Transform rootTransform, Material material, Mesh mesh)
            {
                // meshGroup = rootTransform.Find("MeshGroup");
                // // Create a grouping object which will act as a parent for all the per material meshes
                // if (meshGroup == null)
                // {
                //     meshGroup = new GameObject("MeshGroup").transform;
                //     meshGroup.parent = rootTransform;
                // }

                GameObject materialMesh = new GameObject("MaterialMesh", typeof(MeshFilter), typeof(MeshRenderer));
                materialMesh.transform.SetParent(meshGroup, false);

                // Set the mesh to be rendered
                materialMesh.GetComponent<MeshFilter>().sharedMesh = mesh;

                materialMesh.GetComponent<Renderer>().material = material;

                return materialMesh;
            }

            public static GameObject CreateCollisionMesh(Transform rootTransform, Mesh mesh)
            {
                // meshGroup = rootTransform.Find("MeshGroup");
                // // Create a grouping object which will act as a parent for all the per material meshes
                // if (meshGroup == null)
                // {
                //     meshGroup = new GameObject("MeshGroup").transform;
                //     meshGroup.parent = rootTransform;
                // }
                //
                GameObject colliderMesh = new GameObject("CollisionMesh", typeof(MeshCollider));
                colliderMesh.transform.SetParent(meshGroup, false);
                //
                // // Set the mesh to be rendered
                // colliderMesh.GetComponent<MeshCollider>().sharedMesh = mesh;
                
                return colliderMesh;
            }
    }
    }
#endif