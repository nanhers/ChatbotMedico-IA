using Google.Cloud.Firestore;
using Google.Cloud.Firestore.V1;
using Grpc.Net.Client;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Obtener el puerto desde la variable de entorno PORT (Cloud Run asigna 8080 por defecto)
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://*:{port}");

builder.Services.AddControllers();

// Swagger / OpenAPI (solo requiere el paquete Swashbuckle.AspNetCore)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// FirestoreDb
builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("FirestoreDb");

    if (builder.Environment.IsDevelopment())
    {
        string projectId = builder.Configuration["Firestore:ProjectId"] ?? "demo-chatbotmedico";
        string host = builder.Configuration["Firestore:EmulatorHost"] ?? "127.0.0.1";
        int port = builder.Configuration.GetValue<int?>("Firestore:EmulatorPort") ?? 8080;

        // Forzar emulador SIN ADC: creamos un GrpcChannel inseguro hacia el emulador.
        // (Con FIRESTORE_EMULATOR_HOST a veces el SDK sigue intentando resolver credenciales.)
        var channel = GrpcChannel.ForAddress($"http://{host}:{port}");
        var client = new FirestoreClientBuilder { Endpoint = $"{host}:{port}", ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure }.Build();
        var db = FirestoreDb.Create(projectId, client);

        logger.LogInformation("Using Firestore Emulator. projectId={ProjectId} host={Host} port={Port}", projectId, host, port);
        return db;
    }

    logger.LogInformation("Using Firestore via ADC (non-Development environment)");
    return FirestoreDb.Create();
});

// DI
builder.Services.AddSingleton<ChatbotMedico.Services.Interfaces.IWebhookSignatureValidator, ChatbotMedico.Services.MetaWebhookSignatureValidator>();
builder.Services.AddScoped<ChatbotMedico.Services.Interfaces.ICalendarService, ChatbotMedico.Services.CalendarService>();
builder.Services.AddScoped<ChatbotMedico.Repository.Interfaces.IChatHistoryRepository, ChatbotMedico.Repository.FirestoreChatHistoryRepository>();
builder.Services.AddScoped<ChatbotMedico.Services.Interfaces.IChatHistoryService, ChatbotMedico.Services.ChatHistoryService>();

var useCalendarMock = builder.Configuration.GetValue<bool>("Mocks:UseCalendarMock");
if (useCalendarMock)
{
    builder.Services.AddScoped<ChatbotMedico.Client.Interfaces.ICalendarClient, ChatbotMedico.Client.MockCalendarClient>();
}
else
{
    builder.Services.AddScoped<ChatbotMedico.Client.Interfaces.ICalendarClient, ChatbotMedico.Client.GoogleCalendarClient>();
}

// WhatsApp messaging
var useWhatsAppMock = builder.Configuration.GetValue<bool>("Mocks:UseWhatsAppMock");
if (useWhatsAppMock)
{
    builder.Services.AddScoped<ChatbotMedico.Client.Interfaces.IMessageClient, ChatbotMedico.Client.MockMessageClient>();
}
else
{
    builder.Services.AddScoped<ChatbotMedico.Client.Interfaces.IMessageClient, ChatbotMedico.Client.WhatsApp.WhatsAppMessageClient>();
}

// AI
var useLlmMock = builder.Configuration.GetValue<bool>("Mocks:UseLlmMock");
if (useLlmMock)
{
    builder.Services.AddSingleton<ChatbotMedico.Client.Interfaces.ILlmClient, ChatbotMedico.Client.MockLlmClient>();
}
else
{
    builder.Services.AddSingleton<ChatbotMedico.Client.Interfaces.ILlmClient, ChatbotMedico.Client.Gemini.GeminiLlmClient>();
}

builder.Services.AddScoped<ChatbotMedico.Application.ChatBot>();

var app = builder.Build();

// Swagger UI solo en Development (local)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Configurar ASP.NET Core para que confíe en el Proxy Inverso de Google Cloud Run
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// ASEGÚRATE DE QUE ESTA LÍNEA SIGA COMENTADA O ELIMINADA:
// app.UseHttpsRedirection();

app.UseAuthorization();
app.MapControllers();

app.Run();