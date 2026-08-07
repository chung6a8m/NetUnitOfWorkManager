using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using Xunit;

namespace NetUnitOfWorkManager.Tests
{
    public sealed class PublicContractTests
    {
        [Fact]
        public void Lifecycle_PublicApi_DoesNotExposeAsyncMethods()
        {
            Type[] contractTypes =
            {
                typeof(IUnitOfWorkManager),
                typeof(IUnitOfWorkContext),
                typeof(IUnitOfWorkScope),
                typeof(UnitOfWorkDbSession)
            };

            MethodInfo[] publicMethods = contractTypes
                .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                .ToArray();

            Assert.DoesNotContain(
                publicMethods,
                method => method.Name.EndsWith("Async", StringComparison.Ordinal));
        }

        [Fact]
        public void Scope_UsesSynchronousDisposableContractOnly()
        {
            Type[] interfaces = typeof(IUnitOfWorkScope).GetInterfaces();

            Assert.Contains(typeof(IDisposable), interfaces);
            Assert.DoesNotContain(
                interfaces,
                interfaceType => string.Equals(
                    interfaceType.FullName,
                    "System.IAsyncDisposable",
                    StringComparison.Ordinal));
        }

        [Fact]
        public void Options_Equality_UsesIsolationLevelValue()
        {
            UnitOfWorkOptions first = new UnitOfWorkOptions(IsolationLevel.Serializable);
            UnitOfWorkOptions sameValue = new UnitOfWorkOptions(IsolationLevel.Serializable);
            UnitOfWorkOptions differentValue = new UnitOfWorkOptions(IsolationLevel.ReadCommitted);

            Assert.NotSame(first, sameValue);
            Assert.Equal(first, sameValue);
            Assert.Equal(first.GetHashCode(), sameValue.GetHashCode());
            Assert.NotEqual(first, differentValue);
        }

        [Fact]
        public void Manager_PublicApi_DoesNotExposeClearCurrent()
        {
            bool exposesClearCurrent = typeof(IUnitOfWorkManager)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Any(method => string.Equals(method.Name, "ClearCurrent", StringComparison.Ordinal));

            Assert.False(exposesClearCurrent);
        }

        [Fact]
        public void Begin_HasOneOptionalOptionsParameter()
        {
            MethodInfo begin = typeof(IUnitOfWorkManager).GetMethod("Begin")!;
            ParameterInfo parameter = Assert.Single(begin.GetParameters());

            Assert.Equal(typeof(UnitOfWorkOptions), parameter.ParameterType);
            Assert.True(parameter.IsOptional);
            Assert.Null(parameter.DefaultValue);
            Assert.Equal(typeof(IUnitOfWorkScope), begin.ReturnType);
        }

        [Fact]
        public void DbSession_PublicShape_UsesProviderNativeAdoNetTypes()
        {
            PropertyInfo connection = typeof(UnitOfWorkDbSession).GetProperty("Connection")!;
            PropertyInfo transaction = typeof(UnitOfWorkDbSession).GetProperty("Transaction")!;
            MethodInfo createCommand = typeof(UnitOfWorkDbSession).GetMethod("CreateCommand")!;

            Assert.Equal(typeof(DbConnection), connection.PropertyType);
            Assert.Equal(typeof(DbTransaction), transaction.PropertyType);
            Assert.Equal(typeof(DbCommand), createCommand.ReturnType);
            Assert.Empty(createCommand.GetParameters());
        }
    }
}
