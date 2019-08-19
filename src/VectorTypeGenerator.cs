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
    public static class VectorTypeGenerator {
        private static readonly string[] CoefficientNames = new string[] {
            "x",
            "y",
            "z",
            "w"
        };

        public static (string, SyntaxTree) GenerateSigned32BitType(string scalarTypeName, in int numDimensions) {
            if (numDimensions <= 0 || numDimensions > 4) {
                throw new ArgumentException("Vector types currently only support 1-4 dimensions");
            }

            string typeName = string.Format("vec{0}_{1}", numDimensions, scalarTypeName);

            var usingStrings = new List<string> {
                "System",
                "System.Runtime.CompilerServices",
                "System.Runtime.InteropServices",
                "UnityEngine",
                "Unity.Mathematics",
                "Ramjet.Math.FixedPoint"
            };

            var usings = new SyntaxList<UsingDirectiveSyntax>(
                from s in usingStrings select SF.UsingDirective(SF.ParseName(s)));

            var unit = SF.CompilationUnit()
                .WithUsings(usings);

            var nameSpace = SF.NamespaceDeclaration(SF.ParseName("Ramjet.Math.LinearAlgebra"));

            var type = SF.StructDeclaration(typeName)
                .AddModifiers(SF.Token(SK.PublicKeyword))
                .WithAttributeLists(Utils.GenerateStructLayoutAttributes());

            // Constants

            

            var coefficientFields = new List<MemberDeclarationSyntax>();

            int fieldOffset = 0;
            for (int i = 0; i < numDimensions; i++) {
                var coeff = SF.ParseMemberDeclaration($@"[FieldOffset({fieldOffset})] public {scalarTypeName} {CoefficientNames[i]};");
                coefficientFields.Add(coeff);
                fieldOffset += 4; // Todo: automatically do this, by sizeof type
            }

            type = type.AddMembers(coefficientFields.ToArray());

            var zero = SF.ParseMemberDeclaration($@"public static readonly {typeName} Zero = {typeName}.FromInt(0);");

            var constructorArgs = coefficientFields.Select((coeff, index) => $@"{scalarTypeName} {CoefficientNames[index]}").Aggregate((a, b) => a + ", " + b);
            var constructorAssignments = coefficientFields.Select((coeff, index) => $@"this.{CoefficientNames[index]} = {CoefficientNames[index]}").Aggregate((a, b) => a + ";\n" + b);

            var constructor = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public {typeName}({constructorArgs}) {{
                    {constructorAssignments}
                }}");

            type = type.AddMembers(
                constructor);

            // var fromInt = SF.ParseMemberDeclaration($@"
            //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
            //     public static {typeName} FromInt(int r, int i) {{
            //         return new {typeName}(
            //             {scalarTypeName}.FromInt(r),
            //             {scalarTypeName}.FromInt(i));
            //     }}");

            // var toInt2 = SF.ParseMemberDeclaration($@"
            //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
            //     public static int2 ToInt({typeName} f) {{
            //         return new int2(
            //             {scalarTypeName}.ToInt(f.r),
            //             {scalarTypeName}.ToInt(f.i));
            //     }}");

            // var fromFloat = SF.ParseMemberDeclaration($@"
            //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
            //     public static {typeName} FromFloat(float r, float i) {{
            //         return new {typeName}(
            //             {scalarTypeName}.FromFloat(r),
            //             {scalarTypeName}.FromFloat(i));
            //     }}");

            // var toFloat2 = SF.ParseMemberDeclaration($@"
            //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
            //     public static float2 ToFloat({typeName} f) {{
            //         return new float2(
            //             {scalarTypeName}.ToFloat(f.r),
            //             {scalarTypeName}.ToFloat(f.i));
            //     }}");

            // var fromDouble = SF.ParseMemberDeclaration($@"
            //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
            //     public static {typeName} FromDouble(double r, double i) {{
            //         return new {typeName}(
            //             {scalarTypeName}.FromDouble(r),
            //             {scalarTypeName}.FromDouble(i));
            //     }}");

            // var toDouble2 = SF.ParseMemberDeclaration($@"
            //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
            //     public static double2 ToDouble({typeName} f) {{
            //         return new double2(
            //             {scalarTypeName}.ToDouble(f.r),
            //             {scalarTypeName}.ToDouble(f.i));
            //     }}");

            // type = type.AddMembers(
            //     fromInt,
            //     toInt2,
            //     fromFloat,
            //     toFloat2,
            //     fromDouble,
            //     toDouble2);

            // var opAdd = SF.ParseMemberDeclaration($@"
            //    [MethodImpl(MethodImplOptions.AggressiveInlining)]
            //     public static {typeName} operator +({typeName} lhs, {typeName} rhs) {{
            //         return new {typeName}(lhs.r + rhs.r, lhs.i + rhs.i);
            //     }}");

            // var opSub = SF.ParseMemberDeclaration($@"
            //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
            //     public static {typeName} operator -({typeName} lhs, {typeName} rhs) {{
            //         return new {typeName}(lhs.r - rhs.r, lhs.i - rhs.i);
            //     }}");

            // var opMul = SF.ParseMemberDeclaration($@"
            //    [MethodImpl(MethodImplOptions.AggressiveInlining)]
            //     public static {typeName} operator *({typeName} lhs, {typeName} rhs) {{
            //         return new {typeName}(
            //             lhs.r * rhs.r - lhs.i * rhs.i,
            //             lhs.r * rhs.i + lhs.i * rhs.r
            //         );
            //     }}");

            // type = type.AddMembers(
            //     opAdd,
            //     opSub,
            //     opMul);

            var dotInstructions = coefficientFields.Select((coeff, index) => $@"lhs.{CoefficientNames[index]} * rhs.{CoefficientNames[index]}")
                .Aggregate((a, b) => a + " +\n" + b);
            var dotProduct = SF.ParseMemberDeclaration($@"
               [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static {typeName} dot({typeName} lhs, {typeName} rhs) {{
                    return new {typeName}(
                        {dotInstructions}
                    );
                }}");

            type = type.AddMembers(dotProduct);

            // /* Equality */

            // var equals = SF.ParseMemberDeclaration($@"
            //     public bool Equals({typeName} rhs) {{
            //         return r == rhs.r && i == rhs.i;
            //     }}");

            // var equalsObj = SF.ParseMemberDeclaration($@"
            //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
            //     public override bool Equals(object o) {{
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

            // var getHashCode = SF.ParseMemberDeclaration($@"
            //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
            //     public override int GetHashCode() {{
            //         return i.v ^ r.v;
            //     }}");

            // var toString = SF.ParseMemberDeclaration($@"
            //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
            //     public override string ToString() {{
            //         return string.Format(""{typeName}({{0}}, {{1}})"", r, i);
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

            return (typeName, CSharpSyntaxTree.Create(unit));
        }
    }
}