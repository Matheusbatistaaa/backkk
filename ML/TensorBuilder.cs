using Microsoft.ML.OnnxRuntime.Tensors;
using PerceptAI.API.Models;

namespace PerceptAI.API.ML
{
    public static class TensorBuilder
    {
        
        public static DenseTensor<float> UnitChw(ImageRgb24 image) => Build(image, 0f, 255f);

        public static DenseTensor<float> SignedChw(ImageRgb24 image) => Build(image, 127.5f, 127.5f - 0f, signed: true);

        public static DenseTensor<float> ImageNetChw(ImageRgb24 image)
        {
            var data = new float[3 * image.Width * image.Height];
            int plane = image.Width * image.Height;
            float[] mean = { 0.485f, 0.456f, 0.406f };
            float[] std = { 0.229f, 0.224f, 0.225f };

            for (int i = 0; i < plane; i++)
            {
                data[i] = (image.Pixels[i * 3] / 255f - mean[0]) / std[0];
                data[plane + i] = (image.Pixels[i * 3 + 1] / 255f - mean[1]) / std[1];
                data[2 * plane + i] = (image.Pixels[i * 3 + 2] / 255f - mean[2]) / std[2];
            }

            return new DenseTensor<float>(data, new[] { 1, 3, image.Height, image.Width });
        }

        private static DenseTensor<float> Build(ImageRgb24 image, float offset, float divisor, bool signed = false)
        {
            int plane = image.Width * image.Height;
            var data = new float[3 * plane];
            for (int i = 0; i < plane; i++)
            {
                data[i] = (image.Pixels[i * 3] - offset) / divisor;
                data[plane + i] = (image.Pixels[i * 3 + 1] - offset) / divisor;
                data[2 * plane + i] = (image.Pixels[i * 3 + 2] - offset) / divisor;
            }
            return new DenseTensor<float>(data, new[] { 1, 3, image.Height, image.Width });
        }
    }
}
