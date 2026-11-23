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

// Porta configurável (Render/Railway usam PORT; local usa 5198, o mesmo do frontend).
var port = Environment.GetEnvironmentVariable("PORT") ?? "5198";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Carrega .env local e mapeia para SupabaseSettings.
var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(envPath))
{
    foreach (var line in File.ReadAllLines(envPath))
    {
        if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;
        var idx = line.IndexOf('=');
        if (idx <= 0) continue;
        var key = line[..idx].Trim().Trim('"');
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
// CONFIGURAÇÕES E SERVIÇOS
// ------------------------------------------------------------------
builder.Services.Configure<SupabaseSettings>(builder.Configuration.GetSection(SupabaseSettings.SectionName));

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

builder.Services.AddOpenApi();
builder.Services.AddControllers();

builder.Services.AddSingleton(new EmotionRuleOptions());
builder.Services.AddSingleton<ImagePreprocessingService>();
builder.Services.AddSingleton<FacialMetricsService>();
builder.Services.AddSingleton<EmotionRuleEngine>();
builder.Services.AddSingleton<LandmarkDetectionService>();

builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<ModelLoader>>();
    var mlDir = ResolveMlDirectory(logger);

    string? Resolve(string fileName)
    {
        var path = Path.Combine(mlDir, fileName);
        if (File.Exists(path)) return path;
        logger.LogWarning("Modelo ausente: {Path}", path);
        return null;
    }

    var loader = new ModelLoader(
        Resolve("perceptai_mobilenetv2.onnx"),
        Resolve("face_detection_short_range.onnx"),
        Resolve("face_mesh_Nx3x192x192.onnx"),
        Resolve("palm_detection_full.onnx"),
        Resolve("hand_landmark_full.onnx"));

    logger.LogInformation("Pipeline de landmarks: {Enabled}. Classificador: {Classifier}.",
        loader.HasLandmarkPipeline, loader.HasClassifier);
    if (loader.LoadWarnings.Count > 0)
        logger.LogWarning("Modelos não carregados: {Missing}", string.Join(", ", loader.LoadWarnings));

    return loader;
});

builder.Services.AddSingleton<EmotionDetectionService>();
builder.Services.AddScoped<SupabaseService>();

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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
    app.MapGet("/swagger", async ctx =>
    {
        ctx.Response.Redirect("/scalar/v1");
        await System.Threading.Tasks.Task.CompletedTask;
    });
}

app.UseCors("AllowAll");
app.UseAuthorization();

// Endpoint de teste rápido com a imagem local padrão.
app.MapGet("/debug/test", (Microsoft.AspNetCore.Http.HttpContext ctx) =>
{
    var imgPath = Path.Combine(Directory.GetCurrentDirectory(), "test-images", "example.jpg");
    if (!File.Exists(imgPath))
        return Results.NotFound("Test image not found");

    var image = ImageRgb24.FromFile(imgPath);
    var detector = ctx.RequestServices.GetRequiredService<EmotionDetectionService>();
    var result = detector.AnalyzeDualState(image);

    return Results.Json(new
    {
        estadoFisiologico = result.EstadoFisiologico,
        confiancaFisiologica = result.ConfiancaFisiologica,
        estadoEmocional = result.EstadoEmocional,
        confiancaEmocional = result.ConfiancaEmocional,
        fonte = result.Fonte,
        metricas = result.Metricas,
        regras = result.Regras,
        observacoes = result.Observacoes
    });
});

// Recebe uma imagem Base64 e devolve a análise completa (usado pelo modal "Testar IA").
app.MapPost("/api/test-ai", async (Microsoft.AspNetCore.Http.HttpContext ctx) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    var json = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(body);
    if (!json.TryGetProperty("image_base64", out var imgProp) || imgProp.ValueKind != System.Text.Json.JsonValueKind.String)
        return Results.BadRequest(new { message = "image_base64 required" });

    var base64 = imgProp.GetString();
    if (string.IsNullOrWhiteSpace(base64))
        return Results.BadRequest(new { message = "image_base64 required" });

    var preprocessor = ctx.RequestServices.GetRequiredService<ImagePreprocessingService>();
    var detector = ctx.RequestServices.GetRequiredService<EmotionDetectionService>();

    try
    {
        var image = preprocessor.LoadBase64Image(base64!);
        var probabilities = detector.GetAllProbabilities(image);
        var result = detector.AnalyzeDualState(image);

        return Results.Json(new
        {
            estadoFisiologico = result.EstadoFisiologico,
            confiancaFisiologica = result.ConfiancaFisiologica,
            estadoEmocional = result.EstadoEmocional,
            confiancaEmocional = result.ConfiancaEmocional,
            fonte = result.Fonte,
            metricas = result.Metricas,
            regras = result.Regras,
            observacoes = result.Observacoes,
            allProbabilities = probabilities
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

// Ferramenta de calibração (apenas Development + DEBUG_IMAGE_ROOT definido).
if (app.Environment.IsDevelopment())
{
    var debugRoot = Environment.GetEnvironmentVariable("DEBUG_IMAGE_ROOT");
    app.MapGet("/debug/analyze", (Microsoft.AspNetCore.Http.HttpContext ctx, string path) =>
    {
        if (string.IsNullOrWhiteSpace(debugRoot))
            return Results.NotFound("DEBUG_IMAGE_ROOT não configurado.");

        var root = Path.GetFullPath(debugRoot);
        var full = Path.GetFullPath(Path.Combine(root, path));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            return Results.NotFound("Imagem fora do diretório permitido.");

        var detector = ctx.RequestServices.GetRequiredService<EmotionDetectionService>();
        var result = detector.AnalyzeDualState(ImageRgb24.FromFile(full));

        return Results.Json(new
        {
            arquivo = path,
            estadoFisiologico = result.EstadoFisiologico,
            estadoEmocional = result.EstadoEmocional,
            confianca = result.ConfiancaEmocional ?? result.ConfiancaFisiologica,
            fonte = result.Fonte,
            metricas = result.Metricas,
            regras = result.Regras,
            observacoes = result.Observacoes
        });
    });
}

app.MapControllers();
app.Run();

static string ResolveMlDirectory(ILogger logger)
{
    var candidates = new[]
    {
        Path.Combine(AppContext.BaseDirectory, "ML"),
        Path.Combine(Directory.GetCurrentDirectory(), "ML")
    };

    foreach (var candidate in candidates)
    {
        if (Directory.Exists(candidate)) return candidate;
    }

    var fallback = candidates[0];
    Directory.CreateDirectory(fallback);
    logger.LogWarning("Diretório ML não encontrado; criado vazio em {Path}", fallback);
    return fallback;
}
