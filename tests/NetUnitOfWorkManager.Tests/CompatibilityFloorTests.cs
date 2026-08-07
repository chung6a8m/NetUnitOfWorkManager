using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace NetUnitOfWorkManager.Tests
{
    public sealed class CompatibilityFloorTests
    {
        [Fact]
        public void CoreAssembly_IsPresentInConsumerOutput()
        {
            string assemblyPath = Path.Combine(AppContext.BaseDirectory, "NetUnitOfWorkManager.dll");

            Assert.True(File.Exists(assemblyPath), $"Expected core assembly at '{assemblyPath}'.");

            AssemblyName assemblyName = AssemblyName.GetAssemblyName(assemblyPath);
            Assert.Equal("NetUnitOfWorkManager", assemblyName.Name);
        }
    }
}
