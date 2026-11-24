using PerceptAI.API.Models;

namespace PerceptAI.API.Services
{
    public class ImagePreprocessingService
    {
        
        public ImageRgb24 LoadBase64Image(string base64Image) => ImageRgb24.FromBase64(base64Image);

        public ImageRgb24 LoadFile(string path) => ImageRgb24.FromFile(path);
    }
}
