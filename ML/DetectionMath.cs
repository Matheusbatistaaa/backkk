using System;
using System.Collections.Generic;
using System.Linq;
using PerceptAI.API.Models;

namespace PerceptAI.API.ML
{
    public static class DetectionMath
    {
        public static float Sigmoid(float x) => 1f / (1f + MathF.Exp(-x));

        public static Point2[] GenerateAnchors(int inputSize, int[] strides)
        {
            var anchors = new List<Point2>();
            int idx = 0;
            while (idx < strides.Length)
            {
                int last = idx;
                while (last < strides.Length && strides[last] == strides[idx]) last++;
                int repeats = 2 * (last - idx);
                int cells = inputSize / strides[idx];
                for (int y = 0; y < cells; y++)
                {
                    for (int x = 0; x < cells; x++)
                    {
                        var center = new Point2((x + 0.5f) / cells, (y + 0.5f) / cells);
                        for (int r = 0; r < repeats; r++) anchors.Add(center);
                    }
                }
                idx = last;
            }
            return anchors.ToArray();
        }

        public static List<float[]> WeightedNms(List<float[]> rows, float iouThreshold = 0.3f)
        {
            var remaining = rows
                .Where(r => r.Length >= 5
                            && float.IsFinite(r[0])
                            && float.IsFinite(r[1]) && float.IsFinite(r[2])
                            && r[3] > 0f && r[4] > 0f)
                .OrderByDescending(r => r[0]).ToList();
            var output = new List<float[]>();

            while (remaining.Count > 0)
            {
                var top = remaining[0];
                float tx1 = top[1] - top[3] / 2f, ty1 = top[2] - top[4] / 2f;
                float tx2 = top[1] + top[3] / 2f, ty2 = top[2] + top[4] / 2f;
                float topArea = (tx2 - tx1) * (ty2 - ty1);

                var overlapping = new List<float[]> { top };
                var next = new List<float[]>();
                for (int i = 1; i < remaining.Count; i++)
                {
                    var candidate = remaining[i];
                    float cx1 = candidate[1] - candidate[3] / 2f, cy1 = candidate[2] - candidate[4] / 2f;
                    float cx2 = candidate[1] + candidate[3] / 2f, cy2 = candidate[2] + candidate[4] / 2f;
                    float interW = MathF.Max(0f, MathF.Min(cx2, tx2) - MathF.Max(cx1, tx1));
                    float interH = MathF.Max(0f, MathF.Min(cy2, ty2) - MathF.Max(cy1, ty1));
                    float inter = interW * interH;
                    float union = (cx2 - cx1) * (cy2 - cy1) + topArea - inter;
                    float iou = union > 0f ? inter / union : 0f;

                    if (iou > iouThreshold) overlapping.Add(candidate);
                    else next.Add(candidate);
                }

                var blended = (float[])top.Clone();
                float weightSum = overlapping.Sum(r => r[0]);
                if (weightSum > 0f && overlapping.Count > 1)
                {
                    for (int c = 1; c < blended.Length; c++)
                    {
                        float acc = 0f;
                        foreach (var r in overlapping) acc += r[c] * r[0];
                        blended[c] = acc / weightSum;
                    }
                }

                output.Add(blended);
                remaining = next;
            }

            return output;
        }

        public static float Distance(Point3 a, Point3 b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        public static float Distance(Point2 a, Point2 b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return MathF.Sqrt(dx * dx + dy * dy);
        }
    }
}
