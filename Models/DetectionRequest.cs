using System.ComponentModel.DataAnnotations;

namespace PerceptAI.API.Models
{
    public class DetectionRequest
    {
        [Required(ErrorMessage = "A imagem base64 é obrigatória.")]
        public string Image { get; set; } = string.Empty;

        public string PatientId { get; set; } = string.Empty;

        public string? InternacaoId { get; set; }
    }
}
