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

    - finish mixed-type ops (vector, int, etc)
    - casting operators
    - mul and div by simple fractions, like 2/1, 1/4, etc.
    - automated testing
    - halfEpsilon for fractionalBits = 0
    - negative fractionalBits, and extended scale types?
    - Play nice with Burst autovectorization
    - Optional overflow handling
    - Rounding / Jittering
        - This does not trivially extend to higher linear algebra types
    - Improved type specification for generator
        - Could parse Unity client code for specific instructions
        to generate desired types, could even do this live, with a
        Roslyn analyzer? On-Demand type generation while programming...
        - "It looks like you're trying to use an ungenerated type, do
        you want to generate it?"

    - Generate some other things:
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
    
    * Proxy Types *

    Idea: Generate generic proxy types! Some valid C#, such that it compiles, and we
    get nice intellisense, and we're not flooded with types. Then, we take that
    code, run it through Roslyn code rewriter that replaces the proxies with
    specific, hyper-optimized stuff.

    Goal: Allow library user to write code against Scalar type, such that it works
    for any specific field. A lot like how Rust has TypeClasses.

    Trying this in ProxyTypeTest.cs. Could work, but has some serious downsides.

    * Code Stripping *

    Conversely, we can generate all variants but then strip all the unused ones
    after a Unity build.

    * Computation Graphs *

    A Tensorflow-like syntax that specifies computation graphs in a functional,
    declarative style. Runtime object creation builds a graph that does the
    actual stuff. The syntax would end up looking a lot like the use of a
    proxy type, but the implementation would fight the language and compiler
    less.

    === Precision: Analysis and Design ===

    Many operations are not commutative, precision-wise.

    a * b might overflow, or loose vast amounts of fractional bits
    b * a might be perfectly fine.

    * Dithering *

    8 bit particle simulations and the like would really benefit from
    a solid dithering approach. I believe there is tremendous computational
    power in this, much like dithering can make a world of difference
    in image processing.

    * Interacting Signed and Unsigned Values *

    u32 - u32 -> s32

    This is causing some grief. The uints are not technically closed under
    subtraction. Many ways to deal with this:

    - u32 - u32 -> s32 enforce this method signature. Let users case back to
    u32 if they feel that is right (warning: some values become unrepresentable)
    - use p-adic numbers, since there is no sign bit then

    === Literals ===

    If we're going with edgy, juicy manipulations that border on altering the
    C# language anyway, I nominate we do something for easier declarations
    of literal values.

    Instead of altering the language, why not extend your keyboard shortcuts
    and IDE with macros?

    Have a dedicated function modifier key, then press another key to choose
    type. That command tells the IDE to generate new vec3_27_4(*cursor*).

    * Const Declarations *

    Not supported by C#. Minor issue though, right?

    sizeof(q24_7) // This doesn't compile without unsafe. Compiler cannot infer
    const size from members...

    === Operators ===

    Pow, Exp, Sqrt, Sin, etc.
    
    Try to implement Pow using shifting, such that we can use integer arithmetic for it as well.

    === Burst Auto Vectorization ??? ===

    In Unity.Mathematics sourcecode readme, it says:

    "In addition to this, the Burst compiler is able to recognize these types and provide
    the optimized SIMD type for the running CPU on all supported platforms (x64, ARMv7a...etc.)"

    Does that mean Burst only looks for literal instances of their math types
    and do a specific replace with each? If so, and that is the only way
    to get it to vectorize anything, none of my fixed point code will
    vectorize at all....

    If it doesn't I'll be rather disappointed, and perhaps be moved to say:
    sod it, I'm taking all this into Rust and not looking back.

    Edit: It vectorizes **sometimes**

    https://stackoverflow.com/questions/8193601/sse-multiplication-16-x-uint8-t

    Hypothesis: Burst might only be able to vectorize if the type across
    the operations remains the same? Since in each op we have
    up and down casting to different word lengths, that might
    be where it trips up.

    = SIMD Emulation =

    https://archive.eetasia.com/www.eetasia.com/ART_8800453603_499495_NT_0045eaff.HTM
    Could be feasible. :)

    === CIL Optimization ===

    Some CIL optimizations should be safe, like returning
    
    unsafe_cast<qn_m>(intValue)
    
    instead of 

    new qn_m(intValue)

    ------

    === Generator Type System ===

    Not having higher kinded types in the form of Traits mean
    you can't work with trait bounds.

    Want to figure out the maximum value of the backing integer
    type? You can't easily know this, because you have a System.Type,
    which doesn't know the actual types are bounded by integers of
    various sizes.

    ------

    Beyond the Basics

    I want types that actually go way beyond the qn_m types
    I have now.

    When doing highly quantized physics, I want to track
    velocity with 8 bits of mostly fraction, but for
    an 8 bit position I want only 3 fraction bits or
    less.

    I might often now want the result of a multiplication
    to be downshifted.

    I might want a qs3_2 type, which lives 8 bits.
    I might want to accumulate 16 other numbers and have
    enough accumulator bits for precisely that, like in
    neural networks or other things that use n-dim inner
    products.

    ----

    Useful for finding roslyn structures
    http://roslynquoter.azurewebsites.net
 */

namespace CodeGeneration {
    public class GeneratorConfig {

        public IList<FixedPointType> PrimitiveTypes {
            get => _primitiveTypes;
        }
        
        private List<FixedPointType> _primitiveTypes = new List<FixedPointType> {
            new FixedPointType(new WordType(WordSize.B8, WordSign.Signed), 4),
            new FixedPointType(new WordType(WordSize.B8, WordSign.Signed), 6),
            new FixedPointType(new WordType(WordSize.B8, WordSign.Signed), 7),
            new FixedPointType(new WordType(WordSize.B8, WordSign.Unsigned), 0),
            new FixedPointType(new WordType(WordSize.B8, WordSign.Unsigned), 7),
            new FixedPointType(new WordType(WordSize.B8, WordSign.Unsigned), 8),

            new FixedPointType(new WordType(WordSize.B16, WordSign.Signed), 12),
            new FixedPointType(new WordType(WordSize.B16, WordSign.Signed), 9),
            new FixedPointType(new WordType(WordSize.B16, WordSign.Signed), 14),
            new FixedPointType(new WordType(WordSize.B32, WordSign.Signed), 16),
            new FixedPointType(new WordType(WordSize.B16, WordSign.Unsigned), 8),

            new FixedPointType(new WordType(WordSize.B32, WordSign.Signed), 12),
            new FixedPointType(new WordType(WordSize.B32, WordSign.Unsigned), 12),
        };

        /* Todo: derived types:
        vec_n
        mat
        complex
        proj
        etc
        */
    }

    public static class OutputConfig {
        public const string LibraryNameFixedPoint = "FixedPoint";
        public const string LibraryNameComplex = "Complex";
        public const string LibraryNameLinearAlgebra = "LinearAlgebra";
        public const string OutputPathLib = "output/";
        public const string OutputPathSource = "output/src/";
        public const string OutputPathLibSecondary = "E:/code/unity/BurstDynamics/Assets/Plugins/RamjetMath";

        public const bool EmitSourceCodeToUnityProject = true;
        public const bool CopyToUnityProject = false;
    }

    class Program  {
        public static void Main(string[] args) {
            GenerateLibraries();
        }

        private static void GenerateLibraries() {
            Console.WriteLine("Let's generate some code...");
            Console.WriteLine();

            // Clean output folder
            if (Directory.Exists(OutputConfig.OutputPathLib)) {
                Directory.Delete(OutputConfig.OutputPathLib, true);
            }
            // Ensure directory structure
            Directory.CreateDirectory(OutputConfig.OutputPathLib);
            Directory.CreateDirectory(OutputConfig.OutputPathSource);

            // var fTypes = GenerateAllFixedPointTypeDefinitions();
            var fTypes = new GeneratorConfig().PrimitiveTypes;
            fTypes = ComplementWithSignedTypeDefinitions(fTypes);

            var fixedPointTypes = GenerateFixedPointTypes(OutputConfig.LibraryNameFixedPoint, fTypes);
            // var complexTypes = GenerateComplexTypes(Config.LibraryNameComplex, fixedPointTypes);
            var linalgTypes = GenerateLinearAlgebraTypes(OutputConfig.LibraryNameLinearAlgebra, fixedPointTypes);

            Console.WriteLine();
            Console.WriteLine("All done!");
        }

        private static IList<FixedPointType> GenerateAllFixedPointTypeDefinitions() {
            var fTypes = new List<FixedPointType>();

            // Loop over given type, generate all variants
            void GenerateFTypes(WordType word) {
                int maxFractionalBits = (int)word.Size - (word.Sign == WordSign.Signed ? 1 : 0);
                for (int fractionalBits = 0; fractionalBits <= maxFractionalBits; fractionalBits++) {
                    var fType = new FixedPointType(
                        word,
                        fractionalBits
                    );
                    fTypes.Add(fType);
                }
            }

            // Generate 32-bit fixed point types
            GenerateFTypes(new WordType(WordSize.B32, WordSign.Signed));
            GenerateFTypes(new WordType(WordSize.B32, WordSign.Unsigned));

            // Generate 16-bit fixed point types
            GenerateFTypes(new WordType(WordSize.B16, WordSign.Signed));
            GenerateFTypes(new WordType(WordSize.B16, WordSign.Unsigned));

            // Generate 8-bit fixed point types
            GenerateFTypes(new WordType(WordSize.B8, WordSign.Signed));
            GenerateFTypes(new WordType(WordSize.B8, WordSign.Unsigned));

            return fTypes;
        }

        private static IList<FixedPointType> ComplementWithSignedTypeDefinitions(IList<FixedPointType> fTypes) {
            var newTypes = new List<FixedPointType>(fTypes);

            foreach (var type in fTypes) {
                if (type.signBit == 0) {
                    int signedFTypeIntegerBits = type.signBit == 1 ?
                        type.integerBits :
                        Math.Max(1, type.integerBits - 1);

                    var complementType = new FixedPointType(type.signedWord, type.wordLength - signedFTypeIntegerBits - 1);
                    if (!newTypes.Contains(complementType)) {
                        newTypes.Add(complementType);
                    }
                }
            }

            return newTypes;
        }

        private static List<(FixedPointType type, SyntaxTree tree)> GenerateFixedPointTypes(string libName, in IList<FixedPointType> fTypes) {
            Console.WriteLine("Generating FixedPoint types...");

            var options = new FixedPointTypeGenerator.Options {
                AddRangeChecks = true,
            };

            var types = new List<(FixedPointType type, SyntaxTree tree)>();

            foreach (var fType in fTypes) {
                types.Add(FixedPointTypeGenerator.GenerateType(fType, fTypes, options));
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
            ReferenceLoader.AddGeneratedLibraryReference(references, OutputConfig.LibraryNameFixedPoint);

            var compilationOptions = new CSharpCompilationOptions(
                outputKind: OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                allowUnsafe: true);

            CompileLibrary(libName, compilationOptions, types.Select(tup=>tup.tree), references);

            return types;
        }

        private static List<(FixedPointType type, SyntaxTree tree)> GenerateLinearAlgebraTypes(string libName, List<(FixedPointType type, SyntaxTree tree)> fpTypes) {
            Console.WriteLine("Generating LinearAlgebra types...");

            var types = new List<(FixedPointType type, SyntaxTree tree)>();

            // Vector_2
            for (int i = 0; i < fpTypes.Count; i++) {
                types.Add(VectorTypeGenerator.GenerateType(fpTypes[i].type, 2));
            }

            // Vector_3
            for (int i = 0; i < fpTypes.Count; i++) {
                types.Add(VectorTypeGenerator.GenerateType(fpTypes[i].type, 3));
            }

            // Vector_4
            for (int i = 0; i < fpTypes.Count; i++) {
                types.Add(VectorTypeGenerator.GenerateType(fpTypes[i].type, 4));
            }

            // Matrix_2x2
            for (int i = 0; i < fpTypes.Count; i++) {
                types.Add(MatrixTypeGenerator.Generate2x2Type(fpTypes[i].type));
            }

            // Compile types into library, including needed references
            var references = ReferenceLoader.LoadUnityReferences();
            ReferenceLoader.AddGeneratedLibraryReference(references, OutputConfig.LibraryNameFixedPoint);

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

            var compilation = CSharpCompilation.Create(
                $@"{libName}Compilation",
                types,
                references,
                compilationOptions);

            // and output dll and pdb to disk
            var dllName = libName + ".dll";
            var pdbName = libName + ".pdb";
            var dllOutputPath = Path.Join(OutputConfig.OutputPathLib, dllName);
            var pdbOutputPath = Path.Join(OutputConfig.OutputPathLib, pdbName);
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
            if (OutputConfig.CopyToUnityProject) {
                if (!Directory.Exists(OutputConfig.OutputPathLibSecondary)) {
                    Directory.CreateDirectory(OutputConfig.OutputPathLibSecondary);
                }
                File.Copy(dllOutputPath, Path.Join(OutputConfig.OutputPathLibSecondary, dllName), true);
                File.Copy(pdbOutputPath, Path.Join(OutputConfig.OutputPathLibSecondary, pdbName), true);
            }

            // Optionally also write out each generated type as C# code text files
            // useful for debugging
            if (OutputConfig.EmitSourceCodeToUnityProject) {
                string outputPathSource = Path.Join(OutputConfig.OutputPathLibSecondary, libName);
                if (Directory.Exists(outputPathSource)) {
                    Directory.Delete(outputPathSource, true);
                }

                Directory.CreateDirectory(outputPathSource);

                foreach (var type in types) {
                    var code = type.GetCompilationUnitRoot().NormalizeWhitespace().ToFullString();
                    var typeName = type.GetRoot().DescendantNodes().OfType<StructDeclarationSyntax>().First().Identifier;
                    var textWriter = File.CreateText(Path.Join(outputPathSource, typeName + ".cs"));
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

            if (span.StartLinePosition.Line - 1 >= 0) {
                Console.WriteLine(lines[span.StartLinePosition.Line - 1]);
            }

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

            if (span.StartLinePosition.Line + 1 < span.EndLinePosition.Line) {
                Console.WriteLine(lines[span.StartLinePosition.Line + 1]);
            }
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
            var libDllPath = Path.Join(OutputConfig.OutputPathLib, libraryName + ".dll");
            references.Add(MetadataReference.CreateFromFile(libDllPath));
        }
    }
}