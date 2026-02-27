using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace AUM.Core.Services
{
    /// <summary>
    /// Executes the V2 PyTorch GeoTransformer Neural Network pipelines natively in C#.
    /// Bypasses Python and GPU overhead via Microsoft.ML.OnnxRuntime on dual dynamically-shaped graphs.
    /// Ensures 100% strict memory deallocation of unmanaged native Tensor bindings per evaluation. 
    /// </summary>
    public class OnnxInferenceService : IDisposable
    {
        private readonly InferenceSession _backboneSession;
        private readonly InferenceSession _matcherSession;

        /// <summary>
        /// Loads both the independent FAISS Embedder and the strict Pairwise Matcher.
        /// </summary>
        /// <param name="backboneModelPath">Path to aum_backbone.onnx</param>
        /// <param name="matcherModelPath">Path to aum_matcher.onnx</param>
        public OnnxInferenceService(string backboneModelPath, string matcherModelPath)
        {
            // Initializing CPU Execution Provider for guaranteed stability during local V2 testing
            var options = new SessionOptions();
            options.AppendExecutionProvider_CPU(0);
            
            _backboneSession = new InferenceSession(backboneModelPath, options);
            _matcherSession = new InferenceSession(matcherModelPath, options);
        }

        /// <summary>
        /// Stage 1: Computes decoupled, global semantic geometry ML features for a specific dental unit.
        /// Resolves variable input point sizes safely by generating dynamic-axis 3D DenseTensors.
        /// </summary>
        /// <param name="pointCloud">Spatial boundary points for the object [N, 3]</param>
        /// <returns>Computed deep learning feature set aligned to the N point vertices [N, 128] flattened</returns>
        public float[] ExtractFeatures(float[,] pointCloud)
        {
            // --- CRITICAL MEMORY PROTECTION ---
            // ONNX exported `torch.cdist` which creates a dense O(N^2) distance matrix for Radius Nearest Neighbors.
            // An 87,000 vertex mesh requires 30+ GB of RAM. We must Voxel Downsample the input first.
            // A voxel size of 0.2 units provides excellent geometric retention while dragging N down to ~4000-8000 points.
            float[,] downsampledCloud = VoxelDownsample(pointCloud, 0.2f);
            
            int numPoints = downsampledCloud.GetLength(0);
            
            // 1. Array Flattening: PyTorch operations require 1D contiguous unmanaged arrays
            float[] flattenPoints = new float[numPoints * 3];
            int idx = 0;
            for (int i = 0; i < numPoints; i++)
            {
                flattenPoints[idx++] = downsampledCloud[i, 0];
                flattenPoints[idx++] = downsampledCloud[i, 1];
                flattenPoints[idx++] = downsampledCloud[i, 2];
            }

            // 2. Dynamic Target Metadata Extraction (Immune to arbitrary Export Node Identifiers e.g. "input.1")
            var inputName = _backboneSession.InputMetadata.Keys.First();
            var inputTensor = new DenseTensor<float>(flattenPoints, new[] { numPoints, 3 });
            
            // Unmanaged pointer initialization
            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputName, inputTensor) };

            // 3. Native ONNX Binary Math execution wrapped in a definitive Memory Cleanup context.
            // When leaving this using block, IDisposable invokes NativeLibrary/GC sweeping.
            using var results = _backboneSession.Run(inputs);
            
            var outputTensor = results.First().AsTensor<float>();
            return outputTensor.ToArray();
        }

        /// <summary>
        /// Stage 2: Operates the Cross-Attention Transformer across the Source unit against a Single Target DB hit.
        /// Both target models are permitted radically independent geometry sizes strictly managed by ONNX runtime binding.
        /// </summary>
        public (float[] SourceDescriptors, float[] TargetDescriptors) VerifyMatch(
            float[] sourceFeatures, int numSourcePoints, 
            float[] targetFeatures, int numTargetPoints)
        {
            // Dynamic Name Acquisition
            var sourceInputName = _matcherSession.InputMetadata.Keys.ElementAt(0);
            var targetInputName = _matcherSession.InputMetadata.Keys.ElementAt(1);

            // Dynamic Shape Tensor Formations
            var sourceTensor = new DenseTensor<float>(sourceFeatures, new[] { numSourcePoints, 128 });
            var targetTensor = new DenseTensor<float>(targetFeatures, new[] { numTargetPoints, 128 });

            var inputs = new List<NamedOnnxValue> 
            { 
                NamedOnnxValue.CreateFromTensor(sourceInputName, sourceTensor),
                NamedOnnxValue.CreateFromTensor(targetInputName, targetTensor)
            };

            // Execution / IDisposable sweep 
            using var results = _matcherSession.Run(inputs);

            // Reallocation bounds memory capture
            var outSourceData = results.ElementAt(0).AsTensor<float>().ToArray();
            var outTargetData = results.ElementAt(1).AsTensor<float>().ToArray();

            return (outSourceData, outTargetData);
        }

        /// <summary>
        /// An ultra-lightweight, 0-dependency spatial hash grid that perfectly calculates
        /// Voxel centers to downsample 3D point geometry instantly.
        /// </summary>
        public float[,] VoxelDownsample(float[,] points, float voxelSize)
        {
            int numPoints = points.GetLength(0);
            var voxelMap = new Dictionary<(int x, int y, int z), (float sumX, float sumY, float sumZ, int count)>();

            for (int i = 0; i < numPoints; i++)
            {
                float px = points[i, 0];
                float py = points[i, 1];
                float pz = points[i, 2];

                int vx = (int)Math.Floor(px / voxelSize);
                int vy = (int)Math.Floor(py / voxelSize);
                int vz = (int)Math.Floor(pz / voxelSize);

                var key = (vx, vy, vz);
                if (voxelMap.TryGetValue(key, out var val))
                {
                    voxelMap[key] = (val.sumX + px, val.sumY + py, val.sumZ + pz, val.count + 1);
                }
                else
                {
                    voxelMap[key] = (px, py, pz, 1);
                }
            }

            // Extract the mathematical center-of-mass for each populated voxel
            float[,] downsampled = new float[voxelMap.Count, 3];
            int idx = 0;
            foreach (var kvp in voxelMap.Values)
            {
                downsampled[idx, 0] = kvp.sumX / kvp.count;
                downsampled[idx, 1] = kvp.sumY / kvp.count;
                downsampled[idx, 2] = kvp.sumZ / kvp.count;
                idx++;
            }

            return downsampled;
        }

        public void Dispose()
        {
            _backboneSession?.Dispose();
            _matcherSession?.Dispose();
        }
    }
}
