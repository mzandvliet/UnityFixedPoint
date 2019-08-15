using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Generic;
using System;
using System.IO;

namespace CodeGeneration {
    class Program  {
        private static string OutputPath = "output/";

        public static void Main(string[] args) {
            if (!Directory.Exists(OutputPath)) {
                Directory.CreateDirectory(OutputPath);
            }
            Console.WriteLine("Let's generate some code!");

            var syntraxTrees = new List<SyntaxTree>();
            syntraxTrees.Add(MyGenerator.GenerateFixedPointType(15, 16));
            syntraxTrees.Add(MyGenerator.GenerateFixedPointType(14, 17));
            syntraxTrees.Add(MyGenerator.GenerateFixedPointType(13, 18));
            syntraxTrees.Add(MyGenerator.GenerateFixedPointType(12, 19));

            var references = ReferenceLoader.Load();

            var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
            var compilation = CSharpCompilation.Create(
                "FixedPointTypesCompilation",
                syntraxTrees,
                references: references,
                compilationOptions);

            var emitResult = compilation.Emit(
                Path.Join(OutputPath, "FixedPoint.dll"),
                Path.Join(OutputPath, "FixedPoint.pdb"));

            //If our compilation failed, we can discover exactly why.
            if (!emitResult.Success) {
                Console.WriteLine("Code generation FAILED! Errors:");
                foreach (var diagnostic in emitResult.Diagnostics) {
                    Console.WriteLine(diagnostic.ToString());
                }
            }

            Console.WriteLine("Done!");
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

    public static class MyGenerator {
        public static SyntaxTree GenerateFixedPointType(in int integerBits, in int fractionalBits) {
            const int wordLength = 32;
            if (integerBits + fractionalBits != wordLength-1) {
                throw new ArgumentException(string.Format("Number of integer bits + fractional bits needs to add to {0}", wordLength-1));
            }
            
            string typeName = string.Format("q{0}_{1}", integerBits, fractionalBits);
        
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
        return string.Format(""{typeName}(0f)"", ToDouble(this));
    }}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ToString(string format, IFormatProvider formatProvider) {{
        return string.Format(""{typeName}(0f)"", ToDouble(this));
    }}
}}
";

            var node = CSharpSyntaxTree.ParseText(code);
            return node;
        }
    }
}