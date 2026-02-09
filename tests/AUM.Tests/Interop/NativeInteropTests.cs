using AUM.Core.Interop;
using Xunit;

namespace AUM.Tests.Interop;

/// <summary>
/// Tests for the native interop types and P/Invoke declarations.
/// Note: Tests requiring the native DLL are marked with [Trait("Category", "NativeDll")].
/// </summary>
[Trait("Category", "Engine")]
public class NativeInteropTests
{
    [Fact]
    public void ErrorCode_HasExpectedValues()
    {
        // Verify enum values match exports.h
        Assert.Equal(0, (int)ErrorCode.Success);
        Assert.Equal(-1, (int)ErrorCode.NullPointer);
        Assert.Equal(-2, (int)ErrorCode.FileNotFound);
        Assert.Equal(-3, (int)ErrorCode.InvalidFormat);
        Assert.Equal(-4, (int)ErrorCode.ComputationFailed);
        Assert.Equal(-5, (int)ErrorCode.OutOfMemory);
        Assert.Equal(-6, (int)ErrorCode.IndexNotReady);
        Assert.Equal(-7, (int)ErrorCode.InvalidHandle);
    }
    
    [Fact]
    public void MatchResult_HasCorrectLayout()
    {
        // Verify struct can be created with expected fields
        var result = new MatchResult
        {
            Id = 12345,
            Distance = 0.5f,
            Confidence = 95.5f
        };
        
        Assert.Equal(12345, result.Id);
        Assert.Equal(0.5f, result.Distance);
        Assert.Equal(95.5f, result.Confidence);
    }
    
    [Fact]
    public void MatchResult_ToString_ReturnsFormattedString()
    {
        var result = new MatchResult
        {
            Id = 42,
            Distance = 0.1234f,
            Confidence = 98.7f
        };
        
        var str = result.ToString();
        
        Assert.Contains("Id=42", str);
        Assert.Contains("Distance=", str);
        Assert.Contains("Confidence=", str);
    }
    
    // =========================================================================
    // The following tests require the native DLL to be present.
    // They will be skipped if the DLL is not available.
    // =========================================================================
    
    [Fact]
    [Trait("Category", "NativeDll")]
    public void GetVersion_ReturnsNonEmptyString()
    {
        // Skip if DLL not present
        if (!IsDllAvailable())
        {
            return; // Skip test
        }
        
        var version = NativeMethods.GetVersionString();
        Assert.False(string.IsNullOrEmpty(version));
        Assert.NotEqual("unknown", version);
    }
    
    [Theory]
    [InlineData("nonexistent.stl")]
    [InlineData("")]
    [Trait("Category", "NativeDll")]
    public void ParseStl_InvalidPath_ReturnsError(string path)
    {
        // Skip if DLL not present
        if (!IsDllAvailable())
        {
            return;
        }
        
        var result = NativeMethods.aum_parse_stl(path, out var handle);
        
        Assert.NotEqual(ErrorCode.Success, result);
        Assert.Equal(IntPtr.Zero, handle);
    }
    
    private static bool IsDllAvailable()
    {
        try
        {
            // Try to call a simple function to check if DLL is loaded
            NativeMethods.aum_get_version();
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }
}
