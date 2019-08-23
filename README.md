# UnityFixedPoint
Using Roslyn to generate code for rings of fixed point number types, and mathematical objects that use them as scalar fields.

Why?

I want to use fixed point arithmetic for the following reasons:

- Deterministic numerical calculations across all DotNet platforms
- More control over precision (instead of just chucking 32-bit floats at a problem)

The C# language is not expressive enough to comfortably write all possible variants of fixed point types from within itself. Ideally you would be free to specify where the binary point should lie on a case-by-case basis, with wordlengths ranging from 8 to 128 bits. However, while the language features inheritance, interfaces, and parameterized types through generics, the language misses some features that would make this feasible:

- Parameterizing a type by cardinal numbers (such as the desired number of fractional bits, or the dimensions of a vector)
- Typeclasses, or Traits (more powerful version of interfaces, needed to express e.g. a field with operators Field{Add,Sub,Mul,Div,AdditiveInv,MultiplicativeInv})

I also want to use the generated types as building blocks for other objects, such as: *Vector<q15_16, 3>, Complex<q1_7>, Matrix4x4<q3_13>*, and so on, while still yielding code that is easy to use, maintain, and performs optimally.

Code generation through Roslyn seems the best way to realize these features with a finite amount of work, then.

--

As of 20-08-19 this is completely work-in-progress and not easily usable. I'm currently able to generate code like the following though:

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Mathematics;

namespace Ramjet.Math.FixedPoint
{
    [StructLayout(LayoutKind.Explicit)]
    public struct q15_16
    {
        public const Int32 Scale = 16;
        private const Int32 HalfScale = Scale >> 1;
        private const Int32 SignMask = unchecked((Int32)0b1000_0000_0000_0000_0000_0000_0000_0000);
        private const Int32 FractionMask = unchecked((Int32)0b0000_0000_0000_0000_1111_1111_1111_1111);
        private const Int32 IntegerMask = ~FractionMask;
        public static readonly q15_16 Zero = new q15_16(0);
        public static readonly q15_16 One = FromInt(1);
        public static readonly q15_16 Epsilon = new q15_16(1);
        [FieldOffset(0)]
        public Int32 v;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public q15_16(Int32 x)
        {
            v = x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static q15_16 FromInt(int x)
        {
            return new q15_16(x << Scale);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ToInt(q15_16 f)
        {
            return f.v >> Scale;
        }

[...]

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Mathematics;
using Ramjet.Math.FixedPoint;

namespace Ramjet.Math.LinearAlgebra
{
    [StructLayout(LayoutKind.Explicit)]
    public struct vec3_q15_16
    {
        [FieldOffset(0)]
        public q15_16 x;
        [FieldOffset(4)]
        public q15_16 y;
        [FieldOffset(8)]
        public q15_16 z;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public vec3_q15_16(q15_16 x, q15_16 y, q15_16 z)
        {
            this.x = x;
            this.y = y;
            this.z = z
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static vec3_q15_16 FromInt(int x, int y, int z)
        {
            return new vec3_q15_16(q15_16.FromInt(x), q15_16.FromInt(y), q15_16.FromInt(z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static vec3_q15_16 dot(vec3_q15_16 lhs, vec3_q15_16 rhs)
        {
            return new vec3_q15_16(lhs.x * rhs.x + lhs.y * rhs.y + lhs.z * rhs.z);
        }

[...]

```

Which is an instance of a 3-dimensional vector type from linear algebra, using the q15_16 fixed point scalar type, generated from a single parameterized struct template.

Issues to tackle:

 It's kind of funky to consider the Cartesian product of:

    - All qn.m types, for signed, unsigned, 8, 16, 32, 64, 128 bit
    - 2d, 3d, 4d, n-d vector and matrix types
    - 1d, 2d, 3d, 4d, 5d; 1st, 2nd, 3rd, 4th degree Bezier curves, surfaces and volumes
    - and so on

Generating all these ahead of time yields a huge number of types, most of which any target application will never use. In regular C#, when using generics, the compiler analyses used variants and only compiles the needed ones. I'll have to see if I can get a similar thing going.

Similarly, writing types on top of the generated scalars currently also requires using this code generator. From these scalars on, it's code generation all the way.

--

I am super new to working with Roslyn, and so the generators will be full with string manipulation while I'm still get my bearings.
