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

    public class FixedPointType {
        public readonly string name;
        public readonly WordType word;
        public readonly int fractionalBits;
        public int integerBits {
            get => (int)word.Size - signBit - fractionalBits;
        }
        public int signBit {
            get => (word.Sign == WordSign.Signed ? 1 : 0);
        }
        public int wordLength {
            get => (int)word.Size;
        }

        public FixedPointType(WordType word, int fractionalBits) {
            this.word = word;
            this.fractionalBits = fractionalBits;

            if (integerBits + fractionalBits != wordLength - signBit) {
                throw new ArgumentException(string.Format("Number of integer bits + fractional bits needs to add to {0}", wordLength - signBit));
            }

            this.name = string.Format("q{0}{1}_{2}", signBit == 1 ? "s" : "u", integerBits, fractionalBits);
        }
    }

    public static class FixedPointTypeGenerator {
        public class Options {
            public bool AddRangeChecks = true;
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

        private static string GenerateSignBitMaskLiteral(in FixedPointType fType) {
            var maskBuilder = new StringBuilder();

            maskBuilder.Append("0b");
            maskBuilder.Append("1");
            for (int i = 1; i < fType.wordLength; i++) {
                if (i > 0 && i % 4 == 0) {
                    maskBuilder.Append("_");
                }
                maskBuilder.Append("0");
            }

            return maskBuilder.ToString();
        }

        private static string GenerateFractionalMaskLiteral(in FixedPointType fType, string intBitChar = "0", string fracBitChar = "1") {
            var maskBuilder = new StringBuilder();

            int wordLength = (int)fType.word.Size;
         
            if (fType.integerBits + fType.fractionalBits != wordLength - fType.signBit) {
                throw new ArgumentException(string.Format("Number of integer bits + fractional bits needs to add to {0}", wordLength - fType.signBit));
            }

            maskBuilder.Append("0b");
            for (int i = 0; i < fType.integerBits; i++) {
                if (i > 0 && i % 4 == 0) {
                    maskBuilder.Append("_");
                }
                maskBuilder.Append(intBitChar);
            }
            for (int i = fType.integerBits; i < wordLength; i++) {
                if (i > 0 && i % 4 == 0) {
                    maskBuilder.Append("_");
                }
                maskBuilder.Append(fracBitChar);
            }

            return maskBuilder.ToString();
        }

        private static SyntaxToken GetFieldIdentifier(MemberDeclarationSyntax field) {
            return field.SyntaxTree.GetRoot().DescendantNodesAndSelf().OfType<FieldDeclarationSyntax>().First().Declaration.Variables.First().Identifier;
        }

        private static string GenerateRangeCheck(FixedPointType fType, string variableName, SyntaxToken minName, SyntaxToken maxName) {
            return $@"
                if ({variableName} < {minName} || {variableName} > {maxName}) {{
                    throw new System.ArgumentException(string.Format(
                        ""value {{0}} lies outside of representable range [{{1}} , {{2}}] for {fType.signBit}"",
                        {variableName},
                        {minName.ToString()},
                        {maxName.ToString()}));
                }}";
        }

        private static MemberDeclarationSyntax GenerateAdder(FixedPointType fType, string wordCastOpt) {
            var opAdd = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} operator +({fType.name} lhs, {fType.name} rhs) {{
                    return new {fType.name}({wordCastOpt}(lhs.v + rhs.v));
                }}");

            return opAdd;
        }

        /*
            Todo:
            - Find out the difference between .WithX and .AddX
         */
        public static (FixedPointType, SyntaxTree) GenerateType(in FixedPointType fType, in Options options) {
            string wordType = DotNetWordTypes[fType.word];
            
            var doubleWordDef = new WordType((WordSize)(fType.wordLength * 2), fType.word.Sign);
            string doubleWordType = DotNetWordTypes[doubleWordDef];
            int doubleWordLength = (int)doubleWordDef.Size;
            var signedWordDef = new WordType(fType.word.Size, WordSign.Signed);
            string signedWordType = DotNetWordTypes[signedWordDef];

            string fractionalBitMask =  GenerateFractionalMaskLiteral(fType);
            string integerBitMask =     GenerateFractionalMaskLiteral(fType, "1", "0");

            /*
            Calculate minimum and maximum values in fractional representation
            Note: fType.integerBits already has 1 less in case of sign bit, see above.
             */
            double rangeMinDoubleValue = 0f;
            double rangeMaxDoubleValue = 0f;
            double epsilonDoubleValue = epsilonDoubleValue = 1.0 / Math.Pow(2, fType.fractionalBits);
            if (fType.signBit == 0) {
                rangeMinDoubleValue = 0f;
                rangeMaxDoubleValue = Math.Pow(2, fType.integerBits) - Math.Pow(2, -fType.integerBits);
            } else {
                rangeMinDoubleValue = -Math.Pow(2, fType.integerBits);
                rangeMaxDoubleValue = Math.Pow(2, fType.integerBits) - Math.Pow(2, -fType.integerBits);
            }
            float rangeMinFloatValue = (float)rangeMinDoubleValue;
            float rangeMaxFloatValue = (float)rangeMaxDoubleValue;
            int rangeMinIntValue = (int)rangeMinDoubleValue; // Todo: rounding/flooring
            int rangeMaxIntValue = (int)rangeMaxDoubleValue;

            // Create Type, add usings
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

            var type = SF.StructDeclaration(fType.name)
                .AddModifiers(SF.Token(SK.PublicKeyword))
                .WithAttributeLists(Utils.GenerateStructLayoutAttributes());

            // Constants

            var rangeMinInt = SF.ParseMemberDeclaration(
                $@"public const int RangeMinInt = {rangeMinIntValue};");
            var rangeMaxInt = SF.ParseMemberDeclaration(
                $@"public const int RangeMaxInt = {rangeMaxIntValue};");
            var rangeMinFloat = SF.ParseMemberDeclaration(
                $@"public const float RangeMinFloat = {rangeMinFloatValue}f;");
            var rangeMaxFloat = SF.ParseMemberDeclaration(
                $@"public const float RangeMaxFloat = {rangeMaxFloatValue}f;");
            var rangeMinDouble = SF.ParseMemberDeclaration(
                $@"public const double RangeMinDouble = {rangeMinDoubleValue}d;");
            var rangeMaxDouble = SF.ParseMemberDeclaration(
                $@"public const double RangeMaxDouble = {rangeMaxDoubleValue}d;");
                
            var epsilonDouble = SF.ParseMemberDeclaration(
                $@"public const double EpsilonDouble = {epsilonDoubleValue}d;");

            var scale = SF.ParseMemberDeclaration(
                $@"public const {signedWordType} Scale = {fType.fractionalBits};");
            var halfScale = SF.ParseMemberDeclaration(
                $@"private const {signedWordType} HalfScale = Scale >> 1;");
            
            var fractionMask = SF.ParseMemberDeclaration(
                $@"private const {wordType} FractionMask = unchecked(({wordType}){fractionalBitMask});");
            var integerMask = SF.ParseMemberDeclaration(
                $@"private const {wordType} IntegerMask = unchecked(({wordType}){integerBitMask});");

            var zero = SF.ParseMemberDeclaration(
                $@"public static readonly {fType.name} Zero = new {fType.name}(0);");
            var one = SF.ParseMemberDeclaration(
                $@"public static readonly {fType.name} One = FromInt(1);");

            var epsilon = SF.ParseMemberDeclaration(
                $@"public static readonly {fType.name} Epsilon = new {fType.name}(1);");
            var halfEpsilon = SF.ParseMemberDeclaration(
                $@"private const {doubleWordType} HalfEpsilon = ({doubleWordType})(Scale > 0 ? (({wordType})1 << (Scale-1)) : (0));");

            type = type.AddMembers(
                rangeMinInt,
                rangeMaxInt,
                rangeMinFloat,
                rangeMaxFloat,
                rangeMinDouble,
                rangeMaxDouble,
                epsilonDouble,
                scale,
                halfScale,
                fractionMask,
                integerMask,
                zero,
                one,
                epsilon,
                halfEpsilon);

            // Optional sign mask constant for signed types
            if (fType.signBit == 1) {
                string signBitMask = GenerateSignBitMaskLiteral(fType);

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
                public {fType.name}({wordType} x) {{
                    v = x;
                }}");

            type = type.AddMembers(
                constructor);

            // === Methods ===

            /* Some optional type casts, needed in special cases. */

            /* Optional cast-to-int instruction needed for methods on
            unsigned types that return them as signed. */
            string intCastOpt = fType.signBit == 0 ? "(int)" : "";

            /* Optional cast-to-wordType instruction needed for code
            that performs arithmetic with small types, with in C#
            always return int for some reason. */
            string wordCast = $@"({wordType})";
            string wordCastOpt = fType.wordLength < 32 ? wordCast : "";

            /* Optional cast-to-wordType instruction needed for code
            that performs arithmetic with small types, with in C#
            always return int for some reason. */
            string doubleWordCast = $@"({doubleWordType})";
            string doubleWordCastOpt = doubleWordLength < 32 ? doubleWordCast : "";

            /*
            Type conversions

            Todo:
                - uint
                - explicit casting
            */

            var intRangeCheckOpt = "";
            if (options.AddRangeChecks) {
                intRangeCheckOpt = GenerateRangeCheck(
                    fType,
                    "x",
                    GetFieldIdentifier(rangeMinInt),
                    GetFieldIdentifier(rangeMaxInt));
            }
            var fromInt = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} FromInt(int x) {{
                    {intRangeCheckOpt}
                    return new {fType.name}(({wordType})(x << Scale));
                }}");

            var toInt = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static int ToInt({fType.name} f) {{
                    return {intCastOpt}(f.v >> Scale);
                }}");

            var floatRangeCheckOpt = "";
            if (options.AddRangeChecks) {
                floatRangeCheckOpt = GenerateRangeCheck(
                    fType,
                    "x",
                    GetFieldIdentifier(rangeMinFloat),
                    GetFieldIdentifier(rangeMaxFloat));
            }
            var fromFloat = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} FromFloat(float x) {{
                    {floatRangeCheckOpt}
                    return new {fType.name}(
                        ({wordType})math.round((x * (float)(1 << Scale)))
                    );
                }}");

            var toFloat = SF.ParseMemberDeclaration($@"
                 [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static float ToFloat({fType.name} f) {{
                    return f.v / (float)((1 << Scale));
                }}");

            var doubleRangeCheckOpt = "";
            if (options.AddRangeChecks) {
                doubleRangeCheckOpt = GenerateRangeCheck(
                    fType,
                    "x",
                    GetFieldIdentifier(rangeMinDouble),
                    GetFieldIdentifier(rangeMaxDouble));
            }
            var fromDouble = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} FromDouble(double x) {{
                    {doubleRangeCheckOpt}
                    return new {fType.name}(
                        ({wordType})math.round((x * (double)(1 << Scale)))
                    );
                }}");

            var toDouble = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static double ToDouble({fType.name} f) {{
                    return f.v / (double)(1 << Scale);
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
            return new {fType.name}((f.v << (Scale-1)) >> (Scale-1));
            */
            var fractNegativePath = "";
            if (fType.signBit == 1) {
                // Code path for signed types, in case sign bit is set
                fractNegativePath = $@"
                if ((f.v & SignMask) != ({wordType})0) {{
                    return new {fType.name}({wordCastOpt}((f.v & FractionMask) | IntegerMask));
                }}";
            }
            var frac = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} Frac({fType.name} f) {{
                    {fractNegativePath}
                    return new {fType.name}({wordCastOpt}(f.v & FractionMask));
                }}");

            /*
            Whole

            Two's complement automatically handled properly
            here, because most significant bits are preserved.
            */
            var whole = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} Whole({fType.name} f) {{
                    return new {fType.name}({wordCastOpt}(f.v & IntegerMask));
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
                public static {fType.name} operator +({fType.name} lhs, {fType.name} rhs) {{
                    return new {fType.name}({wordCastOpt}(lhs.v + rhs.v));
                }}");

            var opIncr = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} operator ++({fType.name} lhs) {{
                    return new {fType.name}({wordCastOpt}(lhs.v+1));
                }}");

            var opSub = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} operator -({fType.name} lhs, {fType.name} rhs) {{
                    return new {fType.name}({wordCastOpt}(lhs.v - rhs.v));
                }}");

            var opDecr = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} operator --({fType.name} lhs) {{
                    return new {fType.name}({wordCastOpt}(lhs.v - 1));
                }}");

            /*
            Multiplication

            This works, but could be slow due to cast to 64-bit accumulators
            SIMD would like this to stay in 32-bit world as well?

            Todo: a version that doesn't shift back after each multiply, but
            lets you chain multiple MADS before shifting back.

            You can pre-shift, throwing out some precision, but staying within register limits
            I chose HalfScale here, but you could >> 4 the inputs, with a final shift at the end

            return new {fType.name}((lhs.v>>HalfScale) * (rhs.v>>HalfScale)); // >> 0
             */
            var opMul = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} operator *({fType.name} lhs, {fType.name} rhs) {{
                    {doubleWordType} lhsLong = lhs.v;
                    {doubleWordType} rhsLong = rhs.v;
                    {doubleWordType} result = {doubleWordCastOpt}((lhsLong * rhsLong) + HalfEpsilon);
                    return new {fType.name}({wordCast}(result >> Scale));
                }}");

            /*
            Division

            Here we shift lhs by scale and leave rhs unchanged to cancel
            out scaling effects, using 64 bit accumulator.

            Alternatively:

            Here instead we do dangerous shifting to stay in 32-bit. Works
            for subsets of numbers, I guess. YMMV.
            return new {fType.name}((int)((lhs.v << HalfScale) / (rhs.v >> HalfScale)));
            */

            var opDiv = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} operator /({fType.name} lhs, {fType.name} rhs) {{
                    return new {fType.name}(
                        ({wordType})((({doubleWordCast}lhs.v << Scale) / rhs.v))
                    );
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
                public bool Equals({fType.name} rhs) {{ return v == rhs.v; }}");

            var equalsObj = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public override bool Equals(object o) {{ return Equals(({fType.name})o); }}");

            var opEq = SF.ParseMemberDeclaration($@"
                public static bool operator ==({fType.name} lhs, {fType.name} rhs) {{
                    return lhs.Equals(rhs);
                }}");

            var opNEq = SF.ParseMemberDeclaration($@"
                public static bool operator !=({fType.name} lhs, {fType.name} rhs) {{
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
                public override int GetHashCode() {{ return {intCastOpt}v; }}");

            var toString = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public override string ToString() {{
                    return string.Format(""{fType.name}({{0}})"", ToDouble(this));
                }}");

            var toStringFormat = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public string ToString(string format, IFormatProvider formatProvider) {{
                    return string.Format(""{fType.name}({{0}})"", ToDouble(this));
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

            return (fType, CSharpSyntaxTree.Create(unit));
        }
    }
}