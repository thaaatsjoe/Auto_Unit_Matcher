# Use official NVIDIA PyTorch image (Ampere-compatible + optimized)
FROM nvcr.io/nvidia/pytorch:24.01-py3

# Install system dependencies for Open3D, general graphics, and RTree
RUN apt-get update && apt-get install -y --no-install-recommends \
    libgl1 \
    libgomp1 \
    libglib2.0-0 \
    libspatialindex-dev \
    && rm -rf /var/lib/apt/lists/*

# Install point cloud processing libraries
# open3d for standard format I/O (PLY)
# trimesh for robust STL loading and surface sampling
# scipy for spatial rotations
# rtree for fast Trimesh exact BVH raycasting
RUN pip install --no-cache-dir \
    open3d==0.17.0 \
    trimesh \
    scipy \
    tqdm \
    rtree

# Install basic PyTorch Geometric dependencies (for GeoTransformer/KPConv later)
# Using wheels compatible with PyTorch 2.2.0 and CUDA 12.1 (which aligns with the 24.01 container)
RUN pip install --no-cache-dir \
    torch-scatter torch-sparse torch-cluster torch-spline-conv torch-geometric -f https://data.pyg.org/whl/torch-2.2.0+cu121.html

WORKDIR /workspace

# Keep container running or specify entrypoint
CMD ["/bin/bash"]
