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

    - Figure out rounding for +, -, *, /

    - >, <, >=, <= (for mixed types too?)
    - min, max, clamp
    - negation by writing -value

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

        public readonly string name;

        public readonly WordType word;
        public readonly WordType doubleWord;
        public readonly WordType signedWord;

        public readonly string wordType;
        public readonly string doubleWordType;
        public readonly string signedWordType;

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
        public int doubleWordLength {
            get => (int)word.Size;
        }


        public FixedPointType(WordType word, int fractionalBits) {
            this.word = word;
            this.fractionalBits = fractionalBits;

            if (integerBits + fractionalBits != wordLength - signBit) {
                throw new ArgumentException(string.Format("Number of integer bits + fractional bits needs to add to {0}", wordLength - signBit));
            }

            doubleWord = new WordType((WordSize)(wordLength * 2), word.Sign);
            signedWord = new WordType(word.Size, WordSign.Signed);

            wordType =          DotNetWordTypes[word]; 
            doubleWordType =    DotNetWordTypes[doubleWord];
            signedWordType =    DotNetWordTypes[signedWord];

            this.name = GetTypeName(integerBits, fractionalBits, signBit);
        }

        public static string GetTypeName(int integerBits, int fractionalBits, int signBit) {
            return string.Format("q{0}{1}_{2}", signBit == 1 ? "s" : "u", integerBits, fractionalBits);
        }
    }

    public static class FixedPointTypeGenerator {
        public class Options {
            public bool AddRangeChecks = true;
        }

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
            /*
            Todo: might be better to make this a preprocessor thing, using
            #if ENABLE_FIXED_POINT_RANGE_CHECKS
            ...
            #endif
             */
            return $@"
                if ({variableName} < {minName} || {variableName} > {maxName}) {{
                    throw new System.ArgumentException(string.Format(
                        ""value {{0}} lies outside of representable range [{{1}} , {{2}}] for {fType.signBit}"",
                        {variableName},
                        {minName.ToString()},
                        {maxName.ToString()}));
                }}";
        }

        private static MemberDeclarationSyntax GenerateAdder(FixedPointType lhType, FixedPointType rhType) {
            /*
                Todo:
                - decide whether return type is lhType or rhType

                - Like with all the other ops, reason about
                precision

                If lh has 8 fract bits, rh has 16 frac bits, we may shift rh
                to the right by 8 bits to match. We then also lose 8 of those
                fractional bits.

                If lh has 7 integer bits, and rhType has 12, we run
                a real risk of overflowing, unless the actual value
                in rhType is low enough to no occupy those high bits.
             */
            int signedShift = lhType.fractionalBits - rhType.fractionalBits;
            string shiftOp = signedShift >= 0 ? "<<" : ">>";
            int shiftAmount = Math.Abs(signedShift);

            string wordCastOpt = lhType.word.Size == WordSize.B32 ? "" : $@"({lhType.wordType})";

            var op = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {lhType.name} operator +({lhType.name} lhs, {rhType.name} rhs) {{
                    return new {lhType.name}({wordCastOpt}(lhs.v + ({lhType.wordType})(rhs.v {shiftOp} {shiftAmount})));
                }}");

            return op;
        }

        private static MemberDeclarationSyntax GenerateSubber(FixedPointType lhType, FixedPointType rhType) {
            int signedShift = lhType.fractionalBits - rhType.fractionalBits;
            string shiftOp = signedShift >= 0 ? "<<" : ">>";
            int shiftAmount = Math.Abs(signedShift);

            string wordCastOpt = lhType.word.Size == WordSize.B32 ? "" : $@"({lhType.wordType})";

            var op = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {lhType.name} operator -({lhType.name} lhs, {rhType.name} rhs) {{
                    return new {lhType.name}({wordCastOpt}(lhs.v - ({lhType.wordType})(rhs.v {shiftOp} {shiftAmount})));
                }}");

            return op;
        }

        /*
        Generates a multiplier that returns the type of the left-hand-side argument.
         */
        private static MemberDeclarationSyntax GenerateMultiplier(FixedPointType lhType, FixedPointType rhType) {
            /*
            Todo:

            consider case where rhType.fractionalBits == 0, such that
            HalfShiftBits becomes 1 << -1, which doesn't make sense.

            If one of the types is signed, the resulting type must also be signed?

            We could also generate a linter warning pointing out that this
            might generate invalid results.
            */

            /*
            For each permutation of types, we get a different Half
            constant used for rounding.
            */
            int halfShiftBits = Math.Max(0, rhType.fractionalBits - 1);
            string half = $@"const {lhType.doubleWordType} half = 
                    ({lhType.doubleWordType})(({lhType.wordType})1 << {halfShiftBits});";

            var op = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {lhType.name} operator *({lhType.name} lhs, {rhType.name} rhs) {{
                    {half}

                    {lhType.doubleWordType} lhsLong = lhs.v;
                    {lhType.doubleWordType} rhsLong = ({lhType.doubleWordType})rhs.v;
                    {lhType.doubleWordType} result = ({lhType.doubleWordType})((lhsLong * rhsLong) + half);
                    return new {lhType.name}(({lhType.wordType})(result >> {rhType.fractionalBits}));
                }}");

            return op;
        }

        private static MemberDeclarationSyntax GenerateDivisor(FixedPointType lhType, FixedPointType rhType) {
            /*
            Todo: Not performing any rounding with half tricks here yet,
            need to work out what the right thing is to do.

            Also, we have half as an int here, when 32-bits or less.
            This is because it is applied immediately after a shift, which returns Int32
            in those cases
            */

            string halfType = lhType.wordLength <= 32 ? "int" : $@"{lhType.doubleWordType}";
            int halfShiftBits = Math.Max(0, rhType.fractionalBits - 1);
            string half = $@"const {halfType} half = (1 << {halfShiftBits});";

            var op = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {lhType.name} operator /({lhType.name} lhs, {rhType.name} rhs) {{
                    {half}

                    {lhType.doubleWordType} lhsLong = ({lhType.doubleWordType})((lhs.v << {rhType.fractionalBits}) + half);
                    {lhType.doubleWordType} rhsLong = ({lhType.doubleWordType})rhs.v;
                    return new {lhType.name}(({lhType.wordType})(lhsLong / rhsLong));
                }}");

            return op;
        }

        /*
            Todo:
            - Find out the difference between .WithX and .AddX
         */
        public static (FixedPointType, SyntaxTree) GenerateType(in FixedPointType fType, in List<FixedPointType> fTypes, in Options options) {
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
                $@"public const {fType.signedWordType} Scale = {fType.fractionalBits};");
            var halfScale = SF.ParseMemberDeclaration(
                $@"private const {fType.signedWordType} HalfScale = Scale >> 1;");
            
            var fractionMask = SF.ParseMemberDeclaration(
                $@"private const {fType.wordType} FractionMask = unchecked(({fType.wordType}){fractionalBitMask});");
            var integerMask = SF.ParseMemberDeclaration(
                $@"private const {fType.wordType} IntegerMask = unchecked(({fType.wordType}){integerBitMask});");

            var zero = SF.ParseMemberDeclaration(
                $@"public static readonly {fType.name} Zero = new {fType.name}(0);");
            var one = SF.ParseMemberDeclaration(
                $@"public static readonly {fType.name} One = FromInt(1);");

            var epsilon = SF.ParseMemberDeclaration(
                $@"public static readonly {fType.name} Epsilon = new {fType.name}(1);");
            var half = SF.ParseMemberDeclaration(
                $@"private const {fType.doubleWordType} Half = 
                    ({fType.doubleWordType})(Scale > 0 ? (({fType.wordType})1 << (Scale-1)) : (0));");

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
                half);

            // sign mask constant for signed types

            string signBitMask = GenerateSignBitMaskLiteral(fType);
            var signMask = SF.ParseMemberDeclaration(
                $@"private const {fType.wordType} SignMask = unchecked(({fType.wordType}){signBitMask});");
            
            type = type.AddMembers(signMask);

            // Value field, in which number is stored

            var rawValue = SF.ParseMemberDeclaration(
                $@"[FieldOffset(0)] public {fType.wordType} v;");

            type = type.AddMembers(rawValue);

            // Constructors

            var constructor = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public {fType.name}({fType.wordType} x) {{
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
            string wordCast = $@"({fType.wordType})";
            string wordCastOpt = fType.wordLength < 32 ? wordCast : "";

            /* Optional cast-to-wordType instruction needed for code
            that performs arithmetic with small types, with in C#
            always return int for some reason. */
            string doubleWordCast = $@"({fType.doubleWordType})";
            string doubleWordCastOpt = fType.doubleWordLength < 32 ? doubleWordCast : "";

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
                    return new {fType.name}(({fType.wordType})(x << Scale));
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
                        ({fType.wordType})math.round((x * (float)(1 << Scale)))
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
                        ({fType.wordType})math.round((x * (double)(1 << Scale)))
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
                if ((f.v & SignMask) != ({fType.wordType})0) {{
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

            Todo: Simplify the code for generating all mixed-type variants
            */

            var opAddSelf = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} operator +({fType.name} lhs, {fType.name} rhs) {{
                    return new {fType.name}({wordCastOpt}(lhs.v + rhs.v));
                }}");

            var opSubSelf = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} operator -({fType.name} lhs, {fType.name} rhs) {{
                    return new {fType.name}({wordCastOpt}(lhs.v - rhs.v));
                }}");

            var opIncrSelf = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} operator ++({fType.name} lhs) {{
                    return new {fType.name}({wordCastOpt}(lhs.v+1));
                }}");

            var opDecrSelf = SF.ParseMemberDeclaration($@"
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

            You can pre-shift, throwing out some precision, but staying within register limits.
            I chose HalfScale here, but you could >> 4 the inputs, with a final shift at the end

            return new {fType.name}((lhs.v>>HalfScale) * (rhs.v>>HalfScale)); // >> 0
            */

            var opMulSelf = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} operator *({fType.name} lhs, {fType.name} rhs) {{
                    {fType.doubleWordType} lhsLong = lhs.v;
                    {fType.doubleWordType} rhsLong = rhs.v;
                    {fType.doubleWordType} result = {doubleWordCastOpt}((lhsLong * rhsLong) + Half);
                    return new {fType.name}({wordCast}(result >> Scale));
                }}");

            // var opMulSelf = SF.ParseMemberDeclaration($@"
            //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
            //     public static {fType.name} operator *({fType.name} lhs, {fType.name} rhs) {{
            //         return new {fType.name}(({fType.wordType})((lhs.v * rhs.v) >> Scale));
            //     }}");

            /*
            Division

            Here we shift lhs by scale and leave rhs unchanged to cancel
            out scaling effects, using 64 bit accumulator.

            Alternatively:

            Here instead we do dangerous shifting to stay in 32-bit. Works
            for subsets of numbers, I guess. YMMV.
            return new {fType.name}((int)((lhs.v << HalfScale) / (rhs.v >> HalfScale)));
            */

            var opDivSelf = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} operator /({fType.name} lhs, {fType.name} rhs) {{
                    return new {fType.name}(
                        ({fType.wordType})(((({doubleWordCast}(lhs.v) << Scale)) / rhs.v))
                    );
                }}");

            // Todo: greaterThan, smallerThan for all mixed types
            var opGreaterThanSelf = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static bool operator >({fType.name} lhs, {fType.name} rhs) {{
                    return lhs.v > rhs.v;
                }}");

            var opSmallerThanSelf = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static bool operator <({fType.name} lhs, {fType.name} rhs) {{
                    return lhs.v < rhs.v;
                }}");

            type = type.AddMembers(
                opAddSelf,
                opIncrSelf,
                opSubSelf,
                opDecrSelf,
                opMulSelf,
                opDivSelf,
                opGreaterThanSelf,
                opSmallerThanSelf);

            var opAddInt = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} operator +({fType.name} lhs, int rhs) {{
                    return new {fType.name}({wordCastOpt}(lhs.v + {wordCast}(rhs << Scale)));
                }}");

            var opSubInt = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} operator -({fType.name} lhs, int rhs) {{
                    return new {fType.name}({wordCastOpt}(lhs.v - {wordCast}(rhs << Scale)));
                }}");

            var opMulInt = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} operator *({fType.name} lhs, int rhs) {{
                    {fType.doubleWordType} lhsLong = lhs.v;
                    {fType.doubleWordType} rhsLong = {doubleWordCast}rhs;
                    {fType.doubleWordType} result = {doubleWordCastOpt}((lhsLong * rhsLong));
                    return new {fType.name}({wordCast}(result));
                }}");

            var opDivInt = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} operator /({fType.name} lhs, int rhs) {{
                    {fType.doubleWordType} lhsLong = lhs.v;
                    {fType.doubleWordType} rhsLong = {doubleWordCast}rhs;
                    {fType.doubleWordType} result = {doubleWordCastOpt}((lhsLong / rhsLong));
                    return new {fType.name}({wordCast}(result));
                }}");

            var opShiftRight = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} operator >> ({fType.name} lhs, int rhs) {{
                    int half = (1 << (rhs - 1)) * (int)(SignMask & lhs.v);
                    return new {fType.name}({wordCast}((lhs.v + half) >> rhs));
                }}");

            var opShiftLeft = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} operator << ({fType.name} lhs, int rhs) {{
                    return new {fType.name}({wordCastOpt}(lhs.v << rhs));
                }}");

            type = type.AddMembers(
               opAddInt,
               opSubInt,
               opMulInt,
               opDivInt,
               opShiftRight,
               opShiftLeft);

            /*
            Generate mixed-type operators

            Todo:

            Allow the types we're currently filtering out, work
            out the different arithmetic needed to support them.
            */

            for (int i = 0; i < fTypes.Count; i++) {
                
                var rhType = fTypes[i];

                // Add and Sub all other fTypes that have fewer integer bits

                MemberDeclarationSyntax mixedOp;
                if (rhType.name != fType.name && fType.integerBits >= rhType.integerBits) {
                    mixedOp = GenerateAdder(fType, rhType);
                    type = type.AddMembers(mixedOp);

                    mixedOp = GenerateSubber(fType, rhType);
                    type = type.AddMembers(mixedOp);
                }

                // Mul and Div ops

                if (rhType.name != fType.name && fType.fractionalBits >= rhType.fractionalBits) {
                    mixedOp = GenerateMultiplier(fType, rhType);
                    type = type.AddMembers(mixedOp);

                    mixedOp = GenerateDivisor(fType, rhType);
                    type = type.AddMembers(mixedOp);
                }
            }

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