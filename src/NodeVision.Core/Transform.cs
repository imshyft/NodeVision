using System;

namespace NodeVision.Core
{
    public struct Transform
    {
        public Vector2 Position;
        public Vector2 Scale;
        public float Rotation;

        public Transform(Vector2 position, Vector2 scale, float rotation)
        {
            Position = position;
            Scale = scale;
            Rotation = rotation;
        }

        public static Transform Identity => new Transform(Vector2.Zero, Vector2.One, 0);

        public Transform Inverted()
        {
            float cos = (float)Math.Cos(Rotation);
            float sin = (float)Math.Sin(Rotation);

            float invScaleX = 1 / Scale.X;
            float invScaleY = 1 / Scale.Y;

            return new Transform(
                new Vector2(
                    (Position.X * cos - Position.Y * sin) * invScaleX,
                    (Position.X * sin + Position.Y * cos) * invScaleY
                ),
                new Vector2(invScaleX, invScaleY),
                -Rotation
            );
        }

        public Vector2 TransformPoint(Vector2 point)
        {
            float cos = (float)Math.Cos(Rotation);
            float sin = (float)Math.Sin(Rotation);

            return new Vector2(
                (point.X * cos - point.Y * sin) * Scale.X + Position.X,
                (point.X * sin + point.Y * cos) * Scale.Y + Position.Y
            );
        }
    }
}