# AUM V2: Deep Learning Pipeline Architecture

## 1. Executive Summary
The Auto Unit Matcher (AUM) is pivoting from hand-crafted classical descriptors (SHOT) to 3D Deep Learning (Learned Geometric Features). Classical pipelines like SHOT face an unbreakable mathematical ceiling (~85.3% accuracy in our tests) due to their inability to gracefully handle the severe deformations and boundary artifacts inherent to partial-to-full C&B (Crowns & Bridges) point cloud matching. 

V2 will leverage a robust proprietary dataset of 21,833 vetted C&B STLs to train a deep neural network capable of robust partial-to-full registration. The V1 C++ AUM.Engine will be frozen and preserved as a baseline, while V2 will shift to a Python/PyTorch-based training pipeline, culminating in a native C# ONNX deployment for the production WPF application.

---

## 2. Model Architecture Selection

For unorganized point cloud registration, particularly focusing on partial-to-full overlap scenarios, the architectural choice must prioritize finding robust correspondences despite occlusion and varying point densities.

### Recommended Architecture: GeoTransformer (Geometric Transformer)
**Why it wins for AUM:** 
GeoTransformer (Geometric and Feature Transformer) represents the state-of-the-art for rigid point cloud registration. It specifically excels in low-overlap and partial-to-full matching regimes.
* **Superpoint Extraction:** It downsamples the point cloud into "superpoints" (using algorithms like KPConv or PointNet++) to compute dense features efficiently.
* **Geometric Self-Attention:** Instead of relying purely on spatial coordinates or handcrafted features, it models the geometric structure (distances and angles) between superpoints to inject high-level contextual awareness.
* **Cross-Attention for Matching:** Its cross-attention modules learn to identify overlap regions between the query (partial) and the target (full), aggressively down-weighting non-overlapping regions (like the inside of a full crown that is missing from the scan).

### Alternatives Evaluated
1. **Predator (Overlap-Aware Registration):** Highly capable of detecting overlap regions and predicting match probabilities. It is extremely effective for partial matching, making it a strong alternative to GeoTransformer. However, GeoTransformer generally edges it out in robustness to extreme density variations (common in dental scans).
2. **PointNet++:** Excellent for general point cloud feature extraction but lacks the explicit cross-attention mechanisms needed to robustly identify partial-to-full correspondences out-of-the-box.
3. **MinkowskiEngine / Sparse Convolutions:** Very fast and memory-efficient for voxelized point clouds (FCGF). A strong candidate for the backbone feature extractor within a larger pipeline, but purely convolutional models sometimes struggle with the complex global context needed for dental crown orientation compared to Transformer-based approaches.

**Decision:** We will prototype using a **GeoTransformer** architecture (often implemented with a KPConv or Sparse Convolution backbone) due to its unparalleled performance in low-overlap geometric registration.

---

## 3. Data Augmentation & Training Strategy

To train a robust model using the 21,833 full STLs, we must synthetically generate challenging partial-to-full pairs that accurately mimic real-world intraoral scans.

### 3.1 Dynamic Pair Generation (On-the-fly)
We will dynamically synthesize training pairs during the DataLoader phase. For a given full STL mask $M$, we generate a pair $(P, M)$, where $P$ is a partial view of $M$.
1. **Z-Axis Cropping (Simulated Scanning):** We will apply random plane clipping. By calculating the bounding box of the crown and slicing it with a plane at random angles and depths (simulating the camera trajectory and occlusion), we can generate highly realistic partial intaglio surfaces or missing buccal/lingual walls.
2. **Rotational & Translational Jitter:** The partial query $P$ will be randomly rotated in $SO(3)$ and translated in $\mathbb{R}^3$. The network must learn to predict the rigid transformation $\mathbf{T}$ that aligns $P$ back to $M$.
3. **Sensor Noise Simulation:** Gaussian noise will be injected into the coordinates of the partial points to simulate scanner inaccuracy. Small random downsampling will simulate varying point densities.
4. **Outlier Injection:** We will randomly add a small cluster of stray points to mimic adjacent teeth or soft tissue artifacts that weren't perfectly segmented out.

### 3.2 Training Objective (Contrastive / Metric Learning)
* **Feature Matching Loss:** We will use contrastive loss (e.g., Circle Loss or InfoNCE) to force points that correspond to the same physical location on the crown to have identical feature descriptors, while pushing non-matching points apart in the feature space.
* **Overlap Prediction Loss:** If using a Predator-style or GeoTransformer approach, an auxiliary loss will train the network to binary-classify which points in the full model actually exist in the partial model, and vice versa.
* **Transformation Loss:** A final loss on the predicted rotation matrix (e.g., chordal distance) and translation vector against the ground-truth synthetic transform.

### 3.3 Hardware Constraints & Ampere Optimizations
Given the **12GB VRAM limit** of the local RTX GPU, the training loop must be rigorously optimized for memory efficiency and throughput:
* **Mixed Precision:** The training script will utilize `torch.autocast(device_type='cuda', dtype=torch.bfloat16)` to drastically halve activation memory footprint and accelerate matrix operations.
* **TensorFloat-32 (TF32):** We will enable `torch.backends.cuda.matmul.allow_tf32 = True` to natively utilize the Ampere architecture's Tensor Cores for massive $4\times$ speedups on convolutions and matrix multiplications.
* **Batch Size & Gradient Accumulation:** To fit within 12GB during the memory-heavy cross-attention modules (which scale quadratically with point count), we will enforce strict micro-batch sizes (e.g., $B=1$ or $2$ pairs per forward pass) and simulate macro-batches via gradient accumulation over $N$ steps before calling `optimizer.step()`.

---

## 4. The Docker Training Environment

To guarantee reproducibility and avoid "dependency hell" on Windows, the entire training run will take place within a Docker container leveraging WSL2.

### 4.1 Base Image & Frameworks
* **Image:** `nvcr.io/nvidia/pytorch:24.01-py3` (Official NVIDIA optimized PyTorch container).
* **Benefits:** Comes pre-packaged with perfectly matched CUDA, cuDNN, and NCCL libraries, ensuring 100% maximization of the local RTX GPU. Includes native support for Ampere optimizations out-of-the-box.

### 4.2 Handling WSL2 I/O Bottlenecks
Windows-to-WSL2 cross-OS file translation (via the 9P protocol) is notoriously slow and would cripple a deep learning DataLoader reading 21,000 STLs per epoch.
* **Solution:** The dataset of 21,833 STLs will be copied directly into the WSL2 ext4 virtual hard drive (`\\wsl$\Ubuntu\home\user\dataset`).
* **Volume Mount:** The Docker `run` command will mount this native Linux path into the container: `-v /home/user/dataset:/workspace/data`. This allows the DataLoader to operate at bare-metal NVMe SSD speeds.

### 4.3 GPU Passthrough
The container will be launched with `--gpus all --ipc=host` to give PyTorch direct access to the RTX GPU and sufficient shared memory for DataLoader multiprocessing. No local Windows installations of CUDA toolkits are required.

---

## 5. Windows Production Deployment (ONNX)

The final architecture must integrate seamlessly into the existing C# WPF application without requiring Python, Conda, or Docker on the end-user's machine.

### 5.1 Model Export (PyTorch $\rightarrow$ ONNX)
1. Once training converges, the PyTorch model will be exported to the Open Neural Network Exchange (ONNX) format using `torch.onnx.export`.
2. Dynamic axes will be defined for the input tensors, as the number of points $N$ will vary depending on the specific STL being matched.
3. The ONNX model will be locked and serialized. 

### 5.2 Native C# Execution via ML.NET / OnnxRuntime
1. **Library Inclusion:** The WPF project will ingest the `Microsoft.ML.OnnxRuntime` NuGet package.
2. **Hardware Acceleration Execution Provider (EP):**
   * We will utilize the **DirectML Execution Provider** (`Microsoft.ML.OnnxRuntime.DirectML`) for broad DirectX 12 compatibility across varying end-user hardware (AMD/Intel/NVIDIA), OR
   * The **CUDA Execution Provider** (`Microsoft.ML.OnnxRuntime.Gpu`) if we control the deployment hardware and can guarantee an NVIDIA GPU.
3. **Engine Pipeline Integration:** 
   Our `AUM.Core` module will load the `.onnx` file into an `InferenceSession`. The C# pipeline will:
   * Read the incoming STL.
   * Format it into a 1D/2D float array matching the input layer shape.
   * Call `session.Run()`.
   * Extract the resulting $4\times4$ transformation matrix $\mathbf{T}$.
   * Apply $\mathbf{T}$ identically to how the standard ICP result was applied in V1.

This ensures a blazing-fast, dependency-free, pure C#/.NET 8 deployment.
