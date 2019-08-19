# UnityFixedPoint
Using Roslyn to generate code for rings of fixed point number types, and mathematical objects that use them as scalar fields.

Why?

I want to use fixed point arithmetic for the following reasons:

- Deterministic numerical calculations across all DotNet platforms
- More control over precision (instead of just chucking 32-bit floats at a problem)

However, the C# language is not expressive enough to comfortably write all possible variants of fixed point types. Ideally you would be free to specify where the binary point should lie on a case-by-case basis, with wordlengths ranging from 8 to 128 bits. However, the language misses some features that would make this feasible:

- Parameterizing a type by cardinal numbers (such as the number of fractional bits, or the dimensions of a vector)
- Typeclasses, or Traits (more powerful version of interfaces, needed to express Field{Add,Sub,Mul,Div,AddInv,MulInv})

Code generation through Roslyn seems the best way to realize these features, then.

--

As of 20-08-19 this is completely work-in-progress and not directly usable. I'm currently able to generate snippets like the following though:

```csharp
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
    }
}
```

Which is an instance of a 3-dimensional vector type from linear algebra, using the q15_16 fixed point scalar type, generated from a single parameterized struct template.

--

I am super new to working with Roslyn, and so the generators will be full with string manipulation while I'm still get my bearings.
