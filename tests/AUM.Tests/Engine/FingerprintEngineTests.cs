using AUM.Core.Engine;
using AUM.Core.Interop;
using Xunit;

namespace AUM.Tests.Engine;

/// <summary>
/// Tests for the FingerprintEngine high-level wrapper.
/// Note: Tests requiring the native DLL are marked with [Trait("Category", "NativeDll")].
/// </summary>
[Trait("Category", "Engine")]
public class FingerprintEngineTests
{
    [Fact]
    public void EngineException_ContainsErrorCode()
    {
        var exception = new EngineException(ErrorCode.FileNotFound);
        
        Assert.Equal(ErrorCode.FileNotFound, exception.ErrorCode);
        Assert.Contains("not found", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
    
    [Fact]
    public void EngineException_ThrowIfError_DoesNotThrowOnSuccess()
    {
        // Should not throw
        EngineException.ThrowIfError(ErrorCode.Success);
    }
    
    [Theory]
    [InlineData(ErrorCode.NullPointer)]
    [InlineData(ErrorCode.FileNotFound)]
    [InlineData(ErrorCode.InvalidFormat)]
    [InlineData(ErrorCode.ComputationFailed)]
    public void EngineException_ThrowIfError_ThrowsOnError(ErrorCode errorCode)
    {
        Assert.Throws<EngineException>(() => EngineException.ThrowIfError(errorCode));
    }
    
    [Fact]
    public void EngineException_ThrowIfError_SetsCorrectErrorCode()
    {
        try
        {
            EngineException.ThrowIfError(ErrorCode.InvalidHandle);
            Assert.Fail("Expected exception");
        }
        catch (EngineException ex)
        {
            Assert.Equal(ErrorCode.InvalidHandle, ex.ErrorCode);
        }
    }
    
    // =========================================================================
    // The following tests require the native DLL to be present.
    // =========================================================================
    
    [Fact]
    [Trait("Category", "NativeDll")]
    public void FingerprintEngine_Constructor_CreatesInstance()
    {
        if (!IsDllAvailable())
        {
            return; // Skip test
        }
        
        using var engine = new FingerprintEngine();
        
        Assert.NotNull(engine);
        Assert.Equal(0, engine.IndexCount);
    }
    
    [Fact]
    [Trait("Category", "NativeDll")]
    public void FingerprintEngine_Version_ReturnsString()
    {
        if (!IsDllAvailable())
        {
            return;
        }
        
        using var engine = new FingerprintEngine();
        
        Assert.False(string.IsNullOrEmpty(engine.Version));
    }
    
    [Fact]
    [Trait("Category", "NativeDll")]
    public void FingerprintEngine_ExtractDescriptor_InvalidPath_Throws()
    {
        if (!IsDllAvailable())
        {
            return;
        }
        
        using var engine = new FingerprintEngine();
        
        Assert.Throws<EngineException>(() => engine.ExtractDescriptor("nonexistent.stl"));
    }
    
    [Fact]
    public void FingerprintEngine_ExtractDescriptor_NullPath_ThrowsArgumentException()
    {
        if (!IsDllAvailable())
        {
            return;
        }
        
        using var engine = new FingerprintEngine();
        
        Assert.Throws<ArgumentException>(() => engine.ExtractDescriptor(null!));
        Assert.Throws<ArgumentException>(() => engine.ExtractDescriptor(""));
    }
    
    [Fact]
    public void FingerprintEngine_Query_NullDescriptor_ThrowsArgumentException()
    {
        if (!IsDllAvailable())
        {
            return;
        }
        
        using var engine = new FingerprintEngine();
        
        Assert.Throws<ArgumentException>(() => engine.Query(null!));
        Assert.Throws<ArgumentException>(() => engine.Query(Array.Empty<byte>()));
    }
    
    [Fact]
    public void FingerprintEngine_AddToIndex_NullDescriptor_ThrowsArgumentException()
    {
        if (!IsDllAvailable())
        {
            return;
        }
        
        using var engine = new FingerprintEngine();
        
        Assert.Throws<ArgumentException>(() => engine.AddToIndex(1, null!));
        Assert.Throws<ArgumentException>(() => engine.AddToIndex(1, Array.Empty<byte>()));
    }
    
    [Fact]
    [Trait("Category", "NativeDll")]
    public void FingerprintEngine_Dispose_CanBeCalledMultipleTimes()
    {
        if (!IsDllAvailable())
        {
            return;
        }
        
        var engine = new FingerprintEngine();
        engine.Dispose();
        engine.Dispose(); // Should not throw
    }
    
    [Fact]
    [Trait("Category", "NativeDll")]
    public void FingerprintEngine_AfterDispose_ThrowsObjectDisposed()
    {
        if (!IsDllAvailable())
        {
            return;
        }
        
        var engine = new FingerprintEngine();
        engine.Dispose();
        
        Assert.Throws<ObjectDisposedException>(() => engine.ExtractDescriptor("test.stl"));
    }
    
    private static bool IsDllAvailable()
    {
        try
        {
            NativeMethods.aum_get_version();
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }
}
