using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
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
        public void Assembly_PublicApi_DoesNotExposeTaskBasedOrFakeAsyncSurface()
        {
            Type[] publicTypes = typeof(UnitOfWorkManager).Assembly.GetExportedTypes();

            foreach (Type publicType in publicTypes)
            {
                Assert.DoesNotContain(
                    publicType.GetInterfaces(),
                    interfaceType => string.Equals(
                        interfaceType.FullName,
                        "System.IAsyncDisposable",
                        StringComparison.Ordinal));

                MethodInfo[] declaredMethods = publicType.GetMethods(
                    BindingFlags.Public |
                    BindingFlags.Instance |
                    BindingFlags.Static |
                    BindingFlags.DeclaredOnly);

                Assert.DoesNotContain(
                    declaredMethods,
                    method => method.Name.EndsWith("Async", StringComparison.Ordinal));

                Assert.DoesNotContain(
                    declaredMethods,
                    method => IsTaskLike(method.ReturnType));

                PropertyInfo[] declaredProperties = publicType.GetProperties(
                    BindingFlags.Public |
                    BindingFlags.Instance |
                    BindingFlags.Static |
                    BindingFlags.DeclaredOnly);

                Assert.DoesNotContain(
                    declaredProperties,
                    property => IsTaskLike(property.PropertyType));
            }
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
        public void Suppress_PublicApi_Returns_IDisposable()
        {
            MethodInfo? suppress = typeof(IUnitOfWorkManager).GetMethod(
                "Suppress",
                BindingFlags.Public | BindingFlags.Instance);

            Assert.NotNull(suppress);
            Assert.Empty(suppress.GetParameters());
            Assert.Equal(typeof(IDisposable), suppress.ReturnType);
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

        private static bool IsTaskLike(Type type)
        {
            if (typeof(Task).IsAssignableFrom(type))
            {
                return true;
            }

            Type normalizedType = type.IsGenericType
                ? type.GetGenericTypeDefinition()
                : type;

            string? fullName = normalizedType.FullName;
            return string.Equals(fullName, "System.Threading.Tasks.ValueTask", StringComparison.Ordinal) ||
                   string.Equals(fullName, "System.Threading.Tasks.ValueTask`1", StringComparison.Ordinal) ||
                   string.Equals(fullName, "System.Collections.Generic.IAsyncEnumerable`1", StringComparison.Ordinal);
        }
    }
}
