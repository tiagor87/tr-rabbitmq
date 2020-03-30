using System.Linq;
using RabbitMQ.Client;
using TRRabbitMQ.Core.Models;

namespace TRRabbitMQ.Core.Extensions
{
    public static class ExchangeExtensions
    {
        internal static void Declare(this Exchange exchange, IModel channel)
        {
            if (exchange.IsDefault) return;
            var arguments = exchange.Arguments.ToDictionary(x => x.Key, x => x.Value);
            channel.ExchangeDeclare(exchange.Name.Value, exchange.Type.Value, exchange.Durability.IsDurable, exchange.IsAutoDelete, arguments);
        }
    }
}