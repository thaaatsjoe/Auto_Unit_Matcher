using System.Runtime.InteropServices;

namespace AUM.Core.Interop;

/// <summary>
/// SafeHandle wrapper for native codebook handles.
/// Ensures proper cleanup via aum_free_codebook.
/// </summary>
public sealed class CodebookHandle : SafeHandle
{
    /// <summary>
    /// Creates an empty CodebookHandle.
    /// </summary>
    public CodebookHandle() : base(IntPtr.Zero, true)
    {
    }
    
    /// <summary>
    /// Creates a CodebookHandle wrapping an existing native handle.
    /// </summary>
    internal CodebookHandle(IntPtr handle) : base(IntPtr.Zero, true)
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
            NativeMethods.aum_free_codebook(handle);
            handle = IntPtr.Zero;
        }
        return true;
    }
    
    /// <summary>
    /// Gets the number of clusters (K) in the codebook.
    /// </summary>
    public int K
    {
        get
        {
            if (IsInvalid)
                throw new ObjectDisposedException(nameof(CodebookHandle));
            
            var result = NativeMethods.aum_codebook_k(handle, out var k);
            if (result != ErrorCode.Success)
                throw new InvalidOperationException($"Failed to get codebook K: {result}");
            
            return k;
        }
    }
    
    /// <summary>
    /// Loads a trained codebook from a binary file.
    /// </summary>
    public static CodebookHandle Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be null or empty", nameof(path));
        
        var result = NativeMethods.aum_load_codebook(path, out var codebookPtr);
        if (result != ErrorCode.Success)
            throw new InvalidOperationException($"Failed to load codebook from {path}: {result} - {NativeMethods.GetLastErrorMessage()}");
        
        return new CodebookHandle(codebookPtr);
    }
}
