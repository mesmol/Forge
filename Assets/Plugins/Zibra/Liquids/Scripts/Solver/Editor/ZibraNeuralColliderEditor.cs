#if ZIBRA_LIQUID_PAID_VERSION

using com.zibra.liquid.DataStructures;
using com.zibra.liquid.SDFObjects;
using System;
using com.zibra.liquid.Utilities;
using UnityEngine;
using UnityEditor;
using UnityEngine.Networking;
using System.Collections.Generic;

namespace com.zibra.liquid.Editor.SDFObjects
{
    [CustomEditor(typeof(ZibraNeuralCollider))]
    [CanEditMultipleObjects]
    public class ZibraNeuralColliderEditor : UnityEditor.Editor
    {
        // Limits for representation generation web requests
        private const uint REQUEST_TRIANGLE_COUNT_LIMIT = 100000;
        private const uint REQUEST_SIZE_LIMIT = 3 << 20; // 3mb

        static ZibraNeuralColliderEditor EditorInstance;

        class ZibraNeuralColliderGenerator
        {
            public ZibraNeuralColliderGenerator(ZibraNeuralCollider ZibraNeuralCollider)
            {
                this.NeuralColliderInstance = ZibraNeuralCollider;
            }

            public ZibraNeuralCollider GetCollider()
            {
                return NeuralColliderInstance;
            }

            private Vector3[] VertexCachedBuffer;
            private Bounds MeshBounds;
            private ZibraNeuralCollider NeuralColliderInstance;
            private UnityWebRequest CurrentRequest;
            public void CreateMeshBBCube()
            {
                Mesh mesh = NeuralColliderInstance.GetComponent<MeshFilter>().sharedMesh;

                if (mesh == null)
                {
                    return;
                }

                MeshBounds = mesh.bounds;

                Vector3 center = MeshBounds.center;
                Vector3 lengths = MeshBounds.size;

                NeuralColliderInstance.BoundingBoxMin = center - lengths * 0.5f;
                NeuralColliderInstance.BoundingBoxMax = center + lengths * 0.5f;
            }

            public void Start()
            {
                var mesh = NeuralColliderInstance.GetMesh();

                if (mesh == null)
                {
                    return;
                }

                if (mesh.triangles.Length / 3 > REQUEST_TRIANGLE_COUNT_LIMIT)
                {
                    string errorMessage =
                        $"Mesh is too large. Can't generate representation. Triangle count should not exceed {REQUEST_TRIANGLE_COUNT_LIMIT} triangles, but current mesh have {mesh.triangles.Length / 3} triangles";
                    EditorUtility.DisplayDialog("ZibraLiquid Error.", errorMessage, "OK");
                    Debug.LogError(errorMessage);
                    return;
                }

                if (!EditorApplication.isPlaying)
                {
                    VertexCachedBuffer = new Vector3[mesh.vertices.Length];
                    Array.Copy(mesh.vertices, VertexCachedBuffer, mesh.vertices.Length);
                }

                var meshRepresentation =
                    new MeshRepresentation { vertices = mesh.vertices.Vector3ToString(),
                                             faces = mesh.triangles.IntToString(),
                                             vox_dim = ZibraNeuralCollider.EMBEDDING_GRID_DIMENSION,
                                             sdf_dim = ZibraNeuralCollider.SDF_APPROX_DIMENSION };

                if (CurrentRequest != null)
                {
                    CurrentRequest.Dispose();
                    CurrentRequest = null;
                }

                var json = JsonUtility.ToJson(meshRepresentation);

                if (json.Length > REQUEST_SIZE_LIMIT)
                {
                    string errorMessage =
                        $"Mesh is too large. Can't generate representation. Please decrease vertex/triangle count. Web request should not exceed {REQUEST_SIZE_LIMIT / (1 << 20):N2}mb, but for current mesh {(float)json.Length / (1 << 20):N2}mb is needed.";
                    EditorUtility.DisplayDialog("ZibraLiquid Error.", errorMessage, "OK");
                    Debug.LogError(errorMessage);
                    return;
                }

                var LicenseStatus = ZibraServerAuthenticationManager.GetInstance().GetStatus();

                if (LicenseStatus == ZibraServerAuthenticationManager.Status.OK)
                {
                    string requestURL = ZibraServerAuthenticationManager.GetInstance().GenerationURL;

                    if (requestURL != "")
                    {
                        CurrentRequest = UnityWebRequest.PostWwwForm(requestURL, json);
                        CurrentRequest.SendWebRequest();
                    }
                }
                else
                {
                    string errorMessage = ZibraServerAuthenticationManager.GetInstance().GetErrorMessage();
                    EditorUtility.DisplayDialog("Zibra Liquid Error", errorMessage, "Ok");
                    Debug.LogError(errorMessage);
                }
            }

            public void Abort()
            {
                CurrentRequest?.Dispose();
            }

            public void Update()
            {
                if (CurrentRequest != null && CurrentRequest.isDone)
                {
                    VoxelRepresentation newRepresentation = null;

#if UNITY_2020_2_OR_NEWER
                    if (CurrentRequest.isDone && CurrentRequest.result == UnityWebRequest.Result.Success)
#else
                    if (CurrentRequest.isDone && !CurrentRequest.isHttpError && !CurrentRequest.isNetworkError)
#endif
                    {
                        var json = CurrentRequest.downloadHandler.text;
                        newRepresentation = JsonUtility.FromJson<VoxelRepresentation>(json);

                        if (string.IsNullOrEmpty(newRepresentation.embeds) ||
                            string.IsNullOrEmpty(newRepresentation.sd_grid))
                        {
                            EditorUtility.DisplayDialog("Zibra Liquid Server Error",
                                                        "Server returned empty result. Connect ZibraLiquid support",
                                                        "Ok");
                            Debug.LogError("Server returned empty result. Connect ZibraLiquid support");
                        }
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Zibra Liquid Server Error", CurrentRequest.error, "Ok");
                        Debug.LogError(CurrentRequest.downloadHandler.text);
                    }

                    CurrentRequest.Dispose();
                    CurrentRequest = null;

                    if (newRepresentation == null)
                    {
                        return;
                    }

                    CreateMeshBBCube();

                    NeuralColliderInstance.CurrentRepresentationV3 = newRepresentation;
                    NeuralColliderInstance.CreateRepresentation();

                    // make sure to mark the scene as changed
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                        UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
                }
            }

            public bool IsFinished()
            {
                return CurrentRequest == null;
            }
        }

        static class GenerationQueue
        {
            static Queue<ZibraNeuralColliderGenerator> CollidersToGenerate = new Queue<ZibraNeuralColliderGenerator>();

            static void Update()
            {
                CollidersToGenerate.Peek().Update();
                if (CollidersToGenerate.Peek().IsFinished())
                {
                    RemoveFromQueue();
                    if (CollidersToGenerate.Count > 0)
                    {
                        CollidersToGenerate.Peek().Start();
                    }
                    if (EditorInstance)
                    {
                        EditorInstance.Repaint();
                    }
                }
            }

            static void RemoveFromQueue()
            {
                CollidersToGenerate.Dequeue();
                if (CollidersToGenerate.Count == 0)
                {
                    EditorApplication.update -= Update;
                }
            }

            static public void AddToQueue(ZibraNeuralColliderGenerator generator)
            {
                if (!CollidersToGenerate.Contains(generator))
                {
                    if (CollidersToGenerate.Count == 0)
                    {
                        EditorApplication.update += Update;
                        generator.Start();
                    }
                    CollidersToGenerate.Enqueue(generator);
                }
            }

            static public void Abort()
            {
                if (CollidersToGenerate.Count > 0)
                {
                    CollidersToGenerate.Peek().Abort();
                    CollidersToGenerate.Clear();
                    EditorApplication.update -= Update;
                }
            }

            static public int GetQueueLength()
            {
                return CollidersToGenerate.Count;
            }

            static public bool IsInQueue(ZibraNeuralCollider collider)
            {
                foreach (var item in CollidersToGenerate)
                {
                    if (item.GetCollider() == collider)
                        return true;
                }
                return false;
            }
        }

        private ZibraNeuralCollider[] NeuralColliders;

        private SerializedProperty ForceInteraction;
        private SerializedProperty ForceInteractionCallback;
        private SerializedProperty InvertSDF;
        private SerializedProperty FluidFriction;

        [MenuItem("Zibra AI/Zibra AI - Liquids/Generate all Neural colliders in the Scene", false, 20)]
        static void GenerateAllColliders()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("Neural colliders can only be generated in edit mode.");
                return;
            }

            // Find all neural colliders in the scene
            ZibraNeuralCollider[] allNeuralColliders = FindObjectsOfType<ZibraNeuralCollider>();

            if (allNeuralColliders.Length == 0)
            {
                Debug.LogWarning("No neural colliders found in the scene.");
                return;
            }

            // Find all corresponding game objects
            GameObject[] allNeraulCollidersGameObjects = new GameObject[allNeuralColliders.Length];
            for (int i = 0; i < allNeuralColliders.Length; i++)
            {
                allNeraulCollidersGameObjects[i] = allNeuralColliders[i].gameObject;
            }
            // Set selection to that game objects so user can see generation progress
            Selection.objects = allNeraulCollidersGameObjects;

            // Add all colliders to the generation queue
            foreach (var neuralCollider in allNeuralColliders)
            {
                if (!GenerationQueue.IsInQueue(neuralCollider) && !neuralCollider.HasRepresentationV3)
                {
                    GenerationQueue.AddToQueue(new ZibraNeuralColliderGenerator(neuralCollider));
                }
            }
        }

        protected void Awake()
        {
            ZibraServerAuthenticationManager.GetInstance().Initialize();
        }

        protected void OnEnable()
        {
            EditorInstance = this;

            NeuralColliders = new ZibraNeuralCollider[targets.Length];

            for (int i = 0; i < targets.Length; i++)
            {
                NeuralColliders[i] = targets[i] as ZibraNeuralCollider;
            }

            ForceInteraction = serializedObject.FindProperty("ForceInteraction");
            ForceInteractionCallback = serializedObject.FindProperty("ForceInteractionCallback");
            InvertSDF = serializedObject.FindProperty("InvertSDF");
            FluidFriction = serializedObject.FindProperty("FluidFriction");
        }

        protected void OnDisable()
        {
            if (EditorInstance == this)
            {
                EditorInstance = null;
            }
        }

        private void GenerateColliders(bool regenerate = false)
        {
            foreach (var instance in NeuralColliders)
            {
                if (!GenerationQueue.IsInQueue(instance) && (!instance.HasRepresentationV3 || regenerate))
                {
                    GenerationQueue.AddToQueue(new ZibraNeuralColliderGenerator(instance));
                }
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (EditorApplication.isPlaying)
            {
                // Don't allow generation in playmode
            }
            else if (ZibraServerAuthenticationManager.GetInstance().GetStatus() !=
                     ZibraServerAuthenticationManager.Status.OK)
            {
                GUILayout.Label(ZibraServerAuthenticationManager.GetInstance().GetErrorMessage());
                GUILayout.Space(20);
            }
            else
            {
                int toGenerateCount = 0;
                int toRegenerateCount = 0;

                foreach (var instance in NeuralColliders)
                {
                    if (!GenerationQueue.IsInQueue(instance))
                    {
                        if (instance.HasRepresentationV3)
                        {
                            toRegenerateCount++;
                        }
                        else
                        {
                            toGenerateCount++;
                        }
                    }
                }

                int inQueueCount = NeuralColliders.Length - toGenerateCount - toRegenerateCount;
                int fullQueueLength = GenerationQueue.GetQueueLength();
                if (fullQueueLength > 0)
                {
                    if (fullQueueLength != inQueueCount)
                    {
                        if (inQueueCount == 0)
                        {
                            GUILayout.Label($"Generating other colliders. {fullQueueLength} left in total.");
                        }
                        else
                        {
                            GUILayout.Label(
                                $"Generating colliders. {inQueueCount} left out of selected colliders. {fullQueueLength} colliders left in total.");
                        }
                    }
                    else
                    {
                        GUILayout.Label(NeuralColliders.Length > 1 ? $"Generating colliders. {inQueueCount} left."
                                                                   : "Generating collider.");
                    }
                    if (GUILayout.Button("Abort"))
                    {
                        GenerationQueue.Abort();
                    }

                    GUILayout.Space(10);
                }

                if (toGenerateCount > 0)
                {
                    GUILayout.Label(NeuralColliders.Length > 1
                                        ? $"{toGenerateCount} colliders doesn't have representation."
                                        : "Collider doesn't have representation.");
                    if (GUILayout.Button(NeuralColliders.Length > 1 ? "Generate colliders" : "Generate collider"))
                    {
                        GenerateColliders();
                    }
                }

                if (toRegenerateCount > 0)
                {
                    GUILayout.Label(NeuralColliders.Length > 1 ? $"{toRegenerateCount} colliders already generated."
                                                               : "Collider already generated.");
                    if (GUILayout.Button(NeuralColliders.Length > 1 ? "Regenerate all selected colliders"
                                                                    : "Regenerate collider"))
                    {
                        GenerateColliders(true);
                    }
                }

                if (toGenerateCount != 0 || toRegenerateCount != 0)
                {
                    GUILayout.Space(10);
                }
            }

            bool isColliderComponentMissing = false;
            foreach (var instance in NeuralColliders)
            {
                if (instance.GetComponent<Collider>() == null)
                {
                    isColliderComponentMissing = true;
                    break;
                }
            }

            if (isColliderComponentMissing &&
                GUILayout.Button(NeuralColliders.Length > 1 ? "Add Unity Colliders" : "Add Unity Collider"))
            {
                foreach (var instance in NeuralColliders)
                {
                    if (instance.GetComponent<Collider>() == null)
                    {
                        instance.gameObject.AddComponent<MeshCollider>();
                        EditorUtility.SetDirty(instance.gameObject);
                    }
                }
            }

            EditorGUILayout.PropertyField(FluidFriction);
            EditorGUILayout.PropertyField(ForceInteraction);
            EditorGUILayout.PropertyField(InvertSDF);

            GUILayout.Space(10);

            EditorGUILayout.PropertyField(ForceInteractionCallback);

            bool isRigidbodyComponentMissing = false;
            foreach (var instance in NeuralColliders)
            {
                if (instance.ForceInteraction && instance.GetComponent<Rigidbody>() == null)
                {
                    isRigidbodyComponentMissing = true;
                    break;
                }
            }

            if (isRigidbodyComponentMissing &&
                GUILayout.Button(NeuralColliders.Length > 1 ? "Add Unity Rigidbodies" : "Add Unity Rigidbody"))
            {
                foreach (var instance in NeuralColliders)
                {
                    if (instance.ForceInteraction && instance.GetComponent<Rigidbody>() == null)
                    {
                        instance.gameObject.AddComponent<Rigidbody>();
                        EditorUtility.SetDirty(instance.gameObject);
                    }
                }
            }

            ulong totalMemoryFootprint = 0;
            foreach (var instance in NeuralColliders)
            {
                if (instance.HasRepresentationV3)
                {
                    totalMemoryFootprint += instance.GetMemoryFootrpint();
                }
            }

            if (totalMemoryFootprint != 0)
            {
                GUILayout.Space(10);

                if (NeuralColliders.Length > 1)
                {
                    GUILayout.Label("Multiple voxel colliders selected. Showing sum of all selected instances.");
                }
                GUILayout.Label($"Approximate VRAM footprint:{(float)totalMemoryFootprint / (1 << 20):N2}MB");
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
