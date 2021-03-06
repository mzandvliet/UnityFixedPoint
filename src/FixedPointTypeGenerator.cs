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

    - Currenly we're initializing pure fraction types
    with a member, One, which has a value of 1-epsilon.
    It'd be better to replace that member with Max, such
    that you realize the difference. The number does not
    behave like One.

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

    /// <summary>Stores information about the fixed point type itself, as well
    /// as several dual and associated types commonly used in its
    /// arithmetic.</summary>
    /// Not in the cleverest way, mind you...
    public class FixedPointType : IEquatable<FixedPointType> {
        public static readonly Dictionary<WordType, Type> DotNetWordTypes = new Dictionary<WordType, Type> {
            { new WordType(WordSize.B8 , WordSign.Unsigned),    typeof(byte) },
            { new WordType(WordSize.B16, WordSign.Unsigned),    typeof(ushort) },
            { new WordType(WordSize.B32, WordSign.Unsigned),    typeof(uint) },
            { new WordType(WordSize.B64, WordSign.Unsigned),    typeof(ulong) },
            { new WordType(WordSize.B8 , WordSign.Signed),      typeof(sbyte) },
            { new WordType(WordSize.B16, WordSign.Signed),      typeof(short) },
            { new WordType(WordSize.B32, WordSign.Signed),      typeof(int) },
            { new WordType(WordSize.B64, WordSign.Signed),      typeof(long) },
        };

        public readonly string name;
        public readonly string signedName;

        public readonly WordType word;
        public readonly WordType doubleWord;
        public readonly WordType unsignedWord;
        public readonly WordType signedWord;

        public readonly Type wordType;
        public readonly Type doubleWordType;
        public readonly Type unsignedWordType;
        public readonly Type signedWordType;

        public string wordTypeName => wordType.Name;
        public string doubleWordTypeName => doubleWordType.Name;
        public string unsignedWordTypeName => unsignedWordType.Name;
        public string signedWordTypeName => signedWordType.Name;

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
            get => (int)doubleWord.Size;
        }

        public FixedPointType(WordType word, int fractionalBits) {
            this.word = word;
            this.fractionalBits = fractionalBits;

            if (integerBits + fractionalBits != wordLength - signBit) {
                throw new ArgumentException(string.Format("Number of integer bits + fractional bits needs to add to {0}", wordLength - signBit));
            }

            doubleWord = new WordType((WordSize)(wordLength * 2), word.Sign);
            unsignedWord = new WordType(word.Size, WordSign.Unsigned);
            signedWord = new WordType(word.Size, WordSign.Signed);

            wordType =              DotNetWordTypes[word];
            doubleWordType =       DotNetWordTypes[doubleWord];
            unsignedWordType = DotNetWordTypes[unsignedWord];
            signedWordType =        DotNetWordTypes[signedWord];

            this.name = GetTypeName(integerBits, fractionalBits, signBit);

            /* 
            Construct reference to a complementary signed type, used to
            return values by the subtraction operator.

            Todo: in case u32 - u32 -> s32, we have possible loss of information
            */
            int signedFTypeIntegerBits = signBit == 1 ?
                integerBits :
                Math.Max(1, integerBits - 1);
            this.signedName = GetTypeName(signedFTypeIntegerBits, wordLength - signedFTypeIntegerBits - 1, 1);
        }

        public static string GetTypeName(int integerBits, int fractionalBits, int signBit) {
            return string.Format("q{0}{1}_{2}", signBit == 1 ? "s" : "u", integerBits, fractionalBits);
        }

        public static bool operator ==(FixedPointType lhs, FixedPointType rhs) {
            return Equals(lhs, rhs);
        }

        public static bool operator !=(FixedPointType lhs, FixedPointType rhs) {
            return !Equals(lhs, rhs);
        }

        public bool Equals(FixedPointType other) {
            return word == other.word && fractionalBits == other.fractionalBits;
        }

        public override bool Equals(object obj) {
            var fType = (FixedPointType)obj;
            if (fType != null) {
                return fType == this;
            }
            return false;
        }

        public override int GetHashCode() {
            return (int)(word.GetHashCode() * 0xEEE2123Bu + fractionalBits * 0x9F3FDC37u);
        }

        public override string ToString() {
            return base.ToString();
        }
    }

    public static class FixedPointTypeGenerator {
        private const string SafetyChecksPreProcessorDefine = "FIXED_POINT_SAFETY_CHECKS";

        public class Options {
            public bool AddSafetyChecks = true;
        }

        /*
            Todo:
            - Find out the difference between .WithX and .AddX
         */
        public static (FixedPointType, SyntaxTree) GenerateType(in FixedPointType fType, in IList<FixedPointType> fTypes, in Options options) {
            string fractionalBitMask =          GenerateFractionalMaskLiteral(fType);
            string integerBitMask =             GenerateFractionalMaskLiteral(fType, "1", "0");
            string multiplyOverflowBitMask =    GenerateOverflowCheckMaskLiteral(fType);

            /*
            Calculate minimum and maximum values in fractional representation
            Note: fType.integerBits already has 1 less in case of sign bit, see above.
             */
            double rangeMinDoubleValue = 0f;
            double rangeMaxDoubleValue = 0f;
            double epsilonDoubleValue = 1.0 / Math.Pow(2, fType.fractionalBits);
            if (fType.signBit == 0) {
                rangeMinDoubleValue = 0f;
                rangeMaxDoubleValue = Math.Pow(2, fType.integerBits) - Math.Pow(2, -fType.fractionalBits);
            } else {
                rangeMinDoubleValue = -Math.Pow(2, fType.integerBits);
                rangeMaxDoubleValue = Math.Pow(2, fType.integerBits) - Math.Pow(2, -fType.fractionalBits);
            }
            float rangeMinFloatValue = (float)rangeMinDoubleValue;
            float rangeMaxFloatValue = (float)rangeMaxDoubleValue;
            int rangeMinIntValue = (int)Math.Floor(rangeMinDoubleValue);
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

            var nameSpace = SF.NamespaceDeclaration(SF.ParseName("Ramjet.Mathematics.FixedPoint"));

            var type = SF.StructDeclaration(fType.name)
                .AddModifiers(SF.Token(SK.PublicKeyword))
                .WithAttributeLists(Utils.GenerateStructLayoutAttributes())
                .WithBaseList(Utils.ImplementIEquatable(fType.name));

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
                $@"public const {fType.signedWordTypeName} Scale = {fType.fractionalBits};");
            var halfScale = SF.ParseMemberDeclaration(
                $@"private const {fType.signedWordTypeName} HalfScale = Scale >> 1;");
            
            var fractionMask = SF.ParseMemberDeclaration(
                $@"private const {fType.wordTypeName} FractionMask = unchecked(({fType.wordTypeName}){fractionalBitMask});");
            var fractionMaskU = SF.ParseMemberDeclaration(
                $@"private const {fType.unsignedWordTypeName} FractionMaskU = unchecked(({fType.unsignedWordTypeName}){fractionalBitMask});");
            var integerMask = SF.ParseMemberDeclaration(
                $@"private const {fType.wordTypeName} IntegerMask = unchecked(({fType.wordTypeName}){integerBitMask});");
            var multiplyOverflowMask = SF.ParseMemberDeclaration(
                $@"private const {fType.doubleWordType} MulOverflowMask = unchecked(({fType.doubleWordType}){multiplyOverflowBitMask});");

            var zero = SF.ParseMemberDeclaration(
                $@"public static readonly {fType.name} Zero = Raw(0);");


            long oneValue = (long)Math.Min(Math.Pow(2, fType.fractionalBits), Math.Pow(2, fType.wordLength - fType.signBit) - 1);
            var one = SF.ParseMemberDeclaration(
                $@"public static readonly {fType.name} One = Raw(({fType.wordTypeName}){oneValue});");

            var epsilon = SF.ParseMemberDeclaration(
                $@"public static readonly {fType.name} Epsilon = Raw(1);");
            var half = SF.ParseMemberDeclaration(
                $@"private const {fType.doubleWordTypeName} Half = 
                    ({fType.doubleWordTypeName})(Scale > 0 ? (({fType.wordTypeName})1 << (Scale-1)) : (0));");

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
                fractionMaskU,
                integerMask,
                multiplyOverflowMask,
                zero,
                one,
                epsilon,
                half);

            // sign mask constant for signed types

            string signBitMask = GenerateSignBitMaskLiteral(fType);
            var signMask = SF.ParseMemberDeclaration(
                $@"private const {fType.wordTypeName} SignMask = unchecked(({fType.wordTypeName}){signBitMask});");
            
            type = type.AddMembers(signMask);

            // Value field, in which number is stored

            var rawValue = SF.ParseMemberDeclaration(
                $@"[FieldOffset(0)] public {fType.wordTypeName} v;");

            type = type.AddMembers(rawValue);

            /* === Helpers === */

            var intRangeCheckOpt = "";
            if (options.AddSafetyChecks) {
                intRangeCheckOpt = GenerateRangeCheck(
                    fType,
                    "x",
                    GetFieldIdentifier(rangeMinInt),
                    GetFieldIdentifier(rangeMaxInt));
    }

            var floatRangeCheckOpt = "";
            if (options.AddSafetyChecks) {
                floatRangeCheckOpt = GenerateRangeCheck(
                    fType,
                    "x",
                    GetFieldIdentifier(rangeMinFloat),
                    GetFieldIdentifier(rangeMaxFloat));
            }

            var doubleRangeCheckOpt = "";
            if (options.AddSafetyChecks) {
                doubleRangeCheckOpt = GenerateRangeCheck(
                    fType,
                    "x",
                    GetFieldIdentifier(rangeMinDouble),
                    GetFieldIdentifier(rangeMaxDouble));
            }

            /* Optional cast-to-int instruction needed for methods on
            unsigned types that return them as signed. */
            string intCastOpt = fType.signBit == 0 ? "(int)" : "";

            /* Optional cast-to-wordType instruction needed for code
            that performs arithmetic with small types, with in C#
            always return int for some reason. */
            string wordCast = $@"({fType.wordTypeName})";
            string wordCastOpt = fType.wordLength < 32 ? wordCast : "";

            /* Optional cast-to-wordType instruction needed for code
            that performs arithmetic with small types, with in C#
            always return int for some reason. */
            string doubleWordCast = $@"({fType.doubleWordTypeName})";
            string doubleWordCastOpt = fType.doubleWordLength < 32 ? doubleWordCast : "";

            // Constructors

            var fromRaw = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} Raw({fType.wordTypeName} x) {{
                    return new {fType.name}() {{
                        v = x
                    }};
                }}");

            var constructorInt = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public {fType.name}(int x) {{
                    {intRangeCheckOpt}
                    v = ({fType.wordTypeName})(x << Scale);
                }}");
            var constructorFloat = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public {fType.name}(float x) {{
                    {floatRangeCheckOpt}
                    v = ({fType.wordTypeName})math.round(x * (float)(1 << Scale));
                }}");
            var constructorDouble = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public {fType.name}(double x) {{
                    {doubleRangeCheckOpt}
                    v = ({fType.wordTypeName})math.round(x * (double)(1 << Scale));
                }}");

            type = type.AddMembers(
                fromRaw,
                constructorInt,
                constructorFloat,
                constructorDouble);

            // === Methods ===
            
            var fromIntCast = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static implicit operator {fType.name} (int x) {{
                    {intRangeCheckOpt}
                    return Raw(({fType.wordTypeName})(x << Scale));
                }}");

            var toIntCast = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static implicit operator int ({fType.name} x) {{
                    return {intCastOpt}(x.v >> Scale);
                }}");

            var fromFloatCast = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static implicit operator {fType.name} (float x) {{
                    {floatRangeCheckOpt}
                    return Raw(
                        ({fType.wordTypeName})math.round((x * (float)(1 << Scale)))
                    );
                }}");

            var toFloatCast = SF.ParseMemberDeclaration($@"
                 [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static implicit operator float ({fType.name} x) {{
                    return x.v / (float)((1 << Scale));
                }}");
         
            var fromDoubleCast = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static implicit operator {fType.name} (double x) {{
                    {floatRangeCheckOpt}
                    return Raw(
                        ({fType.wordTypeName})math.round((x * (double)(1 << Scale)))
                    );
                }}");

            var toDoubleCast = SF.ParseMemberDeclaration($@"
                 [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static implicit operator double ({fType.name} x) {{
                    return x.v / (double)((1 << Scale));
                }}");

            type = type.AddMembers(
                fromIntCast,
                toIntCast,
                fromFloatCast,
                toFloatCast,
                fromDoubleCast,
                toDoubleCast);

            /*
            Frac

            There's multiple ways to implement Frac.

            Here we use a masking strategy, taking
            two's complement into account.

            Could also use double bit shifting like so:
            return Raw((f.v << (Scale-1)) >> (Scale-1));
            */
            var fractNegativePath = "";
            if (fType.signBit == 1) {
                // Code path for signed types, in case sign bit is set
                fractNegativePath = $@"
                if ((f.v & SignMask) != ({fType.wordTypeName})0) {{
                    return Raw({wordCastOpt}((f.v & FractionMask) | IntegerMask));
                }}";
            }
            var frac = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} Frac({fType.name} f) {{
                    {fractNegativePath}
                    return Raw({wordCastOpt}(f.v & FractionMask));
                }}");

            /*
            Whole

            Two's complement automatically handled properly
            here, because most significant bits are preserved.
            */
            var whole = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} Whole({fType.name} f) {{
                    return Raw({wordCastOpt}(f.v & IntegerMask));
                }}");

            type = type.AddMembers(
                frac,
                whole
            );

            /*
            === Operator overloading ===
            
            Note: we construct the result by new struct(), which is quite slow.

            Todo:
            - Simplify the code for generating all mixed-type variants
            - Figure out how best to handle unsigned cases becoming signed
                - Example: u32 - u32 -> s32; the unsigneds are not closed
                under subtraction
            */

            var opAddSelf = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} operator +({fType.name} lhs, {fType.name} rhs) {{
                    return Raw({wordCastOpt}(lhs.v + rhs.v));
                }}");

            var opSubSelf = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.signedName} operator -({fType.name} lhs, {fType.name} rhs) {{
                    return {fType.signedName}.Raw(({fType.signedWordTypeName})(lhs.v - rhs.v));
                }}");

            var opIncrSelf = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} operator ++({fType.name} lhs) {{
                    return Raw({wordCastOpt}(lhs.v+1));
                }}");

            var opDecrSelf = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} operator --({fType.name} lhs) {{
                    return Raw({wordCastOpt}(lhs.v - 1));
                }}");

            /*
            Multiplication

            This works, but it has three issues:

            - Is not as accurate as it can be!
                - Rounding technique is wrong?
            - Can overflow, despite using the double-word accumulator
            - Because of mixed word-size arithmetic, Burst/LLVM do not
            want to vectorize loops that feature multiplies

            We can continue as before, but we have to build in debug-mode
            overflow checking.

            Todo: Sign-aware overflow checking

            Keeping everything 100% overflow-safe, while also working
            exclusively in WordLength domain, is actually really tricky.

            Can we leverage modular or hierarchical number spaces to
            absorb ballooning scale, while keeping code paths linear
            in wordlength?
            */

            var opMulSelfOverflowCheck = "";
            if (options.AddSafetyChecks) {
                var absResult = fType.signBit == 1 ? "Math.Abs(result)" : "result";
                opMulSelfOverflowCheck = $@"
                #if {SafetyChecksPreProcessorDefine}
                if (({absResult} & MulOverflowMask) > 0) {{
                    Debug.LogErrorFormat(""{fType.name} multiplication of {{0}} * {{1}} overflowed!"", lhs, rhs);
                }}
                #endif";
            }

            var signResultOpt = "";
            if (fType.signBit == 1) {
                signResultOpt = $@"
                var signResult = ((lhs.v ^ rhs.v) & SignMask) != 0 ? -1 : 1;
                ";
            }
            var signResultMultiplyOpt = "";
            if (fType.signBit == 1) {
                signResultMultiplyOpt = $@"
                * signResult
                ";
            }
            var opMulSelf = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} operator *({fType.name} lhs, {fType.name} rhs) {{
                    var lhsAbs = ({fType.unsignedWordTypeName})Math.Abs(lhs.v);
                    var rhsAbs = ({fType.unsignedWordTypeName})Math.Abs(rhs.v);
                    var lhslo = lhsAbs & FractionMaskU;
                    var lhshi = lhsAbs >> Scale;
                    var rhslo = rhsAbs & FractionMaskU;
                    var rhshi = rhsAbs >> Scale;
                    var lo_lo = lhslo * rhslo;
                    var lo_hi_a = lhslo * rhshi;
                    var lo_hi_b = rhslo * lhshi;
                    var hi_hi = lhshi * rhshi;

                    {signResultOpt}

                    return Raw(
                        ({fType.wordTypeName})(
                            ({fType.signedWordTypeName})((lo_lo >> Scale) +
                            lo_hi_a + lo_hi_b +
                            (hi_hi << Scale))
                            {signResultMultiplyOpt}
                        )
                    );
                }}");

            // Double-Word Length implementation of multiplication, which is much simpler

            // var opMulSelfOverflowCheck = "";
            // if (options.AddSafetyChecks) {
            //     var absResult = fType.signBit == 1 ? "Math.Abs(result)" : "result";
            //     opMulSelfOverflowCheck = $@"
            //     #if {SafetyChecksPreProcessorDefine}
            //     if (({absResult} & MulOverflowMask) > 0) {{
            //         Debug.LogErrorFormat(""{fType.name} multiplication of {{0}} * {{1}} overflowed!"", lhs, rhs);
            //     }}
            //     #endif";
            // }
            // var opMulSelf = SF.ParseMemberDeclaration($@"
            //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
            //     public static {fType.name} operator *({fType.name} lhs, {fType.name} rhs) {{
            //         {fType.doubleWordTypeName} lhsLong = lhs.v;
            //         {fType.doubleWordTypeName} rhsLong = rhs.v;
            //         {fType.doubleWordTypeName} result = {doubleWordCastOpt}((lhsLong * rhsLong) + Half);
            //         {opMulSelfOverflowCheck}
            //         return Raw({wordCast}(result >> Scale));
            //     }}");

            /*
            Division

            Here we shift lhs by scale and leave rhs unchanged to cancel
            out scaling effects, using 64 bit accumulator.

            Alternatively:

            Here instead we do dangerous shifting to stay in 32-bit. Works
            for subsets of numbers, I guess. YMMV.
            return Raw((int)((lhs.v << HalfScale) / (rhs.v >> HalfScale)));
            */

            var opDivSelf = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} operator /({fType.name} lhs, {fType.name} rhs) {{
                    return Raw(
                        ({fType.wordTypeName})(((({doubleWordCast}(lhs.v) << Scale)) / rhs.v))
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

            /*
            Todo: if backing type is unsigned, disallow some ops
            with signed numbers, or generate runtime error plus
            compile-time warnings when sign handling could go bad.

             */

            var opAddInt = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} operator +({fType.name} lhs, int rhs) {{
                    return Raw({wordCastOpt}(lhs.v + {wordCast}(rhs << Scale)));
                }}");

            var opSubInt = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} operator -({fType.name} lhs, int rhs) {{
                    return Raw({wordCastOpt}(lhs.v - {wordCast}(rhs << Scale)));
                }}");

            var opMulInt = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} operator *({fType.name} lhs, int rhs) {{
                    {fType.doubleWordTypeName} lhsLong = lhs.v;
                    {fType.doubleWordTypeName} rhsLong = {doubleWordCast}rhs;
                    {fType.doubleWordTypeName} result = {doubleWordCastOpt}((lhsLong * rhsLong));
                    return Raw({wordCast}(result));
                }}");

            var opDivInt = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} operator /({fType.name} lhs, int rhs) {{
                    return Raw({wordCast}((lhs.v / rhs)));
                }}");

            var opShiftRight = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} operator >> ({fType.name} lhs, int rhs) {{
                    return Raw({wordCast}(lhs.v >> rhs));
                }}");

            var opShiftLeft = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} operator << ({fType.name} lhs, int rhs) {{
                    return Raw({wordCastOpt}(lhs.v << rhs));
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
                    /*
                        Bug: we're not yet rounding properly in some cases, such as
                        qs3_4 operator +(qs3_4 lhs, qs1_6 rhs)
                        where we can yield a balanced result by doing
                        return new qs3_4((sbyte)(lhs.v + (sbyte)((rhs.v + (1 << 1)) >> 2)));
                     */
                    mixedOp = GenerateAddOperator(fType, rhType);
                    type = type.AddMembers(mixedOp);

                    mixedOp = GenerateSubtractOperator(fType, rhType);
                    type = type.AddMembers(mixedOp);
                }

                // Mul and Div ops

                if (rhType.name != fType.name && fType.fractionalBits >= rhType.fractionalBits) {
                    mixedOp = GenerateMultiplyOperator(fType, rhType);
                    type = type.AddMembers(mixedOp);

                    mixedOp = GenerateDivideOperator(fType, rhType);
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
                    return string.Format(""{fType.name}({{0}})"", (double)this);
                }}");

            var toStringFormat = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public string ToString(string format, IFormatProvider formatProvider) {{
                    return string.Format(""{fType.name}({{0}})"", (double)this);
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

        // private static string GenerateOverflowCheckMaskLiteral(in FixedPointType fType) {
        //     var maskBuilder = new StringBuilder();

        //     maskBuilder.Append("0b");
        //     maskBuilder.Append(fType.signBit == 1 ? "0" : "1");
        //     for (int i = 1; i < fType.wordLength - fType.fractionalBits; i++) {
        //         if (i > 0 && i % 4 == 0) {
        //             maskBuilder.Append("_");
        //         }
        //         maskBuilder.Append("1");
        //     }
        //     for (int i = fType.wordLength - fType.fractionalBits; i < fType.doubleWordLength; i++) {
        //         if (i > 0 && i % 4 == 0) {
        //             maskBuilder.Append("_");
        //         }
        //         maskBuilder.Append("0");
        //     }

        //     return maskBuilder.ToString();
        // }

        private static string GenerateOverflowCheckMaskLiteral(in FixedPointType fType) {
            var maskBuilder = new StringBuilder();

            maskBuilder.Append("0b");
            maskBuilder.Append(fType.signBit == 1 ? "0" : "1");
            for (int i = 1; i < fType.wordLength - fType.fractionalBits; i++) {
                if (i > 0 && i % 4 == 0) {
                    maskBuilder.Append("_");
                }
                maskBuilder.Append("1");
            }
            for (int i = fType.wordLength - fType.fractionalBits; i < fType.doubleWordLength; i++) {
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

            maskBuilder.Append("0b");
            for (int i = 0; i <= fType.integerBits; i++) {
                if (i > 0 && i % 4 == 0) {
                    maskBuilder.Append("_");
                }
                maskBuilder.Append(intBitChar);
            }
            for (int i = fType.integerBits+1; i < wordLength; i++) {
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
                    #if {SafetyChecksPreProcessorDefine}
                    throw new System.ArgumentException(string.Format(
                        ""value {{0}} lies outside of representable range [{{1}} , {{2}}] for {fType.name}"",
                        {variableName},
                        {minName.ToString()},
                        {maxName.ToString()}));
                    #endif
                }}";
        }

        private static MemberDeclarationSyntax GenerateAddOperator(FixedPointType lhType, FixedPointType rhType) {
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

            string wordCastOpt = lhType.word.Size == WordSize.B32 ? "" : $@"({lhType.wordTypeName})";

            var op = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {lhType.name} operator +({lhType.name} lhs, {rhType.name} rhs) {{
                    return Raw({wordCastOpt}(lhs.v + ({lhType.wordTypeName})(rhs.v {shiftOp} {shiftAmount})));
                }}");

            return op;
        }

        private static MemberDeclarationSyntax GenerateSubtractOperator(FixedPointType lhType, FixedPointType rhType) {
            int signedShift = lhType.fractionalBits - rhType.fractionalBits;
            string shiftOp = signedShift >= 0 ? "<<" : ">>";
            int shiftAmount = Math.Abs(signedShift);

            string wordCastOpt = lhType.word.Size == WordSize.B32 ? "" : $@"({lhType.wordTypeName})";

            var op = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {lhType.name} operator -({lhType.name} lhs, {rhType.name} rhs) {{
                    return Raw({wordCastOpt}(lhs.v - ({lhType.wordTypeName})(rhs.v {shiftOp} {shiftAmount})));
                }}");

            return op;
        }

        /*
        Generates a multiplier that returns the type of the left-hand-side argument.
         */
        private static MemberDeclarationSyntax GenerateMultiplyOperator(FixedPointType lhType, FixedPointType rhType) {
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
            string half = $@"const {lhType.doubleWordTypeName} half = 
                    ({lhType.doubleWordTypeName})(({lhType.wordTypeName})1 << {halfShiftBits});";

            var op = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {lhType.name} operator *({lhType.name} lhs, {rhType.name} rhs) {{
                    {half}

                    {lhType.doubleWordTypeName} lhsLong = lhs.v;
                    {lhType.doubleWordTypeName} rhsLong = ({lhType.doubleWordTypeName})rhs.v;
                    {lhType.doubleWordTypeName} result = ({lhType.doubleWordTypeName})((lhsLong * rhsLong) + half);
                    return Raw(({lhType.wordTypeName})(result >> {rhType.fractionalBits}));
                }}");

            return op;
        }

        private static MemberDeclarationSyntax GenerateDivideOperator(FixedPointType lhType, FixedPointType rhType) {
            /*
            Todo: Not performing any rounding with half tricks here yet,
            need to work out what the right thing is to do.

            Also, we have half as an int here, when 32-bits or less.
            This is because it is applied immediately after a shift, which returns Int32
            in those cases
            */

            string halfType = lhType.wordLength <= 32 ? "int" : $@"{lhType.doubleWordTypeName}";
            int halfShiftBits = Math.Max(0, rhType.fractionalBits - 1);
            string half = $@"const {halfType} half = (1 << {halfShiftBits});";

            var op = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {lhType.name} operator /({lhType.name} lhs, {rhType.name} rhs) {{
                    {half}

                    {lhType.doubleWordTypeName} lhsLong = ({lhType.doubleWordTypeName})((lhs.v << {rhType.fractionalBits}) + half);
                    {lhType.doubleWordTypeName} rhsLong = ({lhType.doubleWordTypeName})rhs.v;
                    return Raw(({lhType.wordTypeName})(lhsLong / rhsLong));
                }}");

            return op;
        }
    }
}