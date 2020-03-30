using System.Threading.Tasks;

namespace TRRabbitMQ.Core.Utils
{
    public interface IBusSerializer
    {
        Task<T> DeserializeAsync<T>(byte[] data);
        Task<byte[]> SerializeAsync<T>(T obj);
        T Deserialize<T>(byte[] data);
        byte[] Serialize<T>(T obj);
    }
}