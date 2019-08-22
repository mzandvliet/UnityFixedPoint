using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using SK = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

/*
    Todo:
    - unsigned type arithmetic (no need for tricks with sign mask)

    - Add intialization by raw int value
    - support mixed arithmetic with raw int types
        - var num = q24_7.one / 4;

    Notes:

    === Explicit casting ===

    short + short -> int

    Hence, we need copious amounts of casting for non-int word types.

    Here's a stack overflow thread, with Eric Lippert giving some details:
    https://stackoverflow.com/questions/4343624/integer-summing-blues-short-short-problem
    https://stackoverflow.com/questions/941584/byte-byte-int-why

    I can't say I'm entirely on board with the chosen direction, but... now
    we know.

    But wait, there's more restrictions on shifting:

    https://social.msdn.microsoft.com/Forums/vstudio/en-US/1e9d6e3b-bbad-45df-9391-7403becd9641/shift-ltlt-operator-cannot-be-applied-to-uint
 */

namespace CodeGeneration {
    public enum WordSize : int {
        B8 = 8,
        B16 = 16,
        B32 = 32,
        B64 = 64,
    }

    public enum WordSign {
        Unsigned,
        Signed
    }

    public struct WordType {
        public WordSize Size;
        public WordSign Sign;

        public WordType(WordSize size, WordSign sign) {
            Size = size;
            Sign = sign;
        }

        public bool Equals(WordType rhs) {
            return Size == rhs.Size && Sign == rhs.Sign;
        }

        public override bool Equals(object o) {
            return Equals((WordType)o);
        }

        public static bool operator ==(WordType lhs, WordType rhs) {
            return lhs.Equals(rhs);
        }

        public static bool operator !=(WordType lhs, WordType rhs) {
            return !lhs.Equals(rhs);
        }
    }

    public static class FixedPointTypeGenerator {
        private static string GenerateSignBitMaskLiteral(in WordType wordDef) {
            var maskBuilder = new StringBuilder();

            int wordLength = (int)wordDef.Size;

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

        private static string GenerateFractionalMaskLiteral(in WordType wordDef, in int fractionalBits, string intBitChar = "0", string fracBitChar = "1") {
            var maskBuilder = new StringBuilder();

            int wordLength = (int)wordDef.Size;
            int signBit = (wordDef.Sign == WordSign.Signed ? 1 : 0);
            int integerBits = wordLength - signBit - fractionalBits;

            if (integerBits + fractionalBits != wordLength - signBit) {
                throw new ArgumentException(string.Format("Number of integer bits + fractional bits needs to add to {0}", wordLength - signBit));
            }

            maskBuilder.Append("0b");
            for (int i = 0; i < integerBits; i++) {
                if (i > 0 && i % 4 == 0) {
                    maskBuilder.Append("_");
                }
                maskBuilder.Append(intBitChar);
            }
            for (int i = integerBits; i < wordLength; i++) {
                if (i > 0 && i % 4 == 0) {
                    maskBuilder.Append("_");
                }
                maskBuilder.Append(fracBitChar);
            }

            return maskBuilder.ToString();
        }

        public static readonly Dictionary<WordType, string> DotNetWordTypes = new Dictionary<WordType, string> {
            { new WordType(WordSize.B8 , WordSign.Unsigned),    "byte" },
            { new WordType(WordSize.B16, WordSign.Unsigned),    "ushort" },
            { new WordType(WordSize.B32, WordSign.Unsigned),    "uint" },
            { new WordType(WordSize.B64, WordSign.Unsigned),    "ulong" },
            { new WordType(WordSize.B8 , WordSign.Signed),      "sbyte" },
            { new WordType(WordSize.B16, WordSign.Signed),      "short" },
            { new WordType(WordSize.B32, WordSign.Signed),      "int" },
            { new WordType(WordSize.B64, WordSign.Signed),      "long" },
        };

        /*
            Todo:
            - Find out the difference between .WithX and .AddX
            - Question whether we need this extreme verbosity
            - Make cast-to-word-type optional. ({wordType})
         */
        public static (string, SyntaxTree) GenerateType(in WordType wordDef, in int fractionalBits) {
            string wordType = DotNetWordTypes[wordDef];
            int wordLength = (int)wordDef.Size;
            int signBit = (wordDef.Sign == WordSign.Signed ? 1 : 0);
            int integerBits = wordLength - signBit - fractionalBits;

            var doubleWordDef = new WordType((WordSize)(wordLength * 2), wordDef.Sign);
            string doubleWordType = DotNetWordTypes[doubleWordDef];
            var signedWordDef = new WordType(wordDef.Size, WordSign.Signed);
            string signedWordType = DotNetWordTypes[signedWordDef];

            if (integerBits + fractionalBits != wordLength - signBit) {
                throw new ArgumentException(string.Format("Number of integer bits + fractional bits needs to add to {0}", wordLength - signBit));
            }

            string typeName = string.Format("q{0}_{1}", integerBits, fractionalBits);

            string fractionalBitMask =  GenerateFractionalMaskLiteral(wordDef, fractionalBits);
            string integerBitMask =     GenerateFractionalMaskLiteral(wordDef, fractionalBits, "1", "0");

            var usingStrings = new List<string> {
                "System", 
                "System.Runtime.CompilerServices",
                "System.Runtime.InteropServices",
                "UnityEngine",
                "Unity.Mathematics",
            };

            var usings = new SyntaxList<UsingDirectiveSyntax>(
                from s in usingStrings select SF.UsingDirective(SF.ParseName(s)));

            var unit = SF.CompilationUnit().WithUsings(usings);

            var nameSpace = SF.NamespaceDeclaration(SF.ParseName("Ramjet.Math.FixedPoint"));

            var type = SF.StructDeclaration(typeName)
                .AddModifiers(SF.Token(SK.PublicKeyword))
                .WithAttributeLists(Utils.GenerateStructLayoutAttributes());

            // Constants

            var scale = SF.ParseMemberDeclaration(
                $@"public const {signedWordType} Scale = {fractionalBits};");
            var halfScale = SF.ParseMemberDeclaration(
                $@"private const {signedWordType} HalfScale = Scale >> 1;");
            
            var fractionMask = SF.ParseMemberDeclaration(
                $@"private const {wordType} FractionMask = unchecked(({wordType}){fractionalBitMask});");
            var integerMask = SF.ParseMemberDeclaration(
                $@"private const {wordType} IntegerMask = unchecked(({wordType}){integerBitMask});");

            var zero = SF.ParseMemberDeclaration(
                $@"public static readonly {typeName} Zero = new {typeName}(0);");

            var one = SF.ParseMemberDeclaration(
                $@"public static readonly {typeName} One = FromInt(1);");

            var epsilon = SF.ParseMemberDeclaration(
                $@"public static readonly {typeName} Epsilon = new {typeName}(1);");
            var halfEpsilon = SF.ParseMemberDeclaration(
                $@"private const {doubleWordType} HalfEpsilon = ({doubleWordType})(Scale > 0 ? (({wordType})1 << (Scale-1)) : (0));");

            type = type.AddMembers(
                scale,
                halfScale,
                fractionMask,
                integerMask,
                zero,
                one,
                epsilon,
                halfEpsilon);

            // Optional sign mask constant for signed types
            if (signBit == 1) {
                string signBitMask = GenerateSignBitMaskLiteral(wordDef);

                var signMask = SF.ParseMemberDeclaration(
                    $@"private const {wordType} SignMask = unchecked(({wordType}){signBitMask});");
                type = type.AddMembers(signMask);
            }

            // Value field, in which number is stored

            var rawValue = SF.ParseMemberDeclaration(
                $@"[FieldOffset(0)] public {wordType} v;");
            
            type = type.AddMembers(rawValue);

            // Constructors

            var constructor = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public {typeName}({wordType} x) {{
                    v = x;
                }}");

            type = type.AddMembers(
                constructor);

            // === Methods ===

            /* Optional cast-to-int instruction needed for methods on
            unsigned types that return them as signed. */
            string intCast = signBit == 0 ? "(int)" : "";

            /*
            Type conversions

            Todo:
                - uint
                - explicit casting
            */

            var fromInt = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {typeName} FromInt(int x) {{
                    return new {typeName}(({wordType})(x << Scale));
                }}");

            var toInt = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static int ToInt({typeName} f) {{
                    return {intCast}(f.v >> Scale);
                }}");

            var fromFloat = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {typeName} FromFloat(float x) {{
                    return new {typeName}(
                        ({wordType})math.round((x * (float)(1 << Scale)))
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
                        ({wordType})math.round((x * (double)(1 << Scale)))
                    );
                }}");

            var toDouble = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static double ToDouble({typeName} f) {{
                    return ({wordType})(f.v / (double)((1 << Scale)));
                }}");

            type = type.AddMembers(
                fromInt,
                toInt,
                fromFloat,
                toFloat,
                fromDouble,
                toDouble);

            /*
            Frac

            There's multiple ways to implement Frac.

            Here we use a masking strategy, taking
            two's complement into account.

            Could also use double bit shifting like so:
            return new {typeName}((f.v << (Scale-1)) >> (Scale-1));
            */
            var fractNegativePath = "";
            if (signBit == 1) {
                // Code path for signed types, in case sign bit is set
                fractNegativePath = $@"
                if ((f.v & SignMask) != ({wordType})0) {{
                    return new {typeName}(({wordType})((f.v & FractionMask) | IntegerMask));
                }}";
            }
            var frac = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {typeName} Frac({typeName} f) {{
                    {fractNegativePath}
                    return new {typeName}(({wordType})(f.v & FractionMask));
                }}");

            /*
            Whole

            Two's complement automatically handled properly
            here, because most significant bits are preserved.
            */
            var whole = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {typeName} Whole({typeName} f) {{
                    return new {typeName}(({wordType})(f.v & IntegerMask));
                }}");

            type = type.AddMembers(
                frac,
                whole
            );

            /*
            === Operator overloading ===
            
            Note: we construct the result by new struct(), which is quite slow.

            Todo: Generate variants for all supported mixed-type operations
            */

            var opAdd = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {typeName} operator +({typeName} lhs, {typeName} rhs) {{
                    return new {typeName}(({wordType})(lhs.v + rhs.v));
                }}");

            var opIncr = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {typeName} operator ++({typeName} lhs) {{
                    return new {typeName}(({wordType})(lhs.v+1));
                }}");

            var opSub = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {typeName} operator -({typeName} lhs, {typeName} rhs) {{
                    return new {typeName}(({wordType})(lhs.v - rhs.v));
                }}");

            var opDecr = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {typeName} operator --({typeName} lhs) {{
                    return new {typeName}(({wordType})(lhs.v - 1));
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
                    {doubleWordType} lhsLong = lhs.v;
                    {doubleWordType} rhsLong = rhs.v;
                    {doubleWordType} result = (lhsLong * rhsLong) + HalfEpsilon;
                    return new {typeName}(({wordType})(result >> Scale));
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
                    return new {typeName}(({wordType})(((({doubleWordType})lhs.v << Scale) / rhs.v)));
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
                public override int GetHashCode() {{ return {intCast}v; }}");

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