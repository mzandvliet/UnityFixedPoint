namespace Ramjet.Math.Curves {
    public static class BDCMath {
        public static int TriangularNumber(int n) {
            return (n * (n - 1)) / 2;
        }

        public static int IntPow(int x, uint pow) {
            int ret = 1;
            while (pow != 0) {
                if ((pow & 1) == 1)
                    ret *= x;
                x *= x;
                pow >>= 1;
            }
            return ret;
        }
    }
}
