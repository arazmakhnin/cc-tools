using System.Threading.Tasks;
using CcWorks.Workers.Solvers;
using NUnit.Framework;
using Shouldly;

namespace CcWorks.Tests.Solvers
{
    [TestFixture]
    public class BrpMagicStringsSolverTest
    {
        [Test]
        public async Task WithOnlyOneString_ShouldNotReplaceAnything()
        {
            // arrange
            var text = @"public class Sample
{
    public void SampleMethod()
    {
        var s = ""single"";
    }
}";

            // act
            var result = await BrpMagicStringsSolver.Solve(text);

            // assert
            result.ShouldBe(@"public class Sample
{
    public void SampleMethod()
    {
        var s = ""single"";
    }
}");
        }

        [Test]
        public async Task WithOneEmptyString_ShouldReplaceWithStringEmptyWithoutConsts()
        {
            // arrange
            var text = @"public class Sample
{
    public void SampleMethod()
    {
        var s = """";
    }
}";

            // act
            var result = await BrpMagicStringsSolver.Solve(text);

            // assert
            result.ShouldBe(@"public class Sample
{
    public void SampleMethod()
    {
        var s = string.Empty;
    }
}");
        }

        [Test]
        public async Task WithTwoStrings_ShouldReplaceThemWithConst()
        {
            // arrange
            var text = @"public class Sample
{
    public void SampleMethod()
    {
        var s = ""double"";
        var d = ""double"";
    }
}";

            // act
            var result = await BrpMagicStringsSolver.Solve(text);

            // assert
            result.ShouldBe(@"public class Sample
{
    private const string Double = ""double"";
    public void SampleMethod()
    {
        var s = Double;
        var d = Double;
    }
}");
        }

        [Test]
        public async Task WithTwoDifferentDuplicatedStrings_ShouldReplaceThemAllWithConst()
        {
            // arrange
            var text = @"public class Sample
{
    public void SampleMethod()
    {
        var s = ""double"";
        var d = ""double"";
        var a = ""some"";
        var b = ""some"";
    }
}";

            // act
            var result = await BrpMagicStringsSolver.Solve(text);

            // assert
            result.ShouldBe(@"public class Sample
{
    private const string Double = ""double"";
    private const string Some = ""some"";
    public void SampleMethod()
    {
        var s = Double;
        var d = Double;
        var a = Some;
        var b = Some;
    }
}");
        }

        [Test]
        public async Task WithStringsInExpressions_ShouldReplaceThemAllWithConst()
        {
            // arrange
            var text = @"public class Sample
{
    public void SampleMethod()
    {
        var result = Call(""double"");
        var z = result 
            ? result + ""double""
            : """";
    }
}";

            // act
            var result = await BrpMagicStringsSolver.Solve(text);

            // assert
            result.ShouldBe(@"public class Sample
{
    private const string Double = ""double"";
    public void SampleMethod()
    {
        var result = Call(Double);
        var z = result 
            ? result + Double
            : string.Empty;
    }
}");
        }

        [Test]
        public async Task WithNamespaces_ShouldKeepRightIndentation()
        {
            // arrange
            var text = @"namespace Some.Name.Space
{
    public class Sample
    {
        public void SampleMethod()
        {
            var result = Call(""double"");
            var z = result 
                ? result + ""double""
                : """";
        }
    }
}";

            // act
            var result = await BrpMagicStringsSolver.Solve(text);

            // assert
            result.ShouldBe(@"namespace Some.Name.Space
{
    public class Sample
    {
        private const string Double = ""double"";
        public void SampleMethod()
        {
            var result = Call(Double);
            var z = result 
                ? result + Double
                : string.Empty;
        }
    }
}");
        }

        [Test]
        public async Task WithExistingConstant_ReuseItEvenForSingleString()
        {
            // arrange
            var text = @"public class Sample
{
    private const string ExistingConst = ""double"";

    public void SampleMethod()
    {
        var s = ""double"";
        var a = ""some"";
        var b = ""some"";
    }
}";

            // act
            var result = await BrpMagicStringsSolver.Solve(text);

            // assert
            result.ShouldBe(@"public class Sample
{
    private const string Some = ""some"";
    private const string ExistingConst = ""double"";

    public void SampleMethod()
    {
        var s = ExistingConst;
        var a = Some;
        var b = Some;
    }
}");
        }

        [Test]
        public async Task WithMultipleClasses_ShouldReplaceInEveryClass()
        {
            // arrange
            var text = @"namespace Some.Name.Space
{
    public class Sample1
    {
        public void SampleMethod1()
        {
            var a = ""class1"";
            var b = ""class1"";
        }
    }

    public class Sample2
    {
        public void SampleMethod2()
        {
            var s = ""class2"";
            var d = ""class2"";
        }
    }
}";

            // act
            var result = await BrpMagicStringsSolver.Solve(text);

            // assert
            result.ShouldBe(@"namespace Some.Name.Space
{
    public class Sample1
    {
        private const string Class1 = ""class1"";
        public void SampleMethod1()
        {
            var a = Class1;
            var b = Class1;
        }
    }

    public class Sample2
    {
        private const string Class2 = ""class2"";
        public void SampleMethod2()
        {
            var s = Class2;
            var d = Class2;
        }
    }
}");
        }

        [Test]
        public async Task WithInterpolatedString_DoNothing()
        {
            // arrange
            var text = @"public class Sample
{
    public void SampleMethod()
    {
        var parameter
        var a = $""some{parameter}"";
        var b = $""some{parameter}"";
    }
}";

            // act
            var result = await BrpMagicStringsSolver.Solve(text);

            // assert
            result.ShouldBe(text);
        }

        [Test]
        public async Task IgnoredCasesForEmptyString_DoNothing()
        {
            // arrange
            var text = @"public class Sample
{
    private const Empty = """";

    [Test("""")]
    public void SampleMethod(string param = """")
    {
        const string empty = """";
    }
}";

            // act
            var result = await BrpMagicStringsSolver.Solve(text);

            // assert
            result.ShouldBe(text);
        }

        [Test]
        public async Task WithLeadingNumbers_ShouldRemoveThemWhenNaming()
        {
            // arrange
            var text = @"public class Sample
{
    public void SampleMethod()
    {
        var s = ""1double"";
        var d = ""1double"";
        var a = ""12some"";
        var b = ""12some"";
    }
}";

            // act
            var result = await BrpMagicStringsSolver.Solve(text);

            // assert
            result.ShouldBe(
                @"public class Sample
{
    private const string Double = ""1double"";
    private const string Some = ""12some"";
    public void SampleMethod()
    {
        var s = Double;
        var d = Double;
        var a = Some;
        var b = Some;
    }
}");
        }

        [Test]
        public async Task WithAllNumbers_ShouldFallbackToConstantNaming()
        {
            // arrange
            var text = @"public class Sample
{
    public void SampleMethod()
    {
        var s = ""123123123"";
        var d = ""123123123"";
        var a = ""43"";
        var b = ""43"";
    }
}";

            // act
            var result = await BrpMagicStringsSolver.Solve(text);

            // assert
            result.ShouldBe(
                @"public class Sample
{
    private const string C1 = ""123123123"";
    private const string C2 = ""43"";
    public void SampleMethod()
    {
        var s = C1;
        var d = C1;
        var a = C2;
        var b = C2;
    }
}");
        }
    }
}
