using System.Threading;
using System.Threading.Tasks;

namespace ChatbotMedico.Client.MessageClient.Interfaces;

public interface IMessageClient
{
    Task SendTextAsync(string to, string message, CancellationToken ct);
}
