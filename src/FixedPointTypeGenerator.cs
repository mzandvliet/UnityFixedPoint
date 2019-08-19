using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using SK = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

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

        /*
            Todo:
            - Find out the difference between .WithX and .AddX
            - Question whether we need this extreme verbosity
         */
        public static (string, SyntaxTree) GenerateSigned32BitType(in int fractionalBits) {
            string wordType = "Int32";
            const int wordLength = 32;
            int integerBits = wordLength - 1 - fractionalBits;
            if (integerBits + fractionalBits != wordLength - 1) {
                throw new ArgumentException(string.Format("Number of integer bits + fractional bits needs to add to {0}", wordLength - 1));
            }

            string typeName = string.Format("q{0}_{1}", integerBits, fractionalBits);

            string signBitMask = GenerateSignBitMaskLiteral(wordLength);
            string fractionalBitMask = GenerateFractionalMaskLiteral(wordLength, fractionalBits);

            var usingStrings = new List<string> {
                "System", 
                "System.Runtime.CompilerServices",
                "System.Runtime.InteropServices",
                "UnityEngine",
                "Unity.Mathematics",
            };

            var usings = new SyntaxList<UsingDirectiveSyntax>(
                from s in usingStrings select SF.UsingDirective(SF.ParseName(s)));

            var unit = SF.CompilationUnit()
                .WithUsings(usings);

            var nameSpace = SF.NamespaceDeclaration(SF.ParseName("Ramjet.Math.FixedPoint"));

            var type = SF.StructDeclaration(typeName)
                .AddModifiers(SF.Token(SK.PublicKeyword))
                .WithAttributeLists(Utils.GenerateStructLayoutAttributes());

            // Constants

            var scale = SF.ParseMemberDeclaration(
                $@"public const {wordType} Scale = {fractionalBits};");
            var halfScale = SF.ParseMemberDeclaration(
                $@"private const {wordType} HalfScale = Scale >> 1;");
            var signMask = SF.ParseMemberDeclaration(
                $@"private const {wordType} SignMask = unchecked(({wordType}){signBitMask});");
            var fractionMask = SF.ParseMemberDeclaration(
                $@"private const {wordType} FractionMask = unchecked(({wordType}){fractionalBitMask});");
            var integerMask = SF.ParseMemberDeclaration(
                $@"private const {wordType} IntegerMask = ~FractionMask;");

            var zero = SF.ParseMemberDeclaration(
                $@"public static readonly {typeName} Zero = new {typeName}(0);");

            var one = SF.ParseMemberDeclaration(
                $@"public static readonly {typeName} One = FromInt(1);");

            var epsilon = SF.ParseMemberDeclaration(
                $@"public static readonly {typeName} Epsilon = new {typeName}(1);");

            // Value field

            var intValue = SF.ParseMemberDeclaration(
                $@"[FieldOffset(0)] public {wordType} v;");

            
            type =  type.AddMembers(
                scale,
                halfScale,
                signMask,
                fractionMask,
                integerMask,
                zero,
                one,
                epsilon,
                intValue);

            // Constructors

            var constructor = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public {typeName}({wordType} x) {{
                    v = x;
                }}");

            type = type.AddMembers(
                constructor);


            // === Methods ===

            // Type conversion

            var fromInt = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {typeName} FromInt(int x) {{
                    return new {typeName}(x << Scale);
                }}");

            var toInt = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static int ToInt({typeName} f) {{
                    return f.v >> Scale;
                }}");

            var fromFloat = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {typeName} FromFloat(float x) {{
                    return new {typeName}(
                        (int)math.round((x * (float)(1 << Scale)))
                    );
                }}");

            var toFloat = SF.ParseMemberDeclaration($@"
                 [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static float ToFloat({typeName} f) {{
                    return f.v / (float)((1 << Scale));
                }}");

            var fromDouble = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {typeName} FromDouble(double x) {{
                    return new {typeName}(
                        (int)math.round((x * (double)(1 << Scale)))
                    );
                }}");

            var toDouble = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static double ToDouble({typeName} f) {{
                    return f.v / (double)((1 << Scale));
                }}");

            type = type.AddMembers(
                fromInt,
                toInt,
                fromFloat,
                toFloat,
                fromDouble,
                toDouble);

            /*
            There's multiple ways to implement Frac.

            Here we use a masking strategy, taking
            two's complement into account.

            Could also use double bit shifting like so:
            return new {typeName}((f.v << (Scale-1)) >> (Scale-1));
            */
            var frac = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {typeName} Frac({typeName} f) {{
                    if ((f.v & SignMask) != 0) {{
                        return new {typeName}((f.v & FractionMask) | IntegerMask);
                    }}
                    return new {typeName}(f.v & FractionMask);
                }}");

            /*
            Two's complement automatically handled properly
            here, because MSBits are preserved.
            */
            var whole = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {typeName} Whole({typeName} f) {{
                   
                    return new {typeName}(f.v & IntegerMask);
                }}");

            type = type.AddMembers(
                frac,
                whole
            );

            /*
            === Operator overloading ===
            
            Here we construct the result by new struct(), it is quite slow.
            */

            var opAdd = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {typeName} operator +({typeName} lhs, {typeName} rhs) {{
                    return new {typeName}(lhs.v + rhs.v);
                }}");

            var opIncr = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {typeName} operator ++({typeName} lhs) {{
                    return new {typeName}(lhs.v+1);
                }}");

            var opSub = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {typeName} operator -({typeName} lhs, {typeName} rhs) {{
                    return new {typeName}(lhs.v - rhs.v);
                }}");

            var opDecr = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {typeName} operator --({typeName} lhs) {{
                    return new {typeName}(lhs.v - 1);
                }}");

            /*
            Multiplication

            This works, but could be slow due to cast to 64-bit accumulators
            SIMD would like this to stay in 32-bit world as well?

            Todo: a version that doesn't shift back after each multiply, but
            lets you chain multiple MADS before shifting back.

            You can pre-shift, throwing out some precision, but staying within register limits
            I chose HalfScale here, but you could >> 4 the inputs, with a final shift at the end

            return new {typeName}((lhs.v>>HalfScale) * (rhs.v>>HalfScale)); // >> 0
             */
            var opMul = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {typeName} operator *({typeName} lhs, {typeName} rhs) {{
                    return new {typeName}((int)(((long)lhs.v * (long)rhs.v) >> Scale));
                }}");

            /*
            Division

            Here we shift lhs by scale and leave rhs unchanged to cancel
            out scaling effects, using 64 bit accumulator.

            Alternatively:

            Here instead we do dangerous shifting to stay in 32-bit. Works
            for subsets of numbers, I guess. YMMV.
            return new {typeName}((int)((lhs.v << HalfScale) / (rhs.v >> HalfScale)));
            */

            var opDiv = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {typeName} operator /({typeName} lhs, {typeName} rhs) {{
                    return new {typeName}((int)((((long)lhs.v << Scale) / rhs.v)));
                }}");

            type = type.AddMembers(
                opAdd,
                opIncr,
                opSub,
                opDecr,
                opMul,
                opDiv);

            /* Equality */

            var equals = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public bool Equals({typeName} rhs) {{ return v == rhs.v; }}");

            var equalsObj = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public override bool Equals(object o) {{ return Equals(({typeName})o); }}");

            var opEq = SF.ParseMemberDeclaration($@"
                public static bool operator ==({typeName} lhs, {typeName} rhs) {{
                    return lhs.Equals(rhs);
                }}");

            var opNEq = SF.ParseMemberDeclaration($@"
                public static bool operator !=({typeName} lhs, {typeName} rhs) {{
                    return !lhs.Equals(rhs);
                }}");

            type = type.AddMembers(
                equals,
                equalsObj,
                opEq,
                opNEq);

            // Other

            var getHashCode = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public override int GetHashCode() {{ return v; }}");

            var toString = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public override string ToString() {{
                    return string.Format(""{typeName}({{0}})"", ToDouble(this));
                }}");

            var toStringFormat = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public string ToString(string format, IFormatProvider formatProvider) {{
                    return string.Format(""{typeName}({{0}})"", ToDouble(this));
                }}");

            var toStringBinary = SF.ParseMemberDeclaration($@"
                private static string ToBinaryString(int value) {{
                    string b = Convert.ToString(value, 2);
                    b = b.PadLeft(32, '0');
                    return b;
                }}");

            type = type.AddMembers(
                getHashCode,
                toString,
                toStringFormat,
                toStringBinary);

            nameSpace = nameSpace.AddMembers(type);
            unit = unit.AddMembers(nameSpace);

            return (typeName, CSharpSyntaxTree.Create(unit));
        }
    }
}