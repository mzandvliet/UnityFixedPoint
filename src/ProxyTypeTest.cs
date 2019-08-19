
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
    public override SyntaxNode Visit(SyntaxNode node) {
        Console.WriteLine("visiting: " + node.ToFullString());
        return node;
    }
}

public static class ProxyTypeTest {
    public static void RewriteScalarTypeTest() {
        
        string originalCode = $@"
            public void AddNumbers() {{
                var a = Scalar<q15_16>.FromInt(5);
                var b = Scalar<q15_16>.FromInt(4);

                var c = a + b;
                Debug.Log(c);
            }}";

        var unit = SF.ParseCompilationUnit(originalCode);

        var rewriter = new ScalarTypeRewriter();
        var result = rewriter.Visit(unit.SyntaxTree.GetRoot());

        Console.WriteLine("Done! Result: ");
        Console.WriteLine(result.NormalizeWhitespace().ToFullString());
    }
}