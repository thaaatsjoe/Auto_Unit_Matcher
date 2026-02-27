import torch
import torch.nn as nn
import torch.nn.functional as F
import argparse
import os

# ============================================================================
# Architectural Decoupling: Split the Trained Model into Two Modules
# ============================================================================

def builtin_radius_graph_inference(pos: torch.Tensor, max_num_neighbors: int = 32) -> torch.Tensor:
    """Pure PyTorch O(N^2) Nearest Neighbors for ONNX compatibility."""
    dists = torch.cdist(pos, pos)
    K = max_num_neighbors
    if pos.size(0) < K:
        K = pos.size(0)
    _, col = torch.topk(dists, k=K, dim=1, largest=False)
    row = torch.arange(pos.size(0), device=pos.device).view(-1, 1).expand(-1, K)
    edge_index = torch.stack([row.flatten(), col.flatten()], dim=0)
    return edge_index

class InferenceKPConvLayer(nn.Module):
    def __init__(self, original_conv):
        super().__init__()
        self.mlp = original_conv.mlp
        
    def forward(self, x: torch.Tensor, pos: torch.Tensor) -> torch.Tensor:
        edge_index = builtin_radius_graph_inference(pos, 32)
        row = edge_index[0]
        col = edge_index[1]

        rel_pos = pos[row] - pos[col]
        edge_features = torch.cat([x[col], rel_pos], dim=-1)
        edge_features = self.mlp(edge_features)
        
        out = torch.zeros((x.size(0), edge_features.size(1)), device=x.device, dtype=edge_features.dtype)
        # ONNX supports index_add_ natively
        out.index_add_(0, row, edge_features)
        
        # ONNX Opset 14 does NOT support torch.bincount.
        # We replace it with an explicit scatter_add using a ones tensor.
        ones = torch.ones((row.size(0), 1), device=x.device, dtype=torch.float32)
        degree = torch.zeros((x.size(0), 1), device=x.device, dtype=torch.float32)
        degree.scatter_add_(0, row.view(-1, 1), ones)
        
        degree = degree.clamp(min=1.0)
        out = out / degree
        return out

class ONNXMultiheadAttention(nn.Module):
    """
    Standard PyTorch nn.MultiheadAttention hardcodes the sequence length during ONNX tracing 
    (e.g., N=8000). This ONNX-safe wrapper manually computes Scaled Dot-Product Attention 
    and explicitly uses `-1` in .view() to guarantee true dynamic axes in the ONNX graph.
    """
    def __init__(self, mha: nn.MultiheadAttention):
        super().__init__()
        self.embed_dim = mha.embed_dim
        self.num_heads = mha.num_heads
        self.head_dim = self.embed_dim // self.num_heads
        self.in_proj_weight = mha.in_proj_weight
        self.in_proj_bias = mha.in_proj_bias
        self.out_proj = mha.out_proj

    def forward(self, query, key, value):
        # Apply the dense Q/K/V projections
        qkv_q = F.linear(query, self.in_proj_weight[:self.embed_dim], self.in_proj_bias[:self.embed_dim])
        qkv_k = F.linear(key, self.in_proj_weight[self.embed_dim:2*self.embed_dim], self.in_proj_bias[self.embed_dim:2*self.embed_dim])
        qkv_v = F.linear(value, self.in_proj_weight[2*self.embed_dim:], self.in_proj_bias[2*self.embed_dim:])
        
        # CRITICAL: Use -1 for the temporal/sequence dimension 'N' so ONNX infers it dynamically!
        # Reshape to [1, -1, num_heads, head_dim] then transpose to [1, num_heads, -1, head_dim]
        q = qkv_q.view(1, -1, self.num_heads, self.head_dim).transpose(1, 2)
        k = qkv_k.view(1, -1, self.num_heads, self.head_dim).transpose(1, 2)
        v = qkv_v.view(1, -1, self.num_heads, self.head_dim).transpose(1, 2)
        
        # Scaled dot-product attention
        scores = torch.matmul(q, k.transpose(-2, -1)) / (self.head_dim ** 0.5)
        attn = F.softmax(scores, dim=-1)
        
        # Blend values
        out = torch.matmul(attn, v) # [1, num_heads, N, head_dim]
        
        # Re-flatten the heads dynamically using -1
        out = out.transpose(1, 2).contiguous().view(1, -1, self.embed_dim)
        
        # Final output projection
        out = self.out_proj(out)
        
        return out, attn

# ----------------------------------------------------------------------------
# MODULE 1: The Global Semantic Embedder (Backbone)
# ----------------------------------------------------------------------------
class AumBackbone(nn.Module):
    """
    Stage 1: Generates decoupled, independent local geometry features for 
    1-to-N FAISS nearest neighbor search. Does not require the target scan.
    """
    def __init__(self, raw_model):
        super().__init__()
        self.conv1 = InferenceKPConvLayer(raw_model.conv1)
        self.conv2 = InferenceKPConvLayer(raw_model.conv2)
        self.conv3 = InferenceKPConvLayer(raw_model.conv3)
        self.self_attn = ONNXMultiheadAttention(raw_model.self_attn)
        self.norm1 = raw_model.norm1

    def forward(self, pos: torch.Tensor) -> torch.Tensor:
        # Expected Input: 'pos' [N, 3] representing N points in 3D Space
        x = torch.ones((pos.size(0), 1), device=pos.device, dtype=pos.dtype)
        
        # 1. Local KPConv Features
        x = self.conv1(x, pos)
        x = self.conv2(x, pos)
        feat = self.conv3(x, pos)  # [N, 128]
        
        # 2. Add explicit Batch Dimension = 1 for MultiheadAttention
        seq = feat.unsqueeze(0)    # [1, N, 128]
        
        # 3. Structural Self Attention
        self_out, _ = self.self_attn(seq, seq, seq)
        seq = self.norm1(seq + self_out)
        
        # Strip Batch Dimension -> [N, 128]
        return seq.squeeze(0)

# ----------------------------------------------------------------------------
# MODULE 2: The Pairwise Matcher (Cross-Attention)
# ----------------------------------------------------------------------------
class AumMatcher(nn.Module):
    """
    Stage 2: Cross-Attention Transformer used ONLY for the pairwise verification
    of the top candidates returned by FAISS.
    """
    def __init__(self, raw_model):
        super().__init__()
        self.cross_attn = ONNXMultiheadAttention(raw_model.cross_attn)
        self.norm2 = raw_model.norm2
        self.head = raw_model.head

    def forward(self, feat_s: torch.Tensor, feat_t: torch.Tensor):
        # Expected Input: 'feat_s' [N, 128], 'feat_t' [M, 128]
        
        # Re-apply explicit Batch Dimension = 1
        seq_s = feat_s.unsqueeze(0)  # [1, N, 128]
        seq_t = feat_t.unsqueeze(0)  # [1, M, 128]
        
        # 1. Cross Attention
        cross_s, _ = self.cross_attn(seq_s, seq_t, seq_t)
        cross_t, _ = self.cross_attn(seq_t, seq_s, seq_s)
        
        seq_s = self.norm2(seq_s + cross_s)
        seq_t = self.norm2(seq_t + cross_t)
        
        # Strip Batch Dimension
        feat_s_global = seq_s.squeeze(0) # [N, 128]
        feat_t_global = seq_t.squeeze(0) # [M, 128]
        
        # 2. Output Projection and L2 Normalization
        desc_s = torch.cat([feat_s, feat_s_global], dim=-1) # [N, 256]
        desc_t = torch.cat([feat_t, feat_t_global], dim=-1) # [M, 256]
        
        desc_s = self.head(desc_s) # [N, 128]
        desc_t = self.head(desc_t) # [M, 128]
        
        desc_s = F.normalize(desc_s, p=2.0, dim=-1)
        desc_t = F.normalize(desc_t, p=2.0, dim=-1)
        
        return desc_s, desc_t

# ============================================================================
# Main Export Logic
# ============================================================================
def export_onnx(model_path, out_dir):
    from model import GeoTransformer
    device = torch.device('cpu')
    print("1. Loading raw GeoTransformer training weights...")
    raw_model = GeoTransformer(feature_dim=128).to(device)
    raw_model.load_state_dict(torch.load(model_path, map_location=device, weights_only=True))
    raw_model.eval()
    
    # Instantiate modules
    backbone = AumBackbone(raw_model).eval()
    matcher = AumMatcher(raw_model).eval()

    # Create dummy tensors for ONNX tracing
    # Point Cloud sizes MUST NOT be hardcoded. ONNX needs shapes just to trace the math operations.
    dummy_pos_N = torch.randn(8000, 3) 
    dummy_feat_N = torch.randn(8000, 128)
    dummy_feat_M = torch.randn(12000, 128) # Different size for Target to verify asymmetry

    os.makedirs(out_dir, exist_ok=True)
    
    # ------------------------------------------------------------------------
    # Export Backbone (Embedder)
    # ------------------------------------------------------------------------
    backbone_path = os.path.join(out_dir, "aum_backbone.onnx")
    print(f"\n2. Exporting AumBackbone (Stage 1 FAISS Embedder) to {backbone_path}...")
    torch.onnx.export(
        backbone, 
        dummy_pos_N, 
        backbone_path,
        export_params=True,
        opset_version=14,          # Opset 14 supports native PyTorch cdist/topk
        do_constant_folding=True,
        input_names=['points'],    # Logical naming for C# ingestion
        output_names=['features'],
        # CRITICAL ARCHITECTURE REQUIREMENT: Point cloud size must be fully dynamic.
        dynamic_axes={'points': {0: 'num_points'}, 'features': {0: 'num_points'}}
    )
    print("   [SUCCESS] Backbone Exported.")

    # ------------------------------------------------------------------------
    # Export Matcher (Cross-Attention)
    # ------------------------------------------------------------------------
    matcher_path = os.path.join(out_dir, "aum_matcher.onnx")
    print(f"\n3. Exporting AumMatcher (Stage 2 Pairwise Verification) to {matcher_path}...")
    torch.onnx.export(
        matcher, 
        (dummy_feat_N, dummy_feat_M), 
        matcher_path,
        export_params=True,
        opset_version=14,
        do_constant_folding=True,
        input_names=['source_features', 'target_features'], 
        output_names=['source_desc', 'target_desc'],
        # CRITICAL ARCHITECTURE REQUIREMENT: N and M must be dynamic and independent.
        dynamic_axes={
            'source_features': {0: 'num_source_points'}, 
            'target_features': {0: 'num_target_points'},
            'source_desc': {0: 'num_source_points'},
            'target_desc': {0: 'num_target_points'}
        }
    )
    print("   [SUCCESS] Matcher Exported.")
    print("\n[ALL TASKS COMPLETE] Dual ONNX models ready for C# Microsoft.ML.OnnxRuntime Integration.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", type=str, default="out/aum_v2_model.pth")
    parser.add_argument("--out", type=str, default="out/")
    args = parser.parse_args()
    export_onnx(args.model, args.out)
