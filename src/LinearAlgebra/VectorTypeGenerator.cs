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
 
    - Add intialization by raw int value

    - Add additional backing types:
        - unsigned
        - byte
        - short
        - long
        - longlong
        - freakier multi-word and union types

    - mixed precision arithmetic

    e.g. you can freely add the following to a vec2_q24_7:
        - vec2_q0_7
        - vec2_q8_7
        - vec2_q24_7

    - Support float and double as scalar type
 */

namespace CodeGeneration {
    public static class VectorTypeGenerator {
        private static readonly string[] CoefficientNames = new string[] {
            "x",
            "y",
            "z",
            "w"
        };

        public static (FixedPointType, SyntaxTree) GenerateType(FixedPointType fType, in int numDimensions) {
            if (numDimensions <= 0 || numDimensions > 4) {
                throw new ArgumentException("Vector types currently only support 1-4 dimensions");
            }

            string typeName = string.Format("vec{0}_{1}", numDimensions, fType.name);
            string signedTypeName = string.Format("vec{0}_{1}", numDimensions, fType.signedName);

            var usingStrings = new List<string> {
                "System",
                "System.Runtime.CompilerServices",
                "System.Runtime.InteropServices",
                "UnityEngine",
                "Unity.Mathematics",
                "Ramjet.Mathematics.FixedPoint"
            };

            var usings = new SyntaxList<UsingDirectiveSyntax>(
                from s in usingStrings select SF.UsingDirective(SF.ParseName(s)));

            var unit = SF.CompilationUnit()
                .WithUsings(usings);

            var nameSpace = SF.NamespaceDeclaration(SF.ParseName("Ramjet.Mathematics.LinearAlgebra"));

            var type = SF.StructDeclaration(typeName)
                .AddModifiers(SF.Token(SK.PublicKeyword))
                .WithAttributeLists(Utils.GenerateStructLayoutAttributes())
                .WithBaseList(Utils.ImplementIEquatable(typeName));

            var coefficientFields = new List<MemberDeclarationSyntax>();
            var fieldSizeBytes = fType.wordLength / 8;

            int fieldOffset = 0;
            for (int i = 0; i < numDimensions; i++) {
                var coeff = SF.ParseMemberDeclaration($@"[FieldOffset({fieldOffset})] public {fType.name} {CoefficientNames[i]};");
                coefficientFields.Add(coeff);
                fieldOffset += fieldSizeBytes;
            }

            type = type.AddMembers(coefficientFields.ToArray());

            var zero = SF.ParseMemberDeclaration($@"public static readonly {typeName} Zero = {typeName}.FromInt(0);");

            var rawArgs = coefficientFields.Select((coeff, index) => $@"{fType.wordType} {CoefficientNames[index]}").Aggregate((a, b) => a + ", " + b);
            var rawAssignments = coefficientFields.Select((coeff, index) => $@"{CoefficientNames[index]} = {fType.name}.Raw({CoefficientNames[index]})").Aggregate((a, b) => a + ",\n" + b);
            var fromRaw = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {typeName} Raw({rawArgs}) {{
                    return new {typeName} {{
                        {rawAssignments}
                    }};
                }}");

            var constructorArgs = coefficientFields.Select((coeff, index) => $@"{fType.name} {CoefficientNames[index]}").Aggregate((a, b) => a + ", " + b);
            var constructorAssignments = coefficientFields.Select((coeff, index) => $@"this.{CoefficientNames[index]} = {CoefficientNames[index]}").Aggregate((a, b) => a + ";\n" + b) + ";";
            var constructor = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public {typeName}({constructorArgs}) {{
                    {constructorAssignments}
                }}");

            type = type.AddMembers(
                fromRaw,
                constructor);

            // var toInt2 = SF.ParseMemberDeclaration($@"
            //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
            //     public static int2 ToInt({typeName} f) {{
            //         return new int2(
            //             {scalarTypeName}.ToInt(f.r),
            //             {scalarTypeName}.ToInt(f.i));
            //     }}");

            var opAddInstructions = coefficientFields.Select((coeff, index) => $@"lhs.{CoefficientNames[index]} + rhs.{CoefficientNames[index]}")
                .Aggregate((a, b) => a + ", \n" + b);
            var opAdd = SF.ParseMemberDeclaration($@"
               [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {typeName} operator +({typeName} lhs, {typeName} rhs) {{
                    return new {typeName}(
                        {opAddInstructions}
                    );
                }}");

            var opSubInstructions = coefficientFields.Select((coeff, index) => $@"lhs.{CoefficientNames[index]} - rhs.{CoefficientNames[index]}")
                .Aggregate((a, b) => a + ", \n" + b);
            var opSub = SF.ParseMemberDeclaration($@"
               [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {signedTypeName} operator -({typeName} lhs, {typeName} rhs) {{
                    return new {signedTypeName}(
                        {opSubInstructions}
                    );
                }}");

            var opMulScalarRightInstructions = coefficientFields.Select((coeff, index) => $@"lhs.{CoefficientNames[index]} * rhs")
                .Aggregate((a, b) => a + ", \n" + b);
            var opMulScalarRight = SF.ParseMemberDeclaration($@"
               [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {typeName} operator *({typeName} lhs, {fType.name} rhs) {{
                    return new {typeName}(
                        {opMulScalarRightInstructions}
                    );
                }}");

            var opDivScalarRightInstructions = coefficientFields.Select((coeff, index) => $@"lhs.{CoefficientNames[index]} / rhs")
                .Aggregate((a, b) => a + ", \n" + b);
            var opDivScalarRight = SF.ParseMemberDeclaration($@"
               [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {typeName} operator /({typeName} lhs, {fType.name} rhs) {{
                    return new {typeName}(
                        {opDivScalarRightInstructions}
                    );
                }}");

            type = type.AddMembers(
                opAdd,
                opSub,
                opMulScalarRight,
                opDivScalarRight);

            var opMulIntRightInstructions = coefficientFields.Select((coeff, index) => $@"lhs.{CoefficientNames[index]} * rhs")
                .Aggregate((a, b) => a + ", \n" + b);
            var opMulIntRight = SF.ParseMemberDeclaration($@"
               [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {typeName} operator *({typeName} lhs, int rhs) {{
                    return new {typeName}(
                        {opMulScalarRightInstructions}
                    );
                }}");

            var opDivIntRightInstructions = coefficientFields.Select((coeff, index) => $@"lhs.{CoefficientNames[index]} / rhs")
                .Aggregate((a, b) => a + ", \n" + b);
            var opDivIntRight = SF.ParseMemberDeclaration($@"
               [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {typeName} operator /({typeName} lhs, int rhs) {{
                    return new {typeName}(
                        {opDivScalarRightInstructions}
                    );
                }}");

            type = type.AddMembers(
                opMulIntRight,
                opDivIntRight);

            /*
            Todo:
            For these operations, you probably want to directly calculate using
            the underlying int values, then return the result as new qn_m(intValue)

            Example:

            long result =
                (long)lhs.x.v * rhs.x.v +
                (long)lhs.y.v * rhs.y.v +
                (long)lhs.z.v * rhs.z.v +
                (long)lhs.w.v * rhs.w.v +
                4 * qs15_16.HalfEpsilon;

            return new qs15_16((int)(result >> qs15_16.Scale));

            If we could get Burst to turn this into vectorized MAD,
            that'd be perfect.
            */

            var dotInstructions = coefficientFields.Select((coeff, index) => $@"lhs.{CoefficientNames[index]} * rhs.{CoefficientNames[index]}")
                .Aggregate((a, b) => a + " +\n" + b);
            var dotProduct = SF.ParseMemberDeclaration($@"
               [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} dot({typeName} lhs, {typeName} rhs) {{
                    return {dotInstructions};
                }}");

            var lengthSqInstructions = coefficientFields.Select((coeff, index) => $@"lhs.{CoefficientNames[index]} * lhs.{CoefficientNames[index]}")
                .Aggregate((a, b) => a + " +\n" + b);
            var lengthSq = SF.ParseMemberDeclaration($@"
               [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {fType.name} lengthsq({typeName} lhs) {{
                    return {lengthSqInstructions};
                }}");

            var outerProdInstructions = new StringBuilder();
            int dimIdx = 1;
            for (int i = 0; i < numDimensions; i++) {
                string line = $@"lhs.{CoefficientNames[(i) % numDimensions]} * rhs.{CoefficientNames[(i+1) % numDimensions]} - lhs.{CoefficientNames[(i+1) % numDimensions]} * rhs.{CoefficientNames[(i) % numDimensions]}";
                if (i < numDimensions-1) {
                    line += ", ";
                }
                outerProdInstructions.AppendLine(line);
                dimIdx++;
            } 
            var outerProduct = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {signedTypeName} cross({typeName} lhs, {typeName} rhs) {{
                    return new {signedTypeName}(
                        {outerProdInstructions}
                    );
                }}");

            type = type.AddMembers(
                dotProduct,
                lengthSq,
                outerProduct);

            /* Equality */

            var eqInstructions = coefficientFields.Select((coeff, index) => $@"this.{CoefficientNames[index]} == rhs.{CoefficientNames[index]}")
                .Aggregate((a, b) => a + " &&\n" + b);
            var equals = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public bool Equals({typeName} rhs) {{
                    return {eqInstructions};
                }}");

            var equalsObj = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public override bool Equals(object o) {{
                    if (!(o is {typeName})) {{
                        return false;
                    }}
                    return Equals(({typeName})o);
                }}");

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

            /*
            GetHashCode

            Todo: None of these make particular sense for each type yet...
            */
            var getHashCodeInstructions = coefficientFields.Select((coeff, index) => $@"this.{CoefficientNames[index]}.v")
               .Aggregate((a, b) => a + " ^\n" + b);
            var getHashCode = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public override int GetHashCode() {{
                    return (int)({getHashCodeInstructions});
                }}");

            var toStringReplaceList = coefficientFields.Select((coeff, index) => $@"{{{index}:0.000}}")
               .Aggregate((a, b) => a + ", " + b);
            var toStringCoeffs = coefficientFields.Select((coeff, index) => $@"(float)this.{CoefficientNames[index]}")
               .Aggregate((a, b) => a + ", " + b);
            var toString = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public override string ToString() {{
                    return string.Format(""{typeName}({toStringReplaceList})"", {toStringCoeffs});
                }}");

            var toStringFormat = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public string ToString(string format, IFormatProvider formatProvider) {{
                    return ToString();
                }}");

            type = type.AddMembers(
                getHashCode,
                toString,
                toStringFormat);

            nameSpace = nameSpace.AddMembers(type);
            unit = unit.AddMembers(nameSpace);

            return (fType, CSharpSyntaxTree.Create(unit));
        }
    }
}