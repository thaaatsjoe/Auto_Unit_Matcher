import os
import argparse
import multiprocessing as mp
from functools import partial
import torch
from dataset_preview import DentalPartialDataset
from tqdm import tqdm

# ----------------------------------------------------------------------------
# AUM V2 Offline Dataset Pre-processor (STL to PyTorch Tensor .pt)
# ----------------------------------------------------------------------------

def process_single_file(idx, dataset_kwargs, out_dir):
    """
    Instantiates a throwaway Dataset object purely to tap into its __getitem__
    raycasting logic for a single file, bypassing all PyTorch dataloader overhead.
    """
    try:
        # Create a fresh local dataset instance to avoid multiprocessing lockups on C++ pointers
        local_dataset = DentalPartialDataset(**dataset_kwargs)
        
        # Grab the filename to use as the save key
        file_path = local_dataset.files[idx]
        base_name = os.path.basename(file_path).replace(".stl", ".pt")
        save_path = os.path.join(out_dir, base_name)
        
        # Skip if already compiled (allows safe resuming of interrupted jobs)
        if os.path.exists(save_path):
            return True
        
        # Run the heavy Trimesh / Open3D Embree Raycasting math
        sample = local_dataset[idx]
        
        # Convert NumPy arrays directly to PyTorch Half-Precision (FP16) Tensors.
        # This literally halves the physical size of the dataset on the hard drive
        # while keeping more than enough precision (<0.1mm) for coordinates.
        pt_data = {
            'target_cloud': torch.from_numpy(sample['target_cloud']).half(),
            'source_cloud': torch.from_numpy(sample['source_cloud']).half(),
            'transform_matrix': torch.from_numpy(sample['transform_matrix']).to(torch.float32) # Keep TF matrix stable
        }
        
        # Save to disk as native compiled PyTorch binary
        torch.save(pt_data, save_path)
        return True
        
    except Exception as e:
        print(f"\nFailed to process index {idx}: {e}")
        return False

def main():
    parser = argparse.ArgumentParser(description="Convert STLs to PyTorch Tensors")
    parser.add_argument("--data_dir", type=str, default="/workspace/data", help="STL Directory")
    parser.add_argument("--out_dir", type=str, default="/home/AUM_Dataset_PT", help="Tensor Output Directory (Native Linux Ext4)")
    parser.add_argument("--workers", type=int, default=os.cpu_count() - 2, help="CPU Cores to dedicate")
    parser.add_argument("--voxel", type=float, default=0.2, help="Voxel Grid Downsample Resolution")
    args = parser.parse_args()

    # Create the output directory
    os.makedirs(args.out_dir, exist_ok=True)
    
    # We do NOT want numpy or open3d spawning their own hidden background threads
    # otherwise 10 workers * 32 threads = 320 threads locking up the Windows OS.
    os.environ["OMP_NUM_THREADS"] = "1"
    os.environ["OPENBLAS_NUM_THREADS"] = "1"
    os.environ["MKL_NUM_THREADS"] = "1"
    
    print(f"Scanning raw STL files in: {args.data_dir}...")
    dataset_kwargs = {
        'data_dir': args.data_dir,
        'voxel_size': args.voxel,
        'noise_std': 0.005
    }
    
    # Instantiate once just to get the total length and verify access
    master_dataset = DentalPartialDataset(**dataset_kwargs)
    num_files = len(master_dataset)
    print(f"Discovered {num_files} distinct crowns.")
    print(f"Targeting Pre-compiled Database: {args.out_dir}")
    print(f"Igniting {args.workers} concurrent Intel Embree CPU contexts...\n")

    # We MUST use spawn to prevent Open3D C++ segmentation faults on Linux
    mp.set_start_method('spawn', force=True)
    
    # Currying the function arguments natively
    worker_fn = partial(process_single_file, dataset_kwargs=dataset_kwargs, out_dir=args.out_dir)
    indices = range(num_files)
    
    success_count = 0
    with mp.Pool(processes=args.workers) as pool:
        # Map async with a nice progress bar
        for result in tqdm(pool.imap_unordered(worker_fn, indices), total=num_files, desc="Compiling Data"):
            if result:
                success_count += 1
                
    print(f"\nOptimization Complete.")
    print(f"Successfully compiled {success_count}/{num_files} (.pt) PyTorch matrices.")
    print("\nNext Step: Edit train.py to load from the new --data_dir_pt flag!")

if __name__ == "__main__":
    main()
