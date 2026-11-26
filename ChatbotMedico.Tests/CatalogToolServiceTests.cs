using ChatbotMedico.DTOs.AI;
using ChatbotMedico.DTOs.Sedes;
using ChatbotMedico.Repository.Interfaces;
using ChatbotMedico.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ChatbotMedico.Tests;

public sealed class CatalogToolServiceTests
{
    [Fact]
    public async Task ConsultCatalogAsync_WhenCatalogIsSedes_ReturnsReplyMessageWithMaps()
    {
        var sedesRepo = new Mock<ISedesRepository>(MockBehavior.Strict);
        sedesRepo
            .Setup(x => x.GetSingleAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SedeDto
            {
                Name = "Hospital Ángeles Lomas",
                Address = "Av. Vialidad de la Barranca 430, Huixquilucan, Edo. Méx.",
                GoogleMapsUrl = "https://maps.google.com/?q=test"
            });

        var logger = Mock.Of<ILogger<CatalogToolService>>();
        var sut = new CatalogToolService(sedesRepo.Object, logger);

        AiToolActionResultDto result = await sut.ConsultCatalogAsync("sedes", CancellationToken.None);

        Assert.Equal("reply_message", result.ToolName);
        Assert.Contains("Ubicación:", result.ReplyMessage);
        Assert.Contains("Dirección:", result.ReplyMessage);
        Assert.Contains("Google Maps:", result.ReplyMessage);
        Assert.Contains("https://maps.google.com/?q=test", result.ReplyMessage);
    }

    [Fact]
    public async Task ConsultCatalogAsync_WhenCatalogIsNotSupported_ReturnsGenericErrorMessage()
    {
        var sedesRepo = new Mock<ISedesRepository>(MockBehavior.Strict);
        var logger = Mock.Of<ILogger<CatalogToolService>>();
        var sut = new CatalogToolService(sedesRepo.Object, logger);

        AiToolActionResultDto result = await sut.ConsultCatalogAsync("otro", CancellationToken.None);

        Assert.Equal("reply_message", result.ToolName);
        Assert.Equal("Por el momento no tengo la información de ubicación disponible, intenta más tarde.", result.ReplyMessage);
    }

    [Fact]
    public async Task ConsultCatalogAsync_WhenSedesMissing_ReturnsGenericErrorMessage()
    {
        var sedesRepo = new Mock<ISedesRepository>(MockBehavior.Strict);
        sedesRepo
            .Setup(x => x.GetSingleAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((SedeDto?)null);

        var logger = Mock.Of<ILogger<CatalogToolService>>();
        var sut = new CatalogToolService(sedesRepo.Object, logger);

        AiToolActionResultDto result = await sut.ConsultCatalogAsync("sedes", CancellationToken.None);

        Assert.Equal("reply_message", result.ToolName);
        Assert.Equal("Por el momento no tengo la información de ubicación disponible, intenta más tarde.", result.ReplyMessage);
    }
}
