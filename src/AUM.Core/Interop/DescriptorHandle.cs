using System.Runtime.InteropServices;

namespace AUM.Core.Interop;

/// <summary>
/// SafeHandle wrapper for native descriptor handles.
/// Ensures proper cleanup via aum_free_descriptor.
/// </summary>
public sealed class DescriptorHandle : SafeHandle
{
    /// <summary>
    /// Creates a new DescriptorHandle.
    /// </summary>
    public DescriptorHandle() : base(IntPtr.Zero, true)
    {
    }
    
    /// <summary>
    /// Creates a DescriptorHandle wrapping an existing native handle.
    /// </summary>
    internal DescriptorHandle(IntPtr handle) : base(IntPtr.Zero, true)
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
            NativeMethods.aum_free_descriptor(handle);
            handle = IntPtr.Zero;
        }
        return true;
    }
    
    /// <summary>
    /// Serializes the descriptor to a byte array for database storage.
    /// </summary>
    public byte[] Serialize()
    {
        if (IsInvalid)
            throw new ObjectDisposedException(nameof(DescriptorHandle));
        
        var result = NativeMethods.aum_serialize_descriptor(handle, out var blobPtr, out var len);
        if (result != ErrorCode.Success)
            throw new InvalidOperationException($"Failed to serialize descriptor: {result}");
        
        try
        {
            var blob = new byte[len];
            Marshal.Copy(blobPtr, blob, 0, (int)len);
            return blob;
        }
        finally
        {
            NativeMethods.aum_free_blob(blobPtr);
        }
    }
    
    /// <summary>
    /// Creates a DescriptorHandle from a serialized byte array.
    /// </summary>
    public static DescriptorHandle Deserialize(byte[] blob)
    {
        if (blob == null || blob.Length == 0)
            throw new ArgumentException("Blob cannot be null or empty", nameof(blob));
        
        var blobPtr = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            
            var result = NativeMethods.aum_deserialize_descriptor(blobPtr, (nuint)blob.Length, out var descPtr);
            if (result != ErrorCode.Success)
                throw new InvalidOperationException($"Failed to deserialize descriptor: {result}");
            
            return new DescriptorHandle(descPtr);
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
        }
    }
}
