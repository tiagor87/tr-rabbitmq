using System;
using System.Threading.Tasks;
using TRRabbitMQ.Core.Options;

namespace TRRabbitMQ.Core.Messages.Implementations
{
    public class RetryConsumerMessage : IConsumerMessage
    {
        private readonly IConsumerMessage _consumerMessage;

        public RetryConsumerMessage(IConsumerMessage consumerMessage)
        {
            _consumerMessage = consumerMessage;
        }

        public MessageOptions Options => _consumerMessage.Options;
        public byte[] Body => _consumerMessage.Body;
        public int AttemptCount => _consumerMessage.AttemptCount + 1;
        public Exception Error => _consumerMessage.Error;
        public async Task<T> GetDataAsync<T>() => await _consumerMessage.GetDataAsync<T>();
        public T GetData<T>() => Options.Serializer.Deserialize<T>(Body);
        public void Success() => _consumerMessage.Success();
        public void Fail<T>(T error) where T : Exception => _consumerMessage.Fail(error);
    }
}