using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Text;

namespace CodeGeneration {
    public static class ComplexTypeGenerator {
        public static (string, SyntaxTree) GenerateSigned32BitType(in string scalarTypeName) {
            string typeName = string.Format("complex_{0}", scalarTypeName);

            Console.WriteLine(typeName);


            string code = $@"
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Mathematics;

[System.Serializable]
[StructLayout(LayoutKind.Explicit)]
public struct {typeName}
{{
    public static readonly {typeName} Zero = new {typeName}(0,0);

    [FieldOffset(0)]
    public {scalarTypeName} r;
    [FieldOffset(4)]
    public {scalarTypeName} i;

    // constructors

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public {typeName}({scalarTypeName} r, {scalarTypeName} i) {{
        this.r = r;
        this.i = i;
    }}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public {typeName}(float r, float i) {{
        this.r = new {scalarTypeName}(r);
        this.i = new {scalarTypeName}(i);
    }}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public {typeName}(double r, double i) {{
        this.r = new {scalarTypeName}(r);
        this.i = new {scalarTypeName}(i);
    }}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static {typeName} operator +({typeName} lhs, {typeName} rhs) {{
        return new {typeName}(lhs.r + rhs.r, lhs.i + rhs.i);
    }}

    // Subtraction
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static {typeName} operator -({typeName} lhs, {typeName} rhs) {{ return new {typeName}(lhs.r - rhs.r, lhs.i - rhs.i); }}

    // Multiplication
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static {typeName} operator *({typeName} lhs, {typeName} rhs) {{
        return new {typeName}(
            lhs.r * rhs.r - lhs.i * rhs.i,
            lhs.r * rhs.i + lhs.i * rhs.r
        );
    }}

    // Equals 
    public bool Equals({typeName} rhs) {{ return r == rhs.r && i == rhs.i; }}
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object o) {{ return Equals(({typeName})o); }}


    // GetHashCode 
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() {{ return i.v ^ r.v; }}


    // ToString 
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override string ToString() {{
        return string.Format(""{typeName}({{0}}, {{1}})"", r, i);
    }}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ToString(string format, IFormatProvider formatProvider) {{
        return ToString();
    }}
}}
";
            var node = CSharpSyntaxTree.ParseText(code);
            return (typeName, node);
        }
    }
}