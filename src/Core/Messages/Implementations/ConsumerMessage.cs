using System;
using System.Threading.Tasks;
using TRRabbitMQ.Core.Options;

namespace TRRabbitMQ.Core.Messages.Implementations
{
    public class ConsumerMessage : IConsumerMessage
    {
        private readonly Action<IConsumerMessage> _onSuccess;
        private readonly Action<IConsumerMessage> _onRetry;
        private readonly Action<IConsumerMessage> _onFail;

        public ConsumerMessage(
            MessageOptions options,
            byte[] body,
            int attemptCount,
            (Action<IConsumerMessage> onSuccess, Action<IConsumerMessage> onRetry, Action<IConsumerMessage> onFail) actions)
        {
            Body = body;
            _onSuccess = actions.onSuccess;
            _onRetry = actions.onRetry;
            _onFail = actions.onFail;
            Options = options;
            AttemptCount = attemptCount;
        }

        public MessageOptions Options { get; }
        public byte[] Body { get; }
        public Exception Error { get; private set; }
        public int AttemptCount { get; }

        public async Task<T> GetDataAsync<T>() => await Options.Serializer.DeserializeAsync<T>(Body);
        public T GetData<T>() => Options.Serializer.Deserialize<T>(Body);
        public void Success()
        {
            _onSuccess(this);
        }

        public void Fail<T>(T error) where T: Exception
        {
            Error = error;
            if (CanRetry())
            {
                _onRetry(new RetryConsumerMessage(this));
                return;
            }
            _onFail(this);
        }
        
        private bool CanRetry()
        {
            return AttemptCount < Options.MaxAttempts;
        }
    }
}