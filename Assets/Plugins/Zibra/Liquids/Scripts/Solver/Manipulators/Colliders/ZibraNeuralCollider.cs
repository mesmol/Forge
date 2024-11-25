#if ZIBRA_LIQUID_PAID_VERSION

using com.zibra.liquid.DataStructures;
using com.zibra.liquid.Solver;
using com.zibra.liquid.Utilities;
using com.zibra.liquid;
using System;
using System.Runtime.InteropServices;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace com.zibra.liquid.SDFObjects
{
    [ExecuteInEditMode] // Careful! This makes script execute in edit mode.
    // Use "EditorApplication.isPlaying" for play mode only check.
    // Encase this check and "using UnityEditor" in "#if UNITY_EDITOR" preprocessor directive to prevent build errors
    [AddComponentMenu("Zibra/Zibra Neural Collider")]
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshFilter))]
    public class ZibraNeuralCollider : SDFCollider
    {
        public const int SDF_APPROX_DIMENSION = 32;
        public const int EMBEDDING_GRID_DIMENSION = 21;
        public const int EMBEDDING_GRID_SIZE =
            EMBEDDING_GRID_DIMENSION * EMBEDDING_GRID_DIMENSION * EMBEDDING_GRID_DIMENSION;
        public const int SDF_APPX_SIZE = SDF_APPROX_DIMENSION * SDF_APPROX_DIMENSION * SDF_APPROX_DIMENSION;
        public const int PACKING = 4;
        public const int EMBEDDING_BASE_SIZE = 16;
        public const int EMBEDDING_SIZE = EMBEDDING_BASE_SIZE / PACKING;

        private int VoxelCount;

        [SerializeField]
        public Vector3 BoundingBoxMin;
        [SerializeField]
        public Vector3 BoundingBoxMax;

        public VoxelRepresentation CurrentRepresentationV3;
        [HideInInspector]
        public bool HasRepresentationV3;

        [SerializeField]
        public VoxelEmbedding VoxelInfo;

        public void CreateRepresentation()
        {
            HasRepresentationV3 = true;

            var embeds = CurrentRepresentationV3.embeds.StringToBytes();
            VoxelInfo.grid = CurrentRepresentationV3.sd_grid.StringToBytes();

            Array.Resize<Color32>(ref VoxelInfo.embeds, EMBEDDING_GRID_SIZE * EMBEDDING_SIZE);

            for (int i = 0; i < EMBEDDING_GRID_DIMENSION; i++)
            {
                for (int j = 0; j < EMBEDDING_GRID_DIMENSION; j++)
                {
                    for (int k = 0; k < EMBEDDING_GRID_DIMENSION; k++)
                    {
                        for (int t = 0; t < EMBEDDING_SIZE; t++)
                        {
                            int id0 = i + t * EMBEDDING_GRID_DIMENSION +
                                      EMBEDDING_SIZE * EMBEDDING_GRID_DIMENSION * (j + k * EMBEDDING_GRID_DIMENSION);
                            int id1 = t + (i + EMBEDDING_GRID_DIMENSION * (j + k * EMBEDDING_GRID_DIMENSION)) *
                                              EMBEDDING_SIZE;
                            Color32 embeddings = new Color32(embeds[PACKING * id1 + 0], embeds[PACKING * id1 + 1],
                                                             embeds[PACKING * id1 + 2], embeds[PACKING * id1 + 3]);
                            VoxelInfo.embeds[id0] = embeddings;
                        }
                    }
                }
            }

            CurrentRepresentationV3.embeds = null;
            CurrentRepresentationV3.sd_grid = null;

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        public void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.grey;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(0.5f * (BoundingBoxMin + BoundingBoxMax), (BoundingBoxMax - BoundingBoxMin));
        }

        public void OnDrawGizmos()
        {
            OnDrawGizmosSelected();
        }

        public override void InitializeConstData()
        {
        }

        public void Initialize()
        {
            ManipType = ManipulatorType.NeuralCollider;

            if (!isInitialized) // if has not been initialized at all
            {
                ColliderIndex = AllColliders.IndexOf(this);

                colliderParams.CurrentID = ColliderIndex;
                colliderParams.VoxelCount = VoxelCount;
                colliderParams.BBoxMin = BoundingBoxMin;
                colliderParams.BBoxMax = BoundingBoxMax;
                colliderParams.colliderIndex = ColliderIndex;

                AdditionalData.x = (float)chosenSDFType;
                AdditionalData.y = (float)VoxelCount;
            }
        }

        // on game start
        protected void Start()
        {
            colliderParams = new ColliderParams();
            NativeDataPtr = Marshal.AllocHGlobal(Marshal.SizeOf(colliderParams));
            ManipType = ManipulatorType.NeuralCollider;
            Initialize();
        }

#if UNITY_EDITOR

        public Mesh GetMesh()
        {
            Renderer currentRenderer = GetComponent<Renderer>();

            if (currentRenderer == null)
            {
#if UNITY_EDITOR
                EditorUtility.DisplayDialog(
                    "Zibra Liquid Mesh Error",
                    "Render component absent on this object. " +
                        "Add this component only to objects with MeshFilter or SkinnedMeshRenderer components",
                    "Ok");
#endif
                return null;
            }

            if (currentRenderer is MeshRenderer meshRenderer)
            {
                var MeshFilter = meshRenderer.GetComponent<MeshFilter>();

                if (MeshFilter == null)
                {
#if UNITY_EDITOR
                    EditorUtility.DisplayDialog(
                        "Zibra Liquid Mesh Error",
                        "MeshFilter absent on this object. MeshRenderer requires MeshFilter to operate correctly.",
                        "Ok");
#endif
                    return null;
                }

                if (MeshFilter.sharedMesh == null)
                {
#if UNITY_EDITOR
                    EditorUtility.DisplayDialog(
                        "Zibra Liquid Mesh Error",
                        "No mesh found on this object. Attach mesh to the MeshFilter before generating representation.",
                        "Ok");
#endif
                    return null;
                }

                return MeshFilter.sharedMesh;
            }

            if (currentRenderer is SkinnedMeshRenderer skinnedMeshRenderer)
            {
                var mesh = new Mesh();
                skinnedMeshRenderer.BakeMesh(mesh);

                return mesh;
            }

#if UNITY_EDITOR
            EditorUtility.DisplayDialog(
                "Zibra Liquid Mesh Error",
                "Unsupported Renderer type. Only MeshRenderer and SkinnedMeshRenderer are supported at the moment.",
                "Ok");
#endif
            return null;
        }
#endif

        public override ulong GetMemoryFootrpint()
        {
            ulong result = 0;
            if (CurrentRepresentationV3.embeds == null || VoxelInfo.grid == null || VoxelInfo.embeds == null)
                return result;

            result += (ulong)(VoxelInfo.grid.Length + VoxelInfo.embeds.Length) * sizeof(float); // VoxelEmbeddings

            return result;
        }
    }
}

#endif