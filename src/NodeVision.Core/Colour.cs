using System;

namespace NodeVision.Core
{
    public struct Colour
    {
        public float R;
        public float G;
        public float B;
        public float A;

        public Colour(float r, float g, float b, float a = 1)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public static Colour Black => new Colour(0, 0, 0, 1);
        public static Colour White => new Colour(1, 1, 1, 1);
        public static Colour Red => new Colour(1, 0, 0, 1);
        public static Colour Green => new Colour(0, 1, 0, 1);
        public static Colour Blue => new Colour(0, 0, 1, 1);
    }
}