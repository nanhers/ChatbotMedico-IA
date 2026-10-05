using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

var app = builder.Build();

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