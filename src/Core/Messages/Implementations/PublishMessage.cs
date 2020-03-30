using System;
using TRRabbitMQ.Core.Options;
using TRRabbitMQ.Core.Utils;

namespace TRRabbitMQ.Core.Messages.Implementations
{
    public sealed class PublishMessage : IPublishMessage
    {
        private readonly object _data;
        private byte[] _body;

        public PublishMessage(MessageOptions options, object data, int attemptCount)
        {
            _data = data;
            _body = Array.Empty<byte>();
            Options = options;
            AttemptCount = attemptCount;
        }
        
        public PublishMessage(MessageOptions options, object data) : this (options, data, 0)
        {
        }

        public MessageOptions Options { get; }
        public int AttemptCount { get; }
        public byte[] GetBody()
        {
            return GetBody(Options.Serializer);
        }

        public byte[] GetBody(IBusSerializer serializer)
        {
            if (serializer == null) throw new ArgumentNullException(nameof(serializer));
            if (_data is IConsumerMessage consumerMessage) return consumerMessage.Body;
            return _body.Length == 0
                ? _body = serializer.Serialize(_data)
                : _body;
        }
    }
}