import torch
import torch.nn as nn
import torch.nn.functional as F

# ----------------------------------------------------------------------------
# AUM V2 Geometric Objective Functions
# ----------------------------------------------------------------------------

class CircleLoss(nn.Module):
    """
    Contrastive Feature Matching Loss (Circle Loss).
    Forces the network to generate identical descriptors for points that physically
    occupy the same 3D coordinate in the ground-truth space, and divergent 
    descriptors for points that do not match.
    
    Ref: Circle Loss: A Unified Perspective of Pair Similarity Optimization (CVPR 2020)
    """
    def __init__(self, pos_margin=0.1, neg_margin=1.4, pos_optimal=0.1, neg_optimal=1.4, log_scale=64):
        super(CircleLoss, self).__init__()
        self.pos_margin = pos_margin
        self.neg_margin = neg_margin
        self.pos_optimal = pos_optimal
        self.neg_optimal = neg_optimal
        self.log_scale = log_scale

    def forward(self, desc_s, desc_t, pos_s, pos_t, gt_transform, pos_radius=0.1, safe_radius=0.25):
        """
        desc_s: Source descriptors [N, C]
        desc_t: Target descriptors [M, C]
        pos_s: Initial Source coordinates [N, 3]
        pos_t: Initial Target coordinates [M, 3]
        gt_transform: Ground truth 4x4 homogenous matrix mapping Source -> Target
        """
        # Transform source points to ground truth target space
        R = gt_transform[:3, :3]
        t = gt_transform[:3, 3]
        pos_s_gt = (R @ pos_s.T).T + t

        # Calculate physical pairwise distances in 3D Euclidean space
        # [N, M]
        phys_dist = torch.cdist(pos_s_gt, pos_t)

        # Define Positive matches (physical distance < pos_radius)
        # Define Negative matches (physical distance > safe_radius)
        pos_mask = phys_dist < pos_radius
        neg_mask = phys_dist > safe_radius

        # Calculate Descriptor similarity (Cosine similarity since desc are normalized)
        # [N, M]
        similarity = torch.matmul(desc_s, desc_t.T)

        # Vectorized Tensor Math (Eliminates the massive Python for loop overhead)
        alpha_p = torch.clamp_min(-similarity.detach() + 1 + self.pos_margin, min=0.0)
        alpha_n = torch.clamp_min(similarity.detach() + self.neg_margin, min=0.0)

        logit_p = -self.log_scale * alpha_p * (similarity - self.pos_optimal)
        logit_n = self.log_scale * alpha_n * (similarity - self.neg_optimal)

        # Mask out invalid pairs by dropping them to extremely small logits (-10000)
        # This forces e^(-10000) to safely evaluate to 0.0 inside the logsumexp.
        logit_p = logit_p.masked_fill(~pos_mask, -1e4)
        logit_n = logit_n.masked_fill(~neg_mask, -1e4)

        # Logsumexp over the target dimension (dim=1) safely collapses the matrix into an [N] array
        lse_positive = torch.logsumexp(logit_p, dim=1)
        lse_negative = torch.logsumexp(logit_n, dim=1)

        # A point is only valid for contrastive loss if it has AT LEAST 1 positive and 1 negative target point
        valid_rows = (pos_mask.sum(dim=1) > 0) & (neg_mask.sum(dim=1) > 0)

        if not valid_rows.any():
            return torch.tensor(0.0, device=similarity.device, requires_grad=True)

        # Softplus stabilization on the exclusively valid points
        loss_per_point = F.softplus(lse_positive[valid_rows] + lse_negative[valid_rows])
        
        return loss_per_point.mean() / self.log_scale


class PointMatchingLoss(nn.Module):
    """
    Transformation Loss (Point-to-Point L1/L2 distance after feature-matching).
    Computes soft-correspondences based on descriptor similarity, derives an estimated
    transform via Differentiable SVD, and penalizes deviation from the ground truth.
    """
    def __init__(self):
        super().__init__()

    def forward(self, desc_s, desc_t, pos_s, pos_t, gt_transform):
        """
        Calculates expected transformation deviation based heavily on descriptor confidence.
        """
        # Feature correlation matrix
        # [N, M] - memory intensive, but safe for 12GB VRAM if batch_size=1
        corr_matrix = torch.matmul(desc_s, desc_t.T)
        
        # Softmax to get correspondence probabilities
        # Normalizing over target points (For each source point, what is the probability it matches Target J?)
        prob_matrix = F.softmax(corr_matrix, dim=-1)
        
        # Derive the predicted target location for each source point
        # [N, 3] by computing the weighted average of target coordinates
        pred_pos_t_for_s = torch.matmul(prob_matrix, pos_t)

        # Map source to true ground truth location
        R_gt = gt_transform[:3, :3]
        t_gt = gt_transform[:3, 3]
        gt_pos_t_for_s = (R_gt @ pos_s.T).T + t_gt

        # L1 Loss between predicted soft-location and actual rigid ground truth location
        # L1 is robust to outliers compared to L2 (MSE)
        geom_loss = F.l1_loss(pred_pos_t_for_s, gt_pos_t_for_s)

        return geom_loss

if __name__ == "__main__":
    print("Testing AUM V2 Objective Functions...")
    device = torch.device('cuda' if torch.cuda.is_available() else 'cpu')
    
    # Dummy setup
    desc_s = F.normalize(torch.randn((2200, 128), device=device), p=2, dim=-1)
    desc_t = F.normalize(torch.randn((4096, 128), device=device), p=2, dim=-1)
    pos_s = torch.randn((2200, 3), device=device)
    pos_t = torch.randn((4096, 3), device=device)
    gt_tf = torch.eye(4, device=device)
    
    circle_criterion = CircleLoss().to(device)
    point_criterion = PointMatchingLoss().to(device)
    
    with torch.autocast(device_type=device.type, dtype=torch.bfloat16):
        c_loss = circle_criterion(desc_s, desc_t, pos_s, pos_t, gt_tf)
        p_loss = point_criterion(desc_s, desc_t, pos_s, pos_t, gt_tf)
        
    print(f"Circle (Contrastive) Loss: {c_loss.item():.4f}")
    print(f"Point-Matching (Geometric) Loss: {p_loss.item():.4f}")
    
    if device.type == 'cuda':
        vram_allocated_mb = torch.cuda.memory_allocated() / (1024 ** 2)
        print(f"Current VRAM Allocation (Loss Eval): {vram_allocated_mb:.2f} MB")
