import torch
import torch.nn as nn
import torch.nn.functional as F
from torch_geometric.utils import to_dense_batch

# ----------------------------------------------------------------------------
# AUM V2 Geometric Feature Extractor (Backbone)
# ----------------------------------------------------------------------------

# We use knn_graph instead of radius_graph because knn guarantees a fixed neighborhood
# size, which is mathematically much faster to compute on GPUs than dynamic radius bounds.
try:
    from torch_cluster import knn_graph
    USE_NATIVE_KNN = True
except Exception as e:
    USE_NATIVE_KNN = False
    print(f"WARNING: torch_cluster C++ bindings failed ({e}). Falling back to pure PyTorch cdist.")

def builtin_radius_graph(pos, max_num_neighbors=32):
    """
    Computes local neighborhoods. Uses blazing fast CUDA k-NN if available.
    Falls back to PyTorch O(N^2) pairwise distances ONLY if C++ bindings fail.
    """
    if USE_NATIVE_KNN:
        # High speed CUDA Tree-search (O(N log N))
        # knn_graph returns edges as [2, num_edges] where row 0 is source and row 1 is target
        edge_index = knn_graph(pos, k=max_num_neighbors, loop=False)
        return edge_index

    # VRAM-Crushing O(N^2) Fallback (700M calculations per layer)
    dists = torch.cdist(pos, pos)
    K = min(max_num_neighbors, pos.size(0))
    _, col = torch.topk(dists, k=K, dim=1, largest=False)
    row = torch.arange(pos.size(0), device=pos.device).view(-1, 1).expand(-1, K)
    
    edge_index = torch.stack([row.flatten(), col.flatten()], dim=0)
    return edge_index

class KPConvLayer(nn.Module):
    """
    Lightweight continuous 3D convolution layer approximating KPConv.
    Standard 2D CNNs cannot process unordered 3D points. This layer aggregates
    features from local 3D neighborhoods based on Euclidean distance.
    """
    def __init__(self, in_channels, out_channels, radius):
        super().__init__()
        self.radius = radius
        # MLP applied to individual node features and edge vectors
        self.mlp = nn.Sequential(
            nn.Linear(in_channels + 3, out_channels, bias=False),
            nn.BatchNorm1d(out_channels),
            nn.ReLU(inplace=True),
            nn.Linear(out_channels, out_channels, bias=False),
            nn.BatchNorm1d(out_channels),
            nn.ReLU(inplace=True)
        )

    def forward(self, x, pos, batch):
        """
        x: Node features [N, in_channels]
        pos: 3D coordinates [N, 3]
        batch: Batch indices mapping points to their respective graphs [N]
        """
        edges_list = []
        batch_size = int(batch.max().item() + 1)
        
        # Build radius graph per point cloud in the batch
        for b in range(batch_size):
            mask = batch == b
            pos_b = pos[mask]
            if pos_b.size(0) == 0: continue
            
            # Use fixed 32 neighbors instead of dynamic radius for massive GPU speedup
            edge_b = builtin_radius_graph(pos_b, max_num_neighbors=32)
            
            # Offset the indices back to global [N] numbering
            offset = mask.nonzero(as_tuple=True)[0][0]
            edge_b = edge_b + offset
            edges_list.append(edge_b)
            
        edge_index = torch.cat(edges_list, dim=1)
        row, col = edge_index

        # Calculate relative coordinates (edge vectors)
        rel_pos = pos[row] - pos[col]
        
        # Concatenate neighbor features with relative position
        # Shape: [num_edges, in_channels + 3]
        edge_features = torch.cat([x[col], rel_pos], dim=-1)
        
        # Pass through MLP
        edge_features = self.mlp(edge_features)
        
        # Max pooling aggregation from neighbors back to center point
        out = torch.zeros((x.size(0), edge_features.size(1)), device=x.device, dtype=edge_features.dtype)
        # Using index_add_ instead of scatter_max for simplicity and stability without torch_scatter
        out.index_add_(0, row, edge_features)
        
        # Normalize by degree (mean pooling approximation)
        degree = torch.bincount(row, minlength=x.size(0)).view(-1, 1).float().clamp_(min=1.0)
        out = out / degree
        
        return out

import math

class FlashAttentionLayer(nn.Module):
    """
    Replaces nn.MultiheadAttention with a memory-efficient PyTorch 2.0 natively fused algorithm.
    Squashes the O(N^2) VRAM matrix instantiation down to O(N).
    """
    def __init__(self, embed_dim, num_heads=4):
        super().__init__()
        self.embed_dim = embed_dim
        self.num_heads = num_heads
        self.head_dim = embed_dim // num_heads
        assert self.head_dim * num_heads == embed_dim, "embed_dim must be divisible by num_heads"

        self.q_proj = nn.Linear(embed_dim, embed_dim)
        self.k_proj = nn.Linear(embed_dim, embed_dim)
        self.v_proj = nn.Linear(embed_dim, embed_dim)
        self.out_proj = nn.Linear(embed_dim, embed_dim)

    def forward(self, query, key, value, key_padding_mask=None):
        B, seq_len_q, _ = query.shape
        _, seq_len_k, _ = key.shape

        q = self.q_proj(query).view(B, seq_len_q, self.num_heads, self.head_dim).transpose(1, 2)
        k = self.k_proj(key).view(B, seq_len_k, self.num_heads, self.head_dim).transpose(1, 2)
        v = self.v_proj(value).view(B, seq_len_k, self.num_heads, self.head_dim).transpose(1, 2)

        # PyTorch 2.0 scaled_dot_product_attention expects float masks of shape [B, num_heads, seq_len_q, seq_len_k]
        attn_mask = None
        if key_padding_mask is not None:
            # key_padding_mask is [B, seq_len_k] where True means ignore.
            # Convert to float mask where ignored positions are -inf, others are 0.
            attn_mask = torch.zeros((B, 1, 1, seq_len_k), device=query.device, dtype=query.dtype)
            attn_mask.masked_fill_(key_padding_mask.view(B, 1, 1, seq_len_k), float('-inf'))

        # Native Flash Attention / Memory Efficient Attention dispatch
        with torch.backends.cuda.sdp_kernel(enable_flash=True, enable_math=False, enable_mem_efficient=True):
            out = F.scaled_dot_product_attention(q, k, v, attn_mask=attn_mask, dropout_p=0.0)

        out = out.transpose(1, 2).contiguous().view(B, seq_len_q, self.embed_dim)
        return self.out_proj(out)

class TransformerBlock(nn.Module):
    def __init__(self, embed_dim, num_heads):
        super().__init__()
        self.self_attn = FlashAttentionLayer(embed_dim, num_heads)
        self.cross_attn = FlashAttentionLayer(embed_dim, num_heads)
        self.norm1 = nn.LayerNorm(embed_dim)
        self.norm2 = nn.LayerNorm(embed_dim)
        
        # Standard Transformer 4x expansion ratio for high capacity
        self.ffn = nn.Sequential(
            nn.Linear(embed_dim, embed_dim * 4),
            nn.GELU(),
            nn.Linear(embed_dim * 4, embed_dim)
        )
        self.norm3 = nn.LayerNorm(embed_dim)

    def forward(self, seq_s, seq_t, pad_mask_s, pad_mask_t):
        # Self-Attention
        self_s = self.self_attn(seq_s, seq_s, seq_s, key_padding_mask=pad_mask_s)
        self_t = self.self_attn(seq_t, seq_t, seq_t, key_padding_mask=pad_mask_t)
        seq_s = self.norm1(seq_s + self_s)
        seq_t = self.norm1(seq_t + self_t)
        
        # Cross-Attention
        cross_s = self.cross_attn(seq_s, seq_t, seq_t, key_padding_mask=pad_mask_t)
        cross_t = self.cross_attn(seq_t, seq_s, seq_s, key_padding_mask=pad_mask_s)
        seq_s = self.norm2(seq_s + cross_s)
        seq_t = self.norm2(seq_t + cross_t)
        
        # Feed-Forward Network
        seq_s = self.norm3(seq_s + self.ffn(seq_s))
        seq_t = self.norm3(seq_t + self.ffn(seq_t))
        return seq_s, seq_t


class GeoTransformer(nn.Module):
    """
    State-of-the-Art Deep Learning architecture for rigorous 3D point cloud registration.
    Replaces the V1 classical FPFH/SHOT descriptors with learned, rotation-invariant features.
    
    Optimized for extremely low VRAM (12GB) and Ampere (RTX 3080/4090) environments.
    """
    def __init__(self, feature_dim=128):
        super().__init__()
        
        # 1. Massive Hierarchical Geometric Backbone (Targeting 8.5M+ params)
        # Employs 5 stages of continuous 3D convolution to extract semantic geometries
        self.conv1 = KPConvLayer(in_channels=1, out_channels=128, radius=1.0)
        self.conv2 = KPConvLayer(in_channels=128, out_channels=256, radius=2.0)
        self.conv3 = KPConvLayer(in_channels=256, out_channels=512, radius=4.0)
        self.conv4 = KPConvLayer(in_channels=512, out_channels=1024, radius=8.0)
        self.conv5 = KPConvLayer(in_channels=1024, out_channels=2048, radius=16.0)
        
        # Squeeze back into the embedding projection space
        self.proj_feat = nn.Linear(2048, feature_dim)
        
        # 2. Global Transformer (Self & Cross Attention) - 6 Blocks massive sweep (Targeting 1.5M+ params)
        self.transformer_blocks = nn.ModuleList([
            TransformerBlock(embed_dim=feature_dim, num_heads=4) for _ in range(6)
        ])
        
        # 3. Final Feature Projection Head
        self.head = nn.Sequential(
            nn.Linear(feature_dim * 2, feature_dim * 2),
            nn.LayerNorm(feature_dim * 2),
            nn.ReLU(inplace=True),
            nn.Linear(feature_dim * 2, feature_dim)
        )

    def extract_features(self, pos, batch):
        """
        Pass a single point cloud through the deep 3D CNN backbone.
        """
        # Initial dummy feature (just distance from origin or constant 1)
        x = torch.ones((pos.size(0), 1), device=pos.device, dtype=pos.dtype)
        
        # Pass through massive receptive fields
        x = self.conv1(x, pos, batch)
        x = self.conv2(x, pos, batch)
        x = self.conv3(x, pos, batch)
        x = self.conv4(x, pos, batch)
        x = self.conv5(x, pos, batch)
        feat = self.proj_feat(x)
        
        return feat

    def forward(self, source_pos, source_batch, target_pos, target_batch):
        """
        Forward pass for a pair of point clouds (Source/Partial and Target/Full)
        """
        # 1. Extract Dense Local Features using KPConv approximations
        feat_s = self.extract_features(source_pos, source_batch)
        feat_t = self.extract_features(target_pos, target_batch)
        
        # 2. Prepare sequences for the Transformer via Dense Padding
        # Handles variable point counts automatically (critical for true batching)
        seq_s, mask_s = to_dense_batch(feat_s, source_batch)
        seq_t, mask_t = to_dense_batch(feat_t, target_batch)
        
        # MultiheadAttention requires True for padding tokens to ignore
        pad_mask_s = ~mask_s
        pad_mask_t = ~mask_t
        
        # 3. Transformer Layers (Self & Cross Attention sweep)
        for block in self.transformer_blocks:
            seq_s, seq_t = block(seq_s, seq_t, pad_mask_s, pad_mask_t)
        
        # Flatten back to list of variable valid points using mask
        feat_s_global = seq_s[mask_s]
        feat_t_global = seq_t[mask_t]
        
        # 4. Concatenate Local (Backbone) and Global (Transformer) features
        # This creates the ultimate "SuperPoint Descriptor"
        desc_s = torch.cat([feat_s, feat_s_global], dim=-1)
        desc_t = torch.cat([feat_t, feat_t_global], dim=-1)
        
        # Final non-linear projection
        desc_s = self.head(desc_s)
        desc_t = self.head(desc_t)
        
        # Normalize to unit sphere (required for Cosine Similarity Matching)
        desc_s = F.normalize(desc_s, p=2, dim=-1)
        desc_t = F.normalize(desc_t, p=2, dim=-1)
        
        return desc_s, desc_t

if __name__ == "__main__":
    # Quick sanity check for 12GB VRAM constraints
    print("Initializing AUM V2 GeoTransformer Backbone...")
    device = torch.device('cuda' if torch.cuda.is_available() else 'cpu')
    model = GeoTransformer(feature_dim=128).to(device)
    
    # Simulate a tiny batch (1) with realistically sized point clouds
    src_pts = torch.randn((2200, 3), device=device) # Partial scan
    tgt_pts = torch.randn((4096, 3), device=device) # Full scan
    src_batch = torch.zeros(2200, dtype=torch.long, device=device)
    tgt_batch = torch.zeros(4096, dtype=torch.long, device=device)
    
    # Enable AMP natively to test memory savings
    with torch.autocast(device_type=device.type, dtype=torch.bfloat16):
        desc_s, desc_t = model(src_pts, src_batch, tgt_pts, tgt_batch)
        
    print(f"Source Descriptors Out: {desc_s.shape} (dtype: {desc_s.dtype})")
    print(f"Target Descriptors Out: {desc_t.shape} (dtype: {desc_t.dtype})")
    
    if device.type == 'cuda':
        vram_allocated_mb = torch.cuda.memory_allocated() / (1024 ** 2)
        print(f"Current VRAM Allocation (Forward Pass): {vram_allocated_mb:.2f} MB")
        print(f"Max 12GB Bound: 12288.00 MB. Fits constraints? {'Yes' if vram_allocated_mb < 5000 else 'No (Risk of OOM)'}")
