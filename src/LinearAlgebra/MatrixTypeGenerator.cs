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

    - Write a mat_2x2, and other specialized cases

    3x3 might also be handy, as 2x2 homogeneous
    You can generalize over fixed point type and all that.

    Problem:

    - mixed precision arithmetic in a single matrix

    2d homogeneous transform matrix is a 3x3 matrix, which
    can do both rotation, scaling, and translation.

    Translation numbers will typically involve large integer
    parts. But rotation with uniform scale (very common case)
    will typically involve only fractional numbers.

    This presents us with the following idea: We could have
    this homogeneous matrix with different fixed point number
    types for the various coefficients, depending on what
    they actually do.

 */

namespace CodeGeneration {
    public static class MatrixTypeGenerator {
        public static (FixedPointType, SyntaxTree) Generate2x2Type(FixedPointType fType) {
            // string dimsName = shape.Select(a => a.ToString()).Aggregate((a, b) => a + "x" + b); // this would be for general tensors...
            string dimsName = string.Format("2x2");
            string typeName = string.Format("mat{0}_{1}", dimsName, fType.name);

            var usingStrings = new List<string> {
                "System",
                "System.Runtime.CompilerServices",
                "System.Runtime.InteropServices",
                "UnityEngine",
                "Unity.Mathematics",
                "Ramjet.Mathematics.FixedPoint",
            };

            var usings = new SyntaxList<UsingDirectiveSyntax>(
                from s in usingStrings select SF.UsingDirective(SF.ParseName(s)));

            var unit = SF.CompilationUnit().WithUsings(usings);

            var nameSpace = SF.NamespaceDeclaration(SF.ParseName("Ramjet.Mathematics.LinearAlgebra"));

            var type = SF.StructDeclaration(typeName)
                .AddModifiers(SF.Token(SK.PublicKeyword))
                .WithAttributeLists(Utils.GenerateStructLayoutAttributes());

            var fields = new List<MemberDeclarationSyntax>();

            var rowTypeName = $@"vec2_{fType.name}";
            var rowTypeSizeBytes = (fType.wordLength * 2) / 8;

            int fieldOffset = 0;
            for (int i = 0; i < 2; i++) {
                var row = SF.ParseMemberDeclaration($@"[FieldOffset({fieldOffset})] public {fType.name} c{i};");
                fields.Add(row);
                fieldOffset += rowTypeSizeBytes;
            }

            type = type.AddMembers(fields.ToArray());

            // var zero = SF.ParseMemberDeclaration($@"public static readonly {typeName} Zero = {typeName}.FromInt(0);");

            // var constructorArgs = fields.Select((coeff, index) => $@"{fType.name} {CoefficientNames[index]}").Aggregate((a, b) => a + ", " + b);
            // var constructorAssignments = fields.Select((coeff, index) => $@"this.{CoefficientNames[index]} = {CoefficientNames[index]}").Aggregate((a, b) => a + ";\n" + b) + ";";

            // var constructor = SF.ParseMemberDeclaration($@"
            //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
            //     public {typeName}({constructorArgs}) {{
            //         {constructorAssignments}
            //     }}");

            // type = type.AddMembers(
            //     constructor);

            // var fromIntArgs = fields.Select((coeff, index) => $@"int {CoefficientNames[index]}").Aggregate((a, b) => a + ", " + b);
            // var fromIntAssignments = fields.Select((coeff, index) => $@"{fType.name}.FromInt({CoefficientNames[index]})").Aggregate((a, b) => a + ",\n" + b);
            // var fromInt = SF.ParseMemberDeclaration($@"
            //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
            //     public static {typeName} FromInt({fromIntArgs}) {{
            //         return new {typeName}(
            //             {fromIntAssignments}
            //         );
            //     }}");

            // var fromFloatArgs = fields.Select((coeff, index) => $@"float {CoefficientNames[index]}").Aggregate((a, b) => a + ", " + b);
            // var fromFloatAssignments = fields.Select((coeff, index) => $@"{fType.name}.FromFloat({CoefficientNames[index]})").Aggregate((a, b) => a + ",\n" + b);
            // var fromFloat = SF.ParseMemberDeclaration($@"
            //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
            //     public static {typeName} FromFloat({fromFloatArgs}) {{
            //         return new {typeName}(
            //             {fromFloatAssignments}
            //         );
            //     }}");

            // var fromDoubleArgs = fields.Select((coeff, index) => $@"double {CoefficientNames[index]}").Aggregate((a, b) => a + ", " + b);
            // var fromDoubleAssignments = fields.Select((coeff, index) => $@"{fType.name}.FromDouble({CoefficientNames[index]})").Aggregate((a, b) => a + ",\n" + b);
            // var fromDouble = SF.ParseMemberDeclaration($@"
            //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
            //     public static {typeName} FromFloat({fromDoubleArgs}) {{
            //         return new {typeName}(
            //             {fromDoubleAssignments}
            //         );
            //     }}");

            // type = type.AddMembers(
            //     fromInt,
            //     fromFloat,
            //     fromDouble
            // );

            // var opAddInstructions = fields.Select((coeff, index) => $@"lhs.{CoefficientNames[index]} + rhs.{CoefficientNames[index]}")
            //     .Aggregate((a, b) => a + ", \n" + b);
            // var opAdd = SF.ParseMemberDeclaration($@"
            //    [MethodImpl(MethodImplOptions.AggressiveInlining)]
            //     public static {typeName} operator +({typeName} lhs, {typeName} rhs) {{
            //         return new {typeName}(
            //             {opAddInstructions}
            //         );
            //     }}");

            // var opSubInstructions = fields.Select((coeff, index) => $@"lhs.{CoefficientNames[index]} - rhs.{CoefficientNames[index]}")
            //     .Aggregate((a, b) => a + ", \n" + b);
            // var opSub = SF.ParseMemberDeclaration($@"
            //    [MethodImpl(MethodImplOptions.AggressiveInlining)]
            //     public static {typeName} operator -({typeName} lhs, {typeName} rhs) {{
            //         return new {typeName}(
            //             {opSubInstructions}
            //         );
            //     }}");

            // var opMulScalarRightInstructions = fields.Select((coeff, index) => $@"lhs.{CoefficientNames[index]} * rhs")
            //     .Aggregate((a, b) => a + ", \n" + b);
            // var opMulScalarRight = SF.ParseMemberDeclaration($@"
            //    [MethodImpl(MethodImplOptions.AggressiveInlining)]
            //     public static {typeName} operator *({typeName} lhs, {fType.name} rhs) {{
            //         return new {typeName}(
            //             {opMulScalarRightInstructions}
            //         );
            //     }}");

            // var opDivScalarRightInstructions = fields.Select((coeff, index) => $@"lhs.{CoefficientNames[index]} / rhs")
            //     .Aggregate((a, b) => a + ", \n" + b);
            // var opDivScalarRight = SF.ParseMemberDeclaration($@"
            //    [MethodImpl(MethodImplOptions.AggressiveInlining)]
            //     public static {typeName} operator /({typeName} lhs, {fType.name} rhs) {{
            //         return new {typeName}(
            //             {opDivScalarRightInstructions}
            //         );
            //     }}");

            // type = type.AddMembers(
            //     opAdd,
            //     opSub,
            //     opMulScalarRight,
            //     opDivScalarRight);


            // /* Equality */

            // var eqInstructions = fields.Select((coeff, index) => $@"this.{CoefficientNames[index]} == rhs.{CoefficientNames[index]}")
            //     .Aggregate((a, b) => a + " &&\n" + b);
            // var equals = SF.ParseMemberDeclaration($@"
            //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
            //     public bool Equals({typeName} rhs) {{
            //         return {eqInstructions};
            //     }}");

            // var equalsObj = SF.ParseMemberDeclaration($@"
            //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
            //     public override bool Equals(object o) {{
            //         if (!(o is {typeName})) {{
            //             return false;
            //         }}
            //         return Equals(({typeName})o);
            //     }}");

            // var opEq = SF.ParseMemberDeclaration($@"
            //     public static bool operator ==({typeName} lhs, {typeName} rhs) {{
            //         return lhs.Equals(rhs);
            //     }}");

            // var opNEq = SF.ParseMemberDeclaration($@"
            //     public static bool operator !=({typeName} lhs, {typeName} rhs) {{
            //         return !lhs.Equals(rhs);
            //     }}");

            // type = type.AddMembers(
            //     equals,
            //     equalsObj,
            //     opEq,
            //     opNEq);

            // // Other

            // /*
            // GetHashCode

            // Todo: None of these make particular sense for each type yet...
            // */
            // var getHashCodeInstructions = fields.Select((coeff, index) => $@"this.{CoefficientNames[index]}.v")
            //    .Aggregate((a, b) => a + " ^\n" + b);
            // var getHashCode = SF.ParseMemberDeclaration($@"
            //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
            //     public override int GetHashCode() {{
            //         return (int)({getHashCodeInstructions});
            //     }}");

            // var toStringReplaceList = fields.Select((coeff, index) => $@"{{{index}:0.000}}")
            //    .Aggregate((a, b) => a + ", " + b);
            // var toStringCoeffs = fields.Select((coeff, index) => $@"{fType.name}.ToFloat(this.{CoefficientNames[index]})")
            //    .Aggregate((a, b) => a + ", " + b);
            // var toString = SF.ParseMemberDeclaration($@"
            //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
            //     public override string ToString() {{
            //         return string.Format(""{typeName}({toStringReplaceList})"", {toStringCoeffs});
            //     }}");

            // var toStringFormat = SF.ParseMemberDeclaration($@"
            //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
            //     public string ToString(string format, IFormatProvider formatProvider) {{
            //         return ToString();
            //     }}");

            // type = type.AddMembers(
            //     getHashCode,
            //     toString,
            //     toStringFormat);

            nameSpace = nameSpace.AddMembers(type);
            unit = unit.AddMembers(nameSpace);

            return (fType, CSharpSyntaxTree.Create(unit));
        }
    }
}