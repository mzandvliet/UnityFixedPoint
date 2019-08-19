using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Generic;
using System;
using System.IO;
using System.Text;

/*
    Todo:
    - Calculate min/max ranges
    - When creating new FixedPoint, check whether given value lies within representable
    range.
    - Generate some other things:
        - Complex numbers
        - Vectors
        - Bezier curves
        - Uh oh...
        - Burst jobs
    - Use typeclasses to encapsulate +, -, *, /, avoiding boilerplate
        - Linear algebra works over fields, fields always behave the same way
        - Can automatically generate operator implementations and such, since
        addition is linear over all coefficients
    - More localized compiler error reporting

    === Combinatorial Explosion ===

    It's kind of funky to consider the Cartesian product of:

    - All Qn.m types, for signed, unsigned, 8, 16, 32, 64, 128 bit
    - 2d, 3d, 4d vector and matrix types
    - 1d, 2d, 3d, 4d, 5d; 1st, 2nd, 3rd, 4th degree Bezier curves, surfaces and volumes

    Sure, you can generate the code for them all, but that's a gigantic amount of types!
    I wonder how big the library becomes, how much it'll slow things down, and just
    how polluted Intellisense will end up...
    
    Idea: Generate generic proxy types! Some valid C#, such that it compiles, and we
    get nice intellisense, and we're not flooded with types. Then, we take that
    code, run it through Roslyn code rewriter that replaces the proxies with
    specific, hyper-optimized stuff.

    Goal: Allow library user to write code against Scalar type, such that it works
    for any specific field. A lot like how Rust has TypeClasses.

    ------

    Useful for finding roslyn structures
    http://roslynquoter.azurewebsites.net
 */

namespace CodeGeneration {
    class Program  {
        private const string LibraryName = "FixedPoint";
        private const string OutputPathLib = "output/";
        private const string OutputPathSource = "output/src/";
        private const string OutputPathLibSecondary = "E:/code/unity/BurstDynamics/Assets/Plugins/FixedPoint";
        private const bool EmitSourceCode = true;

        public static void Main(string[] args) {
            Console.WriteLine("Let's generate some code...");
            Console.WriteLine();

            // GenerateCode();
            // ProxyTypeTest.RewriteScalarTypeTest();

            var type = VectorTypeGenerator.GenerateSigned32BitType("q15_16", 3);
            Console.WriteLine(type.Item2.GetRoot().NormalizeWhitespace().ToFullString());

            Console.WriteLine();
            Console.WriteLine("Compilation was succesful!");
        }


        private static void GenerateCode() {
            // Ensure directory structure
            if (!Directory.Exists(OutputPathLib)) {
                Directory.CreateDirectory(OutputPathLib);
            }
            if (!Directory.Exists(OutputPathSource)) {
                Directory.CreateDirectory(OutputPathSource);
            }

            // Generate 32-bit fixed point types
            var typeNames = new List<string>();
            var syntaxTrees = new List<SyntaxTree>();
            for (int fractionalBits = 0; fractionalBits < 32; fractionalBits++) {
                // Todo: instead of having typename separate, figure out how to
                // extract it from the returned syntax tree
                (string typeName, SyntaxTree tree) = FixedPointTypeGenerator.GenerateSigned32BitType(fractionalBits);
                typeNames.Add(typeName);
                syntaxTrees.Add(tree);
            }

            // Generate a few complex number types based on fixed point
            for (int i = 0; i < 4; i++) {
                (string typeName, SyntaxTree tree) = ComplexTypeGenerator.GenerateSigned32BitType(typeNames[16 + i * 4]);
                typeNames.Add(typeName);
                syntaxTrees.Add(tree);
            }

            // Compile types into library, including needed references
            var references = ReferenceLoader.Load();

            var compilationOptions = new CSharpCompilationOptions(
                outputKind: OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                allowUnsafe: true);
            var compilation = CSharpCompilation.Create(
                "FixedPointTypesCompilation",
                syntaxTrees,
                references,
                compilationOptions);

            // and output dll and pdb to disk
            var dllName = LibraryName + ".dll";
            var pdbName = LibraryName + ".pdb";
            var dllOutputPath = Path.Join(OutputPathLib, dllName);
            var pdbOutputPath = Path.Join(OutputPathLib, pdbName);
            var emitResult = compilation.Emit(
                dllOutputPath,
                pdbOutputPath);

            // If our compilation failed, we can discover exactly why.
            if (!emitResult.Success) {
                Console.WriteLine("Code generation failed! Errors:");
                foreach (var diagnostic in emitResult.Diagnostics) {
                    Console.WriteLine(diagnostic.ToString());
                }
                return;
            }

            // Copy the resulting files to our Unity project
            File.Copy(dllOutputPath, Path.Join(OutputPathLibSecondary, dllName), true);
            File.Copy(pdbOutputPath, Path.Join(OutputPathLibSecondary, pdbName), true);

            // Optionally also write out each generated type as C# code text files
            // useful for debugging
            if (EmitSourceCode) {
                for (int i = 0; i < syntaxTrees.Count; i++) {
                    var code = syntaxTrees[i].GetCompilationUnitRoot().NormalizeWhitespace().ToFullString();
                    var textWriter = File.CreateText(Path.Join(OutputPathSource, typeNames[i] + ".cs"));
                    textWriter.Write(code);
                    textWriter.Close();
                }
            }
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

        public static IList<PortableExecutableReference> Load() {
            var libs = new List<PortableExecutableReference>();
            for (int i = 0; i < paths.Length; i++) {
                var lib = MetadataReference.CreateFromFile(paths[i]);
                libs.Add(lib);
            }
            return libs;
        }
    }
}