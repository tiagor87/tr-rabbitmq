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

        internal static int GetAttemptCount(this IBasicProperties basicProperties, int defaultValue = 0)
        {
            if (basicProperties.Headers.TryGetValue("AttemptCount", out var value))
            {
                return (int) value;
            }

            return defaultValue;
        }
        
        internal static int GetMaxAttempts(this IBasicProperties basicProperties, int defaultValue = 5)
        {
            if (basicProperties.Headers.TryGetValue("MaxAttempts", out var value))
            {
                return (int) value;
            }

            return defaultValue;
        }
        
    }
    
}