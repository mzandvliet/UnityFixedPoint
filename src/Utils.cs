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

    public static BaseListSyntax ImplementIEquatable(string equatableTypeName) {
        return SF.BaseList(
                SF.SingletonSeparatedList<BaseTypeSyntax>(
                    SF.SimpleBaseType(
                        SF.QualifiedName(
                            SF.IdentifierName("System"),
                            SF.GenericName(
                                SF.Identifier("IEquatable"))
                            .WithTypeArgumentList(
                                SF.TypeArgumentList(
                                    SF.SingletonSeparatedList<TypeSyntax>(
                                        SF.IdentifierName(equatableTypeName))))))));
    }

    public static string ToBitString(int value) {
        string b = System.Convert.ToString(value, 2);
        b = b.PadLeft(32, '0');
        return b;
    }

    public static string ToBitString(ushort value) {
        string b = System.Convert.ToString(value, 2);
        b = b.PadLeft(16, '0');
        return b;
    }

    private static uint[] m_primes = new uint[] {
        0x6E624EB7u,    0x7383ED49u,    0xDD49C23Bu,    0xEBD0D005u,    0x91475DF7u,    0x55E84827u,    0x90A285BBu,    0x5D19E1D5u,
                        0xFAAF07DDu,    0x625C45BDu,    0xC9F27FCBu,    0x6D2523B1u,    0x6E2BF6A9u,    0xCC74B3B7u,    0x83B58237u,    0x833E3E29u,
                        0xA9D919BFu,    0xC3EC1D97u,    0xB8B208C7u,    0x5D3ED947u,    0x4473BBB1u,    0xCBA11D5Fu,    0x685835CFu,    0xC3D32AE1u,
                        0xB966942Fu,    0xFE9856B3u,    0xFA3A3285u,    0xAD55999Du,    0xDCDD5341u,    0x94DDD769u,    0xA1E92D39u,    0x4583C801u,
                        0x9536A0F5u,    0xAF816615u,    0x9AF8D62Du,    0xE3600729u,    0x5F17300Du,    0x670D6809u,    0x7AF32C49u,    0xAE131389u,
                        0x5D1B165Bu,    0x87096CD7u,    0x4C7F6DD1u,    0x4822A3E9u,    0xAAC3C25Du,    0xD21D0945u,    0x88FCAB2Du,    0x614DA60Du,
                        0x5BA2C50Bu,    0x8C455ACBu,    0xCD266C89u,    0xF1852A33u,    0x77E35E77u,    0x863E3729u,    0xE191B035u,    0x68586FAFu,
                        0xD4DFF6D3u,    0xCB634F4Du,    0x9B13B92Du,    0x4ABF0813u,    0x86068063u,    0xD75513F9u,    0x5AB3E8CDu,    0x676E8407u,
                        0xB36DE767u,    0x6FCA387Du,    0xAF0F3103u,    0xE4A056C7u,    0x841D8225u,    0xC9393C7Du,    0xD42EAFA3u,    0xD9AFD06Du,
                        0x97A65421u,    0x7809205Fu,    0x9C9F0823u,    0x5A9CA13Bu,    0xAFCDD5EFu,    0xA88D187Du,    0xCF6EBA1Du,    0x9D88E5A1u,
                        0xEADF0775u,    0x747A9D7Bu,    0x4111F799u,    0xB5F05AF1u,            0xFD80290Bu,    0x8B65ADB7u,    0xDFF4F563u,    0x7069770Du,
                        0xD1224537u,    0xE99ED6F3u,    0x48125549u,    0xEEE2123Bu,            0xE3AD9FE5u,    0xCE1CF8BFu,    0x7BE39F3Bu,    0xFAB9913Fu,
                        0xB4501269u,    0xE04B89FDu,    0xDB3DE101u,    0x7B6D1B4Bu,            0x58399E77u,    0x5EAC29C9u,    0xFC6014F9u,    0x6BF6693Fu,
                        0x9D1B1D9Bu,    0xF842F5C1u,    0xA47EC335u,    0xA477DF57u,            0xC4B1493Fu,    0xBA0966D3u,    0xAFBEE253u,    0x5B419C01u,
                        0x515D90F5u,    0xEC9F68F3u,    0xF9EA92D5u,    0xC2FAFCB9u,            0x616E9CA1u,    0xC5C5394Bu,    0xCAE78587u,    0x7A1541C9u,
                        0xF83BD927u,    0x6A243BCBu,    0x509B84C9u,    0x91D13847u,            0x52F7230Fu,    0xCF286E83u,    0xE121E6ADu,    0xC9CA1249u,
                        0x69B60C81u,    0xE0EB6C25u,    0xF648BEABu,    0x6BDB2B07u,            0xEF63C699u,    0x9001903Fu,    0xA895B9CDu,    0x9D23B201u,
                        0x4B01D3E1u,    0x7461CA0Du,    0x79725379u,    0xD6258E5Bu,            0xEE390C97u,    0x9C8A2F05u,    0x4DDC6509u,    0x7CF083CBu,
                        0x5C4D6CEDu,    0xF9137117u,    0xE857DCE1u,    0xF62213C5u,            0x9CDAA959u,    0xAA269ABFu,    0xD54BA36Fu,    0xFD0847B9u,
                        0x8189A683u,    0xB139D651u,    0xE7579997u,    0xEF7D56C7u,            0x66F38F0Bu,    0x624256A3u,    0x5292ADE1u,    0xD2E590E5u,
                        0xF25BE857u,    0x9BC17CE7u,    0xC8B86851u,    0x64095221u,            0xADF428FFu,    0xA3977109u,    0x745ED837u,    0x9CDC88F5u,
                        0xFA62D721u,    0x7E4DB1CFu,    0x68EEE0F5u,    0xBC3B0A59u,            0x816EFB5Du,    0xA24E82B7u,    0x45A22087u,    0xFC104C3Bu,
                        0x5FFF6B19u,    0x5E6CBF3Bu,    0xB546F2A5u,    0xBBCF63E7u,            0xC53F4755u,    0x6985C229u,    0xE133B0B3u,    0xC3E0A3B9u,
                        0xFE31134Fu,    0x712A34D7u,    0x9D77A59Bu,    0x4942CA39u,            0xB40EC62Du,    0x565ED63Fu,    0x93C30C2Bu,    0xDCAF0351u,
                        0x6E050B01u,    0x750FDBF5u,    0x7F3DD499u,    0x52EAAEBBu,            0x4599C793u,    0x83B5E729u,    0xC267163Fu,    0x67BC9149u,
                        0xAD7C5EC1u,    0x822A7D6Du,    0xB492BF15u,    0xD37220E3u,            0x7AA2C2BDu,    0xE16BC89Du,    0x7AA07CD3u,    0xAF642BA9u,
                        0xA8F2213Bu,    0x9F3FDC37u,    0xAC60D0C3u,    0x9263662Fu,            0xE69626FFu,    0xBD010EEBu,    0x9CEDE1D1u,    0x43BE0B51u,
                        0xAF836EE1u,    0xB130C137u,    0x54834775u,    0x7C022221u,            0xA2D00EDFu,    0xA8977779u,    0x9F1C739Bu,    0x4B1BD187u,
                        0x9DF50593u,    0xF18EEB85u,    0x9E19BFC3u,    0x8196B06Fu,            0xD24EFA19u,    0x7D8048BBu,    0x713BD06Fu,    0x753AD6ADu,
                        0xD19764C7u,    0xB5D0BF63u,    0xF9102C5Fu,    0x9881FB9Fu,            0x56A1530Du,    0x804B722Du,    0x738E50E5u,    0x4FC93C25u,
                        0xCD0445A5u,    0xD2B90D9Bu,    0xD35C9B2Du,    0xA10D9E27u,            0x568DAAA9u,    0x7530254Fu,    0x9F090439u,    0x5E9F85C9u,
                        0x8C4CA03Fu,    0xB8D969EDu,    0xAC5DB57Bu,    0xA91A02EDu,            0xB3C49313u,    0xF43A9ABBu,    0x84E7E01Bu,    0x8E055BE5u
        };
}