using System.Reflection;
using System.Runtime.Versioning;
using Bit.Core.Utilities;
using Xunit;

namespace Bit.Core.Test;

public class TargetFrameworkPinTests
{
    [Fact]
    public void CompiledCoreAssembly_TargetsDotNet10()
    {
        var frameworkName = FrameworkName(typeof(CoreHelpers).Assembly);

        Assert.Contains("Version=v10.0", frameworkName);
    }

    [Fact]
    public void CompiledCoreTestAssembly_TargetsDotNet10()
    {
        var frameworkName = FrameworkName(typeof(TargetFrameworkPinTests).Assembly);

        Assert.Contains("Version=v10.0", frameworkName);
    }

    private static string FrameworkName(Assembly assembly)
    {
        var attribute = assembly.GetCustomAttribute<TargetFrameworkAttribute>();
        Assert.NotNull(attribute);
        return attribute.FrameworkName;
    }
}
