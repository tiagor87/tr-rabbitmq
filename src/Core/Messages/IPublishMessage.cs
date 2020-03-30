using TRRabbitMQ.Core.Options;
using TRRabbitMQ.Core.Utils;

namespace TRRabbitMQ.Core.Messages
{
    public interface IPublishMessage
    {
        MessageOptions Options { get; }
        int AttemptCount { get; }
        byte[] GetBody();
        byte[] GetBody(IBusSerializer serializer);
    }
}