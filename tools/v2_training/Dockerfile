FROM pytorch/pytorch:2.2.0-cuda12.1-cudnn8-devel

# ==============================================================================
# PyTorch C++ ABI Alignment Note:
# Do NOT use nvcr.io/nvidia/pytorch images here. NVIDIA NGC images compile PyTorch 
# with _GLIBCXX_USE_CXX11_ABI=1. However, the pre-compiled PyTorch Geometric 
# wheels (torch-cluster, etc.) are compiled against standard PyPI PyTorch with ABI=0.
# Mixing them causes fatal `undefined symbol: _ZN5torch3jit...` crashes at runtime.
# Using the official standard PyTorch base Docker image below aligns the ABIs (ABI=0).
# ==============================================================================

ENV DEBIAN_FRONTEND=noninteractive
WORKDIR /workspace

# Install system dependencies required for Open3D and general compilation
RUN apt-get update && apt-get install -y \
    libgl1-mesa-glx \
    libglib2.0-0 \
    build-essential \
    curl \
    git \
    && rm -rf /var/lib/apt/lists/*

# Install PyTorch Geometric wheels specifically compiled for vanilla PyTorch 2.2.0 + CUDA 12.1
RUN pip install --no-cache-dir torch-scatter torch-sparse torch-cluster torch-spline-conv \
    -f https://data.pyg.org/whl/torch-2.2.0+cu121.html

# Install additional 3D processing and tracking libraries
RUN pip install --no-cache-dir torch-geometric open3d trimesh wandb plotly

# Default command
CMD ["/bin/bash"]
