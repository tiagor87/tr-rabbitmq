using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace TRRabbitMQ.Core.Messages
{
    public interface IRabbitMqConsumerMessage : IConsumerMessage
    {
        IModel Channel { get; }
        BasicDeliverEventArgs Event { get; }
    }
}