using System;
using PerceptAI.API.Models;

namespace PerceptAI.API.ML
{
    
    public static class ImageWarp
    {
        public static ImageRgb24 Letterbox(ImageRgb24 src, int outSize, out float scale, out float padX, out float padY)
        {
            scale = (float)outSize / Math.Max(src.Width, src.Height);
            padX = (outSize - src.Width * scale) / 2f;
            padY = (outSize - src.Height * scale) / 2f;

            var dst = new byte[outSize * outSize * 3];
            for (int y = 0; y < outSize; y++)
            {
                float sy = (y - padY) / scale;
                for (int x = 0; x < outSize; x++)
                {
                    float sx = (x - padX) / scale;
                    SampleBilinear(src, sx, sy, dst, (y * outSize + x) * 3);
                }
            }
            return new ImageRgb24(outSize, outSize, dst);
        }

        public static ImageRgb24 WarpRoi(ImageRgb24 src, Roi roi, int outSize)
        {
            double rad = roi.AngleDegrees * Math.PI / 180.0;
            float cos = (float)Math.Cos(rad);
            float sin = (float)Math.Sin(rad);
            float factor = roi.Side / outSize;
            float half = outSize / 2f;

            var dst = new byte[outSize * outSize * 3];
            for (int y = 0; y < outSize; y++)
            {
                float dy = (y - half) * factor;
                for (int x = 0; x < outSize; x++)
                {
                    float dx = (x - half) * factor;
                    float sx = roi.CenterX + cos * dx - sin * dy;
                    float sy = roi.CenterY + sin * dx + cos * dy;
                    SampleBilinear(src, sx, sy, dst, (y * outSize + x) * 3);
                }
            }
            return new ImageRgb24(outSize, outSize, dst);
        }

        public static Point2 CropToImage(float u, float v, Roi roi, int outSize)
        {
            double rad = roi.AngleDegrees * Math.PI / 180.0;
            float cos = (float)Math.Cos(rad);
            float sin = (float)Math.Sin(rad);
            float factor = roi.Side / outSize;
            float dx = (u - outSize / 2f) * factor;
            float dy = (v - outSize / 2f) * factor;
            return new Point2(
                roi.CenterX + cos * dx - sin * dy,
                roi.CenterY + sin * dx + cos * dy);
        }

        public static ImageRgb24 ResizeStretch(ImageRgb24 src, int width, int height)
        {
            var dst = new byte[width * height * 3];
            float sxScale = (src.Width - 1) / (float)Math.Max(width - 1, 1);
            float syScale = (src.Height - 1) / (float)Math.Max(height - 1, 1);

            for (int y = 0; y < height; y++)
            {
                float sy = Math.Clamp(y * syScale, 0f, src.Height - 1);
                for (int x = 0; x < width; x++)
                {
                    float sx = Math.Clamp(x * sxScale, 0f, src.Width - 1);
                    SampleBilinear(src, sx, sy, dst, (y * width + x) * 3);
                }
            }
            return new ImageRgb24(width, height, dst);
        }

        private static void SampleBilinear(ImageRgb24 src, float sx, float sy, byte[] dst, int dstIndex)
        {
            if (sx < 0f || sy < 0f || sx > src.Width - 1 || sy > src.Height - 1)
                return; 

            int x0 = (int)MathF.Floor(sx);
            int y0 = (int)MathF.Floor(sy);
            int x1 = Math.Min(x0 + 1, src.Width - 1);
            int y1 = Math.Min(y0 + 1, src.Height - 1);
            float wx = sx - x0;
            float wy = sy - y0;

            for (int c = 0; c < 3; c++)
            {
                float p00 = src.Pixels[(y0 * src.Width + x0) * 3 + c];
                float p10 = src.Pixels[(y0 * src.Width + x1) * 3 + c];
                float p01 = src.Pixels[(y1 * src.Width + x0) * 3 + c];
                float p11 = src.Pixels[(y1 * src.Width + x1) * 3 + c];
                float top = p00 + (p10 - p00) * wx;
                float bottom = p01 + (p11 - p01) * wx;
                dst[dstIndex + c] = (byte)Math.Clamp((int)MathF.Round(top + (bottom - top) * wy), 0, 255);
            }
        }
    }
}
