using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using PerceptAI.API.Models;

namespace PerceptAI.API.ML
{
    /// <summary>
    /// Hand Landmark full (224x224, RGB [0,1]) — 21 pontos por mão.
    /// ROI segue palm_detection_detection_to_roi: caixa 2.6x, centro deslocado
    /// meia caixa na direção dos dedos e rotação punho -> MCP do dedo médio.
    /// </summary>
    public sealed class HandLandmarkPredictor
    {
        public const int InputSize = 224;
        private const float RoiScale = 2.6f;
        private const float RoiShift = 0.5f;

        private readonly InferenceSession _session;
        private readonly string _inputName;

        public HandLandmarkPredictor(InferenceSession session)
        {
            _session = session;
            _inputName = session.InputMetadata.Keys.First();
        }

        public HandLandmarks[] Predict(ImageRgb24 image, IReadOnlyList<PalmDetection> palms, float threshold = 0.5f)
        {
            var hands = new List<HandLandmarks>(palms.Count);

            foreach (var palm in palms)
            {
                var roi = BuildRoi(palm);
                var crop = ImageWarp.WarpRoi(image, roi, InputSize);
                var tensor = TensorBuilder.UnitChw(crop);

                using var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName, tensor) });
                var flat = results.First(r => r.Name == "screen_landmarks").AsTensor<float>().ToArray();
                float presence = results.First(r => r.Name == "presence").AsTensor<float>().ToArray()[0];

                if (presence < threshold) continue;

                int count = flat.Length / 3;
                var points = new Point3[count];
                for (int i = 0; i < count; i++)
                {
                    var mapped = ImageWarp.CropToImage(flat[i * 3], flat[i * 3 + 1], roi, InputSize);
                    points[i] = new Point3(mapped.X, mapped.Y, flat[i * 3 + 2] * (roi.Side / InputSize));
                }

                hands.Add(new HandLandmarks { Points = points, Score = presence });
            }

            return hands.ToArray();
        }

        private static Roi BuildRoi(PalmDetection palm)
        {
            float dx = palm.MiddleMcp.X - palm.Wrist.X;
            float dy = palm.MiddleMcp.Y - palm.Wrist.Y;
            float norm = MathF.Sqrt(dx * dx + dy * dy);
            float ux = norm > 0f ? dx / norm : 0f;
            float uy = norm > 0f ? dy / norm : -1f;

            float centerX = palm.CenterX + RoiShift * palm.Width * ux;
            float centerY = palm.CenterY + RoiShift * palm.Width * uy;
            float angle = (float)(Math.Atan2(uy, ux) * 180.0 / Math.PI) + 90f;

            return new Roi(centerX, centerY, RoiScale * palm.Width, angle);
        }
    }
}
