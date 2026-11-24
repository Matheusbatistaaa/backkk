using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using PerceptAI.API.Models;

namespace PerceptAI.API.ML
{
    
    public sealed class FaceMeshPredictor
    {
        public const int InputSize = 192;
        private const float Margin = 0.25f;

        private readonly InferenceSession _session;
        private readonly string _inputName;

        public FaceMeshPredictor(InferenceSession session)
        {
            _session = session;
            _inputName = session.InputMetadata.Keys.First();
        }

        public FaceMeshPredictorOutput Predict(ImageRgb24 image, FaceDetection face)
        {
            var box = face.Bounds;
            float side = (1f + 2f * Margin) * MathF.Max(box.Width, box.Height);

            float angle = 0f;
            if (face.Keypoints.Length >= 2)
            {
                float dx = face.Keypoints[1].X - face.Keypoints[0].X;
                float dy = face.Keypoints[1].Y - face.Keypoints[0].Y;
                angle = (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI);
            }

            var roi = new Roi(box.CenterX, box.CenterY, side, angle);
            var crop = ImageWarp.WarpRoi(image, roi, InputSize);
            var tensor = TensorBuilder.UnitChw(crop);

            using var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName, tensor) });
            var flat = results.First(r => r.Name == "landmarks").AsTensor<float>().ToArray();
            float logit = results.First(r => r.Name == "score").AsTensor<float>().ToArray()[0];

            int count = flat.Length / 3;
            var points = new Point3[count];
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;

            for (int i = 0; i < count; i++)
            {
                var mapped = ImageWarp.CropToImage(flat[i * 3], flat[i * 3 + 1], roi, InputSize);
                float z = flat[i * 3 + 2] * (roi.Side / InputSize);
                points[i] = new Point3(mapped.X, mapped.Y, z);
                minX = MathF.Min(minX, mapped.X);
                minY = MathF.Min(minY, mapped.Y);
                maxX = MathF.Max(maxX, mapped.X);
                maxY = MathF.Max(maxY, mapped.Y);
            }

            return new FaceMeshPredictorOutput(
                new FaceLandmarks
                {
                    Points = points,
                    Score = DetectionMath.Sigmoid(logit),
                    Bounds = new RectF(minX, minY, maxX - minX, maxY - minY)
                },
                roi);
        }
    }

    public sealed record FaceMeshPredictorOutput(FaceLandmarks Landmarks, Roi Roi);
}
