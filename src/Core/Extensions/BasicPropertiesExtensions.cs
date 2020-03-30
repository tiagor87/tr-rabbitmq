using System.Collections.Generic;
using RabbitMQ.Client;
using TRRabbitMQ.Core.Messages;

namespace TRRabbitMQ.Core.Extensions
{
    
    public static class BasicPropertiesExtensions
    {
        internal static void AddAttemptHeaders(this IBasicProperties basicProperties, IPublishMessage consumerMessage)
        {
            basicProperties.Headers = new Dictionary<string, object>();
            basicProperties.Headers.Add("MaxAttempts", consumerMessage.Options.MaxAttempts);
            basicProperties.Headers.Add("AttemptCount", consumerMessage.AttemptCount);
        }
        
    }
    
}