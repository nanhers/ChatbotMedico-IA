using ChatbotMedico.DTOs.Messaging;
using ChatbotMedico.Repository.Interfaces;
using ChatbotMedico.Services;
using Microsoft.Extensions.Configuration;
using Moq;

namespace ChatbotMedico.Tests;

public sealed class ChatHistoryServiceTests
{
    [Fact]
    public async Task AppendAsync_WhenConversationDoesNotExist_CreatesConversationWithSingleMessage()
    {
        var repo = new Mock<IChatHistoryRepository>(MockBehavior.Strict);

        repo.Setup(r => r.GetAsync("5215550000001", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatHistoryConversationDto?)null);

        repo.Setup(r => r.CreateAsync(It.Is<ChatHistoryConversationDto>(c =>
                c.PhoneNumber == "5215550000001" &&
                c.Messages.Count == 1 &&
                c.Messages[0].Role == "user" &&
                c.Messages[0].Text == "hola"),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Firestore:MaxHistoryMessages"] = "100"
            })
            .Build();

        var sut = new ChatHistoryService(repo.Object, config);

        await sut.AppendAsync("5215550000001", new ChatHistoryMessageDto { Role = "user", Text = "hola" }, CancellationToken.None);

        repo.VerifyAll();
    }

    [Fact]
    public async Task AppendAsync_WhenConversationExists_UpdatesConversationAndAppendsMessage()
    {
        var repo = new Mock<IChatHistoryRepository>(MockBehavior.Strict);

        repo.Setup(r => r.GetAsync("5215550000001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatHistoryConversationDto
            {
                PhoneNumber = "5215550000001",
                Messages = new List<ChatHistoryMessageDto>
                {
                    new() { Role = "user", Text = "m1" }
                }
            });

        repo.Setup(r => r.UpdateAsync(It.Is<ChatHistoryConversationDto>(c =>
                c.PhoneNumber == "5215550000001" &&
                c.Messages.Count == 2 &&
                c.Messages[1].Text == "m2"),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Firestore:MaxHistoryMessages"] = "100"
            })
            .Build();

        var sut = new ChatHistoryService(repo.Object, config);

        await sut.AppendAsync("5215550000001", new ChatHistoryMessageDto { Role = "user", Text = "m2" }, CancellationToken.None);

        repo.VerifyAll();
    }

    [Fact]
    public async Task AppendAsync_WhenExceedsMax_TrimsToLastN()
    {
        var repo = new Mock<IChatHistoryRepository>(MockBehavior.Strict);

        repo.Setup(r => r.GetAsync("p", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatHistoryConversationDto
            {
                PhoneNumber = "p",
                Messages = new List<ChatHistoryMessageDto>
                {
                    new() { Role = "user", Text = "1" },
                    new() { Role = "user", Text = "2" },
                    new() { Role = "user", Text = "3" }
                }
            });

        repo.Setup(r => r.UpdateAsync(It.Is<ChatHistoryConversationDto>(c =>
                c.PhoneNumber == "p" &&
                c.Messages.Count == 3 &&
                c.Messages[0].Text == "2" &&
                c.Messages[1].Text == "3" &&
                c.Messages[2].Text == "4"),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Firestore:MaxHistoryMessages"] = "3"
            })
            .Build();

        var sut = new ChatHistoryService(repo.Object, config);

        await sut.AppendAsync("p", new ChatHistoryMessageDto { Role = "user", Text = "4" }, CancellationToken.None);

        repo.VerifyAll();
    }

    [Fact]
    public void Ctor_WhenMaxHistoryMessagesMissing_Throws()
    {
        var repo = new Mock<IChatHistoryRepository>(MockBehavior.Strict);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        Assert.Throws<InvalidOperationException>(() => new ChatHistoryService(repo.Object, config));
    }
}
