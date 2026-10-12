using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Obtener el puerto desde la variable de entorno PORT (Cloud Run asigna 8080 por defecto)
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://*:{port}");

builder.Services.AddControllers();

// Swagger / OpenAPI (solo requiere el paquete Swashbuckle.AspNetCore)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// DI
builder.Services.AddScoped<ChatbotMedico.Services.Interfaces.ICalendarService, ChatbotMedico.Services.CalendarService>();
builder.Services.AddScoped<ChatbotMedico.Client.Interfaces.ICalendarClient, ChatbotMedico.Client.GoogleCalendarClient>();

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