
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
    - WILL BREAK DEBUGGERS
        - E.g. Unity debugger will fire up while
        expecting the unmodified code. I suspect
        it will mostly work, until you try interacting
        with code paths using Scalar<ScalarType>.

    --

    We'll need a bunch of extra Roslyn stuff

    - Semantic module

    https://joshvarty.com/2015/02/05/learn-roslyn-now-part-8-data-flow-analysis/
    Dataflow analysis is close to what we ultimately want

    --

    Using CSharpSyntaxRewriter we can visit all GenericNameSyntax, which
    will include all uses of Scalar<qn_m> an assembly has.

    --

    Debugger Compatibility

    Is it possible at all????

    https://docs.microsoft.com/en-us/dotnet/framework/debug-trace-profile/enhancing-debugging-with-the-debugger-display-attributes
    https://docs.unity3d.com/Manual/ScriptCompilationAssemblyDefinitionFiles.html
    https://docs.unity3d.com/ScriptReference/Compilation.AssemblyBuilder.html
    https://vscode.readthedocs.io/en/latest/editor/debugging/#launchjson-attributes

    Options:
    - Write game code external to Unity, preprocess it, then copy files
    to Unity project folder after type replacement, and debugging from
    there.
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
    private Dictionary<ScalarType, TypeSyntax> _typeMap;

    public ScalarTypeRewriter() {
        _typeMap = new Dictionary<ScalarType, TypeSyntax>();
        var typeNames = Enum.GetNames(typeof(ScalarType));
        foreach (var name in typeNames) {
            _typeMap.Add((ScalarType)Enum.Parse(typeof(ScalarType), name), SF.ParseTypeName("q15_16"));
        }
    }

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

        if (node.TypeArgumentList.Arguments.Count != 1) {
            Console.WriteLine(string.Format("Error: Unexpected TypeArgument count {0} for ScalarType, there should be only one.", node.TypeArgumentList.Arguments.Count));
            return node;
        }

        ScalarType scalarType;
        try {
            scalarType = (ScalarType)Enum.Parse(typeof(ScalarType), node.TypeArgumentList.Arguments[0].ToFullString());
        } catch(Exception e) {
            Console.WriteLine(string.Format("Error: Type argument {0} not recognized as valid ScalarType. Reason:\n{1}", node.TypeArgumentList.Arguments[0], e.Message));
            return node;
        }
        
        var replacementNode = _typeMap[scalarType];

        base.VisitGenericName(node);
        return replacementNode;
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