import os
import argparse
import torch
from torch.utils.data import DataLoader
from tqdm import tqdm

from dataset_preview import DentalPartialDataset
from model import GeoTransformer
from loss import CircleLoss, PointMatchingLoss

# ----------------------------------------------------------------------------
# AUM V2 GeoTransformer Training Loop (RTX 3080 Ti - 12GB VRAM Optimized)
# ----------------------------------------------------------------------------

def pyg_collate(batch_list):
    """
    Custom collate function for PyG variable sized point clouds.
    Combines them into a flat tensor securely on the CPU to allow safe multiprocessing serialization.
    """
    device = torch.device('cpu')
    
    tgt_pos_list = []
    src_pos_list = []
    tgt_batch_list = []
    src_batch_list = []
    gt_tf_list = []
    
    for i, item in enumerate(batch_list):
        tgt = item['target_cloud'].to(torch.float32)
        src = item['source_cloud'].to(torch.float32)
        
        tgt_pos_list.append(tgt)
        src_pos_list.append(src)
        
        tgt_batch_list.append(torch.full((tgt.shape[0],), i, dtype=torch.long))
        src_batch_list.append(torch.full((src.shape[0],), i, dtype=torch.long))
        
        gt_tf_list.append(item['transform_matrix'].to(torch.float32))
        
    return {
        'target_cloud': torch.cat(tgt_pos_list, dim=0),
        'source_cloud': torch.cat(src_pos_list, dim=0),
        'target_batch': torch.cat(tgt_batch_list, dim=0),
        'source_batch': torch.cat(src_batch_list, dim=0),
        'transform_matrix': torch.stack(gt_tf_list, dim=0)
    }

def train_epoch(model, dataloader, optimizer, circle_criterion, geom_criterion, device, scaler, grad_accum_steps):
    model.train()
    total_loss = 0.0
    
    optimizer.zero_grad()
    
    # We use tqdm for a nice terminal progress bar
    pbar = tqdm(enumerate(dataloader), total=len(dataloader), desc="Training", leave=False)
    
    for i, batch in pbar:
        # 1. Unpack DataLoader Batch & Push correctly to VRAM here (safe multi-processing boundary)
        tgt_pos_flat = batch['target_cloud'].to(device, non_blocking=True)
        src_pos_flat = batch['source_cloud'].to(device, non_blocking=True)
        tgt_batch = batch['target_batch'].to(device, non_blocking=True)
        src_batch = batch['source_batch'].to(device, non_blocking=True)
        gt_tf = batch['transform_matrix'].to(device, non_blocking=True)
        
        B = gt_tf.shape[0]
        
        # 2. Forward Pass (Mixed Precision for VRAM)
        with torch.autocast(device_type=device.type, dtype=torch.bfloat16):
            desc_s, desc_t = model(src_pos_flat, src_batch, tgt_pos_flat, tgt_batch)
            
            c_loss_sum = 0.0
            g_loss_sum = 0.0
            
            # Compute loss per point-cloud pair to prevent BxNxM cross-batch mathematical fusion
            for b in range(B):
                mask_s = src_batch == b
                mask_t = tgt_batch == b
                
                c_l = circle_criterion(desc_s[mask_s], desc_t[mask_t], src_pos_flat[mask_s], tgt_pos_flat[mask_t], gt_tf[b])
                g_l = geom_criterion(desc_s[mask_s], desc_t[mask_t], src_pos_flat[mask_s], tgt_pos_flat[mask_t], gt_tf[b])
                
                c_loss_sum += c_l
                g_loss_sum += g_l
                
            c_loss = c_loss_sum / B
            g_loss = g_loss_sum / B
            
            # Weighting factors
            loss = c_loss + (g_loss * 5.0)
            
            # Normalize loss for gradient accumulation
            loss = loss / grad_accum_steps
            
        # 3. Backward Pass (Scaler handles fp16 gradients safely)
        scaler.scale(loss).backward()
        
        # Step optimizer every N iterations (Gradient Accumulation)
        if (i + 1) % grad_accum_steps == 0 or (i + 1) == len(dataloader):
            scaler.step(optimizer)
            scaler.update()
            optimizer.zero_grad(set_to_none=True)
            
        total_loss += loss.item() * grad_accum_steps
        pbar.set_postfix({'Loss': f"{loss.item() * grad_accum_steps:.4f}"})
        
    return total_loss / len(dataloader)

class PrecompiledTensorDataset(torch.utils.data.Dataset):
    """
    Dedicated blazing-fast DataLoader that streams pre-rendered PyTorch `.pt` 
    Tensors natively off the NVMe drive. Completely bypasses the massive CPU 
    geometry-rendering bottleneck.
    """
    def __init__(self, data_dir):
        import glob
        self.files = glob.glob(os.path.join(data_dir, "*.pt"))
        if not self.files:
            raise ValueError(f"No .pt files found in {data_dir}. Did you run preprocess.py first?")
            
    def __len__(self):
        return len(self.files)
        
    def __getitem__(self, idx):
        # Native torch.load takes <0.001 seconds
        return torch.load(self.files[idx], map_location='cpu', weights_only=True)

def main():
    parser = argparse.ArgumentParser(description="AUM V2 GeoTransformer Training")
    parser.add_argument("--data_dir", type=str, default="/workspace/data_pt", help="Path to pre-compiled .pt dataset (NOT STL)")
    parser.add_argument("--epochs", type=int, default=10, help="Number of training epochs")
    parser.add_argument("--batch_size", type=int, default=4, help="Physical batch size (4 fits comfortably inside 12GB)")
    parser.add_argument("--grad_accum", type=int, default=4, help="Gradient Accumulation steps to simulate batch_size=16")
    parser.add_argument("--num_workers", type=int, default=0, help="DataLoader Multi-processing workers (Must be 0 for .pt files to avoid IPC serialization delays)")
    parser.add_argument("--lr", type=float, default=1e-4, help="Learning Rate")
    args = parser.parse_args()
    
    # CRITICAL: Prevent Open3D and NumPy from spawning 32 threads inside EACH of the workers 
    if args.num_workers > 0:
        os.environ["OMP_NUM_THREADS"] = "1"
        os.environ["OPENBLAS_NUM_THREADS"] = "1"
        os.environ["MKL_NUM_THREADS"] = "1"
        os.environ["VECLIB_MAXIMUM_THREADS"] = "1"
        os.environ["NUMEXPR_NUM_THREADS"] = "1"

    # Hardware Setup
    device = torch.device('cuda' if torch.cuda.is_available() else 'cpu')
    print(f"Training Device: {device}")
    
    # Enable TF32 matrix multiplication for Ampere acceleration
    if device.type == 'cuda':
        torch.backends.cuda.matmul.allow_tf32 = True
        torch.backends.cudnn.allow_tf32 = True
        print(f"CUDA TF32 MatMul Enabled: {torch.backends.cuda.matmul.allow_tf32}")

    # Initialize Dataset (Native PyTorch Streamer)
    print(f"Initializing Accelerated Tensor Dataset at {args.data_dir}...")
    dataset = PrecompiledTensorDataset(data_dir=args.data_dir)
    print(f"Dataset compiled. Found {len(dataset)} items.")
    
    import multiprocessing as mp
    try:
        if args.num_workers > 0:
            mp.set_start_method('spawn', force=True)
    except RuntimeError:
        pass

    dataloader = DataLoader(
        dataset, 
        batch_size=args.batch_size, 
        shuffle=True, 
        num_workers=0, 
        pin_memory=True, 
        collate_fn=pyg_collate
    )

    # Initialize Neural Network
    model = GeoTransformer(feature_dim=128).to(device)
    total_params = sum(p.numel() for p in model.parameters())
    print(f"GeoTransformer Online. Parameters: {total_params:,}")

    # Loss Functions & Optimizer
    circle_criterion = CircleLoss().to(device)
    geom_criterion = PointMatchingLoss().to(device)
    
    # AdamW is robust for Transformers
    optimizer = torch.optim.AdamW(model.parameters(), lr=args.lr, weight_decay=1e-4)
    
    # Mixed precision gradient scaler
    scaler = torch.cuda.amp.GradScaler()

    # Main Loop
    print("\nStarting Training Loop...")
    for epoch in range(1, args.epochs + 1):
        loss = train_epoch(
            model=model, 
            dataloader=dataloader, 
            optimizer=optimizer, 
            circle_criterion=circle_criterion, 
            geom_criterion=geom_criterion, 
            device=device, 
            scaler=scaler,
            grad_accum_steps=args.grad_accum
        )
        print(f"Epoch {epoch}/{args.epochs} | Avg Loss: {loss:.4f}")
        
        if device.type == 'cuda':
            vram_mb = torch.cuda.max_memory_allocated() / (1024 ** 2)
            print(f"-> Peak VRAM usage this epoch: {vram_mb:.2f} MB")
            torch.cuda.reset_peak_memory_stats()
            
    print("Training Completed Successfully.")

if __name__ == "__main__":
    main()
