using System.Runtime.InteropServices;

namespace AUM.Core.Interop;

/// <summary>
/// SafeHandle wrapper for native point cloud handles.
/// Ensures proper cleanup via aum_free_point_cloud.
/// </summary>
public sealed class PointCloudHandle : SafeHandle
{
    /// <summary>
    /// Creates a new PointCloudHandle.
    /// </summary>
    public PointCloudHandle() : base(IntPtr.Zero, true)
    {
    }
    
    /// <summary>
    /// Creates a PointCloudHandle wrapping an existing native handle.
    /// </summary>
    internal PointCloudHandle(IntPtr handle) : base(IntPtr.Zero, true)
    {
        SetHandle(handle);
    }
    
    /// <inheritdoc/>
    public override bool IsInvalid => handle == IntPtr.Zero;
    
    /// <inheritdoc/>
    protected override bool ReleaseHandle()
    {
        if (handle != IntPtr.Zero)
        {
            NativeMethods.aum_free_point_cloud(handle);
            handle = IntPtr.Zero;
        }
        return true;
    }
    
    /// <summary>
    /// Gets the number of points in the point cloud.
    /// </summary>
    public nuint PointCount
    {
        get
        {
            if (IsInvalid)
                throw new ObjectDisposedException(nameof(PointCloudHandle));
            
            var result = NativeMethods.aum_point_cloud_size(handle, out var size);
            if (result != ErrorCode.Success)
                throw new InvalidOperationException($"Failed to get point cloud size: {result}");
            
            return size;
        }
    }
}
