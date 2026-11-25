using System;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PerceptAI.API.ML;
using PerceptAI.API.Models;
using PerceptAI.API.Services;
using PerceptAI.API.Settings;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// API acessível apenas na própria máquina: o front (Vite) faz o proxy /api.
var port = Environment.GetEnvironmentVariable("PORT") ?? "5198";
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

// Carrega .env local e mapeia para SupabaseSettings
var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(envPath))
{
    foreach (var line in File.ReadAllLines(envPath))
    {
        if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;
        var idx = line.IndexOf('=');
        if (idx <= 0) continue;
        var key   = line[..idx].Trim().Trim('"');
        var value = line[(idx + 1)..].Trim().Trim('"');
        builder.Configuration[key] = value;
    }
    var supabaseUrl = builder.Configuration["VITE_SUPABASE_URL"]
                   ?? builder.Configuration["SUPABASE_URL"]
                   ?? "";
    var serviceRoleKey = builder.Configuration["SUPABASE_SERVICE_ROLE_KEY"]
                      ?? builder.Configuration["VITE_SUPABASE_SERVICE_ROLE_KEY"]
                      ?? builder.Configuration["VITE_SUPABASE_ANON_KEY"]
                      ?? "";

    if (!string.IsNullOrWhiteSpace(supabaseUrl))
        builder.Configuration["SupabaseSettings:Url"] = supabaseUrl;
    if (!string.IsNullOrWhiteSpace(serviceRoleKey))
        builder.Configuration["SupabaseSettings:ServiceRoleKey"] = serviceRoleKey;
}

// ------------------------------------------------------------------
// CONFIGURAÇÕES
// ------------------------------------------------------------------
builder.Services.Configure<SupabaseSettings>(builder.Configuration.GetSection(SupabaseSettings.SectionName));

// ------------------------------------------------------------------
// CORS
// ------------------------------------------------------------------
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// ------------------------------------------------------------------
// OpenAPI (Scalar)
// ------------------------------------------------------------------
builder.Services.AddOpenApi();

// ------------------------------------------------------------------
// Serviços de negócio
// ------------------------------------------------------------------
builder.Services.AddControllers();
builder.Services.AddSingleton<ImagePreprocessingService>();
builder.Services.AddSingleton(sp =>
    builder.Configuration.GetSection("EmotionRules").Get<EmotionRuleOptions>() ?? new EmotionRuleOptions());

builder.Services.AddSingleton(sp =>
{
    var baseDir = AppContext.BaseDirectory;
    var physioPath = Path.Combine(baseDir, "ML", "perceptai_physio.onnx");
    var emotionPath = Path.Combine(baseDir, "ML", "perceptai_emotion.onnx");

    if (!File.Exists(physioPath))
        physioPath = Path.Combine(Directory.GetCurrentDirectory(), "ML", "perceptai_physio.onnx");
    if (!File.Exists(emotionPath))
        emotionPath = Path.Combine(Directory.GetCurrentDirectory(), "ML", "perceptai_emotion.onnx");
    if (!File.Exists(physioPath) || !File.Exists(emotionPath))
        throw new FileNotFoundException($"ONNX models not found. Physio: '{physioPath}', Emotion: '{emotionPath}'.");

    // Modelos do pipeline de marcos são opcionais: se faltarem, a API usa o classificador.
    var landmarkDir = Path.Combine(Directory.GetCurrentDirectory(), "ML");
    if (!Directory.Exists(landmarkDir))
        landmarkDir = Path.Combine(baseDir, "ML");

    return new ModelLoader(physioPath, emotionPath, landmarkDir);
});

builder.Services.AddSingleton<EmotionDetectionService>();
builder.Services.AddSingleton<FacialMetricsService>();
builder.Services.AddSingleton<EmotionRuleEngine>();
builder.Services.AddSingleton<ExpressionBaselineService>();
builder.Services.AddSingleton<LandmarkDetectionService>();
builder.Services.AddSingleton<DetectionPipelineService>();
builder.Services.AddScoped<SupabaseService>();
builder.Services.AddSingleton<LocalCadastroService>();

// ------------------------------------------------------------------
// HttpClient para Supabase (pré-configurado)
// ------------------------------------------------------------------
builder.Services.AddHttpClient("supabase", (sp, client) =>
{
    var settings = sp.GetRequiredService<IOptions<SupabaseSettings>>().Value;
    if (!string.IsNullOrWhiteSpace(settings.Url) && !string.IsNullOrWhiteSpace(settings.ServiceRoleKey))
    {
        client.BaseAddress = new Uri($"{settings.Url.TrimEnd('/')}/rest/v1/");
        client.DefaultRequestHeaders.Add("apikey", settings.ServiceRoleKey);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {settings.ServiceRoleKey}");
        client.DefaultRequestHeaders.Add("Accept", "application/json");
    }
});

var app = builder.Build();

app.Logger.LogInformation("PerceptAI ouvindo em http://127.0.0.1:{Port} (local)", port);
app.Logger.LogInformation("Pipeline de detecção: {Pipeline}",
    app.Services.GetRequiredService<ModelLoader>().HasLandmarkPipeline ? "landmarks" : "classificador (fallback)");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
    app.MapGet("/swagger", async ctx =>
    {
        ctx.Response.Redirect("/scalar/v1");
        await Task.CompletedTask;
    });
}

app.UseCors("AllowAll");
app.UseAuthorization();

// Endpoint de depuração local – executa uma imagem fixa pelo pipeline completo
app.MapGet("/debug/test", async (Microsoft.AspNetCore.Http.HttpContext ctx) =>
{
    var imgPath = Path.Combine(Directory.GetCurrentDirectory(), "test-images", "example.jpg");
    if (!File.Exists(imgPath))
        return Results.NotFound("Test image not found");

    var imgBytes = await File.ReadAllBytesAsync(imgPath);
    var base64 = Convert.ToBase64String(imgBytes);

    var pipeline = ctx.RequestServices.GetRequiredService<DetectionPipelineService>();
    try
    {
        var outcome = pipeline.Analyze(base64);
        return Results.Json(new
        {
            estadoPrincipal = outcome.Decision.EstadoPrincipal,
            estadoFisiologico = outcome.Decision.EstadoFisiologico,
            confiancaFisiologica = outcome.Decision.ConfiancaFisiologica,
            estadoEmocional = outcome.Decision.EstadoEmocional,
            emocaoSecundaria = outcome.Decision.EmocaoSecundaria,
            confiancaEmocional = outcome.Decision.ConfiancaEmocional,
            fonte = outcome.Decision.Fonte
        });
    }
    catch (FormatException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

// Teste de IA – imagem Base64 + análise detalhada (usado pelo modal de teste do front)
app.MapPost("/api/test-ai", async (Microsoft.AspNetCore.Http.HttpContext ctx) =>
{
    using var reader = new System.IO.StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    var json = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(body);
    if (!json.TryGetProperty("image_base64", out var imgProp) || imgProp.ValueKind != System.Text.Json.JsonValueKind.String)
        return Results.BadRequest(new { message = "image_base64 required" });

    var base64 = imgProp.GetString();
    if (string.IsNullOrWhiteSpace(base64))
        return Results.BadRequest(new { message = "image_base64 required" });

    var pipeline = ctx.RequestServices.GetRequiredService<DetectionPipelineService>();

    try
    {
        var outcome = pipeline.Analyze(base64!, includeClassifierProbabilities: true);
        var decision = outcome.Decision;

        return Results.Json(new
        {
            estadoPrincipal = decision.EstadoPrincipal,
            estadoFisiologico = decision.EstadoFisiologico,
            confiancaFisiologica = decision.ConfiancaFisiologica,
            estadoEmocional = decision.EstadoEmocional,
            emocaoSecundaria = decision.EmocaoSecundaria,
            confiancaEmocional = decision.ConfiancaEmocional,
            fonte = decision.Fonte,
            facesDetectadas = outcome.FacesDetected,
            observacoes = decision.Observacoes,
            regras = decision.Regras,
            metricas = decision.Metricas,
            allProbabilities = outcome.ClassifierProbabilities
                               ?? new System.Collections.Generic.Dictionary<string, float>()
        });
    }
    catch (FormatException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapControllers();
app.Run();
