using System;

namespace PerceptAI.API.Models
{
    public readonly record struct Point2(float X, float Y);

    public readonly record struct Point3(float X, float Y, float Z);

    public readonly record struct RectF(float X, float Y, float Width, float Height)
    {
        public float Right => X + Width;
        public float Bottom => Y + Height;
        public float CenterX => X + Width / 2f;
        public float CenterY => Y + Height / 2f;
    }

    public readonly record struct Roi(float CenterX, float CenterY, float Side, float AngleDegrees);

    public sealed class FaceDetection
    {
        public RectF Bounds { get; init; }
        
        public Point2[] Keypoints { get; init; } = Array.Empty<Point2>();
        public float Score { get; init; }
    }

    public sealed class FaceLandmarks
    {
        public Point3[] Points { get; init; } = Array.Empty<Point3>();
        public float Score { get; init; }
        public RectF Bounds { get; init; }
    }

    public sealed class HandLandmarks
    {
        public Point3[] Points { get; init; } = Array.Empty<Point3>();
        public float Score { get; init; }
    }

    public sealed class LandmarkFrame
    {
        public FaceLandmarks? Face { get; init; }
        public HandLandmarks[] Hands { get; init; } = Array.Empty<HandLandmarks>();
        public int FacesDetected { get; init; }
        public bool Available { get; init; }
    }
}
