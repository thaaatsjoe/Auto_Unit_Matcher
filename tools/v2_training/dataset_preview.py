import os
import argparse
import numpy as np
import open3d as o3d
import random
import glob
from torch.utils.data import Dataset

def load_stl(filepath, voxel_size=0.2):
    """Load STL and return a strictly downsampled point cloud via Voxel Grid."""
    mesh = o3d.io.read_triangle_mesh(filepath)
    mesh.compute_vertex_normals()
    
    # 1. Convert mesh vertices to point cloud
    pcd = o3d.geometry.PointCloud()
    pcd.points = mesh.vertices
    if mesh.has_vertex_normals():
        pcd.normals = mesh.vertex_normals
        
    # 2. Strict Voxel Grid Downsampling (HashSet Deduplication equivalent)
    # Compresses the heavy ~87,000 STL vertices down into a standardized ~6,400 points
    downpcd = pcd.voxel_down_sample(voxel_size=voxel_size)
    return downpcd

def z_axis_crop(pcd, crop_ratio=0.5):
    """Simulate a partial scan by chopping off the bottom (intaglio) along the Z axis."""
    points = np.asarray(pcd.points)
    normals = np.asarray(pcd.normals)

    # Find the bounding box Z values
    z_min = np.min(points[:, 2])
    z_max = np.max(points[:, 2])
    
    # Define a cutoff Z based on crop ratio
    # e.g., crop_ratio = 0.5 chops the bottom 50%
    z_range = z_max - z_min
    z_threshold = z_min + (z_range * crop_ratio)
    
    # Keep points above the threshold (occlusal surface simulation)
    mask = points[:, 2] > z_threshold
    
    cropped_pcd = o3d.geometry.PointCloud()
    cropped_pcd.points = o3d.utility.Vector3dVector(points[mask])
    if normals.shape[0] > 0:
        cropped_pcd.normals = o3d.utility.Vector3dVector(normals[mask])
        
    return cropped_pcd

def add_noise(pcd, noise_std=0.01):
    """Add Gaussian noise to the points to simulate scanner inaccuracy."""
    points = np.asarray(pcd.points)
    noise = np.random.normal(0, noise_std, points.shape)
    noisy_points = points + noise
    
    noisy_pcd = o3d.geometry.PointCloud()
    noisy_pcd.points = o3d.utility.Vector3dVector(noisy_points)
    noisy_pcd.normals = pcd.normals 
    return noisy_pcd

def apply_random_rotation(pcd):
    """Apply an arbitrary random 3D rotation (SO(3)) to break positional dependency."""
    R = pcd.get_rotation_matrix_from_xyz((
        random.uniform(0, 2 * np.pi),
        random.uniform(0, 2 * np.pi),
        random.uniform(0, 2 * np.pi)
    ))
    rotated_pcd = pcd.rotate(R, center=(0, 0, 0))
    return rotated_pcd

def visualize(original_pcd, augmented_pcd):
    """Visualize both point clouds side by side."""
    # Color them differently for clarity: Original=Gray, Augmented=Red
    original_pcd.paint_uniform_color([0.6, 0.6, 0.6])
    augmented_pcd.paint_uniform_color([1.0, 0.2, 0.2])
    
    # Shift the augmented point cloud so they sit side-by-side in the visualizer
    bbox = original_pcd.get_axis_aligned_bounding_box()
    extent = bbox.get_extent()
    augmented_pcd.translate((extent[0] * 1.5, 0, 0))
    
    print("Visualizing Original (Gray) vs Augmented Partial Scan (Red)...")
    o3d.visualization.draw_geometries([original_pcd, augmented_pcd])

# ----------------------------------------------------------------------------
# AUM V2 Supervised Training Dataset Loader
# ----------------------------------------------------------------------------
class DentalPartialDataset(Dataset):
    """
    Simulates clinical conditions by returning a full ground truth crown (Target)
    alongside a cropped, noisy, rotated partial intraoral scan (Source).
    """
    def __init__(self, data_dir, voxel_size=0.2, noise_std=0.005):
        self.files = glob.glob(os.path.join(data_dir, "*.stl"))
        if not self.files:
            raise ValueError(f"No STL files found in '{data_dir}'! Check your volume mounts.")
        self.voxel_size = voxel_size
        self.noise_std = noise_std
        
    def __len__(self):
        return len(self.files)
        
    def __getitem__(self, idx):
        filepath = self.files[idx]
        
        # 1. Base Scan (Target geometry)
        pcd = load_stl(filepath, voxel_size=self.voxel_size)
        tgt_pts = np.asarray(pcd.points)
        
        # 2. Augmented Scan (Source partial geometry)
        crop_ratio = random.uniform(0.1, 0.6)
        source_pcd = z_axis_crop(pcd, crop_ratio)
        source_pcd = add_noise(source_pcd, self.noise_std)
        
        # Apply arbitrary rigid rotation to break spatial dependency
        R = source_pcd.get_rotation_matrix_from_xyz((
            random.uniform(0, 2 * np.pi),
            random.uniform(0, 2 * np.pi),
            random.uniform(0, 2 * np.pi)
        ))
        source_pcd.rotate(R, center=(0, 0, 0))
        
        # The rotation R was applied to source_pcd. 
        # Source * Transform = Target. Since Source = Target * R, Source * R_inv = Target.
        transform = np.eye(4)
        transform[:3, :3] = np.linalg.inv(R)
        
        return {
            'target_cloud': tgt_pts,
            'source_cloud': np.asarray(source_pcd.points),
            'transform_matrix': transform
        }

def main():
    parser = argparse.ArgumentParser(description="Dataset Preview & Augmentation Test")
    parser.add_argument("--stl", type=str, required=True, help="Path to a sample STL file")
    parser.add_argument("--crop", type=float, default=0.5, help="Z-axis crop ratio (0.0=No crop, 0.5=Half)")
    parser.add_argument("--noise", type=float, default=0.01, help="Gaussian noise standard deviation")
    parser.add_argument("--rotate", action="store_true", help="Apply random SE(3) rotation")
    args = parser.parse_args()

    if not os.path.exists(args.stl):
        print(f"Error: File '{args.stl}' not found.")
        return

    print(f"Loading STL: '{args.stl}'")
    original_pcd = load_stl(args.stl)
    print(f"Sampled baseline point cloud: {len(original_pcd.points)} points.")
    
    augmented_pcd = o3d.geometry.PointCloud(original_pcd)
    
    print("\nApplying Non-Rigid Data Augmentations:")
    if args.crop > 0.0:
        print(f" -> Performing Z-axis crop (Removing bottom {args.crop*100}% of mesh)")
        augmented_pcd = z_axis_crop(augmented_pcd, args.crop)
        
    if args.noise > 0.0:
        print(f" -> Injecting sensor noise (std_dev: {args.noise}mm)")
        augmented_pcd = add_noise(augmented_pcd, args.noise)
        
    if args.rotate:
        print(" -> Applying random SO(3) 3D coordinate rotation")
        augmented_pcd = apply_random_rotation(augmented_pcd)
        
    print(f"\nAugmented partial cloud contains {len(augmented_pcd.points)} points.")
    visualize(original_pcd, augmented_pcd)

if __name__ == "__main__":
    main()
