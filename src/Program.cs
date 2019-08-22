using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Generic;
using System;
using System.IO;
using System.Text;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/*
    Todo:
    - Calculate min/max ranges
    - When creating new FixedPoint, check whether given value lies within representable
    range.
    - Optional overflow handling
    - Rounding / Jittering
        - This does not trivially extend to higher linear algebra types

    - Generate some other things:
        - Complex numbers
        - Linear Algebra
        - Geometric Algebra
        - Bezier curves
        - Burst jobs
        - Uh oh...
    - Use traits to encapsulate +, -, *, /, avoiding boilerplate
        - Scalar fields
        - Vector fields
        - etc.
    - More localized compiler error reporting
    - Roslyn Analyzer that checks dataflow through client code, tracking
    precision in terms of min/max ranges, reports on it in-line.

    === Combinatorial Explosion ===

    It's kind of funky to consider the Cartesian product of:

    - All qn.m types, for signed, unsigned, 8, 16, 32, 64, 128 bit
    - 2d, 3d, 4d, n-d vector and matrix types
    - 1d, 2d, 3d, 4d, 5d; 1st, 2nd, 3rd, 4th degree Bezier curves, surfaces and volumes

    Sure, you can generate the code for them all, but that's a gigantic amount of types!
    I wonder how big the library becomes, how much it'll slow things down, and just
    how polluted Intellisense will end up...
    
    === Proxy Types ===

    Idea: Generate generic proxy types! Some valid C#, such that it compiles, and we
    get nice intellisense, and we're not flooded with types. Then, we take that
    code, run it through Roslyn code rewriter that replaces the proxies with
    specific, hyper-optimized stuff.

    Goal: Allow library user to write code against Scalar type, such that it works
    for any specific field. A lot like how Rust has TypeClasses.

    Trying this in ProxyTypeTest.cs. Could work, but has some serious downsides.

    === Literals ===

    If we're going with edgy, juicy manipulations that border on altering the
    C# language anyway, I nominate we do something for easier declarations
    of literal values.

    Instead of altering the language, why not extend your keyboard shortcuts
    and IDE with macros?

    Have a dedicated function modifier key, then press another key to choose
    type. That command tells the IDE to generate new vec3_27_4(*cursor*).

    === Const Declarations ===

    Not supported by C#. Minor issue though, right?

    sizeof(q24_7) // This doesn't compile without unsafe. Compiler cannot infer
    const size from members...

    ------

    Useful for finding roslyn structures
    http://roslynquoter.azurewebsites.net
 */

namespace CodeGeneration {
    public static class Config {
        public const string LibraryNameFixedPoint = "FixedPoint";
        public const string LibraryNameComplex = "Complex";
        public const string LibraryNameLinearAlgebra = "LinearAlgebra";
        public const string OutputPathLib = "output/";
        public const string OutputPathSource = "output/src/";
        public const string OutputPathLibSecondary = "E:/code/unity/BurstDynamics/Assets/Plugins/RamjetMath";

        public const bool EmitSourceCode = true;
        public const bool CopyToUnityProject = true;
    }

    class Program  {
        public static void Main(string[] args) {
            Console.WriteLine("Let's generate some code...");
            Console.WriteLine();

            var fixedPointTypes = GenerateFixedPointTypes(Config.LibraryNameFixedPoint);
            var complexTypes = GenerateComplexTypes(Config.LibraryNameComplex, fixedPointTypes);
            var linalgTypes = GenerateLinearAlgebraTypes(Config.LibraryNameLinearAlgebra, fixedPointTypes);

            // ProxyTypeTest.RewriteScalarTypeTest();

            Console.WriteLine();
            Console.WriteLine("All done!");
        }

        private static List<(string typeName, SyntaxTree tree)> GenerateFixedPointTypes(string libName) {
            Console.WriteLine("Generating FixedPoint types...");

            var types = new List<(string typeName, SyntaxTree tree)>();

            // Generate 32-bit fixed point types
            for (int fractionalBits = 0; fractionalBits < 32; fractionalBits++) {
                // Todo: instead of having typename separate, figure out how to
                // extract it from the returned syntax tree
                types.Add(FixedPointTypeGenerator.GenerateSigned32BitType(fractionalBits));
            }

            // Compile types into library, including needed references
            var references = ReferenceLoader.LoadUnityReferences();

            var compilationOptions = new CSharpCompilationOptions(
                outputKind: OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                allowUnsafe: true);

            CompileLibrary(libName, compilationOptions, types.Select(tup => tup.tree), references);

            return types;
        }

        private static List<(string typeName, SyntaxTree tree)> GenerateComplexTypes(string libName, List<(string typeName, SyntaxTree tree)> fpTypes) {
            Console.WriteLine("Generating Complex types...");

            var types = new List<(string typeName, SyntaxTree tree)>();

            for (int i = 0; i < fpTypes.Count; i++) {
                types.Add(ComplexTypeGenerator.GenerateSigned32BitType(fpTypes[i].typeName));
            }

            // Compile types into library, including needed references
            var references = ReferenceLoader.LoadUnityReferences();
            ReferenceLoader.AddGeneratedLibraryReference(references, Config.LibraryNameFixedPoint);

            var compilationOptions = new CSharpCompilationOptions(
                outputKind: OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                allowUnsafe: true);

            CompileLibrary(libName, compilationOptions, types.Select(tup=>tup.tree), references);

            return types;
        }

        private static List<(string typeName, SyntaxTree tree)> GenerateLinearAlgebraTypes(string libName, List<(string typeName, SyntaxTree tree)> fpTypes) {
            Console.WriteLine("Generating LinearAlgebra types...");

            var types = new List<(string typeName, SyntaxTree tree)>();

            // Vector_2d
            for (int i = 0; i < fpTypes.Count; i++) {
                types.Add(VectorTypeGenerator.GenerateSigned32BitType(fpTypes[i].typeName, 2));
            }

            // Vector_3d
            for (int i = 0; i < fpTypes.Count; i++) {
                types.Add(VectorTypeGenerator.GenerateSigned32BitType(fpTypes[i].typeName, 3));
            }

            // Compile types into library, including needed references
            var references = ReferenceLoader.LoadUnityReferences();
            ReferenceLoader.AddGeneratedLibraryReference(references, Config.LibraryNameFixedPoint);

            var compilationOptions = new CSharpCompilationOptions(
                outputKind: OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                allowUnsafe: true);

            CompileLibrary(libName, compilationOptions, types.Select(tup => tup.tree), references);

            return types;
        }

        private static void CompileLibrary(
            string libName,
            CSharpCompilationOptions compilationOptions,
            IEnumerable<SyntaxTree> types,
            IEnumerable<PortableExecutableReference> references) {

            Console.WriteLine("Compiling library: " + libName + ".dll ...");

            // Ensure directory structure
            if (!Directory.Exists(Config.OutputPathLib)) {
                Directory.CreateDirectory(Config.OutputPathLib);
            }
            if (!Directory.Exists(Config.OutputPathSource)) {
                Directory.CreateDirectory(Config.OutputPathSource);
            }
            
            var compilation = CSharpCompilation.Create(
                $@"{libName}Compilation",
                types,
                references,
                compilationOptions);

            // and output dll and pdb to disk
            var dllName = libName + ".dll";
            var pdbName = libName + ".pdb";
            var dllOutputPath = Path.Join(Config.OutputPathLib, dllName);
            var pdbOutputPath = Path.Join(Config.OutputPathLib, pdbName);
            var emitResult = compilation.Emit(
                dllOutputPath,
                pdbOutputPath);

            // If our compilation failed, we can discover exactly why.
            if (!emitResult.Success) {
                Console.WriteLine(string.Format("Code generation for failed! Errors:"));
                foreach (var diagnostic in emitResult.Diagnostics) {
                    PrintDiagnostic(diagnostic);
                }
                
                Console.WriteLine("Aborting...");
                return;
            }

            // Copy the resulting files to our Unity project
            if (Config.CopyToUnityProject) {
                if (!Directory.Exists(Config.OutputPathLibSecondary)) {
                    Directory.CreateDirectory(Config.OutputPathLibSecondary);
                }
                File.Copy(dllOutputPath, Path.Join(Config.OutputPathLibSecondary, dllName), true);
                File.Copy(pdbOutputPath, Path.Join(Config.OutputPathLibSecondary, pdbName), true);
            }

            // Optionally also write out each generated type as C# code text files
            // useful for debugging
            if (Config.EmitSourceCode) {
                foreach (var type in types) {
                    var code = type.GetCompilationUnitRoot().NormalizeWhitespace().ToFullString();
                    var typeName = type.GetRoot().DescendantNodes().OfType<StructDeclarationSyntax>().First().Identifier;
                    var textWriter = File.CreateText(Path.Join(Config.OutputPathSource, typeName + ".cs"));
                    textWriter.Write(code);
                    textWriter.Close();
                }
            }
        }

        public static void PrintSyntaxTreeWithLineNumbers(SyntaxTree tree) {
            string code = tree.GetRoot().NormalizeWhitespace().ToFullString();
            var lines = code.Split('\n');
            var codeBuilder = new StringBuilder();
            for (int i = 0; i <= lines.Length; i++) {
                codeBuilder.AppendLine(string.Format("{0:0000}: {1}", i + 1, lines[i-1]));
            }
            Console.WriteLine(codeBuilder.ToString());
        }

        public static void PrintDiagnostic(Diagnostic diagnostic) {
            /*
                Todo:
                less clutter
                color coding (get it to work in VSCODE)
             */
            var brokenType = diagnostic.Location.SourceTree.GetRoot().DescendantNodes().OfType<StructDeclarationSyntax>().First().Identifier;
            Console.WriteLine($"In type {brokenType}:");
            Console.WriteLine(diagnostic.ToString());

            var code = diagnostic.Location.SourceTree.GetText();
            var codeString = code.ToString();
            var lines = codeString.Split('\n');

            var span = diagnostic.Location.GetLineSpan();

            var line = lines[span.StartLinePosition.Line];
            var lineStart = line.Substring(0, span.StartLinePosition.Character);
            var errorStart = line.Substring(span.StartLinePosition.Character);
            // Console.ForegroundColor = ConsoleColor.White;
            // Console.Write(lineStart);
            // Console.BackgroundColor = ConsoleColor.Red;
            // Console.Write(errorStart + "\n");
            // Console.ResetColor();

            Console.Write(lineStart);
            Console.Write("[<!!!ERROR!!!>]");
            Console.Write(errorStart + "\n");
        }

        private static int FindNewline(string text, int startIndex) {
            int index = startIndex;
            while (index < text.Length) {
                if (text[index] == '\n') {
                    return index;
                }
                index++;
            }
            return startIndex;
        }
    }

    public static class ReferenceLoader {
        private static readonly string[] paths = new string[] {
            "C:/Program Files/Unity/Hub/Editor/2019.2.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.dll",
            "C:/Program Files/Unity/Hub/Editor/2019.2.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll",

            "E:/code/unity/BurstDynamics/Library/ScriptAssemblies/Unity.Burst.dll",
            "E:/code/unity/BurstDynamics/Library/ScriptAssemblies/Unity.Jobs.dll",
            "E:/code/unity/BurstDynamics/Library/ScriptAssemblies/Unity.Collections.dll",
            "E:/code/unity/BurstDynamics/Library/ScriptAssemblies/Unity.Mathematics.dll",
            "E:/code/unity/BurstDynamics/Library/PackageCache/com.unity.burst@1.1.2/Unity.Burst.Unsafe.dll",
            "E:/code/unity/BurstDynamics/Library/PackageCache/com.unity.collections@0.1.1-preview/System.Runtime.CompilerServices.Unsafe.dll",

            "C:/Program Files/Unity/Hub/Editor/2019.2.0f1/Editor/Data/MonoBleedingEdge/lib/mono/4.7.1-api/mscorlib.dll",
            "C:/Program Files/Unity/Hub/Editor/2019.2.0f1/Editor/Data/MonoBleedingEdge/lib/mono/4.7.1-api/System.dll",
        };

        public static IList<PortableExecutableReference> LoadUnityReferences() {
            var libs = new List<PortableExecutableReference>();
            for (int i = 0; i < paths.Length; i++) {
                var lib = MetadataReference.CreateFromFile(paths[i]);
                libs.Add(lib);
            }
            return libs;
        }

        public static void AddGeneratedLibraryReference(IList<PortableExecutableReference> references, string libraryName) {
            var libDllPath = Path.Join(Config.OutputPathLib, libraryName + ".dll");
            references.Add(MetadataReference.CreateFromFile(libDllPath));
        }
    }
}