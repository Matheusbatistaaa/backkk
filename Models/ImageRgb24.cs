using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PerceptAI.API.Models
{
    /// <summary>Imagem RGB intercalada em memória, usada por todo o pipeline de landmarks.</summary>
    public sealed class ImageRgb24
    {
        public int Width { get; }
        public int Height { get; }
        public byte[] Pixels { get; }

        public ImageRgb24(int width, int height, byte[] pixels)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Dimensões da imagem devem ser positivas.");
            if (pixels.Length != width * height * 3)
                throw new ArgumentException("Buffer RGB com tamanho incompatível.", nameof(pixels));

            Width = width;
            Height = height;
            Pixels = pixels;
        }

        public static ImageRgb24 FromBase64(string base64)
        {
            if (string.IsNullOrWhiteSpace(base64))
                throw new FormatException("Imagem Base64 vazia.");

            var payload = base64.Contains(',') ? base64[(base64.IndexOf(',') + 1)..] : base64;
            return FromBytes(Convert.FromBase64String(payload.Trim()));
        }

        public static ImageRgb24 FromFile(string path) => FromBytes(File.ReadAllBytes(path));

        public static ImageRgb24 FromBytes(byte[] bytes) => FromImage(Image.Load<Rgb24>(bytes));

        public static ImageRgb24 FromImage(Image<Rgb24> image)
        {
            var buffer = new byte[image.Width * image.Height * 3];
            image.CopyPixelDataTo(buffer);
            return new ImageRgb24(image.Width, image.Height, buffer);
        }

        public (byte R, byte G, byte B) GetPixel(int x, int y)
        {
            int i = (y * Width + x) * 3;
            return (Pixels[i], Pixels[i + 1], Pixels[i + 2]);
        }

        public void SetPixel(int x, int y, byte r, byte g, byte b)
        {
            int i = (y * Width + x) * 3;
            Pixels[i] = r;
            Pixels[i + 1] = g;
            Pixels[i + 2] = b;
        }
    }
}
