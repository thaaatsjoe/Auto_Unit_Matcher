using System;
using Xunit;

namespace AUM.Tests;

/// <summary>
/// Placeholder test to verify test infrastructure is working.
/// </summary>
public class SanityTests
{
    [Fact]
    public void TestFrameworkWorks()
    {
        Assert.True(true, "Test framework is operational");
    }
    
    [Fact]
    public void EnvironmentIs64Bit()
    {
        Assert.True(Environment.Is64BitProcess, "Application must run as 64-bit for PCL/FAISS compatibility");
    }
}
