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
 */

namespace CodeGeneration {
    public static class BezierCurveTypeGenerator {
        public static (string, SyntaxTree) GenerateSigned32BitType(string fieldTypeName, in int numDegrees) {
            if (numDegrees < 0) {
                throw new ArgumentException("Bezier types must degree 0 or higher polynomials");
            }

            string typeName = string.Format("BDC_{0}_{1}", numDegrees, fieldTypeName);

            var usingStrings = new List<string> {
                "System",
                "System.Runtime.CompilerServices",
                "System.Runtime.InteropServices",
                "UnityEngine",
                "Unity.Mathematics",
                "Unity.Collections",
                "Ramjet.Math.FixedPoint",
                "Ramjet.Math.LinearAlgebra"
            };

            var usings = new SyntaxList<UsingDirectiveSyntax>(
                from s in usingStrings select SF.UsingDirective(SF.ParseName(s)));

            var unit = SF.CompilationUnit()
                .WithUsings(usings);

            var nameSpace = SF.NamespaceDeclaration(SF.ParseName("Ramjet.Math.Curves"));

            var type = SF.StructDeclaration(typeName)
                .AddModifiers(SF.Token(SK.PublicKeyword))
                .AddModifiers(SF.Token(SK.StaticKeyword));

            // var dotInstructions = coefficientFields.Select((coeff, index) => $@"lhs.{CoefficientNames[index]} * rhs.{CoefficientNames[index]}")
            //     .Aggregate((a, b) => a + " +\n" + b);
            // var dotProduct = SF.ParseMemberDeclaration($@"
            //    [MethodImpl(MethodImplOptions.AggressiveInlining)]
            //     public static {typeName} dot({typeName} lhs, {typeName} rhs) {{
            //         return new {typeName}(
            //             {dotInstructions}
            //         );
            //     }}");

            // type = type.AddMembers(dotProduct);

            nameSpace = nameSpace.AddMembers(type);
            unit = unit.AddMembers(nameSpace);

            return (typeName, CSharpSyntaxTree.Create(unit));
        }
    }
}