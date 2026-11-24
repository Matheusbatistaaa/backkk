using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PerceptAI.API.Settings;

namespace PerceptAI.API.Services
{
    
    public class SupabaseService
    {
        private readonly HttpClient _http;
        private readonly ILogger<SupabaseService> _logger;

        private record DetectionRow(
            string patient_id,
            string tipo_emocao,
            float confianca,
            string timestamp);

        private record AlertRow(
            string detection_id,
            string cuidador_id,
            bool lido);

        private record ExpressaoRow(
            string internacao_id, 
            string mood,          
            int confianca,        
            string timestamp); 

        private record DetectionCreated(string id);

        private record PatientRow(string cuidador_id);

        public SupabaseService(
            IHttpClientFactory httpClientFactory,
            ILogger<SupabaseService> logger)
        {
            _http   = httpClientFactory.CreateClient("supabase");
            _logger = logger;
        }

        public async Task<bool> SaveDetectionAsync(
            string patientId,
            string emotion,
            float confidence,
            DateTime timestamp)
        {
            
            string? detectionId = await InsertDetectionAsync(patientId, emotion, confidence, timestamp);
            if (detectionId is null)
            {
                _logger.LogWarning("Detecção não pôde ser salva; alerta não será gerado.");
                return false;
            }

            string? cuidadorId = await FetchCuidadorIdAsync(patientId);
            if (cuidadorId is null)
            {
                _logger.LogWarning(
                    "Paciente {PatientId} não encontrado ou sem cuidador; alerta não será gerado.",
                    patientId);
                return false;
            }

            await InsertAlertAsync(detectionId, cuidadorId);

            return true;
        }

        public async Task<bool> SaveExpressaoAsync(
            string internacaoId,
            string mood,
            float confidenceRaw)
        {
            
            int confiancaPerc = (int)Math.Round(confidenceRaw * 100);

            if (string.IsNullOrWhiteSpace(internacaoId))
            {
                _logger.LogError("InternacaoId inválido (vazio): {InternacaoId}", internacaoId);
                return false;
            }

            var row = new ExpressaoRow(
                internacao_id: internacaoId,
                mood:          mood,
                confianca:     confiancaPerc,
                timestamp:     DateTime.UtcNow.ToString("o"));

            try
            {
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "expressoes_faciais");
                httpRequest.Headers.Add("Prefer", "return=minimal");
                httpRequest.Content = JsonContent.Create(row);

                var response = await _http.SendAsync(httpRequest);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    _logger.LogError(
                        "Erro ao inserir expressão facial: {Status} — {Body}",
                        response.StatusCode, body);
                    return false;
                }

                _logger.LogInformation(
                    "Expressão facial salva: internacao={InternacaoId} emocao={Emocao} confianca={Confianca}%",
                    internacaoId, mood, confiancaPerc);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exceção ao salvar expressão facial para internação {InternacaoId}", internacaoId);
                return false;
            }
        }

        public async Task<string> FetchDetectionsRawAsync(int limit = 10)
        {
            try
            {
                var url = $"detections?select=*&order=timestamp.desc&limit={limit}";
                var response = await _http.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Erro ao buscar detecções brutas: {Status} — {Body}", response.StatusCode, body);
                    return "[]";
                }

                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exceção ao buscar detecções brutas");
                return "[]";
            }
        }

        private async Task<string?> InsertDetectionAsync(
            string patientId,
            string emotion,
            float confidence,
            DateTime timestamp)
        {
            var row = new DetectionRow(
                patient_id:  patientId,
                tipo_emocao: emotion,
                confianca:   confidence,
                timestamp:   timestamp.ToString("o")); 

            try
            {
                
                using var request = new HttpRequestMessage(HttpMethod.Post, "detections");
                request.Headers.Add("Prefer", "return=representation");
                request.Content = JsonContent.Create(row);

                var response = await _http.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    _logger.LogError(
                        "Erro ao inserir detecção: {Status} — {Body}",
                        response.StatusCode, body);
                    return null;
                }

                var created = await response.Content.ReadFromJsonAsync<DetectionCreated[]>();
                return created?[0]?.id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exceção ao inserir detecção para paciente {PatientId}", patientId);
                return null;
            }
        }

        private async Task<string?> FetchCuidadorIdAsync(string patientId)
        {
            try
            {
                
                var url = $"patients?id=eq.{patientId}&select=cuidador_id";
                var rows = await _http.GetFromJsonAsync<PatientRow[]>(url);
                return rows?.Length > 0 ? rows[0].cuidador_id : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exceção ao buscar cuidador do paciente {PatientId}", patientId);
                return null;
            }
        }

        private async Task InsertAlertAsync(string detectionId, string cuidadorId)
        {
            var row = new AlertRow(
                detection_id: detectionId,
                cuidador_id:  cuidadorId,
                lido:         false);

            try
            {
                var response = await _http.PostAsJsonAsync("alerts", row);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    _logger.LogError(
                        "Erro ao criar alerta: {Status} — {Body}",
                        response.StatusCode, body);
                }
                else
                {
                    _logger.LogInformation(
                        "Alerta criado para cuidador {CuidadorId} (detecção {DetectionId})",
                        cuidadorId, detectionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exceção ao criar alerta para detecção {DetectionId}", detectionId);
            }
        }
    }
}
