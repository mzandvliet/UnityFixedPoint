using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

/*
    Based on: https://carlos.mendible.com/2017/03/02/create-a-class-with-net-core-and-roslyn/

    Learned:
    - SyntaxTrees are immutable, nice
    - SyntaxFactory lets you instantiate all of the syntax stuffs
        - Namespaces
        - Classes
        - Arguments
        - Fields
        - Etc
        - Parsers:
            - ParseName
            - ParseTypeName
            - ParseExpression
            - ParseStatement
    - SyntaxKind contains all primitive language elements
 */

public static class ExampleClassGenerator {
    public static void Generate() {
        // Make a namespace
        var nspaceDecl = SF.NamespaceDeclaration(SF.ParseName("ExampleNamespace"));

        // Make a class
        var classDecl = SF.ClassDeclaration("Order");

        // Class inherits base type and implements interface
        classDecl = classDecl.AddBaseListTypes(
            SF.SimpleBaseType(SF.ParseTypeName("BaseEntity<Order>")),
            SF.SimpleBaseType(SF.ParseTypeName("IHaveIdentity"))
        );

        var varDecl = SF.VariableDeclaration(SF.ParseTypeName("bool")).AddVariables(SF.VariableDeclarator("canceled"));

        var fieldDecl = SF.FieldDeclaration(varDecl).AddModifiers(SF.Token(SyntaxKind.PrivateKeyword));

        var propDecl = SF.PropertyDeclaration(SF.ParseTypeName("int"), "Quantity")
            .AddModifiers(SF.Token(SyntaxKind.PublicKeyword))
            .AddAccessorListAccessors(
                SF.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(SF.Token(SyntaxKind.SemicolonToken)),
                SF.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).WithSemicolonToken(SF.Token(SyntaxKind.SemicolonToken))
            );

        var methodBody = SF.ParseStatement("canceled = true");

        var methodDecl = SF.MethodDeclaration(SF.ParseTypeName("void"), "MarkAsCanceled")
            .AddModifiers(SF.Token(SyntaxKind.PublicKeyword))
            .WithBody(SF.Block(methodBody));

        classDecl = classDecl.AddMembers(
            fieldDecl,
            propDecl,
            methodDecl);

        nspaceDecl = nspaceDecl.AddMembers(classDecl);

        var code = nspaceDecl.NormalizeWhitespace().ToFullString();

        Console.WriteLine(code);
    }
}