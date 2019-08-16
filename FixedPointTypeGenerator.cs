using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Text;

namespace CodeGeneration {
    public static class FixedPointTypeGenerator {
        private static string GenerateSignBitMaskLiteral(int wordLength) {
            var maskBuilder = new StringBuilder();

            maskBuilder.Append("0b");
            maskBuilder.Append("1");
            for (int i = 1; i < wordLength; i++) {
                if (i > 0 && i % 4 == 0) {
                    maskBuilder.Append("_");
                }
                maskBuilder.Append("0");
            }

            return maskBuilder.ToString();
        }

        private static string GenerateFractionalMaskLiteral(int wordLength, int fractionalBits) {
            var maskBuilder = new StringBuilder();

            int integerBits = wordLength - 1 - fractionalBits;
            if (integerBits + fractionalBits != wordLength - 1) {
                throw new ArgumentException(string.Format("Number of integer bits + fractional bits needs to add to {0}", wordLength - 1));
            }

            maskBuilder.Append("0b");
            for (int i = 0; i < integerBits + 1; i++) {
                if (i > 0 && i % 4 == 0) {
                    maskBuilder.Append("_");
                }
                maskBuilder.Append("0");
            }
            for (int i = integerBits + 1; i < wordLength; i++) {
                if (i > 0 && i % 4 == 0) {
                    maskBuilder.Append("_");
                }
                maskBuilder.Append("1");
            }

            return maskBuilder.ToString();
        }

        public static (string, SyntaxTree) GenerateSigned32BitType(in int fractionalBits) {
            const int wordLength = 32;
            int integerBits = wordLength - 1 - fractionalBits;
            if (integerBits + fractionalBits != wordLength - 1) {
                throw new ArgumentException(string.Format("Number of integer bits + fractional bits needs to add to {0}", wordLength - 1));
            }

            string typeName = string.Format("q{0}_{1}", integerBits, fractionalBits);

            string signBitMask = GenerateSignBitMaskLiteral(wordLength);
            string fractionalBitMask = GenerateFractionalMaskLiteral(wordLength, fractionalBits);

            Console.WriteLine(typeName);
            Console.WriteLine("signMask: " + signBitMask);
            Console.WriteLine("fracMask: " + fractionalBitMask);

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
    const int SignMask = unchecked((int){signBitMask});
    const int FractionMask = unchecked((int){fractionalBitMask});
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
    public static {typeName} Frac({typeName} f) {{
        if ((f.v & SignMask) != 0) {{
            return new {typeName}((f.v & FractionMask) | IntegerMask);
        }}
        return new {typeName}(f.v & FractionMask);

        // return new {typeName}((f.v << (Scale-1)) >> (Scale-1));
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals({typeName} rhs) {{ return v == rhs.v; }}
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object o) {{ return Equals(({typeName})o); }}
    public static bool operator ==({typeName} lhs, {typeName} rhs) {{
        return lhs.Equals(rhs);
    }}
    public static bool operator !=({typeName} lhs, {typeName} rhs) {{
        return !lhs.Equals(rhs);
    }}

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

    private static string ToBinaryString(int value) {{
        string b = Convert.ToString(value, 2);
        b = b.PadLeft(32, '0');
        return b;
    }}
}}
";
            var node = CSharpSyntaxTree.ParseText(code);
            return (typeName, node);
        }
    }
}