using System;
using System.Threading.Tasks;
using TRRabbitMQ.Core.Options;

namespace TRRabbitMQ.Core.Messages
{
    public interface IConsumerMessage
    {
        MessageOptions Options { get; }
        int AttemptCount { get; }
        byte[] Body { get; }
        Exception Error { get; }
        Task<T> GetDataAsync<T>();
        T GetData<T>();
        void Success();
        void Fail<T>(T error) where T: Exception;
    }
}