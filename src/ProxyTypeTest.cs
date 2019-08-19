
/*
    Idea: Surrogate Scalar Type

    Clients write their code using a single type.
    Easy to use, tracks the relevant things, etc.

    It's valid C#.

    An analyzer could read along and track
    precision for you, give hints, or ask
    you to specify expected min/max ranges.

    Then at compilation, a switcheroo!

    The client code is fed into a rewriter that
    replaces the surrogate type with dedicated
    fixed point types, generating only those
    that are actually in use.

    Downsides:
    - Locks clients into using an IDE that
    supports the analyzer & compiler.

    --

    We'll need a bunch of extra Roslyn stuff

    - Semantic module

    https://joshvarty.com/2015/02/05/learn-roslyn-now-part-8-data-flow-analysis/
    Dataflow analysis is close to what we ultimately want

    --

    Using CSharpSyntaxRewriter we can visit all GenericNameSyntax, which
    will include all uses of Scalar<qn_m> an assembly has.
*/

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using SK = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

public enum ScalarType {
    i32,
    f32,
    q15_16
}

public struct Scalar<ScalarType> {
    public const int Scale = 16;
    public int v;

    public Scalar(int i) {
        v = i;
    }

    public static Scalar<ScalarType> operator *(Scalar<ScalarType> lhs, Scalar<ScalarType> rhs) {
        // return new Scalar<ScalarType>((int)(((long)lhs.v * (long)rhs.v) >> Scale));
        return new Scalar<ScalarType>(lhs.v * rhs.v);
    }
}

public class ScalarTypeRewriter : CSharpSyntaxRewriter {
    // public override SyntaxNode Visit(SyntaxNode node) {
    //     if (node == null) {
    //         Console.WriteLine("Warning: visiting a node which is null...");
    //         return null;
    //     }
    //     Console.WriteLine("visiting: " + node.ToString());
    //     Console.WriteLine("type: " + node.GetType().ToString());
    //     base.Visit(node);
    //     return node;
    // }

    public override SyntaxNode VisitGenericName(GenericNameSyntax node) {
        if (node == null) {
            Console.WriteLine("Warning: visiting a node which is null...");
            return null;
        }
        Console.WriteLine("visiting generic name: " + node.ToFullString());
        Console.WriteLine("identifier: " + node.Identifier);
        Console.WriteLine("type arguments:");
        foreach (var typeArg in node.TypeArgumentList.Arguments) {
            Console.WriteLine("    - " + typeArg.ToFullString());
        }

        base.VisitGenericName(node);
        return node;
    }
}

public static class ProxyTypeTest {
    public static void RewriteScalarTypeTest() {
        
        string originalCode = $@"
            public class MyBeautifulClass {{
                public void AddNumbers() {{
                    var a = Scalar<q15_16>.FromInt(5);
                    var b = Scalar<q15_16>.FromInt(4);

                    var c = a + b;
                    Debug.Log(c);
                }}
            }}";

        

        var tree = CSharpSyntaxTree.ParseText(originalCode);

        var msCorlibPath = "C:/Program Files/Unity/Hub/Editor/2019.2.0f1/Editor/Data/MonoBleedingEdge/lib/mono/4.7.1-api/mscorlib.dll";
        var msCorlib = MetadataReference.CreateFromFile(msCorlibPath);
        var compilation = CSharpCompilation.Create("MyCompilation",
            syntaxTrees: new[] { tree }, references: new[] { msCorlib });
        var model = compilation.GetSemanticModel(tree);

        var root = tree.GetRoot();
        var myClass = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First();
        var myMethod = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First();

        Console.WriteLine(myClass.Identifier.ToString());
        Console.WriteLine(myMethod.Identifier.ToString());

        var methodSymbol = model.GetDeclaredSymbol(myMethod);

        var rewriter = new ScalarTypeRewriter();
        var result = rewriter.Visit(root);

        Console.WriteLine("Done! Result: ");
        Console.WriteLine(result.NormalizeWhitespace().ToFullString());
    }
}