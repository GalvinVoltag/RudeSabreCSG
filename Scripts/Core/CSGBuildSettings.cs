#if UNITY_EDITOR || RUNTIME_CSG

using UnityEngine;
using System.Collections;
using UnityEngine.Rendering;

namespace Sabresaurus.SabreCSG
{
    /// <summary>
    /// Decides which polygons end up sharing a built visual mesh. Every mode still keeps one material per mesh.
    /// </summary>
    public enum MeshSplitMode
    {
        /// <summary>All polygons using the same material share a mesh.</summary>
        PerMaterial,
        /// <summary>Each brush gets its own meshes. Compound brushes count as a single brush.</summary>
        PerBrush,
        /// <summary>Each group brush gets its own meshes. Brushes outside of any group fall back to per brush.</summary>
        PerGroup,
    }

    [System.Serializable]
    public class CSGBuildSettings
    {
        // Whether to also do a collision pass
        public bool GenerateCollisionMeshes = true;

        // Also calculate tangents (needed for Unity's built in bump mapping)
        public bool GenerateTangents = true;

        public MeshSplitMode MeshSplitMode = MeshSplitMode.PerMaterial;

        public bool OptimizeGeometry = false;

        // Optimized geometry is only supported when meshes are split per material
        public bool UsesOptimizedGeometry
        {
            get { return OptimizeGeometry && !IsSplittingMeshes; }
        }

        public bool IsSplittingMeshes
        {
            get { return MeshSplitMode != MeshSplitMode.PerMaterial; }
        }

        public bool SaveMeshesAsAssets = false;

        // Generate a UV2 channel for lightmapping
        [Tooltip("Note that when enabled this will have a non-trivial increase in build times")]
        public bool GenerateLightmapUVs = false;

        // Split the generated visual mesh along a world space grid so large flat surfaces have
        // enough vertices for per vertex lighting (e.g. ULTRAKILL's vertex lit shaders). Collision
        // meshes are left untouched.
        public bool SubdivideVisualMesh = false;

        // Size of the subdivision grid in world units. Larger faces get an extra vertex every
        // SubdivisionCellSize units in each direction.
        [Range(0.25f, 32f)]
        public float SubdivisionCellSize = 4f;

        // Unwrap settings, note these are different to what is displayed in the Model Importer, see http://docs.unity3d.com/401/Documentation/Manual/LightmappingUV.html
        [Range(0f, 1f)]
        public float UnwrapAngleError = 0.08f; // 0 to 1

        [Range(0f, 1f)]
        public float UnwrapAreaError = 0.15f; // 0 to 1

        [Range(0f, 180f)]
        public float UnwrapHardAngle = 88f; // degrees, 0 to 180

        public float UnwrapPackMargin = 0.00390625f; // Assumes a 1024 texture, pack margin = PadPixels / 1024

        /*
            Fix most of the light leaking that occurs after CSG operations.
            See https://github.com/sabresaurus/SabreCSG/issues/19#issuecomment-358567922

            In Unity even a simple box placement like below will cause dynamic light to leak through
            what may appear to be a closed corner to the player:

              Dynamic
            Light Source
               X
                X   +------+
                 X  |      |
                  X |      |
                   X|      |
             +------+------+
             |      |X
             |      | X
             |      |  X
             +------+   X
                     Light Leaks Through
                        Causes Seam

            When CSG operations hollow out brushes you may get a shape like this with the same problem:

              Dynamic
            Light Source
                X
                 X   +------+
                  X  |      |
                   X |      |
                    X|      |
                 +---+  +---+   <---+ NoCSG would not have caused a hole here.
                 |    X |
                 |     X|   <---+ This wall's normal is facing the wrong way
                 |      |         and will not block the incoming light.
                 +------+X
                     Light Leaks Through
                        Causes Seam

            By setting the shadow casting mode of the mesh to be "Two Sided" the backfacing wall will
            block the incoming light and prevent the light leak artifact from appearing.
       */

        public ShadowCastingMode ShadowCastingMode = ShadowCastingMode.TwoSided;
        public ReflectionProbeUsage ReflectionProbeUsage = ReflectionProbeUsage.BlendProbes;

        // What default physics material to use on collision meshes
        public PhysicMaterial DefaultPhysicsMaterial = null;

        public Material DefaultVisualMaterial = null;

        [HideInInspector]
        public bool IsBuilt = false; // Only really relevant to last build settings

        public CSGBuildSettings ShallowCopy()
        {
            return (CSGBuildSettings)this.MemberwiseClone();
        }

        /// <summary>
        /// Compares two build settings and returns if they are practially different.
        /// </summary>
        public static bool AreDifferent(CSGBuildSettings settings1, CSGBuildSettings settings2)
        {
            if (settings1.GenerateCollisionMeshes != settings2.GenerateCollisionMeshes)
            {
                return true;
            }
            if (settings1.GenerateTangents != settings2.GenerateTangents)
            {
                return true;
            }
            if (settings1.MeshSplitMode != settings2.MeshSplitMode)
            {
                return true;
            }
            if (settings1.UsesOptimizedGeometry != settings2.UsesOptimizedGeometry)
            {
                return true;
            }
            if (settings1.GenerateLightmapUVs != settings2.GenerateLightmapUVs)
            {
                return true;
            }
            if (settings1.SubdivideVisualMesh != settings2.SubdivideVisualMesh)
            {
                return true;
            }
            if (settings1.SubdivideVisualMesh && settings1.SubdivisionCellSize != settings2.SubdivisionCellSize)
            {
                return true;
            }

            // Only compare UV unwrap settings if unwrapping is in use
            if (settings1.GenerateLightmapUVs && settings1.GenerateLightmapUVs)
            {
                if (settings1.UnwrapAngleError != settings2.UnwrapAngleError)
                {
                    return true;
                }

                if (settings1.UnwrapAreaError != settings2.UnwrapAreaError)
                {
                    return true;
                }

                if (settings1.UnwrapHardAngle != settings2.UnwrapHardAngle)
                {
                    return true;
                }

                if (settings1.UnwrapPackMargin != settings2.UnwrapPackMargin)
                {
                    return true;
                }
            }

            if (settings1.DefaultPhysicsMaterial != settings2.DefaultPhysicsMaterial)
            {
                return true;
            }
            if (settings1.DefaultVisualMaterial != settings2.DefaultVisualMaterial)
            {
                return true;
            }

            if (settings1.ShadowCastingMode != settings2.ShadowCastingMode)
            {
                return true;
            }

            if (settings1.ReflectionProbeUsage != settings2.ReflectionProbeUsage)
            {
                return true;
            }

            // Don't compare IsBuilt

            // No practical differences found
            return false;
        }
    }
}

#endif