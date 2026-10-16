using System.Reflection;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Schema;
using DotNetOpenApiExtract.Core.Tests.Schema.TypeNullabilityDeclarations;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Schema
{
    /// <summary>
    /// The NRT positions of a member's declared type: the layout by the open signature, the context chain,
    /// the navigation steps and P-NRT2 (nullable only for an annotated, non-value position that is not
    /// below a type parameter of the declaration).
    /// </summary>
    /// <remarks>
    /// The expected values are the <c>?</c> written in the declarations of
    /// <see cref="TypeNullabilityDeclarations"/>, below in this file.
    /// </remarks>
    public class TypeNullabilityTests
    {
        private const string NullableAttribute = "System.Runtime.CompilerServices.NullableAttribute";

        private static TypeNullability Property(Type type, string name) =>
            TypeNullability.ForProperty(type.GetProperty(name)!);

        private static MethodInfo Method(Type type, string name) => type.GetMethod(name)!;

        /// <summary>The member carries no NullableAttribute of its own, so its bytes come from a context.</summary>
        private static void ShouldHaveNoNullableAttribute(ICustomAttributeProvider member)
        {
            var attributes = member switch
            {
                MemberInfo m => m.GetCustomAttributesData(),
                ParameterInfo p => p.GetCustomAttributesData(),
                _ => throw new ArgumentException(member.ToString()),
            };
            attributes.Select(a => a.AttributeType.FullName).Should().NotContain(NullableAttribute,
                "the case reads the bytes from a NullableContextAttribute");
        }

        // --- Layout by the open signature ---------------------------------------------------------------

        [Fact]
        public void Layout_FollowsTheOpenSignature_AfterATypeParameterClosedByAValueType()
        {
            // KeyValuePair<T, string?>: the string is annotated whatever closes T; read by the closed
            // KeyValuePair<int, string>, int takes no byte and the string would read T's byte.
            var pair = Property(typeof(Page<int>), nameof(Page<int>.Pair));

            pair.Argument(0).Type.Should().Be<int>();
            pair.Argument(0).IsTypeParameter.Should().BeTrue();
            pair.Argument(1).Type.Should().Be<string>();
            pair.Argument(1).IsNullable.Should().BeTrue();
        }

        [Fact]
        public void Layout_ClosedByAReferenceType_MarksTheTypeParameterPositionAndTheOnesAfterIt()
        {
            var items = Property(typeof(Page<Item>), nameof(Page<Item>.Items));
            items.Argument(0).Type.Should().Be<Item>();
            items.Argument(0).IsNullable.Should().BeTrue();

            var pair = Property(typeof(Page<Item>), nameof(Page<Item>.Pair));
            pair.Argument(0).IsNullable.Should().BeFalse();
            pair.Argument(1).IsNullable.Should().BeTrue();
        }

        [Fact]
        public void Layout_OfAMemberOfAClosedGenericBaseType_IsTheBaseDefinitionsLayout()
        {
            var property = typeof(ItemPage).GetProperty(nameof(ItemPage.Pair))!;
            property.DeclaringType.Should().Be<Page<Item>>();

            var pair = TypeNullability.ForProperty(property);

            pair.Argument(0).Type.Should().Be<Item>();
            pair.Argument(0).IsNullable.Should().BeFalse();
            pair.Argument(1).IsNullable.Should().BeTrue();
        }

        [Fact]
        public void Layout_OfAParameter_FollowsTheOpenSignatureOfAGenericDeclaringType()
        {
            var parameter = Method(typeof(Holder<int>), nameof(Holder<int>.Put)).GetParameters()[0];

            var pair = TypeNullability.ForParameter(parameter);

            pair.Argument(0).Type.Should().Be<int>();
            pair.Argument(1).Type.Should().Be<string>();
            pair.Argument(1).IsNullable.Should().BeTrue();
        }

        [Fact]
        public void Layout_OfParameters_MarksArraysAndElementsByTheirQuestionMarks()
        {
            var parameters = Method(typeof(Signatures), nameof(Signatures.Take)).GetParameters();

            var names = TypeNullability.ForParameter(parameters[0]);
            names.IsNullable.Should().BeFalse();
            names.Argument(0).IsNullable.Should().BeTrue();

            var nullableArray = TypeNullability.ForParameter(parameters[1]);
            nullableArray.IsNullable.Should().BeTrue();
            nullableArray.Element().IsNullable.Should().BeFalse();

            var arrayOfNullable = TypeNullability.ForParameter(parameters[2]);
            arrayOfNullable.IsNullable.Should().BeFalse();
            arrayOfNullable.Element().IsNullable.Should().BeTrue();
        }

        [Fact]
        public void Layout_OfAReturnType_MarksEachNestedPosition()
        {
            // List<Item?>[]? Get()
            var result = TypeNullability.ForReturn(Method(typeof(Signatures), nameof(Signatures.Get)));

            result.IsNullable.Should().BeTrue();
            result.Element().Type.Should().Be<List<Item>>();
            result.Element().IsNullable.Should().BeFalse();
            result.Element().Argument(0).Type.Should().Be<Item>();
            result.Element().Argument(0).IsNullable.Should().BeTrue();
        }

        // --- Context chain ------------------------------------------------------------------------------

        [Fact]
        public void Context_OfTheDeclaringType_AppliesToEveryPositionOfAMemberWithoutAnAttribute()
        {
            var property = typeof(MostlyNullable).GetProperty(nameof(MostlyNullable.Lists))!;
            ShouldHaveNoNullableAttribute(property);

            // List<string?>? from the type's context (2) at every position.
            var lists = TypeNullability.ForProperty(property);
            lists.Flag.Should().Be(2);
            lists.Argument(0).IsNullable.Should().BeTrue();
        }

        [Fact]
        public void Context_OfTheEnclosingType_AppliesToANestedTypesMember()
        {
            var property = typeof(MostlyNullable.Nested).GetProperty(nameof(MostlyNullable.Nested.Names))!;
            ShouldHaveNoNullableAttribute(property);
            ShouldHaveNoNullableContext(typeof(MostlyNullable.Nested));

            TypeNullability.ForProperty(property).Argument(0).IsNullable.Should().BeTrue();
        }

        [Fact]
        public void Context_OfTheMethod_AppliesToAParameterWithoutAnAttribute()
        {
            var method = Method(typeof(Signatures), nameof(Signatures.MostlyNullable));
            var parameter = method.GetParameters()[0];
            ShouldHaveNoNullableAttribute(parameter);
            method.GetCustomAttributesData().Select(a => a.AttributeType.FullName)
                .Should().Contain("System.Runtime.CompilerServices.NullableContextAttribute", "the method carries the context");

            TypeNullability.ForParameter(parameter).Argument(0).IsNullable.Should().BeTrue();
        }

        [Fact]
        public void Context_OfADisabledBlock_IsObliviousAtEveryPosition()
        {
            var names = Property(typeof(ObliviousBlock), nameof(ObliviousBlock.Names));

            names.Flag.Should().Be(0);
            names.Argument(0).Flag.Should().Be(0);
            names.Argument(0).IsNullable.Should().BeFalse();
        }

        private static void ShouldHaveNoNullableContext(Type type) =>
            type.GetCustomAttributesData().Select(a => a.AttributeType.FullName)
                .Should().NotContain("System.Runtime.CompilerServices.NullableContextAttribute",
                    "the case reads the context of the enclosing type");

        // --- Navigation ---------------------------------------------------------------------------------

        [Fact]
        public void Navigation_ThroughNullableOfAGenericValueType_IsTransparent()
        {
            // Nullable<KeyValuePair<string, Item?>>
            var pair = Property(typeof(Navigation), nameof(Navigation.MaybePair)).WithoutNullable();

            pair.Type.Should().Be<KeyValuePair<string, Item>>();
            pair.Argument(0).Type.Should().Be<string>();
            pair.Argument(0).IsNullable.Should().BeFalse();
            pair.Argument(1).Type.Should().Be<Item>();
            pair.Argument(1).IsNullable.Should().BeTrue();
        }

        public static TheoryData<string, Type, int, Type, bool> ImplementedInterfaces() => new()
        {
            // Swapped<TValue, TKey> : Dictionary<TKey, TValue>: the value is the first argument of the class.
            { nameof(Navigation.SwappedItems), typeof(IDictionary<string, Item>), 0, typeof(string), false },
            { nameof(Navigation.SwappedItems), typeof(IDictionary<string, Item>), 1, typeof(Item), true },
            { nameof(Navigation.SwappedItems), typeof(IReadOnlyDictionary<string, Item>), 1, typeof(Item), true },
            { nameof(Navigation.ItemsByName), typeof(IReadOnlyDictionary<string, Item>), 0, typeof(string), false },
            { nameof(Navigation.ItemsByName), typeof(IReadOnlyDictionary<string, Item>), 1, typeof(Item), true },
            { nameof(Navigation.NamesByItem), typeof(IDictionary<Item, string>), 1, typeof(string), true },
            { nameof(Navigation.NamesByItem), typeof(IDictionary<Item, string>), 0, typeof(Item), false },
            { nameof(Navigation.ItemStream), typeof(IAsyncEnumerable<Item>), 0, typeof(Item), true },
            { nameof(Navigation.Items), typeof(IAsyncEnumerable<Item>), 0, typeof(Item), true },
        };

        [Theory]
        [MemberData(nameof(ImplementedInterfaces))]
        public void Navigation_IntoAnImplementedInterface_TakesTheArgumentTheDefinitionPassesToIt(
            string property, Type implemented, int argument, Type expectedType, bool expectedNullable)
        {
            var position = Property(typeof(Navigation), property).Implemented(implemented).Argument(argument);

            position.Type.Should().Be(expectedType);
            position.IsNullable.Should().Be(expectedNullable);
        }

        // --- P-NRT2 -------------------------------------------------------------------------------------

        [Fact]
        public void Predicate_IsFalse_ForANotAnnotatedPosition()
        {
            var position = Property(typeof(Navigation), nameof(Navigation.NotAnnotated)).Argument(0);

            position.Flag.Should().Be(1);
            position.IsNullable.Should().BeFalse();
        }

        [Fact]
        public void Predicate_IsFalse_ForAnObliviousPosition()
        {
            var position = Property(typeof(ObliviousBlock), nameof(ObliviousBlock.Items)).Argument(0);

            position.Flag.Should().Be(0);
            position.IsNullable.Should().BeFalse();
        }

        [Fact]
        public void Predicate_IsFalse_ForAnAnnotatedTypeParameterClosedByAValueType()
        {
            var position = Property(typeof(Page<int>), nameof(Page<int>.Items)).Argument(0);

            position.Flag.Should().Be(2);
            position.Type.Should().Be<int>();
            position.IsNullable.Should().BeFalse();
        }

        [Fact]
        public void Predicate_IsFalse_ForAnAnnotatedPositionBelowATypeParameter()
        {
            // List<T?>? with one byte for every position, closed by List<Item>: the Item inside reads 2,
            // but the declaration does not annotate inside T.
            var parameterPosition = Property(typeof(UniformPage<List<Item>>), nameof(UniformPage<List<Item>>.Items)).Argument(0);
            parameterPosition.IsTypeParameter.Should().BeTrue();
            parameterPosition.IsNullable.Should().BeTrue();

            var below = parameterPosition.Argument(0);
            below.Type.Should().Be<Item>();
            below.Flag.Should().Be(2);
            below.BelowTypeParameter.Should().BeTrue();
            below.IsNullable.Should().BeFalse();
        }
    }
}

#nullable enable
namespace DotNetOpenApiExtract.Core.Tests.Schema.TypeNullabilityDeclarations
{
    public class Item
    {
        public string Name { get; set; } = string.Empty;
    }

    public class Page<T>
    {
        public List<T?> Items { get; set; } = [];

        public KeyValuePair<T, string?> Pair { get; set; }
    }

    /// <summary>One byte for every position of the member: <c>List&lt;T?&gt;?</c>.</summary>
    public class UniformPage<T>
    {
        public List<T?>? Items { get; set; }
    }

    public class ItemPage : Page<Item>
    {
    }

    public class Holder<T>
    {
        public void Put(KeyValuePair<T, string?> pair) { }
    }

    public class Swapped<TValue, TKey> : Dictionary<TKey, TValue>
        where TKey : notnull
    {
    }

    public sealed class ItemSequence<T> : IAsyncEnumerable<T>
    {
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    public class Navigation
    {
        public KeyValuePair<string, Item?>? MaybePair { get; set; }

        public Swapped<Item?, string> SwappedItems { get; set; } = [];

        public Dictionary<string, Item?> ItemsByName { get; set; } = [];

        public Dictionary<Item, string?> NamesByItem { get; set; } = [];

        public ItemSequence<Item?> ItemStream { get; set; } = new();

        public IAsyncEnumerable<Item?> Items { get; set; } = new ItemSequence<Item?>();

        public List<string> NotAnnotated { get; set; } = [];
    }

    public class Signatures
    {
        public void Take(List<string?> names, string[]? nullableArray, string?[] arrayOfNullable) { }

        public List<Item?>[]? Get() => null;

        /// <summary>Most positions nullable: the method's context is 2 and the parameters carry no attribute.</summary>
        public string? MostlyNullable(List<string?>? first, string? second, object? third, Item? fourth) => null;
    }

    /// <summary>Most members nullable: the type's context is 2 and they carry no attribute.</summary>
    public class MostlyNullable
    {
        public List<string?>? Lists { get; set; }

        public string? First { get; set; }

        public string? Second { get; set; }

        public Item? Third { get; set; }

        public object? Fourth { get; set; }

        public class Nested
        {
            public List<string?>? Names { get; set; }
        }
    }

#nullable disable
    public class ObliviousBlock
    {
        public List<string> Names { get; set; }

        public List<Item> Items { get; set; }
    }
#nullable restore
}
