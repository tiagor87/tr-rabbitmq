using System;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TRRabbitMQ.Core.Options;

namespace TRRabbitMQ.Core.Messages.Implementations
{
    public class RabbitMqConsumerMessage : ConsumerMessage, IRabbitMqConsumerMessage
    {
        public RabbitMqConsumerMessage(
            MessageOptions options,
            byte[] body,
            int attemptCount,
            Action<IConsumerMessage> onSuccess,
            Action<IConsumerMessage> onRetry,
            Action<IConsumerMessage> onFail,
            IModel channel,
            BasicDeliverEventArgs @event) : base(options, body, attemptCount, onSuccess, onRetry, onFail)
        {
            Channel = channel;
            Event = @event;
        }
        
        public IModel Channel { get; }
        public BasicDeliverEventArgs Event { get; }
    }
}