using System.Threading;
using System.Threading.Tasks;

namespace ChatbotMedico.Client.Interfaces;

public interface IMessageClient
{
    Task SendTextAsync(string to, string message, CancellationToken ct);
}
