using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Generic;
using System;
using System.IO;

/*
    Todo:
    - Fix bugs in the below
    - Calculate min/max ranges
    - When creating new FixedPoint, check whether given value lies within representable
    range.
    - Use more Roslyn structure, less raw string manipulation
    - Generate some other things:
        - Complex numbers
        - Vectors
        - Bezier curves
        - Uh oh...

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
 */

namespace CodeGeneration {
    class Program  {
        private const string LibraryName = "FixedPoint";
        private const string OutputPathLib = "../output/";
        private const string OutputPathSource = "../output/src/";
        private const string OutputPathLibSecondary = "E:/code/unity/BurstDynamics/Assets/Plugins/FixedPoint";
        private const bool EmitSourceCode = true;

        public static void Main(string[] args) {
            Console.WriteLine("Let's generate some code...");

            // Ensure directory structure
            if (!Directory.Exists(OutputPathLib)) {
                Directory.CreateDirectory(OutputPathLib);
            }
            if (!Directory.Exists(OutputPathSource)) {
                Directory.CreateDirectory(OutputPathSource);
            }

            // Generate types
            var typeNames = new List<string>();
            var syntaxTrees = new List<SyntaxTree>();
            for (int fractionalBits = 0; fractionalBits < 31; fractionalBits++) {
                // Todo: instead of having typename separate, figure out how to
                // extract it from the returned syntax tree
                (string typeName, SyntaxTree tree) = FixedPointGenerator.GenerateSigned32BitType(fractionalBits);
                typeNames.Add(typeName);
                syntaxTrees.Add(tree);
            }

            // Compile types into library, including needed references
            var references = ReferenceLoader.Load();

            var compilationOptions = new CSharpCompilationOptions(
                outputKind: OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                allowUnsafe: false);
            var compilation = CSharpCompilation.Create(
                "FixedPointTypesCompilation",
                syntaxTrees,
                references: references,
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
                    var code = syntaxTrees[i].GetText();
                    var textWriter = File.CreateText(Path.Join(OutputPathSource, typeNames[i] + ".cs"));
                    textWriter.Write(code);
                    textWriter.Close();
                }
            }
            
            Console.WriteLine("Compilation was succesful!");
        }
    }

    public static class ReferenceLoader {
        private static readonly string[] paths = new string[] {
            "C:/Program Files/Unity/Hub/Editor/2019.2.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.dll",
            "E:/code/unity/BurstDynamics/Library/ScriptAssemblies/Unity.Burst.dll",
            "E:/code/unity/BurstDynamics/Library/ScriptAssemblies/Unity.Jobs.dll",
            "E:/code/unity/BurstDynamics/Library/ScriptAssemblies/Unity.Collections.dll",
            "E:/code/unity/BurstDynamics/Library/ScriptAssemblies/Unity.Mathematics.dll",
            "E:/code/unity/BurstDynamics/Library/PackageCache/com.unity.burst@1.1.2/Unity.Burst.Unsafe.dll",
            "C:/Program Files/Unity/Hub/Editor/2019.2.0f1/Editor/Data/MonoBleedingEdge/lib/mono/4.7.1-api/mscorlib.dll",
            "C:/Program Files/Unity/Hub/Editor/2019.2.0f1/Editor/Data/MonoBleedingEdge/lib/mono/4.7.1-api/System.dll",
            "E:/code/unity/BurstDynamics/Library/PackageCache/com.unity.collections@0.1.1-preview/System.Runtime.CompilerServices.Unsafe.dll",
        };

        public static IList<PortableExecutableReference> Load() {
            var libs = new List<PortableExecutableReference>();
            for (int i = 0; i < paths.Length; i++) {
                // var lib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
                var lib = MetadataReference.CreateFromFile(paths[i]);
                libs.Add(lib);
            }
            return libs;
        }
    }

    public static class FixedPointGenerator {
        public static (string, SyntaxTree) GenerateSigned32BitType(in int fractionalBits) {
            const int wordLength = 32;
            int integerBits = wordLength - 1 - fractionalBits;
            if (integerBits + fractionalBits != wordLength-1) {
                throw new ArgumentException(string.Format("Number of integer bits + fractional bits needs to add to {0}", wordLength-1));
            }
            
            string typeName = string.Format("q{0}_{1}", integerBits, fractionalBits);

            // Todo: generate the mask values for each type
        
            string code = $@"
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Mathematics;

[System.Serializable]
[StructLayout(LayoutKind.Explicit)]
public struct {typeName}
{{
    public const int Scale = {fractionalBits};
    const int HalfScale = Scale >> 1;
    const int SignMask = unchecked((int)0x80000000);
    const int FractionMask = unchecked((int)((0xFFFFFFFF >> ({wordLength} - Scale))));
    const int NegativeFracPadding = unchecked((int)0xFFFF0000);
    const int IntegerMask = ~FractionMask;

    public static readonly {typeName} Zero = new {typeName}(0);

    [FieldOffset(0)]
    public int v;

   // constructors

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public {typeName}(int x) {{
        v = x;
    }}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public {typeName}(float x) {{
        v = (int)math.round((x * (float)(1 << Scale)));
    }}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public {typeName}(double x) {{
        v = (int)math.round((x * (double)(1 << Scale)));
    }}

    // Fractional part
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static {typeName} Fract({typeName} f) {{
        return new {typeName}((f.v << Scale) >> Scale);
    }}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static {typeName} Whole({typeName} f) {{
        /*
        Two's complement automatically handled properly
        here, because MSBits are preserved.
        */
        return new {typeName}(f.v & IntegerMask);
    }}

    // Conversion (todo: implement as typecast)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static {typeName} FromInt(int x) {{
        return new {typeName}(x << Scale);
    }}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ToInt({typeName} f) {{
        return f.v >> Scale;
    }}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ToFloat({typeName} f) {{
        return f.v / (float)((1 << Scale));
    }}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ToDouble({typeName} f) {{
        return f.v / (double)((1 << Scale));
    }}

    // Addition
    // Todo: one possible bit of overflow

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static {typeName} operator +({typeName} lhs, {typeName} rhs) {{
        /* Here we construct the result by new struct(), it is quite slow. */
        return new {typeName}(lhs.v + rhs.v);
    }}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static {typeName} operator ++({typeName} lhs) {{ return new {typeName}(lhs.v+1); }} // todo: ref?

    // Subtraction
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static {typeName} operator -({typeName} lhs, {typeName} rhs) {{ return new {typeName}(lhs.v - rhs.v); }}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static {typeName} operator --({typeName} lhs) {{ return new {typeName}(lhs.v - 1); }} // todo: ref?

    // Multiplication
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static {typeName} operator *({typeName} lhs, {typeName} rhs) {{
        // This works, but could be slow due to cast to 64-bit accumulators
        // SIMD would like this to stay in 32-bit world as well?
        return new {typeName}((int)(((long)lhs.v * (long)rhs.v) >> Scale));

        // You can pre-shift, throwing out some precision, but staying within register limits
        // I chose HalfScale here, but you could >> 4 the inputs, with a final shift at the end
        //return new {typeName}((lhs.v>>HalfScale) * (rhs.v>>HalfScale)); // >> 0
    }}

    // Division
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static {typeName} operator /({typeName} lhs, {typeName} rhs) {{
        // Here we shift lhs by scale and leave rhs unchanged to cancel
        // out scaling effects, using 64 bit accumulator.
        return new {typeName}((int)((((long)lhs.v << Scale) / rhs.v)));

        // Here instead we do dangerous shifting to stay in 32-bit. Works
        // for subsets of numbers, I guess. YMMV.
        //return new {typeName}((int)((lhs.v << HalfScale) / (rhs.v >> HalfScale)));
    }}

    // Equals 
    public bool Equals({typeName} rhs) {{ return v == rhs.v; }}
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object o) {{ return Equals(({typeName})o); }}


    // GetHashCode 
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() {{ return v; }}


    // ToString 
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override string ToString() {{
        return string.Format(""{typeName}({{0}})"", ToDouble(this));
    }}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ToString(string format, IFormatProvider formatProvider) {{
        return string.Format(""{typeName}({{0}})"", ToDouble(this));
    }}
}}
";
            var node = CSharpSyntaxTree.ParseText(code);
            return (typeName, node);
        }
    }
}