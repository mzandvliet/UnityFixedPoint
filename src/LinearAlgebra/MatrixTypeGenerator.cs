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
    public static class MatrixTypeGenerator {
        public static (string, SyntaxTree) GenerateSigned32BitType(string scalarTypeName, params int[] shape) {
            string shapeName = shape.Select((dimLength, dim) => $@"{dimLength}").Aggregate((a, b) => a + "x" + b);
            string typeName = string.Format("mat{0}_{1}", shapeName, scalarTypeName);

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

            var constructorArgs = "";
            var constructorAssignments = "";

            var constructor = SF.ParseMemberDeclaration($@"
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public {typeName}({constructorArgs}) {{
                    {constructorAssignments}
                }}");

            type = type.AddMembers(
                constructor);

            nameSpace = nameSpace.AddMembers(type);
            unit = unit.AddMembers(nameSpace);

            return (typeName, CSharpSyntaxTree.Create(unit));
        }
    }
}