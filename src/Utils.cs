using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using SK = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

public static class Utils {
    public static SyntaxList<AttributeListSyntax> GenerateStructLayoutAttributes() {
        return SF.SingletonList(
            SF.AttributeList(
                SF.SingletonSeparatedList(
                    SF.Attribute(SF.IdentifierName("StructLayout"))
                        .WithArgumentList(
                            SF.AttributeArgumentList(
                                SF.SingletonSeparatedList(
                                    SF.AttributeArgument(
                                        SF.MemberAccessExpression(
                                            SK.SimpleMemberAccessExpression,
                                            SF.IdentifierName("LayoutKind"),
                                            SF.IdentifierName("Explicit")))))))));
    }
}