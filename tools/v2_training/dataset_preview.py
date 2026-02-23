import os
import random
import glob
import math
import numpy as np
import open3d as o3d
import trimesh
from scipy.spatial.transform import Rotation as R
import torch
from torch.utils.data import Dataset, DataLoader

class DentalPartialDataset(Dataset):
    def __init__(self, data_dir, num_points=4096, noise_std=0.005, crop_range=(0.3, 0.7)):
        self.data_dir = data_dir
        self.num_points = num_points
        self.noise_std = noise_std
        self.crop_range = crop_range
        self.files = glob.glob(os.path.join(data_dir, "*.stl"))
        if not self.files:
            raise ValueError(f"No STL files found in {data_dir}. Ensure volume is mounted correctly.")

    def __len__(self):
        return len(self.files)

    def _sample_points(self, mesh, num_points):
        # Sample points uniformly from the mesh surface mathematically
        points, _ = trimesh.sample.sample_surface(mesh, num_points)
        return points

    def __getitem__(self, idx):
        file_path = self.files[idx]
        
        # 1. Load full crown STL
        # Using trimesh as it is highly robust for parsing arbitrary STLs
        mesh = trimesh.load(file_path, force='mesh')
        
        # Center the mesh naturally before ANY operations so our raycasts properly align
        centroid = np.mean(mesh.vertices, axis=0)
        mesh.vertices -= centroid
        
        # 2. Downsample to uniform point cloud (Target) to save VRAM
        target_points = self._sample_points(mesh, self.num_points)
        
        # 3. Clone to create Source (Partial)
        source_points = target_points.copy()
        
        # 4. The Cut: Virtual Scanner (Exact Mesh Raycasting)
        # Open3D's Hidden Point Removal uses spherical bounding volumes which inherently 
        # fail to resolve deep concavities (like occlusal fossae). 
        # We replace it with exact BVH raycasting from trimesh, simulating physical light rays.
        
        diameter = mesh.scale
        cam_dist = diameter * 5.0
        
        all_visible_indices = set()
        camera_positions = []
        
        # 1. Orbiting Cameras (49 degrees)
        NUM_STEPS = 20
        elev_rad = math.radians(49.0)
        for i in range(NUM_STEPS):
            azimuth = 2.0 * math.pi * i / NUM_STEPS
            cam_x = cam_dist * math.cos(azimuth) * math.cos(elev_rad)
            cam_y = cam_dist * math.sin(azimuth) * math.cos(elev_rad)
            cam_z = cam_dist * math.sin(elev_rad)
            camera_positions.append([cam_x, cam_y, cam_z])
            
        # 2. Top-Down Camera (90 degrees) to capture deep fossae
        camera_positions.append([0.0, 0.0, cam_dist])
        
        # Convert trimesh to Open3D Tensor Mesh for hardware-accelerated Embree Raycasting
        # This executes exact BVH ray collisions physically, but 100x faster than pure python
        legacy_mesh = o3d.geometry.TriangleMesh(
            o3d.utility.Vector3dVector(mesh.vertices),
            o3d.utility.Vector3iVector(mesh.faces)
        )
        t_mesh = o3d.t.geometry.TriangleMesh.from_legacy(legacy_mesh)
        scene = o3d.t.geometry.RaycastingScene()
        scene.add_triangles(t_mesh)
        
        for camera_pos in camera_positions:
            camera_pos = np.array(camera_pos)
            
            # Form rays from the camera looking exactly at each sampled point
            ray_origins = np.tile(camera_pos, (len(source_points), 1))
            ray_directions = source_points - camera_pos
            
            dists = np.linalg.norm(ray_directions, axis=1)
            ray_directions /= (dists[:, None] + 1e-8)  # normalize
            
            # Cast rays using Open3D Tensor backend
            rays_np = np.hstack([ray_origins, ray_directions]).astype(np.float32)
            rays_tensor = o3d.core.Tensor(rays_np, dtype=o3d.core.Dtype.Float32)
            
            ans = scene.cast_rays(rays_tensor)
            hit_dists = ans['t_hit'].numpy()
            
            # Check if the hit distance is identical to the point distance
            expected_dists = dists.astype(np.float32)
            
            # 0.2mm tolerance for floating precision
            is_visible = np.abs(hit_dists - expected_dists) < 0.2
            visible_rays = np.where(is_visible)[0]
            all_visible_indices.update(visible_rays)
        
        # Filter source points down to ONLY the cumulative exterior shell
        visible_array = list(all_visible_indices)
        source_points = source_points[visible_array]
        
        # Ensure we still have points (edge cases)
        if len(source_points) < 100:
             # Fallback if the scanner somehow missed the object
             source_points = target_points.copy()[:len(target_points)//2]
        
        # 5. The Transform: Random SO(3) & Translation
        # We define gt_transform as the exact matrix needed to map Source -> Target
        gt_transform = np.eye(4)
        gt_transform[:3, :3] = R.random().as_matrix()
        # Random translation up to 10mm in any direction
        gt_transform[:3, 3] = np.random.randn(3) * 10.0  
        
        # To make Source naturally unaligned, we apply the INVERSE of gt_transform
        inv_transform = np.linalg.inv(gt_transform)
        source_points = (inv_transform[:3, :3] @ source_points.T).T + inv_transform[:3, 3]
        
        # 6. The Noise: Random Gaussian Jitter
        # Simulates 3D scanner static/inaccuracy
        noise = np.random.randn(*source_points.shape) * self.noise_std
        source_points += noise
        
        return {
            'target_cloud': target_points.astype(np.float32),
            'source_cloud': source_points.astype(np.float32),
            'transform_matrix': gt_transform.astype(np.float32)
        }

def save_point_cloud(points, filename):
    pcd = o3d.geometry.PointCloud()
    pcd.points = o3d.utility.Vector3dVector(points)
    o3d.io.write_point_cloud(filename, pcd)

if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--data_dir", type=str, default="/workspace/data")
    parser.add_argument("--out_dir", type=str, default="/workspace/out")
    args = parser.parse_args()

    os.makedirs(args.out_dir, exist_ok=True)
    
    print(f"Loading dataset from: {args.data_dir}...")
    
    # Instantiate Dataset
    try:
        dataset = DentalPartialDataset(data_dir=args.data_dir)
        print(f"Found {len(dataset)} STL files.")
    except Exception as e:
        print(f"ERROR: {e}")
        exit(1)
        
    # Pull exactly ONE augmented pair to verify the pipeline
    print("Pulling and augmenting a single pair...")
    sample = dataset[0]
    
    target_pts = sample['target_cloud']
    source_pts = sample['source_cloud']
    gt_tf = sample['transform_matrix']
    
    print(f"\nTarget points (Full):   {target_pts.shape}")
    print(f"Source points (Cut):    {source_pts.shape}")
    print(f"Ground Truth Transform (Source -> Target):\n{gt_tf}")
    
    # Save to disk as PLY for 3D Viewer verification
    target_path = os.path.join(args.out_dir, "preview_target.ply")
    source_path = os.path.join(args.out_dir, "preview_source.ply")
    
    save_point_cloud(target_pts, target_path)
    save_point_cloud(source_pts, source_path)
    
    print(f"\nSaved previews to:")
    print(f"  - {target_path}")
    print(f"  - {source_path}")
    print("Pipeline ready! Drag and drop the .ply files into a 3D viewer.")
