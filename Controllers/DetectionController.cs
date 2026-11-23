using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using PerceptAI.API.Models;
using PerceptAI.API.Services;

namespace PerceptAI.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DetectionController : ControllerBase
    {
        private readonly ImagePreprocessingService _preprocessingService;
        private readonly EmotionDetectionService _detectionService;
        private readonly SupabaseService _supabaseService;
        private readonly double _threshold;

        public DetectionController(
            ImagePreprocessingService preprocessingService,
            EmotionDetectionService detectionService,
            SupabaseService supabaseService,
            IConfiguration configuration)
        {
            _preprocessingService = preprocessingService;
            _detectionService = detectionService;
            _supabaseService = supabaseService;
            // Threshold usado apenas para o fluxo legado (app mobile).
            _threshold = configuration.GetValue<double>("DetectionSettings:Threshold", 0.75);
        }

        /// <summary>
        /// Recebe uma imagem em Base64 e o ID do paciente/internação, executa o pipeline
        /// (landmarks -> métricas -> regras) e persiste o resultado no Supabase.
        /// </summary>
        [HttpPost("detect")]
        public async Task<IActionResult> Detect([FromBody] DetectionRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Image))
            {
                return BadRequest(new { message = "Corpo de requisição inválido ou imagem vazia." });
            }

            bool temPatient = !string.IsNullOrWhiteSpace(request.PatientId);
            bool temInternacao = !string.IsNullOrWhiteSpace(request.InternacaoId);

            if (!temPatient && !temInternacao)
            {
                return BadRequest(new { message = "PatientId ou InternacaoId é obrigatório." });
            }

            try
            {
                var image = _preprocessingService.LoadBase64Image(request.Image);
                var result = _detectionService.AnalyzeDualState(image);

                string emotion = result.EstadoEmocional
                    ?? (result.EstadoFisiologico == "dormindo" ? "dormindo" : "neutro");

                var response = new DetectionResponse
                {
                    Emotion = emotion,
                    Confidence = result.PrimaryConfidence,
                    Timestamp = DateTime.UtcNow,
                    EstadoFisiologico = result.EstadoFisiologico,
                    EstadoEmocional = result.EstadoEmocional,
                    ConfiancaFisiologica = result.ConfiancaFisiologica,
                    ConfiancaEmocional = result.ConfiancaEmocional,
                    Fonte = result.Fonte,
                    Metricas = result.Metricas,
                    Regras = result.Regras,
                    Observacoes = result.Observacoes
                };

                if (temInternacao)
                {
                    await _supabaseService.SaveExpressaoAsync(request.InternacaoId!, emotion, response.Confidence);
                }
                else if (temPatient && response.Confidence >= _threshold)
                {
                    await _supabaseService.SaveDetectionAsync(request.PatientId, emotion, response.Confidence, response.Timestamp);
                }

                return Ok(response);
            }
            catch (FormatException)
            {
                return BadRequest(new { message = "Formato Base64 da imagem é inválido." });
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(422, new { message = ex.Message });
            }
            catch (Exception)
            {
                return StatusCode(500, new { message = "Erro interno ao processar a detecção." });
            }
        }

        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new
            {
                status = "ok",
                pipeline = _detectionService.LandmarkPipelineAvailable ? "landmarks" : "classificador",
                timestamp = DateTime.UtcNow
            });
        }
    }
}
